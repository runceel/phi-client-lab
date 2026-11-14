using PhiClient.Core;

namespace PhiClient.Core.Tests;

public sealed class MapCompatibilityTests
{
    private static PixelImage Sheet()
    {
        var pixels = new byte[512 * 96 * 4];
        for (var y = 0; y < 96; y++)
        for (var x = 0; x < 512; x++)
        {
            var chip = y / 48 * 16 + x / 32;
            var quarter = x % 32 / 16 + (y % 48 >= 32 ? 2 : 0);
            var i = (y * 512 + x) * 4;
            pixels[i] = (byte)(chip * 4 + quarter + 1);
            pixels[i + 3] = (byte)(y % 48 < 16 ? 0 : 255);
        }
        return new(512, 96, pixels);
    }

    private static byte Color(PixelImage image, int x, int y) => image.Pixels[(y * image.Width + x) * 4];

    [Fact]
    public void All256WaterNeighborhoodsMatchTheIndependentFourQuarterTable()
    {
        // wphi d8f11bf, mapRender.ts Q_TABLE: entries encode chip*4 + source quarter + 1.
        int[][] expected =
        [
            [97, 97, 93, 93, 95, 95, 104, 0],
            [98, 98, 94, 94, 95, 95, 103, 0],
            [99, 99, 93, 93, 96, 96, 102, 0],
            [100, 100, 94, 94, 96, 96, 101, 0]
        ];
        var neighbors = new[] { 0, 1, 2, 3, 5, 6, 7, 8 };
        int[][] inputs = [[0, 1, 3], [2, 1, 4], [5, 6, 3], [7, 6, 4]];
        var sheet = Sheet();
        for (var mask = 0; mask < 256; mask++)
        {
            var tiles = Enumerable.Repeat((byte)' ', 9).ToArray();
            tiles[4] = (byte)'_';
            for (var n = 0; n < 8; n++)
                if ((mask & (1 << n)) != 0) tiles[neighbors[n]] = (byte)'_';
            var image = MapGraphics.Draw(3, 3, tiles, new byte[9], [], _ => sheet, null, _ => null);
            for (var q = 0; q < 4; q++)
            {
                var pattern = ((mask >> inputs[q][0]) & 1) |
                    (((mask >> inputs[q][1]) & 1) << 1) | (((mask >> inputs[q][2]) & 1) << 2);
                var want = expected[q][pattern];
                if (want == 0) want = 4 * 4 + q + 1;
                Assert.Equal(want, Color(image, 32 + (q & 1) * 16 + 4, 48 + q / 2 * 16 + 4));
            }
        }
    }

    [Fact]
    public void EagleBoardIsSquareClearsPersonAttributesAndUsesTMan()
    {
        var frame = new EagleFrame(3, 2, 0, 0,
            Enumerable.Repeat((byte)' ', 6).ToArray(), [0x58, 1, 0, 0, 0, 0]);
        var board = EagleBoard.Build(frame);
        Assert.Equal(3, board.Dim);
        Assert.Equal(new byte[] { 0, 1, 0, 0, 0, 0, 0, 0, 0 }, board.Attributes);
        Assert.Equal("   ???", System.Text.Encoding.ASCII.GetString(board.Tiles, 3, 6));
        Assert.Equal(new Actor(0, 0, "", "t_man", 'F', '*', DefaultGraphic: 0x40), Assert.Single(board.Actors));
        var slots = new List<int>();
        var spritePixels = Enumerable.Repeat((byte)255, 96 * 128 * 4).ToArray();
        for (var y = 0; y < 128; y++)
        for (var x = 64; x < 96; x++) spritePixels[(y * 96 + x) * 4] = 127;
        var sprite = new PixelImage(96, 128, spritePixels);
        var rendered = MapGraphics.Draw(3, 3, board.Tiles, board.Attributes, board.Actors,
            slot => { slots.Add(slot); return Sheet(); }, null, _ => sprite, 1, true);
        Assert.Equal(96, rendered.Height);
        Assert.All(slots, slot => Assert.Equal(0, slot));
        Assert.Equal(255, Color(rendered, 0, 0));
        var still = MapGraphics.Draw(3, 3, board.Tiles, board.Attributes, board.Actors,
            _ => Sheet(), null, _ => sprite, 0, true);
        Assert.Equal(still.Pixels, rendered.Pixels);
        var noActor = MapGraphics.Draw(3, 3, board.Tiles, board.Attributes, [],
            _ => Sheet(), null, _ => null, eagle: true);
        Assert.Equal(1, Color(noActor, 4, 4));
    }

