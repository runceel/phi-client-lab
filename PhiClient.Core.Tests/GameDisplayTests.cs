using PhiClient.Core;

namespace PhiClient.Core.Tests;

public sealed class GameDisplayTests
{
    [Fact]
    public void StatusAndConditionsPreserveMissingValuesAndDisplayNamedConditions()
    {
        var status = PlayerStatus.Parse("冒険者 : 42 : 50 : 12 : 20 : 100 : 25 : 1 : 2 : 3 : 4");
        Assert.Equal("冒険者", status.Name);
        Assert.Equal((42, 50, 12, 20, 100, 25, 1, 2, 3, 4),
            (status.Hp, status.MaxHp, status.Mp, status.MaxMp, status.Experience, status.Gold,
                status.Fighter, status.Warrior, status.Magician, status.Cleric));
        var incomplete = PlayerStatus.Parse("Name : unknown : 50");
        Assert.Null(incomplete.Hp);
        Assert.Equal(50, incomplete.MaxHp);
        Assert.Null(incomplete.Mp);
        var parser = new ProtocolParser();
        var condition = Assert.IsType<Conditions>(Assert.Single(parser.Feed("#cond ** *  *"u8.ToArray())));
        Assert.Equal(["Poison", "Palsy", "Confusion", "Blind"], condition.Active);
        parser.Feed("#more"u8.ToArray());
        Assert.IsType<LogLine>(Assert.Single(parser.Feed("#cond *******"u8.ToArray())));
    }

    [Fact]
    public void LegacyMapReadsSeparateItemSignPlaneAndPreservesUnchangedTerrain()
    {
        var parser = new ProtocolParser();
        var bytes = Enumerable.Repeat((byte)' ', 118).ToArray();
        "#map M E 00:      "u8.CopyTo(bytes);
        bytes[18] = (byte)'#';
        bytes[68] = (byte)'.';
        bytes[69] = (byte)'-';
        parser.Feed(bytes);
        var map = Assert.IsType<MapFrame>(Assert.Single(parser.Feed("#map ."u8.ToArray())));
        Assert.Equal(5, map.Size);
        Assert.Equal('E', map.Direction);
        Assert.Equal(25, map.Tiles.Length);
        Assert.Equal((byte)'#', map.Tiles[0]);
        Assert.Equal(0x68, map.Attributes[0]);
        parser.Feed("#map M -"u8.ToArray());
        var unchanged = Assert.IsType<MapFrame>(Assert.Single(parser.Feed("#map ."u8.ToArray())));
        Assert.Equal(map.Tiles, unchanged.Tiles);
        Assert.Equal(map.Attributes, unchanged.Attributes);
        Assert.IsType<LogLine>(Assert.Single(parser.Feed("#m57 M"u8.ToArray())));
        Assert.Empty(parser.Feed("#m57 ."u8.ToArray()));
        Assert.IsType<LogLine>(Assert.Single(parser.Feed("#map M"u8.ToArray())));
    }

    [Fact]
    public void EagleEyeUsesByteRowsAndRejectsOutOfBoundsDataWithoutCorruptingPosition()
    {
        var parser = new ProtocolParser();
        parser.Feed("#ex-eagleeye start"u8.ToArray());
        var row = Enumerable.Repeat((byte)' ', 27).ToArray();
        System.Text.Encoding.ASCII.GetBytes("#ex-eagleeye M 02 00 ").CopyTo(row, 0);
        row[21] = (byte)'#'; row[22] = 0x80;
        row[23] = (byte)' '; row[24] = 0x58;
        row[25] = (byte)'+'; row[26] = 0x08;
        parser.Feed(row);
        parser.Feed("#ex-eagleeye pos 1 0"u8.ToArray());
        Assert.IsType<LogLine>(Assert.Single(parser.Feed("#ex-eagleeye pos -1 999"u8.ToArray())));
        Assert.IsType<LogLine>(Assert.Single(parser.Feed("#ex-eagleeye M 99 99 "u8.ToArray())));
        var frame = Assert.IsType<EagleFrame>(Assert.Single(parser.Feed("#ex-eagleeye end"u8.ToArray())));
        Assert.Equal((3, 1, 1, 0), (frame.Width, frame.Height, frame.SelfX, frame.SelfY));
        Assert.Equal([(byte)'#', (byte)' ', (byte)'+'], frame.Tiles);
        Assert.Equal([(byte)0x80, (byte)0x58, (byte)8], frame.Attributes);
        parser.Feed("#ex-eagleeye start"u8.ToArray());
        Assert.IsType<LogLine>(Assert.Single(parser.Feed("#ex-eagleeye end"u8.ToArray())));
    }
}
