namespace PhiClient.Core;

public sealed record MapMotion(MapPosition? Position = null, string World = "", int X = 0, int Y = 0,
    long Started = 0, long Now = 0)
{
    public (int X, int Y) Offset
    {
        get
        {
            var remaining = 1 - Math.Clamp((Now - Started) / 90.0, 0, 1);
            return ((int)Math.Floor(X * 32 * remaining + 0.5), (int)Math.Floor(Y * 32 * remaining + 0.5));
        }
    }

    public MapMotion Update(MapPosition? position, string world, long now, bool northFixed)
    {
        if (position == Position && world == World)
            return Offset == (0, 0) ? this : this with { Now = now };
        var dx = position is null || Position is null ? 0 : position.X - Position.X;
        var dy = position is null || Position is null ? 0 : position.Y - Position.Y;
        var distance = Math.Max(Math.Abs((double)dx), Math.Abs((double)dy));
        return new(position, world, northFixed && world == World && distance is > 0 and <= 2 ? (int)dx : 0,
            northFixed && world == World && distance is > 0 and <= 2 ? (int)dy : 0, now, now);
    }

    public MapMotion Stop() => this with { X = 0, Y = 0 };
}
