using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Media.Imaging;
using PhiClient.Core;
using Windows.Graphics.Imaging;

namespace PhiClient;

internal sealed class GraphicsStore
{
    private readonly Dictionary<string, PixelImage?> images = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PixelImage?> bundledImages = new(StringComparer.OrdinalIgnoreCase);
    private static readonly string DefaultChip = Path.Combine("Skins", "Mine", "chip", "Default.bmp");
    private static readonly string ItemsSheet = Path.Combine("Skins", "Mine", "Images", "Items.bmp");
    private static readonly string BundledFolder = Path.Combine(AppContext.BaseDirectory, "Assets", "BundledBasic");
    private readonly Dictionary<string, string> aliases = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> defaults = new(StringComparer.Ordinal);
    private readonly HashSet<string> issues = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<(string, string, int), string?> resolvedCharacters = [];
    private readonly Dictionary<string, string> resolvedChips = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim gate = new(1);
    public string Folder { get; private set; } = "";
    public int Revision { get; private set; }
    public int AnimationFrame { get; set; }
    public string Notice => (Folder.Length == 0 ? "同梱の基本画像を表示します。外部素材は「View」→「画像素材フォルダー…」で指定できます。"
        : "外部素材を優先し、未設定・欠落した画像だけ同梱の基本画像で補います。") +
        (issues.Count == 0 ? "" : " 読み込みを確認してください: " + string.Join(" | ", issues.Take(4)));

