using PhiClient.Core;

namespace PhiClient.Core.Tests;

public sealed class MapGraphicsTests
{
    private static PixelImage Solid(int width, int height, byte color)
    {
        var bytes = new byte[width * height * 4];
        for (var i = 0; i < bytes.Length; i += 4) { bytes[i] = color; bytes[i + 3] = 255; }
        return new(width, height, bytes);
    }

    private static byte Color(PixelImage image, int x, int y) => image.Pixels[(y * image.Width + x) * 4];

    [Fact]
    public void OriginalMasksAndTealKeyProduceTransparentPixels()
    {
        var raw = Solid(1024, 96, 17);
        raw.Pixels.AsSpan((512 + 1) * 4, 3).Fill(201);
        raw.Pixels.AsSpan((512 + 2) * 4, 3).Fill(200);
        var chips = raw.Transparent(horizontalMask: true);
        Assert.Equal(512, chips.Width);
        Assert.Equal(17, Color(chips, 1, 0));
        Assert.Equal(255, chips.Pixels[7]);
        Assert.Equal(0, chips.Pixels[11]);
        var items = Solid(256, 64, 77);
        items.Pixels.AsSpan(256 * 32 * 4, 3).Fill(255);
        Assert.Equal(255, items.Transparent(verticalMask: true).Pixels[3]);
        Assert.Equal(0, items.Transparent(verticalMask: true).Pixels[7]);
        var actor = Solid(96, 160, 1);
        actor.Pixels[0] = actor.Pixels[1] = 128;
        actor.Pixels[2] = 0;
        var keyed = actor.Transparent();
        Assert.Equal(0, keyed.Pixels[3]);
        Assert.Equal(255, keyed.Pixels[7]);
        Assert.Throws<ArgumentException>(() => Solid(32, 32, 0).Transparent());
    }

    [Fact]
    public void FramesRespectDirectionSizeAnimationAndNonAnimatedObjects()
    {
        var sprite = Solid(96, 160, 0);
        for (var y = 0; y < 128; y++)
        for (var x = 0; x < 96; x++)
            sprite.Pixels[(y * 96 + x) * 4] = (byte)(y / 32 * 10 + x / 16);
        var actor = new Actor(0, 0, "sprite", Direction: 'L', Size: '*');
        var rendered = MapGraphics.Draw(1, 1, [(byte)' '], [0], [actor], _ => null, null, _ => sprite, 1);
        Assert.Equal(34, Color(rendered, 0, 5));
        Assert.Equal(35, Color(rendered, 31, 5));
        var normal = actor with { Size = '#', Direction = 'R' };
        rendered = MapGraphics.Draw(1, 1, [(byte)' '], [0], [normal], _ => null, null, _ => sprite, 1);
        Assert.Equal(11, Color(rendered, 8, 5));
        Assert.Equal(0, Color(rendered, 7, 5));
        rendered = MapGraphics.Draw(1, 1, [(byte)' '], [0],
            [normal with { DefaultGraphic = 0x75 }], _ => null, null, _ => sprite, 1);
        Assert.Equal(10, Color(rendered, 8, 5));
    }

    [Fact]
    public void CharactersShareCellsAndForegroundRowsOccludeEarlierCharacters()
    {
        var small = new Actor(0, 0, "first");
        var second = small with { Name = "second", Layer = 1 };
        var red = Solid(96, 160, 50);
        var blue = Solid(96, 160, 100);
        var rendered = MapGraphics.Draw(1, 1, [(byte)' '], [0], [small, second],
            _ => null, null, actor => actor.Name == "first" ? red : blue);
        Assert.Equal(50, Color(rendered, 0, 5));
        Assert.Equal(100, Color(rendered, 16, 5));
        var sheet = Solid(512, 96, 200);
        rendered = MapGraphics.Draw(1, 2, [(byte)' ', (byte)'#'], [0, 0], [small],
            _ => sheet, null, _ => red);
        Assert.Equal(50, Color(rendered, 8, 31));
        Assert.Equal(200, Color(rendered, 8, 32));
    }

    [Fact]
    public void MapsetAttributesSignAndItemsUseTheirOwnSheetsAndCropping()
    {
        var chip = Solid(512, 96, 20);
        var other = Solid(512, 96, 40);
        var items = Solid(256, 32, 60);
        var rendered = MapGraphics.Draw(2, 1, [(byte)' ', (byte)'x'], [1, 0x58], [],
            slot => slot == 1 ? other : chip, items, _ => null);
        Assert.Equal(40, Color(rendered, 0, 40));
        Assert.Equal(60, Color(rendered, 32, 8));
        Assert.Equal(60, Color(rendered, 32, 28));
        Assert.Equal(20, Color(rendered, 32, 29));
        Assert.Throws<ArgumentException>(() => MapGraphics.Draw(15, 15, [], [], [], _ => null, null, _ => null));
    }

    [Theory]
    [InlineData(@"..\private")]
    [InlineData(@"\\server\share")]
    [InlineData("C:secret")]
    [InlineData("../secret")]
    [InlineData("bad.")]
    public void ServerImageNamesCannotEscapeTheChosenFolder(string name) => Assert.False(GraphicsNames.Safe(name));

    [Fact]
    public void ParserRetainsPackedSpriteFieldsAndMapsetDefinitions()
    {
        var parser = new ProtocolParser();
        var terrain = Enumerable.Repeat((byte)' ', 116).ToArray();
        "#m57 M N 00:      "u8.CopyTo(terrain);
        parser.Feed(terrain);
        var packed = Enumerable.Repeat((byte)' ', 142).ToArray();
        "#m57 W "u8.CopyTo(packed);
        foreach (var offset in new[] { 0, 68 })
        {
            packed[offset + 7] = (byte)'2';
            packed[offset + 13] = (byte)'3';
            packed[offset + 15] = (byte)'4';
            packed[offset + 17] = (byte)'R';
            "Hero"u8.CopyTo(packed.AsSpan(offset + 19));
            "01"u8.CopyTo(packed.AsSpan(offset + 51));
            "hero"u8.CopyTo(packed.AsSpan(offset + 54));
            packed[offset + 70] = (byte)'*';
            "40"u8.CopyTo(packed.AsSpan(offset + 72));
        }
        parser.Feed(packed);
        var map = Assert.IsType<MapFrame>(Assert.Single(parser.Feed("#m57 ."u8.ToArray())));
        Assert.Equal(2, map.Actors.Count);
        Assert.All(map.Actors, actor => Assert.Equal(new Actor(3, 4, "Hero", "hero", 'R', '*', 2, 0x40, 1), actor));
        Assert.Equal(new MapSetNotice(0, "castle"), Assert.Single(parser.Feed("#mapset castle"u8.ToArray())));
        Assert.Equal(new MapSetNotice(2, "cave"), Assert.Single(parser.Feed("#mapset-define 2 cave"u8.ToArray())));
        Assert.Equal(new MapSetNotice(3, "cave"), Assert.Single(parser.Feed("#mapset-define 255 cave"u8.ToArray())));
        parser.Feed("#more"u8.ToArray());
        Assert.IsType<LogLine>(Assert.Single(parser.Feed("#mapset cave"u8.ToArray())));
    }
}
