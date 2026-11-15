using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhiClient.Core;

public sealed record MapCellEntry(long X, long Y, byte Tile, byte Attribute);
public sealed record MapBlockDocument(string World, long X, long Y, MapCellEntry[] Cells);

public sealed class AutomapStore(string folder)
{
    private readonly SemaphoreSlim writes = new(1, 1);
    private string WorldFolder(string world) => Path.Combine(folder,
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(world))));

    public async Task<AutomapWorld> LoadAsync(string world, CancellationToken token = default)
    {
        var directory = WorldFolder(world);
        if (!Directory.Exists(directory)) return new() { Loaded = true };
        var blocks = ImmutableDictionary<MapPosition, ImmutableDictionary<MapPosition, MapCell>>.Empty;
        foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
        {
            if (blocks.Count >= 4096 || new FileInfo(file).Length > 128 * 1024)
                throw new InvalidDataException("記録地図のファイル数または区画サイズが上限を超えています。");
            var doc = JsonSerializer.Deserialize(await File.ReadAllBytesAsync(file, token),
                AutomapJsonContext.Default.MapBlockDocument) ?? throw new InvalidDataException("記録地図が空です。");
            if (doc.World != world || doc.Cells is null || doc.Cells.Length > 1024)
                throw new InvalidDataException("記録地図の世界名または区画内のマス数が不正です。");
            var block = new MapPosition(doc.X, doc.Y);
            var cells = ImmutableDictionary<MapPosition, MapCell>.Empty;
            foreach (var entry in doc.Cells)
            {
                if (entry is null) throw new InvalidDataException("記録地図に空のマスがあります。");
                var position = new MapPosition(entry.X, entry.Y);
                if (Automap.BlockOf(position) != block || cells.ContainsKey(position))
                    throw new InvalidDataException("記録地図の座標が不正または重複しています。");
                cells = cells.Add(position, new(entry.Tile, entry.Attribute));
            }
            if (blocks.ContainsKey(block)) throw new InvalidDataException("記録地図の区画が重複しています。");
            blocks = blocks.Add(block, cells);
        }
        return new() { Blocks = blocks, Loaded = true };
    }

    public async Task SaveBlockAsync(string world, MapPosition block,
        ImmutableDictionary<MapPosition, MapCell> cells, CancellationToken token = default)
    {
        if (cells.Count > 1024 || cells.Keys.Any(position => Automap.BlockOf(position) != block))
            throw new ArgumentException("記録地図の区画が不正です。");
        var directory = WorldFolder(world);
        var file = Path.Combine(directory, $"{block.X}_{block.Y}.json");
        var doc = new MapBlockDocument(world, block.X, block.Y, cells.Select(pair =>
            new MapCellEntry(pair.Key.X, pair.Key.Y, pair.Value.Tile, pair.Value.Attribute)).ToArray());
        await writes.WaitAsync(token);
        try
        {
            Directory.CreateDirectory(directory);
            await File.WriteAllBytesAsync(file + ".tmp",
                JsonSerializer.SerializeToUtf8Bytes(doc, AutomapJsonContext.Default.MapBlockDocument), token);
            File.Move(file + ".tmp", file, true);
        }
        finally
        {
            try { if (File.Exists(file + ".tmp")) File.Delete(file + ".tmp"); }
            finally { writes.Release(); }
        }
    }

    public async Task ClearAsync(string world, CancellationToken token = default)
    {
        await writes.WaitAsync(token);
        try
        {
            var directory = WorldFolder(world);
            if (Directory.Exists(directory))
            {
                foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
                    File.Delete(file);
            }
        }
        finally { writes.Release(); }
    }
}

[JsonSerializable(typeof(MapBlockDocument))]
internal partial class AutomapJsonContext : JsonSerializerContext;