    [Fact]
    public void LabelsUseLastNonemptyNamePerCellAndStableYOrder()
    {
        Actor[] actors =
        [
            new(3, 3, "Self"), new(1, 4, "Late"), new(2, 1, "/*r*/Old/*.*/"),
            new(2, 1, "/*b*/New/*.*/"), new(1, 1, "Same row"), new(2, 1, "/*r*/")
        ];
        var labels = MapLabels.Names(actors, 7);
        Assert.DoesNotContain(labels, label => label.Text is "Self" or "Old" or "");
        var name = Assert.Single(labels, label => label.Text == "New");
        Assert.Equal(80, name.X);
        Assert.Equal(67.2, name.Y, 6);
        Assert.Equal(new[] { "Old", "New", "Same row", "", "Self", "Late" }, MapLabels.NameList(actors));
        var missing = MapLabels.Missing([new(1, 1, "/*r*/欠落"), new(1, 1, "Second", Layer: 1)], 7, _ => false);
        Assert.Equal(new[] { "欠", "S" }, missing.Select(label => label.Text));
        Assert.Equal(new[] { 40.0, 56.0 }, missing.Select(label => label.X));
        Assert.All(missing, label => Assert.Equal(64, label.Y));
        Assert.Equal("?", Assert.Single(MapLabels.Missing([new(0, 0, "")], 7, _ => false)).Text);
    }

    [Fact]
    public void NearestScalingAndHitTestingKeepPixelBoundaries()
    {
        var image = new PixelImage(2, 1, [1, 2, 3, 255, 11, 12, 13, 255]);
        var twice = image.ScaleNearest(2);
        Assert.Equal(4, twice.Width);
        Assert.Equal(new byte[] { 1, 1, 11, 11 }, Enumerable.Range(0, 4).Select(x => Color(twice, x, 1)).ToArray());
        var dpi = image.ScaleNearest(3, 2);
        Assert.Equal(new byte[] { 1, 1, 11 }, Enumerable.Range(0, 3).Select(x => Color(dpi, x, 1)).ToArray());
        Assert.Null(MapLabels.Hit(10, 15.9, 7));
        Assert.Null(MapLabels.Hit(224, 16, 7));
        Assert.Equal((0, 0), MapLabels.Hit(112, 128, 7));
        Assert.Equal((0, 0), MapLabels.Hit(224, 256, 7, 2));
        Assert.Null(MapLabels.Hit(0, 0, 7, 0));
    }

    [Fact]
    public void EmptyTerrainIsBlackWithTransparentTopAndMagnificationUsesOriginalAnchor()
    {
        var empty = MapGraphics.Draw(7, 7, [], [], [], _ => null, null, _ => null);
        Assert.Equal(0, empty.Pixels[(15 * empty.Width) * 4 + 3]);
        Assert.Equal(255, empty.Pixels[(16 * empty.Width) * 4 + 3]);
        Assert.Equal(0, Color(empty, 0, 16));
        Assert.Equal((88.0, 71.0, 64L, 64L), MapGraphics.MagnifiedDestination(104, 101, new(64, 64, 4)));
        var sprite = new PixelImage(96, 128, Enumerable.Repeat((byte)255, 96 * 128 * 4).ToArray());
        var image = MapGraphics.Draw(7, 7, [], [], [new(3, 3, "Big", Magnify: new(64, 64, 4))],
            _ => null, null, _ => sprite);
        Assert.Equal(255, Color(image, 88, 71));
        Assert.Equal(255, Color(image, 151, 134));
        Assert.Equal(0, Color(image, 87, 71));
        Assert.Equal(0, Color(image, 152, 134));
        var extreme = MapGraphics.Draw(1, 1, [], [],
            [new(0, 0, "Offscreen", Magnify: new(long.MaxValue, long.MaxValue, long.MinValue))],
            _ => null, null, _ => sprite);
        Assert.Equal(32, extreme.Width);
    }

