using System.Collections.Immutable;

namespace PhiClient.Core;

public readonly record struct MapCell(byte Tile, byte Attribute);
public sealed record AutomapWorld
{
    public ImmutableDictionary<MapPosition, ImmutableDictionary<MapPosition, MapCell>> Blocks { get; init; } =
        ImmutableDictionary<MapPosition, ImmutableDictionary<MapPosition, MapCell>>.Empty;
    public ImmutableHashSet<MapPosition> Dirty { get; init; } = [];
    public bool Loaded { get; init; }
    public string Error { get; init; } = "";
    public MapCell? Cell(MapPosition position) =>
        Blocks.GetValueOrDefault(Automap.BlockOf(position))?.TryGetValue(position, out var cell) == true ? cell : null;

    public AutomapWorld Absorb(MapFrame frame)
    {
        if (frame is not { Size: 7, Position: { } pos, Tiles.Length: 49, Attributes.Length: 49 }) return this;
        var next = this;
        for (var i = 0; i < 49; i++)
        {
            if (frame.Tiles[i] == '?') continue;
            var position = new MapPosition(pos.X + i % 7 - 3, pos.Y + i / 7 - 3);
            var cell = new MapCell(frame.Tiles[i], frame.Attributes[i]);
            if (next.Cell(position) == cell) continue;
            var block = Automap.BlockOf(position);
            if (!next.Blocks.ContainsKey(block) && next.Blocks.Count >= 4096)
                throw new InvalidOperationException("記録地図が4096区画に達しました。不要な世界の記録地図を消してください。");
            var cells = next.Blocks.GetValueOrDefault(block) ?? ImmutableDictionary<MapPosition, MapCell>.Empty;
            next = next with { Blocks = next.Blocks.SetItem(block, cells.SetItem(position, cell)),
                Dirty = next.Dirty.Add(block) };
        }
        return next;
    }

    public AutomapWorld MergeLoaded(AutomapWorld saved)
    {
        var blocks = saved.Blocks;
        foreach (var (key, cells) in Blocks)
            blocks = blocks.SetItem(key, (blocks.GetValueOrDefault(key) ??
                ImmutableDictionary<MapPosition, MapCell>.Empty).SetItems(cells));
        if (blocks.Count > 4096) throw new InvalidDataException("記録地図が4096区画を超えています。");
        return this with { Blocks = blocks, Loaded = true, Error = "" };
    }
}

public sealed record MapDisplay(MapFrame Frame, bool[] Recalled);
public static class Automap
{
    public static MapPosition BlockOf(MapPosition position)
    {
        static long Floor(long value) => value / 32 - (value < 0 && value % 32 != 0 ? 1 : 0);
        return new(Floor(position.X), Floor(position.Y));
    }

    public static int RingFor(double width, double height, double chip)
    {
        if (!double.IsFinite(width) || !double.IsFinite(height) || !double.IsFinite(chip) || chip <= 0)
            throw new ArgumentOutOfRangeException(nameof(chip));
        var side = Math.Min(width, height);
        return side < chip * 7 ? 0 : (int)Math.Clamp(Math.Floor((side / chip - 7) / 2 + 0.5), 0, 4);
    }

    public static MapDisplay Compose(MapFrame map, AutomapWorld memory, int ring, MapPosition offset)
    {
        if (map is not { Size: 7, Position: { } original, Tiles.Length: 49, Attributes.Length: 49 })
            return new(map, []);
        var pos = new MapPosition(original.X + offset.X, original.Y + offset.Y);
        if (ring is < 0 or > 4) throw new ArgumentOutOfRangeException(nameof(ring));
        if (ring == 0) return new(map with { Position = pos }, []);
        var dim = 7 + 2 * ring;
        var tiles = new byte[dim * dim];
        var attrs = new byte[tiles.Length];
        var recalled = new bool[tiles.Length];
        for (var row = 0; row < dim; row++)
        for (var col = 0; col < dim; col++)
        {
            var i = row * dim + col;
            var inner = row >= ring && row < ring + 7 && col >= ring && col < ring + 7;
            var live = inner ? (row - ring) * 7 + col - ring : -1;
            if (live >= 0 && map.Tiles[live] != '?')
            {
                tiles[i] = map.Tiles[live];
                attrs[i] = map.Attributes[live];
            }
            else if (memory.Cell(new(pos.X + col - dim / 2, pos.Y + row - dim / 2)) is { } cell)
            {
                tiles[i] = cell.Tile;
                attrs[i] = cell.Attribute;
                recalled[i] = true;
            }
            else
            {
                tiles[i] = live >= 0 ? map.Tiles[live] : (byte)'?';
                attrs[i] = live >= 0 ? map.Attributes[live] : (byte)0;
            }
        }
        return new(new(map.Direction, tiles, attrs,
            map.Actors.Select(actor => actor with { X = actor.X + ring, Y = actor.Y + ring }).ToArray(), dim)
            { Position = pos }, recalled);
    }
}
