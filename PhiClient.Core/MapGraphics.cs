namespace PhiClient.Core;

public sealed class PixelImage
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Pixels { get; }

    public PixelImage(int width, int height, byte[] pixels)
    {
        if (width is <= 0 or > 4096 || height is <= 0 or > 4096 || pixels.Length != width * height * 4)
            throw new ArgumentException("Invalid BGRA image dimensions.");
        (Width, Height, Pixels) = (width, height, pixels);
    }

    public PixelImage ScaleNearest(int factor)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(factor, 1);
        return ScaleNearest(checked(Width * factor), checked(Height * factor));
    }

    public PixelImage Crop(int x, int y, int width, int height)
    {
        if (x < 0 || y < 0 || width <= 0 || height <= 0 || x > Width - width || y > Height - height)
            throw new ArgumentOutOfRangeException(nameof(width), "Crop must fit inside the source image.");
        var pixels = new byte[width * height * 4];
        for (var row = 0; row < height; row++)
            Pixels.AsSpan(((y + row) * Width + x) * 4, width * 4).CopyTo(pixels.AsSpan(row * width * 4));
        return new(width, height, pixels);
    }

    public PixelImage ScaleNearest(int width, int height)
    {
        if (width is < 1 or > 4096 || height is < 1 or > 4096)
            throw new ArgumentOutOfRangeException(nameof(width), "Scaled image must fit 4096 pixels per side.");
        if (width == Width && height == Height) return this;
        var pixels = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
            Pixels.AsSpan(((y * Height / height) * Width + x * Width / width) * 4, 4)
                .CopyTo(pixels.AsSpan((y * width + x) * 4, 4));
        return new(width, height, pixels);
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
    public static bool Allowed(string name) => Safe(name) && !name.StartsWith("LANU_", StringComparison.OrdinalIgnoreCase);
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
    public static IReadOnlyList<(Actor Actor, int Offset)> ArrangeRow(IEnumerable<Actor> actors) =>
        actors.OrderBy(actor => actor.X).ThenBy(actor => actor.Layer).GroupBy(actor => actor.X)
            .SelectMany(group =>
            {
                var row = group.ToArray();
                if (row.Length == 2 && row.Count(actor => actor.Size == '*') == 1)
                    row = row.OrderBy(actor => actor.Size == '*' ? 0 : 1).ToArray();
                return row.Select((actor, index) => (actor, row.Length != 2 || row.All(a => a.Size == '*') ? 0 :
                    row.All(a => a.Size != '*') ? (index == 0 ? -8 : 8) : actor.Size == '*' ? 0 : 8));
            }).ToArray();

    public static (double X, double Y, long Width, long Height) MagnifiedDestination(double x, double y, Magnify size) =>
        (x - Math.Floor(((double)size.Width - 32) / 2),
            y - Math.Floor(((double)size.Height - 32) * 5 / 6) - size.Z, size.Width, size.Height);

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
        Func<Actor, PixelImage?> character, int frame = 0, bool eagle = false, bool[]? recalled = null,
        int slideX = 0, int slideY = 0)
    {
        if (width is < 1 or > 15 || height is < 1 or > 15 ||
            tiles.Length != 0 && tiles.Length != width * height || attributes.Length != tiles.Length)
            throw new ArgumentException("Invalid map graphics frame.");
        if (recalled is { Length: > 0 } && recalled.Length != width * height)
            throw new ArgumentException("Invalid recalled map cells.", nameof(recalled));
        var top = eagle ? 0 : 16;
        var canvas = new PixelImage(width * 32, height * 32 + top, new byte[width * 32 * (height * 32 + top) * 4]);
        for (var i = top * canvas.Width * 4 + 3; i < canvas.Pixels.Length; i += 4) canvas.Pixels[i] = 255;
        void Copy(PixelImage image, int sx, int sy, int w, int h, int dx, int dy)
        {
            dx += slideX;
            dy += slideY;
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
        void CopyScaled(PixelImage image, int sx, int sy, int sw, int sh,
            (double X, double Y, long Width, long Height) destination)
        {
            var (dx, dy, dw, dh) = destination;
            if (dw == 0 || dh == 0) return;
            if (dw < 0) dx += dw;
            if (dh < 0) dy += dh;
            var w = Math.Abs((double)dw);
            var h = Math.Abs((double)dh);
            var left = (int)Math.Clamp(Math.Ceiling(dx + slideX), 0, canvas.Width);
            var right = (int)Math.Clamp(Math.Ceiling(dx + w + slideX), 0, canvas.Width);
            var first = (int)Math.Clamp(Math.Ceiling(dy + slideY), 0, canvas.Height);
            var last = (int)Math.Clamp(Math.Ceiling(dy + h + slideY), 0, canvas.Height);
            for (var y = first; y < last; y++)
            for (var x = left; x < right; x++)
                Copy(image, sx + (int)((x - slideX - dx) * sw / w), sy + (int)((y - slideY - dy) * sh / h),
                    1, 1, x - slideX, y - slideY);
        }
        void Tile(PixelImage sheet, int index, int x, int y) =>
            Copy(sheet, index % 16 * 32, index / 16 * 48, 32, 48, x * 32, y * 32 + top - 16);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; tiles.Length > 0 && x < width; x++)
            {
                var i = y * width + x;
                if (chip(eagle ? 0 : attributes[i] & 3) is { } sheet)
                    foreach (var index in Chips(tiles[i])) Tile(sheet, index, x, y);
            }
            for (var x = 0; tiles.Length > 0 && x < width; x++)
            {
                var i = y * width + x;
                if (chip(eagle ? 0 : attributes[i] & 3) is not { } sheet) continue;
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
                            x * 32 + (q & 1) * 16, y * 32 + top + q / 2 * 16);
                    }
                }
                if ((attributes[i] & 8) != 0)
                    Tile(sheet, "@#H=[{|%".Contains((char)tiles[i]) ? 20 : 18, x, y);
            }
            for (var x = 0; tiles.Length > 0 && x < width; x++)
            {
                var i = y * width + x;
                var no = (attributes[i] & 0x70) >> 4;
                if (no == 0) continue;
                if (eagle && chip(0) is { } sheet) Tile(sheet, 19, x, y);
                else if (items is not null)
                {
                    var circle = tiles[i] is (byte)'x' or (byte)'%';
                    Copy(items, no * 32, circle && no is 5 or 6 ? 5 : 0, 32, circle ? 21 : 32, x * 32, y * 32 + top - 8);
                }
            }
            foreach (var (actor, dx) in ArrangeRow(actors.Where(a => a.Y == y && a.X >= 0 && a.X < width)))
            {
                if (character(actor) is { } image)
                {
                    var source = CharacterFrame(actor, eagle ? 0 : frame);
                    var x = actor.X * 32 + (32 - source.Width) / 2 + dx;
                    var destY = y * 32 + top - (eagle ? 0 : 11);
                    if (actor.Magnify is { } magnify)
                        CopyScaled(image, source.X, source.Y, source.Width, 32, MagnifiedDestination(x, destY, magnify));
                    else
                        Copy(image, source.X, source.Y, source.Width, 32,
                            x, destY);
                }
            }
        }
        if (recalled is not null)
            for (var i = 0; i < recalled.Length; i++)
            {
                if (!recalled[i]) continue;
                for (var y = 0; y < 32; y++)
                for (var x = 0; x < 32; x++)
                {
                    var dx = i % width * 32 + x + slideX;
                    var dy = i / width * 32 + top + y + slideY;
                    if (dx < 0 || dx >= canvas.Width || dy < 0 || dy >= canvas.Height) continue;
                    var pixel = (dy * canvas.Width + dx) * 4;
                    for (var channel = 0; channel < 3; channel++)
                        canvas.Pixels[pixel + channel] = (byte)(canvas.Pixels[pixel + channel] * 3 / 4);
                }
            }
        return canvas;
    }
}
