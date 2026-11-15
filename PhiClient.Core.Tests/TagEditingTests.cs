using PhiClient.Core;

namespace PhiClient.Core.Tests;

public sealed class TagEditingTests
{
    [Fact]
    public void OriginalClosedAndValueEntryTagsKeepTheSpecifiedCaretPositions()
    {
        var red = TagEditing.Groups[0].Tags.Single(tag => tag.Key == "color-red");
        Assert.Equal(("a/*color=red*/b/*.*/c", 20), TagEditing.Wrap("abc", 1, 1, red));
        var custom = TagEditing.Groups[0].Tags.Single(tag => tag.Key == "custom");
        Assert.Equal(("/*color=#x*//*.*/", 9), TagEditing.Wrap("x", 0, 1, custom));
        var font = TagEditing.Groups[3].Tags.Single();
        Assert.Equal(("a/*font=b*//*.*/c", 8), TagEditing.Wrap("abc", 1, 1, font));
    }

    [Fact]
    public void EmptySelectionsAndMultilineJapaneseSelectionsRemainEditableWithoutSending()
    {
        var bold = TagEditing.Groups[2].Tags.Single(tag => tag.Key == "bold");
        Assert.Equal(("前/*style=bold*//*.*/後", 20), TagEditing.Wrap("前後", 1, 0, bold));
        var text = "前あ\r\nい後";
        var wrapped = TagEditing.Wrap(text, 1, 4, bold);
        Assert.Equal("前/*style=bold*/あ\r\nい/*.*/後", wrapped.Text);
        Assert.Equal(wrapped.Text.Length - 1, wrapped.Caret);
        Assert.Throws<ArgumentOutOfRangeException>(() => TagEditing.Wrap(text, 1, text.Length, bold));
    }
}
