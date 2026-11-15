using System.Collections.Immutable;

namespace PhiClient.Core;

public sealed record TextTag(string Key, string Caption, string Open, string Close = "/*.*/");
public sealed record TextTagGroup(string Caption, ImmutableArray<TextTag> Tags);

public static class TagEditing
{
    public static ImmutableArray<TextTagGroup> Groups { get; } =
    [
        new("色タグ", [new("custom", "Custom", "/*color=#", "*//*.*/"),
            .. new[] { "Red", "Blue", "Cyan", "Magenta", "Navy", "Lime", "Purple", "Yellow",
                "Silver", "Olive", "Teal", "Maroon", "White", "Green", "Gray" }.Select(color =>
                    new TextTag("color-" + color.ToLowerInvariant(), color, $"/*color={color.ToLowerInvariant()}*/"))]),
        new("サイズタグ", [new("large", "Large", "/*size=large*/"), new("small", "Small", "/*size=small*/")]),
        new("スタイルタグ", [.. new[] { "Bold", "Italic", "Strike", "Under", "Sup", "Sub" }.Select(style =>
            new TextTag(style.ToLowerInvariant(), style, $"/*style={style.ToLowerInvariant()}*/"))]),
        new("フォントタグ", [new("font", "フォント種別", "/*font=", "*//*.*/")])
    ];

    public static (string Text, int Caret) Wrap(string text, int start, int length, TextTag tag)
    {
        if (start < 0 || length < 0 || start > text.Length - length)
            throw new ArgumentOutOfRangeException(nameof(start), "選択範囲を確認してください。");
        var next = text[..start] + tag.Open + text.Substring(start, length) + tag.Close + text[(start + length)..];
        return (next, start + tag.Open.Length + (tag.Open.EndsWith("*/", StringComparison.Ordinal)
            ? length + tag.Close.Length : 0));
    }
}