    private static byte[] ActorLine(string name = "Hero")
    {
        var line = Enumerable.Repeat((byte)' ', 74).ToArray();
        "#m57 O 00001"u8.CopyTo(line);
        line[13] = line[15] = (byte)'3';
        line[17] = (byte)'B';
        PhiWire.Encoding.GetBytes(name).CopyTo(line, 19);
        "00"u8.CopyTo(line.AsSpan(51));
        "hero"u8.CopyTo(line.AsSpan(54));
        line[70] = (byte)'#';
        "01"u8.CopyTo(line.AsSpan(72));
        return line;
    }

    [Fact]
    public void InvalidRequiredActorFieldsDoNotClearPriorActorsAndMagnificationIsCapturedOnArrival()
    {
        var parser = new ProtocolParser();
        parser.Feed(ActorLine());
        Assert.Single(Assert.IsType<MapFrame>(Assert.Single(parser.Feed("#m57 ."u8.ToArray()))).Actors);
        foreach (var field in new[] { 7, 8, 13, 15, 51, 72 })
        {
            var bad = ActorLine("Invalid");
            bad[field] = (byte)'!';
            parser.Feed(bad);
            var prior = Assert.IsType<MapFrame>(Assert.Single(parser.Feed("#m57 ."u8.ToArray())));
            Assert.Equal("Hero", Assert.Single(prior.Actors).Name);
            Assert.Empty(prior.Tiles);
        }
        parser.Feed(PhiWire.Encoding.GetBytes("#ex-obj S 64 64 4  Leading"));
        parser.Feed(ActorLine(" Leading"));
        var frame = Assert.IsType<MapFrame>(Assert.Single(parser.Feed("#m57 ."u8.ToArray())));
        Assert.Equal(" Leading", Assert.Single(frame.Actors).Name);
        Assert.Null(frame.Actors[0].Magnify);
        parser.Feed("#ex-obj S 64 64 4 Hero"u8.ToArray());
        parser.Feed(ActorLine());
        parser.Feed("#ex-obj S 80 96 -2 Hero"u8.ToArray());
        frame = Assert.IsType<MapFrame>(Assert.Single(parser.Feed("#m57 ."u8.ToArray())));
        Assert.Equal(new Magnify(64, 64, 4), Assert.Single(frame.Actors).Magnify);
        parser.Feed(ActorLine());
        frame = Assert.IsType<MapFrame>(Assert.Single(parser.Feed("#m57 ."u8.ToArray())));
        Assert.Equal(new Magnify(80, 96, -2), Assert.Single(frame.Actors).Magnify);
        var outside = ActorLine();
        outside[13] = (byte)'9';
        parser.Feed(outside);
        parser.Feed("#map M -"u8.ToArray());
        frame = Assert.IsType<MapFrame>(Assert.Single(parser.Feed("#map ."u8.ToArray())));
        Assert.Equal(9, Assert.Single(frame.Actors).X);
        Assert.Equal(5, frame.Size);
    }

