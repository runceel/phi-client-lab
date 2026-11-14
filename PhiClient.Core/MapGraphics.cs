namespace PhiClient.Core;

public sealed class PixelImage
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Pixels { get; }

    public PixelImage(int width, int height, byte[] pixels)
    {
        if (width is <= 0 or > 2048 || height is <= 0 or > 2048 || pixels.Length != width * height * 4)
            throw new ArgumentException("Invalid BGRA image dimensions.");
        (Width, Height, Pixels) = (width, height, pixels);
    }

    public PixelImage Transparent(bool horizontalMask = false, bool verticalMask = false)
    {
        if (horizontalMask && Width != 1024 || horizontalMask && Height != 96 ||
            verticalMask && (Width < 256 || Height != 64) ||
            !horizontalMask && !verticalMask && (Width != 96 || Height < 128))
            throw new ArgumentException("Expected 1024x96 map chips, at least 256x64 items, or 96x128+ characters.");
        var width = horizontalMask ? Width / 2 : Width;
        var height = verticalMask ? Height / 2 : Height;
        var result = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var source = (y * Width + x) * 4;
            var target = (y * width + x) * 4;
            Pixels.AsSpan(source, 3).CopyTo(result.AsSpan(target, 3));
            var mask = horizontalMask ? source + width * 4 : source + height * Width * 4;
            result[target + 3] = horizontalMask || verticalMask
                ? (byte)(Pixels[mask] > 200 && Pixels[mask + 1] > 200 && Pixels[mask + 2] > 200 ? 255 : 0)
                : (byte)(Pixels[source] == 128 && Pixels[source + 1] == 128 && Pixels[source + 2] == 0 ? 0 : 255);
        }
        return new(width, height, result);
    }
}

public static class GraphicsNames
{
    public static bool Safe(string name) => !string.IsNullOrWhiteSpace(name) && name.Length <= 128 &&
        name is not "." and not ".." && !name.Any(c => c < 32 || "\\/:*?\"<>|".Contains(c)) &&
        !name.EndsWith('.') && !name.EndsWith(' ');

    public static string DefaultKey(int code) => code switch
    {
        0 => "人・銅像", 1 => "知的生物", 2 => "獣", 3 => "狂戦士", 4 => "魔法生物",
        5 => "アンデッド", 6 => "精神体", 7 => "イレイザー", 0x40 => "setcg使用キャラ",
        0x80 => "生物", 0x81 => "看板", 0x82 => "魔法エフェクト",
        0x70 => "アニメオブジェ", 0x75 => "アニメなしオブジェ", _ => "知的生物"
    };
}

public static class MapGraphics
{
    public static int[] Chips(byte tile) => (char)tile switch
    {
        ':' => [1], '+' => [2], 'T' => [1, 3], '_' => [4], 'o' => [5], '@' => [6], '#' => [7],
        'H' => [8], 'I' => [9], '=' => [10], '[' => [11], '{' => [12], '|' => [13], 'x' => [14],
        '%' => [15], '/' => [16], '>' => [17], '?' => [31], _ => [0]
    };

    public static (int X, int Y, int Width) CharacterFrame(Actor actor, int frame) =>
        (actor.Size == '*' ? 32 + (frame & 1) * 32 : (frame & 1) * 16,
            actor.Direction switch { 'B' => 0, 'R' => 32, 'L' => 96, _ => 64 },
            actor.Size == '*' ? 32 : 16);

