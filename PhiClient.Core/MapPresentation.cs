using System.Text.RegularExpressions;

namespace PhiClient.Core;

public sealed record EagleBoard(int Dim, byte[] Tiles, byte[] Attributes, IReadOnlyList<Actor> Actors)
{
    public static EagleBoard Build(EagleFrame frame)
    {
        if (frame.Width is < 1 or > 15 || frame.Height is < 1 or > 15 ||
            frame.Tiles.Length != frame.Width * frame.Height || frame.Attributes.Length != frame.Tiles.Length)
            throw new ArgumentException("Invalid EagleEye board.");
        var dim = Math.Max(frame.Width, frame.Height);
        var tiles = Enumerable.Repeat((byte)'?', dim * dim).ToArray();
        var attributes = new byte[tiles.Length];
        var actors = new List<Actor>();
        for (var y = 0; y < frame.Height; y++)
        for (var x = 0; x < frame.Width; x++)
        {
            var source = y * frame.Width + x;
            var target = y * dim + x;
            tiles[target] = frame.Tiles[source];
            if (frame.Attributes[source] == 0x58)
                actors.Add(new(x, y, "", "t_man", 'F', '*', DefaultGraphic: 0x40));
            else attributes[target] = frame.Attributes[source];
        }
        return new(dim, tiles, attributes, actors);
    }
}

public sealed record MapLabel(string Text, double X, double Y, bool Missing = false);

public static partial class MapLabels
{
    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex Tags();

    public static string Clean(string name) => Tags().Replace(name, "").Trim();

    public static IReadOnlyList<string> NameList(IReadOnlyList<Actor> actors) =>
        actors.OrderBy(actor => actor.Y).Select(actor => Clean(actor.Name)).ToArray();

    public static IReadOnlyList<MapLabel> Names(IReadOnlyList<Actor> actors, int dim, int top = 16)
    {
        var labels = new Dictionary<(int, int), MapLabel>();
        foreach (var actor in actors)
        {
            if (actor.X < 0 || actor.Y < 0 || actor.X >= dim || actor.Y >= dim ||
                actor.X == dim / 2 && actor.Y == dim / 2 || Clean(actor.Name) is not { Length: > 0 } name)
                continue;
            labels[(actor.X, actor.Y)] = new(name, actor.X * 32 + 16, actor.Y * 32 + top + 19.2);
        }
        return labels.Values.ToArray();
    }

    public static IReadOnlyList<MapLabel> Missing(IReadOnlyList<Actor> actors, int dim,
        Func<Actor, bool> available, int top = 16) =>
        Enumerable.Range(0, dim).SelectMany(y => MapGraphics.ArrangeRow(actors.Where(actor =>
            actor.Y == y && actor.X >= 0 && actor.X < dim)))
            .Where(item => !available(item.Actor))
            .Select(item => new MapLabel(Clean(item.Actor.Name) is { Length: > 0 } name
                    ? char.ConvertFromUtf32(char.ConvertToUtf32(name, 0)) : "?",
                item.Actor.X * 32 + 16 + item.Offset, item.Actor.Y * 32 + top + 16, true)).ToArray();

    public static (int X, int Y)? Hit(double x, double y, int dim, double zoom = 1)
    {
        x /= zoom;
        y = y / zoom - 16;
        return zoom <= 0 || x < 0 || y < 0 || x >= dim * 32 || y >= dim * 32
            ? null : ((int)(x / 32) - dim / 2, (int)(y / 32) - dim / 2);
    }
}
