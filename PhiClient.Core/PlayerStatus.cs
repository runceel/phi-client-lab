using System.Globalization;

namespace PhiClient.Core;

public sealed record PlayerStatus(string Name, long? Hp, long? MaxHp, long? Mp, long? MaxMp,
    long? Experience, long? Gold, long? Fighter, long? Warrior, long? Magician, long? Cleric)
{
    public static PlayerStatus Parse(string text)
    {
        var fields = ProtocolParser.PlainText(text).Split(':');
        long? Number(int index) => index < fields.Length &&
            long.TryParse(fields[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;
        return new(fields[0].Trim(), Number(1), Number(2), Number(3), Number(4), Number(5),
            Number(6), Number(7), Number(8), Number(9), Number(10));
    }
}

public sealed record Conditions(string Flags) : ServerEvent
{
    private static readonly string[] Names = ["Poison", "Palsy", "Panic", "Confusion", "Berserk", "Silence", "Blind"];
    public IReadOnlyList<string> Active => Names.Where((_, i) => i < Flags.Length && Flags[i] == '*').ToArray();
}

public sealed record LocationNotice(string Kind, string Value) : ServerEvent;
public sealed record ActionMode(bool Magic, bool Active) : ServerEvent;
public sealed record EagleFrame(int Width, int Height, int SelfX, int SelfY, byte[] Tiles, byte[] Attributes) : ServerEvent;