    public async Task SetFolderAsync(string folder)
    {
        if (folder.Length > 0 && (!Path.IsPathFullyQualified(folder) || folder.StartsWith(@"\\") || !Directory.Exists(folder)))
            throw new ArgumentException("このPCにある画像素材フォルダーの絶対パスを指定してください。ネットワーク共有は指定できません。");
        await gate.WaitAsync();
        try
        {
            Folder = folder.Length == 0 ? "" : Path.GetFullPath(folder);
            if (Folder.Length > 0 && Directory.Exists(Path.Combine(Folder, "resources", "phi", "chara")))
                Folder = Path.Combine(Folder, "resources", "phi");
            images.Clear();
            bundledImages.Clear();
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
        if (Folder.Length == 0) return DefaultChip;
        if (resolvedChips.TryGetValue(name, out var cached)) return cached;
        if (resolvedChips.Count >= 4096) resolvedChips.Clear();
        var path = GraphicsNames.Allowed(name) && name != "default" ? Path.Combine("chip", name + ".bmp") : "";
        if (path.Length > 0 && File.Exists(Path.Combine(Folder, path))) return resolvedChips[name] = path;
        if (path.Length > 0) AddIssue($"Missing mapset '{name}'; using Default.bmp.");
        return resolvedChips[name] = DefaultChip;
    }

    private static string BundledCharacter(Actor actor) => Path.Combine("actors", (actor.DefaultGraphic switch
    {
        0 or 0x40 => "person", 2 => "beast", 3 => "berserker", 4 => "golem", 5 => "skeleton",
        6 => "spirit", 7 => "eraser", 0x81 => "sign", 0x82 => "magic",
        0x70 => "animated-object", 0x75 => "object", _ => "wanderer"
    }) + ".bmp");

    public async Task PrepareAsync(GameSession session)
    {
        await gate.WaitAsync();
        try
        {
            await LoadAsync("map.bmp", 1, bundled: true);
            await LoadAsync("items.bmp", 2, bundled: true);
            if (Folder.Length > 0)
            {
                await LoadAsync(DefaultChip, 1);
                foreach (var mapset in session.MapSets) await LoadAsync(ChipPath(mapset), 1);
                await LoadAsync(ItemsSheet, 2);
            }
            foreach (var actor in (session.Map?.Actors ?? []).Concat(
                session.Eagle is { } eagle ? EagleBoard.Build(eagle).Actors : []).Distinct())
            {
                await LoadAsync(BundledCharacter(actor), 0, bundled: true);
                if (Folder.Length > 0)
                {
                    var path = CharacterPath(actor);
                    if (path is not null) await LoadAsync(path, 0);
                    else AddIssue($"外部キャラクター画像がないため基本画像で補います: {actor.Name} ({actor.Graphic})。");
                }
            }
        }
        finally { gate.Release(); }
    }

    public async Task<PixelImage> RegistrationPreviewAsync(string graphic)
    {
        await gate.WaitAsync();
        try
        {
            if (Folder.Length == 0)
                throw new InvalidOperationException("素材フォルダーを設定すると、候補の実際のキャラグラを確認できます。");
            if (!GraphicsNames.Allowed(graphic))
                throw new ArgumentException("この候補の素材名は安全なローカルファイル名として扱えません。");
            var path = Path.Combine("chara", graphic + ".bmp");
            await LoadAsync(path, 0);
            if (images.GetValueOrDefault(path) is not { } image)
                throw new IOException($"「{graphic}」の素材が見つからないか、画像形式が一致しません。素材フォルダーを確認してください。");
            var frame = MapGraphics.CharacterFrame(new(0, 0, "", Direction: 'F', Size: '*'), 0);
            return image.Crop(frame.X, frame.Y, frame.Width, 32);
        }
        finally { gate.Release(); }
    }

    private async Task LoadAsync(string relative, int kind, bool bundled = false)
    {
        var cache = bundled ? bundledImages : images;
        if (cache.ContainsKey(relative)) return;
        if (cache.Count >= 512)
        {
            AddIssue("Graphics cache limit reached (512 sheets). Reload graphics to clear it.");
            return;
        }
        cache.Add(relative, null);
        try
        {
            if (!GraphicsNames.Allowed(Path.GetFileName(relative)))
                throw new ArgumentException("This graphic filename is excluded.");
            var path = Path.Combine(bundled ? BundledFolder : Folder, relative);
            var file = new FileInfo(path);
            if (!file.Exists) throw new FileNotFoundException("File not found.");
            if (file.Length > 8 * 1024 * 1024) throw new IOException("Image exceeds 8 MiB.");
            await using var stream = File.OpenRead(path);
            var decoder = await BitmapDecoder.CreateAsync(BitmapDecoder.BmpDecoderId, stream.AsRandomAccessStream());
            if (decoder.PixelWidth > 2048 || decoder.PixelHeight > 2048)
                throw new IOException("Image exceeds 2048 pixels.");
            var data = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight,
                new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
            cache[relative] = new PixelImage((int)decoder.PixelWidth, (int)decoder.PixelHeight, data.DetachPixelData())
                .Transparent(horizontalMask: kind == 1, verticalMask: kind == 2);
            Revision++;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or COMException)
        {
            AddIssue($"{(bundled ? "同梱画像" : "外部画像")} {relative}: {ex.Message}");
        }
    }

    public WriteableBitmap? Draw(GameSession session, double rasterScale, MapDisplay? display, double zoom, bool smooth)
    {
        var eagle = session.ShowEagle ? session.Eagle : null;
        var map = display?.Frame ?? session.Map;
        if (map is null && eagle is null) return null;
        var frame = eagle is null ? AnimationFrame : 0;
        var slide = eagle is null && smooth && session.NorthFixed ? session.Model.Motion.Offset : (0, 0);
        var key = (map, eagle, session.Model.MapSets, Revision, frame, rasterScale, zoom, slide.Item1, slide.Item2);
        if (session.GraphicsKey == key && session.GraphicsImage is not null) return session.GraphicsImage;
        PixelImage? Get(string path) => images.GetValueOrDefault(path);
        PixelImage? Bundled(string path) => bundledImages.GetValueOrDefault(path);
        var board = eagle is null ? null : EagleBoard.Build(eagle);
        var dim = board?.Dim ?? map!.Size;
        var actors = board?.Actors ?? map!.Actors;
        PixelImage? Character(Actor actor) =>
            (Folder.Length > 0 && CharacterPath(actor) is { } path ? Get(path) : null) ?? Bundled(BundledCharacter(actor));
        var rendered = MapGraphics.Draw(dim, dim,
            board?.Tiles ?? map!.Tiles, board?.Attributes ?? map!.Attributes, actors,
            slot => Get(ChipPath(session.MapSets[slot])) ?? Get(DefaultChip) ?? Bundled("map.bmp"),
            Get(ItemsSheet) ?? Bundled("items.bmp"),
            Character, frame, eagle is not null, eagle is null ? display?.Recalled : null, slide.Item1, slide.Item2);
        session.GraphicsWidth = (int)Math.Round(rendered.Width * zoom);
        session.GraphicsHeight = (int)Math.Round(rendered.Height * zoom);
        session.GraphicsDim = dim;
        session.GraphicsZoom = zoom;
        session.GraphicsLabels = MapLabels.Missing(actors, dim, actor => Character(actor) is not null, board is null ? 16 : 0)
            .Concat(MapLabels.Names(actors, dim, board is null ? 16 : 0))
            .Select(label => label with { X = (label.X + slide.Item1) * zoom, Y = (label.Y + slide.Item2) * zoom }).ToArray();
        var scale = Math.Min(rasterScale * zoom, 4096.0 / Math.Max(rendered.Width, rendered.Height));
        if (scale < rasterScale * zoom)
            AddIssue("このDPIと倍率では描画解像度の上限に達しました。倍率を下げると最近傍表示を保てます。");
        rendered = rendered.ScaleNearest((int)Math.Round(rendered.Width * scale),
            (int)Math.Round(rendered.Height * scale));
        if (session.GraphicsImage is not { } bitmap || bitmap.PixelWidth != rendered.Width || bitmap.PixelHeight != rendered.Height)
            session.GraphicsImage = bitmap = new WriteableBitmap(rendered.Width, rendered.Height);
        using (var buffer = bitmap.PixelBuffer.AsStream()) buffer.Write(rendered.Pixels);
        bitmap.Invalidate();
        session.GraphicsKey = key with { Item4 = Revision };
        return bitmap;
    }
}