    public static PixelImage Draw(int width, int height, byte[] tiles, byte[] attributes,
        IReadOnlyList<Actor> actors, Func<int, PixelImage?> chip, PixelImage? items,
        Func<Actor, PixelImage?> character, int frame = 0, bool eagle = false)
    {
        if (width is < 1 or > 15 || height is < 1 or > 15 ||
            tiles.Length != width * height || attributes.Length != tiles.Length)
            throw new ArgumentException("Invalid map graphics frame.");
        var canvas = new PixelImage(width * 32, height * 32 + 16, new byte[width * 32 * (height * 32 + 16) * 4]);
        for (var i = 3; i < canvas.Pixels.Length; i += 4) canvas.Pixels[i] = 255;
        void Copy(PixelImage image, int sx, int sy, int w, int h, int dx, int dy)
        {
            for (var y = 0; y < h; y++)
            for (var x = 0; x < w; x++)
            {
                if (sx + x < 0 || sx + x >= image.Width || sy + y < 0 || sy + y >= image.Height ||
                    dx + x < 0 || dx + x >= canvas.Width || dy + y < 0 || dy + y >= canvas.Height) continue;
                var src = ((sy + y) * image.Width + sx + x) * 4;
                if (image.Pixels[src + 3] == 0) continue;
                var dst = ((dy + y) * canvas.Width + dx + x) * 4;
                image.Pixels.AsSpan(src, 4).CopyTo(canvas.Pixels.AsSpan(dst, 4));
            }
        }
        void Tile(PixelImage sheet, int index, int x, int y) =>
            Copy(sheet, index % 16 * 32, index / 16 * 48, 32, 48, x * 32, y * 32);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var i = y * width + x;
                if (chip(attributes[i] & 3) is { } sheet)
                    foreach (var index in Chips(tiles[i])) Tile(sheet, index, x, y);
            }
            for (var x = 0; x < width; x++)
            {
                var i = y * width + x;
                if (chip(attributes[i] & 3) is not { } sheet) continue;
                if (tiles[i] == '_')
                {
                    bool Water(int dx, int dy) => x + dx >= 0 && x + dx < width && y + dy >= 0 &&
                        y + dy < height && tiles[(y + dy) * width + x + dx] == '_';
                    for (var q = 0; q < 4; q++)
                    {
                        var dx = (q & 1) == 0 ? -1 : 1;
                        var dy = q < 2 ? -1 : 1;
                        var pattern = (Water(dx, dy) ? 1 : 0) | (Water(0, dy) ? 2 : 0) | (Water(dx, 0) ? 4 : 0);
                        if (pattern == 7) continue;
                        var index = pattern < 2 ? 24 : pattern < 6 ? 23 : 25;
                        var quarter = pattern < 2 ? q : pattern < 4 ? q & 1 :
                            pattern < 6 ? (q < 2 ? 2 : 3) : 3 - q;
                        Copy(sheet, index % 16 * 32 + (quarter & 1) * 16,
                            index / 16 * 48 + 16 + (quarter / 2) * 16, 16, 16,
                            x * 32 + (q & 1) * 16, y * 32 + 16 + q / 2 * 16);
                    }
                }
                if ((attributes[i] & 8) != 0)
                    Tile(sheet, "@#H=[{|%".Contains((char)tiles[i]) ? 20 : 18, x, y);
            }
            for (var x = 0; x < width; x++)
            {
                var i = y * width + x;
                var no = (attributes[i] & 0x70) >> 4;
                if (no == 0 || eagle && attributes[i] == 0x58) continue;
                if (eagle && chip(attributes[i] & 3) is { } sheet) Tile(sheet, 19, x, y);
                else if (items is not null)
                {
                    var circle = tiles[i] is (byte)'x' or (byte)'%';
                    Copy(items, no * 32, circle && no is 5 or 6 ? 5 : 0, 32, circle ? 21 : 32, x * 32, y * 32 + 8);
                }
            }
            foreach (var group in actors.Where(a => a.Y == y && a.X >= 0 && a.X < width)
                .OrderBy(a => a.X).ThenBy(a => a.Layer).GroupBy(a => a.X))
            {
                var row = group.ToArray();
                if (row.Length == 2 && row.Count(a => a.Size == '*') == 1)
                    row = row.OrderBy(a => a.Size == '*' ? 0 : 1).ToArray();
                for (var i = 0; i < row.Length; i++)
                {
                    var actor = row[i];
                    var dx = row.Length != 2 || row.All(a => a.Size == '*') ? 0 :
                        row.All(a => a.Size != '*') ? (i == 0 ? -8 : 8) : actor.Size == '*' ? 0 : 8;
                    if (character(actor) is { } image)
                    {
                        var source = CharacterFrame(actor, actor.DefaultGraphic == 0x75 ? 0 : frame);
                        Copy(image, source.X, source.Y, source.Width, 32,
                            actor.X * 32 + (32 - source.Width) / 2 + dx, y * 32 + (eagle ? 16 : 5));
                    }
                    else
                    {
                        for (var py = 0; py < 12; py++)
                        for (var px = 0; px < 8; px++)
                        {
                            var dst = ((y * 32 + 20 + py) * canvas.Width + actor.X * 32 + 12 + px + dx) * 4;
                            if (dst >= 0 && dst + 3 < canvas.Pixels.Length)
                            {
                                canvas.Pixels[dst] = 0;
                                canvas.Pixels[dst + 1] = canvas.Pixels[dst + 2] = 255;
                            }
                        }
                    }
                }
            }
        }
        return canvas;
    }
}
