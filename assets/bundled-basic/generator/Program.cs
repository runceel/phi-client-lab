using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using PhiClient.Core;

// All shapes and palette choices below are authored here, with no image inputs.
var root = Path.GetFullPath(args.Length == 0 ? Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..") : args[0]);
Directory.CreateDirectory(root);
var pack = new Pack(root);
pack.Generate();
pack.Validate();
Console.WriteLine("PASS: original sheets, BMP decode, masks, keys, frames, water quadrants, seams and renderer previews.");

sealed class Art(int width, int height)
{
    public int W { get; } = width;
    public int H { get; } = height;
    public byte[] P { get; } = new byte[width * height * 4];
    public PixelImage Image => new(W, H, (byte[])P.Clone());
    public static uint C(string hex) => Convert.ToUInt32(hex, 16) | 0xff000000;
    public void Dot(int x, int y, uint c)
    {
        if (x < 0 || y < 0 || x >= W || y >= H) return;
        var i = (y * W + x) * 4;
        P[i] = (byte)c; P[i + 1] = (byte)(c >> 8); P[i + 2] = (byte)(c >> 16); P[i + 3] = (byte)(c >> 24);
    }
    public uint At(int x, int y)
    {
        var i = (y * W + x) * 4;
        return (uint)(P[i] | P[i + 1] << 8 | P[i + 2] << 16 | P[i + 3] << 24);
    }
    public void Rect(int x, int y, int w, int h, uint c)
    {
        for (var yy = y; yy < y + h; yy++) for (var xx = x; xx < x + w; xx++) Dot(xx, yy, c);
    }
    public void Line(int x, int y, int xx, int yy, uint c)
    {
        var dx = Math.Abs(xx - x); var sx = x < xx ? 1 : -1;
        var dy = -Math.Abs(yy - y); var sy = y < yy ? 1 : -1; var e = dx + dy;
        while (true)
        {
            Dot(x, y, c); if (x == xx && y == yy) break;
            var e2 = e * 2;
            if (e2 >= dy) { e += dy; x += sx; }
            if (e2 <= dx) { e += dx; y += sy; }
        }
    }
    public void Ellipse(int x, int y, int w, int h, uint c)
    {
        for (var yy = 0; yy < h; yy++) for (var xx = 0; xx < w; xx++)
            if (Math.Pow((xx + .5 - w / 2.0) / (w / 2.0), 2) + Math.Pow((yy + .5 - h / 2.0) / (h / 2.0), 2) <= 1)
                Dot(x + xx, y + yy, c);
    }
    public void Blit(Art a, int x, int y)
    {
        for (var yy = 0; yy < a.H; yy++) for (var xx = 0; xx < a.W; xx++)
            if (a.P[(yy * a.W + xx) * 4 + 3] != 0) Dot(x + xx, y + yy, a.At(xx, yy));
    }
    public Art Crop(int x, int y, int w, int h) => From(Image.Crop(x, y, w, h));
    public static Art From(PixelImage im)
    {
        var a = new Art(im.Width, im.Height); im.Pixels.CopyTo(a.P, 0); return a;
    }
    public void Png(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var s = File.Create(path);
        s.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        void Chunk(string type, byte[] data)
        {
            static byte[] Big(uint v) => [(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v];
            s.Write(Big((uint)data.Length));
            var bytes = Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
            s.Write(bytes);
            uint crc = 0xffffffff;
            foreach (var b in bytes)
            {
                crc ^= b;
                for (var i = 0; i < 8; i++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0);
            }
            s.Write(Big(~crc));
        }
        byte[] header = [(byte)(W >> 24), (byte)(W >> 16), (byte)(W >> 8), (byte)W,
            (byte)(H >> 24), (byte)(H >> 16), (byte)(H >> 8), (byte)H, 8, 6, 0, 0, 0];
        Chunk("IHDR", header);
        using var payload = new MemoryStream();
        using (var z = new ZLibStream(payload, CompressionLevel.SmallestSize, true))
            for (var y = 0; y < H; y++)
            {
                z.WriteByte(0);
                for (var x = 0; x < W; x++)
                {
                    var i = (y * W + x) * 4;
                    z.WriteByte(P[i + 2]); z.WriteByte(P[i + 1]); z.WriteByte(P[i]); z.WriteByte(P[i + 3]);
                }
            }
        Chunk("IDAT", payload.ToArray()); Chunk("IEND", []);
    }
    public void Bmp(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var b = new BinaryWriter(File.Create(path));
        var stride = (W * 3 + 3) & ~3;
        b.Write((ushort)0x4d42); b.Write(54 + stride * H); b.Write(0); b.Write(54);
        b.Write(40); b.Write(W); b.Write(H); b.Write((ushort)1); b.Write((ushort)24);
        b.Write(0); b.Write(stride * H); b.Write(2835); b.Write(2835); b.Write(0); b.Write(0);
        for (var y = H - 1; y >= 0; y--)
        {
            for (var x = 0; x < W; x++) b.Write(P, (y * W + x) * 4, 3);
            for (var p = W * 3; p < stride; p++) b.Write((byte)0);
        }
    }
}

sealed class Pack(string root)
{
    static uint C(string s) => Art.C(s);
    static readonly uint Ink = C("303532"), Deep = C("41483e"), Moss = C("647756"), Leaf = C("809069"),
        LightLeaf = C("a0aa7c"), Soil = C("a29a7a"), Sand = C("bcb08b"), Pale = C("d6c9a0"),
        Stone = C("888b83"), StoneLight = C("b1b1a0"), Wood = C("796149"), WoodLight = C("aa8c61"),
        Rust = C("916757"), Water = C("536f80"), WaterLight = C("78969d"), Cloth = C("5f7880"),
        Skin = C("c6a37d"), SkinLight = C("e0c29a"), Bone = C("d0c9aa");
    readonly Dictionary<string, Art> sheets = [];
    readonly List<object> files = [];
    readonly string[] kinds = ["person", "wanderer", "beast", "berserker", "golem", "skeleton", "spirit",
        "eraser", "sign", "magic", "animated-object", "object"];
    static readonly (int Code, string Key, string Sheet)[] Defaults =
    [
        (0,"人・銅像","person"), (1,"知的生物","wanderer"), (2,"獣","beast"), (3,"狂戦士","berserker"),
        (4,"魔法生物","golem"), (5,"アンデッド","skeleton"), (6,"精神体","spirit"), (7,"イレイザー","eraser"),
        (0x40,"setcg使用キャラ","person"), (0x80,"生物","wanderer"), (0x81,"看板","sign"),
        (0x82,"魔法エフェクト","magic"), (0x70,"アニメオブジェ","animated-object"), (0x75,"アニメなしオブジェ","object")
    ];
    static readonly string[] TileNames = ["earth", "grass", "flowers", "tree-overlay", "water", "paving",
        "rock", "masonry", "fence", "ice-pillar", "timber-door", "iron-door", "arched-window", "chest",
        "open-chest", "lattice", "stairs", "rune-circle", "sign-ground", "eagle-item", "sign-wall",
        "reserved-earth", "reserved-earth", "shore-edge", "shore-corner", "shore-inset", "reserved-earth",
        "reserved-earth", "reserved-earth", "reserved-earth", "reserved-earth", "unknown-fog"];
    string PathFor(string name) => Path.Combine(root, name.Replace('/', Path.DirectorySeparatorChar));
    static int Noise(int x, int y, int seed)
    {
        var h = (uint)(x * 374761393 + y * 668265263 + seed * 144269);
        h = (h ^ h >> 13) * 1274126177u;
        return (int)((h ^ h >> 16) >> 24);
    }
    static Art Ground(int kind)
    {
        var a = new Art(32, 48);
        for (var y = 0; y < 32; y++) for (var x = 0; x < 32; x++)
        {
            // Periodic texture fixes both opposite edges exactly, without a cell border.
            var n = Noise(x % 31, y % 31, kind);
            var color = kind switch
            {
                1 => n < 14 ? Soil : n < 40 ? Moss : n < 80 ? Leaf : n > 245 ? LightLeaf : C("8c976f"),
                4 => n < 22 ? C("4c6879") : n > 230 ? C("648493") : Water,
                31 => n < 45 ? C("4b514c") : n > 225 ? C("5a6058") : C("535a52"),
                _ => n < 30 ? Soil : n > 225 ? Pale : Sand
            };
            a.Dot(x, y + 16, color);
        }
        if (kind == 4)
            foreach (var (x, y) in new[] { (3, 5), (18, 13), (8, 25), (24, 28) })
            { a.Line(x, y + 16, x + 5, y + 16, WaterLight); a.Dot(x + 6, y + 15, C("65868e")); }
        return a;
    }
    static void Chest(Art a, int x, int y, bool open)
    {
        a.Ellipse(x - 1, y + 12, 24, 6, Deep);
        a.Rect(x, y + 1, 23, 15, Ink); a.Rect(x + 1, y + 2, 21, 12, Wood);
        a.Line(x + 2, y + 8, x + 20, y + 8, WoodLight);
        if (open) { a.Rect(x + 2, y - 5, 19, 8, Ink); a.Rect(x + 3, y - 4, 17, 5, Rust); a.Rect(x + 3, y + 3, 17, 5, Deep); }
        else { a.Line(x + 2, y + 2, x + 20, y + 2, WoodLight); a.Rect(x + 3, y + 5, 17, 3, C("8d7351")); }
        foreach (var dx in new[] { 3, 18 }) a.Rect(x + dx, y + 1, 2, 13, StoneLight);
        a.Rect(x + 10, y + 7, 3, 4, Pale); a.Dot(x + 11, y + 9, Ink);
    }
    static void Sign(Art a, int x, int y, bool wall)
    {
        if (!wall) { a.Rect(x + 9, y + 10, 3, 15, Ink); a.Rect(x + 10, y + 11, 1, 13, WoodLight); }
        a.Rect(x, y, 23, 14, Ink); a.Rect(x + 1, y + 1, 21, 11, WoodLight);
        a.Line(x + 2, y + 2, x + 20, y + 2, Pale);
        a.Line(x + 4, y + 5, x + 17, y + 5, Wood);
        a.Line(x + 4, y + 8, x + 14, y + 8, Wood);
    }
    static Art Chip(int index)
    {
        var a = Ground(index == 1 || index == 2 ? 1 : index == 4 ? 4 : index == 31 ? 31 : 0);
        if (index is 3 or 18 or 19 or 20 or 23 or 24 or 25) a = new Art(32, 48);
        switch (index)
        {
            case 2:
                foreach (var (x, y) in new[] { (6, 25), (22, 21), (16, 37), (27, 40) })
                { a.Line(x, y, x, y + 4, Moss); a.Dot(x - 1, y, Pale); a.Dot(x + 1, y, Pale); a.Dot(x, y - 1, Bone); a.Dot(x, y + 1, Bone); a.Dot(x, y, WoodLight); }
                break;
            case 3:
                a.Ellipse(4, 37, 25, 8, Moss); a.Rect(14, 21, 5, 21, Ink); a.Rect(15, 23, 3, 18, Wood);
                a.Line(16, 32, 10, 24, WoodLight); a.Line(17, 26, 22, 20, Wood);
                a.Ellipse(2, 6, 28, 27, Deep); a.Ellipse(3, 4, 25, 25, Moss);
                a.Ellipse(4, 5, 17, 17, Leaf); a.Ellipse(9, 2, 15, 14, Leaf);
                foreach (var (x, y) in new[] { (8, 9), (15, 6), (19, 17), (7, 21), (14, 23), (22, 12) })
                { a.Line(x, y, x + 3, y, LightLeaf); a.Dot(x - 1, y + 2, Leaf); }
                break;
            case 5:
            case 7:
                for (var y = index == 7 ? 0 : 16; y < 48; y++)
                for (var x = 0; x < 32; x++)
                {
                    var row = y / 8; var xx = (x + (row % 2) * 8) % 16;
                    a.Dot(x, y, y % 8 == 7 || xx == 15 ? C("696d64") : y % 8 == 0 || xx == 0 ? StoneLight :
                        Noise(x, y, 5) > 210 ? C("969a8d") : Stone);
                }
                if (index == 7) { a.Line(0, 0, 31, 0, Pale); a.Rect(0, 44, 32, 4, Deep); }
                break;
            case 6:
                a.Ellipse(2, 37, 28, 8, Soil);
                for (var y = 9; y < 42; y++) for (var x = 4; x < 29; x++)
                {
                    if (Math.Abs(x - 15) > (y < 23 ? y / 2 - 1 : 13 - (y - 23) / 8)) continue;
                    a.Dot(x, y, x < 13 ? StoneLight : x < 20 ? Stone : Deep);
                }
                a.Line(15, 11, 10, 28, Pale); a.Line(18, 21, 22, 38, C("6c746c")); break;
            case 8:
                a.Line(0, 25, 31, 25, Ink); a.Line(0, 32, 31, 32, Ink);
                a.Line(0, 24, 31, 24, WoodLight); a.Line(0, 31, 31, 31, Wood);
                foreach (var x in new[] { 4, 25 })
                { a.Rect(x, 15, 4, 28, Ink); a.Rect(x + 1, 16, 2, 25, Wood); a.Line(x, 15, x + 3, 15, WoodLight); }
                break;
            case 9:
                a.Ellipse(5, 38, 22, 6, Soil); a.Rect(8, 7, 17, 33, C("506e7e"));
                a.Rect(9, 5, 15, 34, WaterLight); a.Rect(11, 7, 5, 31, C("b8ced0"));
                a.Line(12, 4, 22, 4, Bone); a.Line(18, 8, 21, 34, C("91b5bd")); break;
            case 10:
            case 11:
            case 12:
                a = Chip(7);
                a.Rect(3, 7, 26, 36, Ink); a.Rect(5, 8, 22, 33, Wood);
                if (index == 12)
                {
                    a.Rect(6, 10, 20, 29, StoneLight); a.Ellipse(8, 10, 16, 16, Deep); a.Rect(8, 18, 16, 18, Deep);
                    a.Line(15, 11, 15, 35, Pale); a.Line(8, 23, 23, 23, Pale); a.Line(8, 35, 23, 35, Pale);
                }
                else
                {
                    for (var x = 7; x < 27; x += 5) a.Line(x, 10, x, 38, WoodLight);
                    a.Rect(15, 8, 2, 33, Ink); a.Rect(5, 15, 22, 2, index == 11 ? Stone : Deep);
                    a.Rect(5, 32, 22, 2, index == 11 ? Stone : Deep);
                    a.Dot(13, 26, Pale); a.Dot(18, 26, Pale);
                    if (index == 11) { a.Ellipse(10, 19, 12, 11, Stone); a.Line(15, 19, 15, 28, Pale); }
                }
                break;
            case 13: Chest(a, 5, 28, false); break;
            case 14: Chest(a, 5, 28, true); break;
            case 15:
                a = Chip(7); a.Rect(3, 8, 26, 35, Ink);
                for (var x = 6; x < 28; x += 5) { a.Line(x, 10, x, 41, Stone); a.Line(x + 1, 10, x + 1, 41, StoneLight); }
                a.Line(4, 19, 27, 19, Deep); a.Line(4, 34, 27, 34, Deep); break;
            case 16:
                for (var y = 18; y < 48; y += 6)
                { a.Rect(0, y, 32, 5, Stone); a.Line(0, y, 31, y, StoneLight); a.Line(0, y + 5, 31, y + 5, Deep); }
                break;
            case 17:
                a.Ellipse(1, 17, 30, 30, Wood); a.Ellipse(3, 19, 26, 26, Pale); a.Ellipse(4, 20, 24, 24, Soil);
                a.Line(8, 24, 24, 39, Wood); a.Line(24, 24, 8, 39, Wood); a.Line(6, 31, 26, 31, Wood);
                a.Ellipse(11, 27, 10, 10, Pale); a.Ellipse(13, 29, 6, 6, Soil); break;
            case 18: Sign(a, 5, 18, false); break;
            case 19: a.Ellipse(11, 29, 10, 7, Deep); a.Ellipse(10, 25, 10, 9, Rust); a.Dot(12, 27, Pale); break;
            case 20: Sign(a, 5, 10, true); break;
            case 23:
            case 24:
            case 25:
                // MapGraphics selects/reflects authored 16x16 shoreline quadrants.
                for (var q = 0; q < 4; q++)
                for (var y = 0; y < 16; y++) for (var x = 0; x < 16; x++)
                {
                    var xx = (q & 1) == 0 ? x : 15 - x; var yy = q < 2 ? y : 15 - y;
                    var d = index == 23 ? q switch { 0 => x, 1 => 15 - x, 2 => y, _ => 15 - y } :
                        index == 24 ? Math.Min(xx, yy) :
                        (int)Math.Sqrt((15 - xx) * (15 - xx) + (15 - yy) * (15 - yy));
                    if (d > 3) continue;
                    a.Dot((q & 1) * 16 + x, 16 + q / 2 * 16 + y,
                        d == 3 ? WaterLight : d == 2 ? Soil : Sand);
                }
                break;
        }
        return a;
    }
    static Art ActorFrame(string kind, int width, int dir, int frame)
    {
        if (width == 16 && kind is "sign" or "object" or "animated-object")
            return Art.From(ActorFrame(kind, 32, dir, frame).Image.ScaleNearest(16, 32));
        var a = new Art(width, 32); var cx = width / 2; var step = frame == 0 ? -1 : 1;
        if (kind is "sign") { Sign(a, cx - 7, 9, false); return a; }
        if (kind is "object" or "animated-object") { Chest(a, cx - 11, 14, kind == "animated-object" && frame == 1); return a; }
        if (kind == "magic")
        {
            a.Ellipse(cx - 7, 23, 14, 6, Water); a.Ellipse(cx - 5, 24, 10, 3, WaterLight);
            for (var i = 0; i < 4; i++)
            { var x = cx + (i % 2 == 0 ? -4 : 4); var y = 5 + i * 5 + frame; a.Line(cx, y - 2, x, y + 3, Bone); }
            a.Ellipse(cx - 2, 10 + frame, 5, 10, WaterLight); return a;
        }
        if (kind == "eraser")
        {
            a.Ellipse(cx - 7, 18 + frame, 15, 12 - frame, Ink); a.Ellipse(cx - 6, 18 + frame, 13, 9, C("65747a"));
            a.Ellipse(cx - 4, 19 + frame, 6, 4, WaterLight);
            if (dir != 0) { a.Dot(cx + (dir == 1 ? 3 : -2), 24, Pale); a.Dot(cx + 2, 24, Pale); } return a;
        }
        if (kind == "beast")
        {
            var side = dir is 1 or 3;
            a.Ellipse(cx - 7, 12, 14, 13, Ink); a.Ellipse(cx - 6, 13, 12, 10, C("8c7f64"));
            foreach (var dx in new[] { -5, 3 }) { a.Rect(cx + dx, 22, 3, 6 + (dx < 0 ? step : -step), Deep); }
            var hx = cx + (dir == 1 ? 3 : dir == 3 ? -5 : -1);
            a.Ellipse(hx - 4, dir == 0 ? 8 : 10, 9, 10, Ink); a.Ellipse(hx - 3, 11, 7, 6, WoodLight);
            a.Line(hx - 3, 12, hx - 4, 7, Deep); a.Line(hx + 3, 12, hx + 3, 7, Deep);
            if (dir != 0) { a.Dot(hx - 1, 13, Ink); a.Dot(hx + 2, 13, Ink); a.Dot(hx + (side ? (dir == 1 ? 4 : -3) : 0), 16, Ink); }
            a.Line(cx - (dir == 1 ? 6 : -5), 17, cx - (dir == 1 ? 9 : -8), 11 + frame, Wood);
            return a;
        }
        if (kind == "spirit")
        {
            a.Ellipse(cx - 6, 6 + frame, 12, 15, Ink); a.Ellipse(cx - 5, 7 + frame, 10, 16, C("b6beb2"));
            for (var y = 18; y < 28; y++) a.Rect(cx - 5 + (y - 18) / 3, y, 10 - (y - 18) / 2, 1, C("91a59d"));
            a.Line(cx - 4, 26, cx - 6 + frame, 29, C("b6beb2"));
            if (dir != 0) { a.Dot(cx - 2, 12 + frame, Deep); a.Dot(cx + 2, 12 + frame, Deep); }
            return a;
        }
        var golem = kind == "golem"; var skel = kind == "skeleton"; var warrior = kind == "berserker";
        var body = golem ? Stone : skel ? Bone : warrior ? Rust : kind == "wanderer" ? Moss : Cloth;
        var half = golem ? Math.Min(cx - 1, 8) : warrior ? width == 16 ? 4 : 6 : 5;
        a.Ellipse(cx - half, 27, half * 2 + 1, 4, Deep);
        a.Rect(cx - 4, 23, 3, 6 + step, Ink); a.Rect(cx + 1, 23, 3, 6 - step, Ink);
        a.Rect(cx - 4, 24, 2, 3 + step, skel ? Bone : Wood); a.Rect(cx + 1, 24, 2, 3 - step, skel ? Bone : Wood);
        a.Rect(cx - half, 13, half * 2 + 1, 12, Ink); a.Rect(cx - half + 1, 14, half * 2 - 1, 10, body);
        a.Line(cx - half + 1, 15, cx - half + 1, 22, golem ? StoneLight : skel ? Pale : C("8b9b8a"));
        a.Rect(cx - half - 1, 15 + frame, 2, 8, Ink); a.Rect(cx + half, 16 - frame, 2, 8, Ink);
        a.Dot(cx - half - 1, 23 + frame, golem ? StoneLight : skel ? Bone : Skin);
        a.Dot(cx + half, 24 - frame, golem ? StoneLight : skel ? Bone : Skin);
        if (golem)
        { a.Rect(cx - 5, 3, 10, 11, Ink); a.Rect(cx - 4, 4, 8, 9, Stone); }
        else
        { a.Ellipse(cx - 5, 3, 10, 11, Ink); a.Ellipse(cx - 4, 4, 8, 9, skel ? Bone : Skin); }
        a.Line(cx - 3, 4, cx + 2, 4, golem ? StoneLight : skel ? Pale : SkinLight);
        if (!skel && !golem)
        {
            a.Rect(cx - 4, 3, 8, 3, warrior ? Stone : Wood); a.Rect(cx - 5, 5, 2, dir == 0 ? 8 : 5, Wood);
            if (dir == 0) a.Rect(cx - 3, 6, 6, 6, Wood);
            if (dir is 1 or 3)
            {
                var side = dir == 1 ? 1 : -1;
                a.Rect(cx - side * 3 - 1, 5, 2, 6, Wood);
                a.Dot(cx + side * 4, 9, SkinLight);
            }
            if (dir == 2) { a.Rect(cx - 4, 5, 2, 3, Wood); a.Rect(cx + 2, 5, 2, 3, Wood); }
            if (kind == "wanderer") { a.Line(cx - 5, 4, cx, 1, Moss); a.Line(cx, 1, cx + 4, 4, Moss); }
        }
        if (dir != 0)
        {
            var shift = dir == 1 ? 2 : dir == 3 ? -2 : 0;
            a.Dot(cx - 2 + shift, 8, Ink); if (dir == 2) a.Dot(cx + 2, 8, Ink);
            a.Dot(cx + shift, 11, golem ? Deep : skel ? Deep : Wood);
        }
        if (dir == 0 && !skel && !golem) { a.Rect(cx - 3, 15, 7, 7, Wood); a.Rect(cx - 2, 16, 5, 4, WoodLight); }
        else a.Rect(cx - half + 1, 22, half * 2 - 1, 2, golem ? Deep : Wood);
        if (!golem && !skel && dir != 0)
        {
            a.Line(cx - 2, 14, cx + 2, 14, Pale);
            a.Line(cx, 15, cx + 1, 19, warrior ? WoodLight : C("98b0aa"));
            a.Dot(cx, 22, Pale);
        }
        if (skel) for (var y = 16; y < 22; y += 2) a.Line(cx - 3, y, cx + 3, y, Deep);
        if (warrior) { a.Line(cx + half + 2, 11, cx + half + 2, 27, WoodLight); a.Rect(cx + half, 9, 4, 5, StoneLight); }
        if (golem) { a.Line(cx, 15, cx - 2, 19, Deep); a.Line(cx - 2, 19, cx + 1, 22, Deep); }
        return a;
    }
    void Save(string name, Art art)
    {
        art.Png(PathFor(name + ".png")); sheets[name] = art;
        var legacy = name == "map" ? new Art(1024, 96) : name == "items" ? new Art(256, 64) : new Art(art.W, art.H);
        legacy.Rect(0, 0, legacy.W, legacy.H, C("000000"));
        if (name.StartsWith("actors/")) legacy.Rect(0, 0, legacy.W, legacy.H, C("008080"));
        legacy.Blit(art, 0, 0);
        if (name is "map" or "items")
            for (var y = 0; y < art.H; y++) for (var x = 0; x < art.W; x++)
                legacy.Dot(x + (name == "map" ? art.W : 0), y + (name == "items" ? art.H : 0),
                    art.P[(y * art.W + x) * 4 + 3] == 0 ? C("000000") : C("ffffff"));
        legacy.Bmp(PathFor(name + ".bmp"));
        foreach (var ext in new[] { ".png", ".bmp" })
            files.Add(new { path = name + ext, width = ext == ".png" ? art.W : legacy.W,
                height = ext == ".png" ? art.H : legacy.H,
                sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(PathFor(name + ext)))).ToLowerInvariant() });
    }
    public void Generate()
    {
        var map = new Art(512, 96);
        for (var i = 0; i < 32; i++) map.Blit(Chip(i), i % 16 * 32, i / 16 * 48);
        Save("map", map);
        foreach (var kind in kinds)
        {
            var actor = new Art(96, 128);
            for (var dir = 0; dir < 4; dir++) for (var f = 0; f < 2; f++)
            {
                actor.Blit(ActorFrame(kind, 16, dir, f), f * 16, dir * 32);
                actor.Blit(ActorFrame(kind, 32, dir, f), 32 + f * 32, dir * 32);
            }
            Save("actors/" + kind, actor);
        }
        var items = new Art(256, 32);
        for (var i = 1; i < 8; i++)
        {
            var a = new Art(32, 32);
            switch (i)
            {
                case 1:
                    foreach (var (x, y) in new[] { (9, 18), (15, 21), (19, 16) })
                    { a.Ellipse(x - 1, y - 1, 8, 5, Wood); a.Ellipse(x, y - 1, 6, 3, Pale); a.Line(x + 1, y + 2, x + 5, y + 2, WoodLight); } break;
                case 2: a.Line(9, 23, 24, 7, Ink); a.Line(10, 23, 24, 8, StoneLight); a.Line(9, 17, 16, 23, WoodLight); a.Line(7, 25, 11, 21, Wood); break;
                case 3: a.Ellipse(7, 9, 18, 17, Ink); a.Ellipse(9, 10, 14, 14, Cloth); a.Line(16, 10, 16, 23, StoneLight); a.Line(10, 16, 22, 16, StoneLight); break;
                case 4: a.Ellipse(10, 12, 13, 13, Wood); a.Ellipse(12, 14, 9, 8, Pale); a.Ellipse(14, 15, 5, 5, Soil); a.Dot(16, 12, WaterLight); break;
                case 5: a.Ellipse(11, 12, 11, 12, Ink); a.Ellipse(12, 12, 9, 9, Water); a.Line(14, 13, 17, 13, WaterLight); break;
                case 6: a.Ellipse(11, 12, 11, 12, Ink); a.Ellipse(12, 12, 9, 9, Rust); a.Dot(14, 14, Pale); break;
                case 7: a.Ellipse(8, 12, 17, 15, Wood); a.Ellipse(10, 13, 13, 11, WoodLight); a.Rect(13, 8, 6, 5, Wood); a.Line(12, 12, 20, 12, Pale); break;
            }
            items.Blit(a, i * 32, 0);
        }
        Save("items", items);
        WritePreview();
        var manifest = new
        {
            schemaVersion = 1, id = "phi-original-basic-v1", source = "Original deterministic pixel art; generator has no image inputs.",
            license = "See LICENSE.txt; no repository-wide license was found.",
            cellSize = 32, normalTopPadding = 16, eagleTopPadding = 0, alpha = "binary straight alpha; no premultiplication",
            png = new { map = "map.png", items = "items.png", characters = "actors/{sheet}.png",
                decoding = "BGRA8 straight; use directly as PixelImage, do NOT call Transparent()" },
            bmp = new { map = "map.bmp", items = "items.bmp", characters = "actors/{sheet}.bmp",
                decoding = "24-bit RGB; use existing Transparent(horizontalMask:true), Transparent(verticalMask:true), Transparent() respectively" },
            mapLayout = new { columns = 16, rows = 2, frameWidth = 32, frameHeight = 48, groundY = 16,
                slots = TileNames.Select((name, index) => new { index, name }).ToArray(),
                terrain = new Dictionary<string, int[]> { ["default"] = [0], [":"] = [1], ["+"] = [2], ["T"] = [1,3], ["_"] = [4],
                    ["o"] = [5], ["@"] = [6], ["#"] = [7], ["H"] = [8], ["I"] = [9], ["="] = [10], ["["] = [11],
                    ["{"] = [12], ["|"] = [13], ["x"] = [14], ["%"] = [15], ["/"] = [16], [">"] = [17], ["?"] = [31] } },
            actorLayout = new { width = 96, height = 128, directions = new[] { "B", "R", "F", "L" }, rowHeight = 32,
                smallFrameX = new[] { 0, 16 }, smallFrameWidth = 16, largeFrameX = new[] { 32,64 }, largeFrameWidth = 32, frames = 2 },
            defaults = Defaults.Select(d => new { code = d.Code, key = d.Key, sheet = d.Sheet }).ToArray(),
            unknownActorSheet = "wanderer", unknownTerrainSlot = 0, unseenTerrainSlot = 31,
            preview = new { contactSheet = "contact-sheet.png", scene = "scene-7x7.png", sceneFrame1 = "scene-7x7-frame1.png",
                sceneNative = "scene-7x7-native.png", sceneDescription = "Synthetic renderer fixture, not a live game screenshot." },
            files
        };
        File.WriteAllText(PathFor("manifest.json"), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }) + "\n", new UTF8Encoding(false));
    }
    void WritePreview()
    {
        // Labels are supplied in the adjacent manifest/contact index, never rasterized into game art.
        var contact = new Art(768, 672); contact.Rect(0, 0, contact.W, contact.H, C("252d2a"));
        for (var i = 0; i < 32; i++)
        {
            var cell = sheets["map"].Crop(i % 16 * 32, i / 16 * 48, 32, 48);
            contact.Blit(Art.From(cell.Image.ScaleNearest(2)), 16 + i % 8 * 94, 12 + i / 8 * 106);
        }
        for (var i = 0; i < kinds.Length; i++)
        {
            var a = sheets["actors/" + kinds[i]];
            for (var dir = 0; dir < 4; dir++)
                contact.Blit(Art.From(a.Crop(32, dir * 32, 32, 32).Image.ScaleNearest(1)), 16 + i % 6 * 124 + dir * 28, 448 + i / 6 * 86);
            contact.Blit(Art.From(a.Crop(64, 64, 32, 32).Image.ScaleNearest(1)), 52 + i % 6 * 124, 480 + i / 6 * 86);
        }
        contact.Blit(sheets["items"], 16, 632); contact.Png(PathFor("contact-sheet.png"));
        var lines = new[] { "TT:::__", "T:+::__", "::ooH__", "#=ooo__", "#[o>o::", "#{oo|::", "??/oo::" };
        var tiles = Encoding.ASCII.GetBytes(string.Concat(lines)); var attrs = new byte[49];
        attrs[2 * 7 + 4] = 8; attrs[3 * 7 + 3] = 0x20; attrs[4 * 7 + 3] = 0x50;
        var actors = new[] { new Actor(3, 3, "Traveller", Direction: 'F', Size: '*', DefaultGraphic: 0),
            new Actor(4, 2, "Scout", Direction:'L', DefaultGraphic:1), new Actor(2, 1, "Beast", Direction:'R', Size:'*', DefaultGraphic:2),
            new Actor(5, 5, "Golem", Direction:'B', Size:'*', DefaultGraphic:4) };
        for (var frame = 0; frame < 2; frame++)
        {
            var scene = MapGraphics.Draw(7, 7, tiles, attrs, actors, _ => sheets["map"].Image, sheets["items"].Image,
                a => sheets["actors/" + Defaults.Single(d => d.Code == a.DefaultGraphic).Sheet].Image, frame);
            Art.From(scene.ScaleNearest(3)).Png(PathFor(frame == 0 ? "scene-7x7.png" : "scene-7x7-frame1.png"));
            if (frame == 0) Art.From(scene).Png(PathFor("scene-7x7-native.png"));
        }
    }
    public void Validate()
    {
        static void Check(bool value, string message) { if (!value) throw new InvalidDataException(message); }
        foreach (var (name, art) in sheets)
        {
            Check(art.P.Where((_, i) => i % 4 == 3).All(a => a is 0 or 255), name + " nonbinary alpha");
            Check(art.P.Where((_, i) => i % 4 == 3).Any(a => a == 255), name + " empty");
            var raw = ReadBmp(PathFor(name + ".bmp"));
            var decoded = raw.Transparent(name == "map", name == "items");
            Check(decoded.Width == art.W && decoded.Height == art.H, name + " dimensions");
            for (var i = 0; i < art.P.Length; i += 4)
            {
                Check(decoded.Pixels[i + 3] == art.P[i + 3], name + " alpha roundtrip");
                if (art.P[i + 3] != 0) Check(decoded.Pixels.AsSpan(i, 3).SequenceEqual(art.P.AsSpan(i, 3)), name + " RGB roundtrip");
            }
            if (name.StartsWith("actors/"))
            {
                Check(art.W == 96 && art.H == 128, name + " actor format");
                for (var dir = 0; dir < 4; dir++) for (var f = 0; f < 2; f++) foreach (var size in new[] { ' ', '*' })
                {
                    var fr = MapGraphics.CharacterFrame(new(0,0,"",Direction:"BRFL"[dir],Size:size), f);
                    var image = art.Image.Crop(fr.X, fr.Y, fr.Width, 32);
                    Check(image.Pixels.Where((_,i) => i % 4 == 3).Count(b => b != 0) >= 20, name + " empty frame");
                }
            }
        }
        Check(Defaults.All(d => GraphicsNames.DefaultKey(d.Code) == d.Key && sheets.ContainsKey("actors/" + d.Sheet)), "default actor contract");
        Check(GraphicsNames.DefaultKey(0x66) == "知的生物", "unknown actor contract");
        foreach (var i in new[] { 0, 1, 4, 31 })
        {
            var chip = Chip(i);
            for (var n = 0; n < 32; n++)
            {
                Check(chip.At(0, 16+n) == chip.At(31, 16+n), $"horizontal seam {i}");
                Check(chip.At(n, 16) == chip.At(n, 47), $"vertical seam {i}");
            }
        }
        foreach (var t in " :+T_o@#HI=[{|x%/>?")
            foreach (var i in MapGraphics.Chips((byte)t))
            {
                var cell = sheets["map"].Image.Crop(i % 16 * 32, i / 16 * 48 + 16, 32, 32);
                Check(cell.Pixels.Where((_, p) => p % 4 == 3).Any(a => a != 0), "uncovered terrain " + t);
            }
        var combinations = new HashSet<int>();
        for (var mask = 0; mask < 256; mask++)
        {
            var tiles = Enumerable.Repeat((byte)':',9).ToArray(); tiles[4] = (byte)'_';
            var neighbors = new[] {0,1,2,3,5,6,7,8};
            for (var bit = 0; bit < 8; bit++) if ((mask & 1 << bit) != 0) tiles[neighbors[bit]] = (byte)'_';
            var result = MapGraphics.Draw(3,3,tiles,new byte[9],[],_=>sheets["map"].Image,null,_=>null);
            var center = result.Crop(32,48,32,32);
            Check(center.Pixels.Where((_,i)=>i%4==3).All(a=>a==255), "water alpha hole " + mask);
            combinations.Add(BitConverter.ToInt32(SHA256.HashData(center.Pixels)));
        }
        Check(combinations.Count > 10, "water quadrant coverage");
        foreach (var kind in kinds.Except(new[] { "sign", "object" }))
            Check(!sheets["actors/" + kind].Crop(32,64,32,32).P.SequenceEqual(sheets["actors/" + kind].Crop(64,64,32,32).P), kind + " animation missing");
        foreach (var kind in new[] { "person", "wanderer", "beast", "berserker", "golem", "skeleton" })
            Check(!sheets["actors/" + kind].Crop(32,0,32,32).P.SequenceEqual(sheets["actors/" + kind].Crop(32,64,32,32).P), kind + " facing missing");
    }
    static PixelImage ReadBmp(string path)
    {
        using var b = new BinaryReader(File.OpenRead(path));
        if (b.ReadUInt16() != 0x4d42) throw new InvalidDataException(path);
        b.BaseStream.Position = 10; var offset = b.ReadInt32();
        if (b.ReadInt32() != 40) throw new InvalidDataException(path);
        var w = b.ReadInt32(); var h = b.ReadInt32();
        if (b.ReadUInt16() != 1 || b.ReadUInt16() != 24) throw new InvalidDataException(path);
        var a = new Art(w,h); var stride = (w*3+3)&~3;
        for (var y = 0; y < h; y++)
        {
            b.BaseStream.Position = offset + (h-1-y)*stride;
            for (var x = 0; x < w; x++) a.Dot(x,y,(uint)(b.ReadByte() | b.ReadByte()<<8 | b.ReadByte()<<16) | 0xff000000);
        }
        return a.Image;
    }
}
