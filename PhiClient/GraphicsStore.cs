using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media.Imaging;
using PhiClient.Core;
using Windows.Graphics.Imaging;

namespace PhiClient;

internal sealed class GraphicsStore
{
    private readonly Dictionary<string, PixelImage?> images = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> aliases = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> defaults = new(StringComparer.Ordinal);
    private readonly HashSet<string> issues = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string, string, int), string?> resolvedCharacters = [];
    private readonly Dictionary<string, string> resolvedChips = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim gate = new(1);
    public string Folder { get; private set; } = "";
    public int Revision { get; private set; }
    public int AnimationFrame { get; set; }
    public string Notice => Folder.Length == 0 ? "Graphics not configured: schematic map. Choose View > Graphics folder."
        : issues.Count == 0 ? "User graphics loaded." : "Graphics: " + string.Join(" | ", issues.Take(4));

    public async Task SetFolderAsync(string folder)
    {
        if (folder.Length > 0 && (!Path.IsPathFullyQualified(folder) || folder.StartsWith(@"\\") || !Directory.Exists(folder)))
            throw new ArgumentException("Choose an existing absolute local graphics folder (not a network share).");
        await gate.WaitAsync();
        try
        {
            Folder = folder.Length == 0 ? "" : Path.GetFullPath(folder);
            images.Clear();
            aliases.Clear();
            defaults.Clear();
            issues.Clear();
            resolvedCharacters.Clear();
            resolvedChips.Clear();
            Revision++;
            if (Folder.Length == 0) return;
            var chara = Path.Combine(Folder, "chara");
            if (Directory.Exists(chara))
                foreach (var file in Directory.EnumerateFiles(chara, "*.ali").Take(256))
                    foreach (var line in (await ReadIndexAsync(file)).Split('\n'))
                    {
                        var parts = line.Trim().Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length == 2 && GraphicsNames.Safe(parts[0]) &&
                            (aliases.Count < 10000 || aliases.ContainsKey(parts[1])))
                            aliases[parts[1]] = parts[0];
                    }
            var index = Path.Combine(Folder, "Skins", "Mine", "Chara", "Index.txt");
            if (File.Exists(index))
                foreach (var line in (await ReadIndexAsync(index)).Split('\n'))
                {
                    if (line.TrimStart().StartsWith("//", StringComparison.Ordinal)) continue;
                    var parts = line.Split('=', 2);
                    if (parts.Length == 2 && GraphicsNames.Safe(parts[1].Trim()))
                        defaults[parts[0].Trim()] = parts[1].Trim();
                }
            else AddIssue(@"Missing Skins\Mine\Chara\Index.txt (default characters unavailable).");
        }
        finally { gate.Release(); }
    }

    private static async Task<string> ReadIndexAsync(string path)
    {
        if (new FileInfo(path).Length > 128 * 1024) throw new IOException("Graphics index exceeds 128 KiB.");
        return PhiWire.Encoding.GetString(await File.ReadAllBytesAsync(path));
    }

    private void AddIssue(string issue)
    {
        if (issues.Count < 32 && issues.Add(issue)) Revision++;
    }

    private string? CharacterPath(Actor actor)
    {
        var key = (actor.Name, actor.Graphic, actor.DefaultGraphic);
        if (resolvedCharacters.TryGetValue(key, out var cached)) return cached;
        if (resolvedCharacters.Count >= 4096) resolvedCharacters.Clear();
        return resolvedCharacters[key] = FindCharacter(actor);
    }

    private string? FindCharacter(Actor actor)
    {
        aliases.TryGetValue(actor.Name, out var alias);
        foreach (var name in actor.DefaultGraphic == 0x40
            ? new[] { actor.Graphic, alias, actor.Name } : new[] { alias, actor.Graphic, actor.Name })
            if (name is not null && GraphicsNames.Allowed(name) && File.Exists(Path.Combine(Folder, "chara", name + ".bmp")))
                return Path.Combine("chara", name + ".bmp");
        if (defaults.TryGetValue(GraphicsNames.DefaultKey(actor.DefaultGraphic), out var file) && GraphicsNames.Allowed(file))
            foreach (var rel in new[] { Path.Combine("Skins", "Mine", "Chara", file), Path.Combine("chara", file) })
                if (File.Exists(Path.Combine(Folder, rel))) return rel;
        return null;
    }

    private string ChipPath(string name)
    {
        if (resolvedChips.TryGetValue(name, out var cached)) return cached;
        if (resolvedChips.Count >= 4096) resolvedChips.Clear();
        var path = GraphicsNames.Allowed(name) && name != "default" ? Path.Combine("chip", name + ".bmp") : "";
        if (path.Length > 0 && File.Exists(Path.Combine(Folder, path))) return resolvedChips[name] = path;
        if (path.Length > 0) AddIssue($"Missing mapset '{name}'; using Default.bmp.");
        return resolvedChips[name] = Path.Combine("Skins", "Mine", "chip", "Default.bmp");
    }

    public async Task PrepareAsync(GameSession session)
    {
        await gate.WaitAsync();
        try
        {
            if (Folder.Length == 0) return;
            foreach (var mapset in session.MapSets) await LoadAsync(ChipPath(mapset), 1);
            await LoadAsync(Path.Combine("Skins", "Mine", "Images", "Items.bmp"), 2);
            foreach (var actor in (session.Map?.Actors ?? []).Concat(
                session.Eagle is { } eagle ? EagleBoard.Build(eagle).Actors : []).Distinct())
            {
                var path = CharacterPath(actor);
                if (path is not null) await LoadAsync(path, 0);
                else AddIssue($"Character image unavailable: {actor.Name} ({actor.Graphic}).");
            }
        }
        finally { gate.Release(); }
    }

    private async Task LoadAsync(string relative, int kind)
    {
        if (images.ContainsKey(relative)) return;
        if (images.Count >= 512)
        {
            AddIssue("Graphics cache limit reached (512 sheets). Reload graphics to clear it.");
            return;
        }
        images.Add(relative, null);
        try
        {
            if (!GraphicsNames.Allowed(Path.GetFileName(relative)))
                throw new ArgumentException("This graphic filename is excluded.");
            var path = Path.Combine(Folder, relative);
            var file = new FileInfo(path);
            if (!file.Exists) throw new FileNotFoundException("File not found.");
            if (file.Length > 8 * 1024 * 1024) throw new IOException("Image exceeds 8 MiB.");
            await using var stream = File.OpenRead(path);
            var decoder = await BitmapDecoder.CreateAsync(BitmapDecoder.BmpDecoderId, stream.AsRandomAccessStream());
            if (decoder.PixelWidth > 2048 || decoder.PixelHeight > 2048)
                throw new IOException("Image exceeds 2048 pixels.");
            var data = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight,
                new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
            images[relative] = new PixelImage((int)decoder.PixelWidth, (int)decoder.PixelHeight, data.DetachPixelData())
                .Transparent(horizontalMask: kind == 1, verticalMask: kind == 2);
            Revision++;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or COMException)
        {
            AddIssue($"{relative}: {ex.Message}");
        }
    }

    public WriteableBitmap? Draw(GameSession session, double rasterScale)
    {
        var eagle = session.ShowEagle ? session.Eagle : null;
        var map = session.Map;
        if (Folder.Length == 0 || map is null && eagle is null) return null;
        var frame = eagle is null ? AnimationFrame : 0;
        var key = (map, eagle, session.Model.MapSets, Revision, frame, rasterScale);
        if (session.GraphicsKey == key && session.GraphicsImage is not null) return session.GraphicsImage;
        PixelImage? Get(string path) => images.GetValueOrDefault(path);
        var board = eagle is null ? null : EagleBoard.Build(eagle);
        var dim = board?.Dim ?? map!.Size;
        var actors = board?.Actors ?? map!.Actors;
        PixelImage? Character(Actor actor) => CharacterPath(actor) is { } path ? Get(path) : null;
        var rendered = MapGraphics.Draw(dim, dim,
            board?.Tiles ?? map!.Tiles, board?.Attributes ?? map!.Attributes, actors,
            slot => Get(ChipPath(session.MapSets[slot])), Get(Path.Combine("Skins", "Mine", "Images", "Items.bmp")),
            Character, frame, eagle is not null);
        session.GraphicsWidth = rendered.Width;
        session.GraphicsHeight = rendered.Height;
        session.GraphicsLabels = MapLabels.Missing(actors, dim, actor => Character(actor) is not null, board is null ? 16 : 0)
            .Concat(MapLabels.Names(actors, dim, board is null ? 16 : 0)).ToArray();
        rendered = rendered.ScaleNearest((int)Math.Round(rendered.Width * rasterScale),
            (int)Math.Round(rendered.Height * rasterScale));
        if (session.GraphicsImage is not { } bitmap || bitmap.PixelWidth != rendered.Width || bitmap.PixelHeight != rendered.Height)
            session.GraphicsImage = bitmap = new WriteableBitmap(rendered.Width, rendered.Height);
        using (var buffer = bitmap.PixelBuffer.AsStream()) buffer.Write(rendered.Pixels);
        bitmap.Invalidate();
        session.GraphicsKey = key with { Item4 = Revision };
        return bitmap;
    }
}