    [Fact]
    public void MagnificationNamesWithSpacesAndMalformedUpdatesPreserveTheLastValidDefinition()
    {
        var parser = new ProtocolParser();
        parser.Feed("#ex-obj S 64 48 -3 Big Hero"u8.ToArray());
        foreach (var command in new[]
        {
            "#ex-obj S bad 96 4 Big Hero", "#ex-obj S 80 bad 4 Big Hero",
            "#ex-obj S 80 96 bad Big Hero", "#ex-obj S 80 96 4"
        })
            Assert.IsType<LogLine>(Assert.Single(parser.Feed(PhiWire.Encoding.GetBytes(command))));
        parser.Feed(ActorLine("Big Hero"));
        var actor = Assert.Single(Assert.IsType<MapFrame>(Assert.Single(parser.Feed("#m57 ."u8.ToArray()))).Actors);
        Assert.Equal(new Magnify(64, 48, -3), actor.Magnify);
        parser.Feed("#ex-obj S 0 -16 0 Big Hero"u8.ToArray());
        parser.Feed(ActorLine("Big Hero"));
        actor = Assert.Single(Assert.IsType<MapFrame>(Assert.Single(parser.Feed("#m57 ."u8.ToArray()))).Actors);
        Assert.Equal(new Magnify(0, -16, 0), actor.Magnify);
        parser.Feed("#ex-obj S 9223372036854775807 -9223372036854775808 2147483648 Big Hero"u8.ToArray());
        parser.Feed(ActorLine("Big Hero"));
        actor = Assert.Single(Assert.IsType<MapFrame>(Assert.Single(parser.Feed("#m57 ."u8.ToArray()))).Actors);
        Assert.Equal(new Magnify(long.MaxValue, long.MinValue, 2147483648), actor.Magnify);
    }

    [Fact]
    public void AllCharacterDirectionsSizesAndFramesKeepTheirSourcePixelsAndVerticalAnchor()
    {
        var pixels = new byte[96 * 128 * 4];
        for (var y = 0; y < 128; y++)
        for (var x = 0; x < 96; x++)
        {
            var i = (y * 96 + x) * 4;
            pixels[i] = (byte)(y / 32 * 6 + x / 16 + 1);
            pixels[i + 3] = 255;
        }
        var sprite = new PixelImage(96, 128, pixels);
        for (var row = 0; row < 4; row++)
        foreach (var giant in new[] { false, true })
        for (var frame = 0; frame < 2; frame++)
        {
            var actor = new Actor(1, 1, "Actor", Direction: "BRFL"[row],
                Size: giant ? '*' : '#', DefaultGraphic: 0x75);
            var image = MapGraphics.Draw(3, 3, [], [], [actor], _ => null, null, _ => sprite, frame);
            var x = giant ? 32 : 40;
            Assert.Equal(row * 6 + (giant ? 2 + frame * 2 : frame) + 1, Color(image, x, 37));
            Assert.Equal(0, Color(image, x, 36));
            Assert.Equal(0, Color(image, x, 69));
            Assert.Equal(0, Color(image, x - 1, 37));
        }
    }

    [Fact]
    public void TwoActorPermutationsKeepSmallOffsetsAndGiantFirstAtTheSameCell()
    {
        var small = new Actor(1, 1, "Small", Layer: 0);
        var giant = new Actor(1, 1, "Giant", Size: '*', Layer: 1);
        foreach (var input in new[] { new[] { small, giant }, new[] { giant, small } })
        {
            var arranged = MapGraphics.ArrangeRow(input);
            Assert.Equal(new[] { "Giant", "Small" }, arranged.Select(item => item.Actor.Name));
            Assert.Equal(new[] { 0, 8 }, arranged.Select(item => item.Offset));
        }
        var twoSmall = MapGraphics.ArrangeRow([small with { Layer = 1 }, small with { Name = "First", Layer = 0 }]);
        Assert.Equal(new[] { "First", "Small" }, twoSmall.Select(item => item.Actor.Name));
        Assert.Equal(new[] { -8, 8 }, twoSmall.Select(item => item.Offset));
        Assert.All(MapGraphics.ArrangeRow([giant, giant with { Layer = 0 }]), item => Assert.Equal(0, item.Offset));
        Assert.All(MapGraphics.ArrangeRow([small, small, giant]), item => Assert.Equal(0, item.Offset));
    }

    [Theory]
    [InlineData("LANU_actor")]
    [InlineData("lanu_actor.bmp")]
    public void ExcludedGraphicsCannotBeResolved(string name) => Assert.False(GraphicsNames.Allowed(name));
}
