using PhiClient.Core;

namespace PhiClient.Core.Tests;

public sealed class SoundRulesTests
{
    [Fact]
    public void OriginalLineNumbersSurviveRejectedRulesAndFirstMatchWins()
    {
        var sounds = SoundRules.Parse("説明\r\n/攻撃/\r\n/(/\r\n/PRIV/i\r\n/攻撃/\r\n",
            "説明\r\nsound\\attack.wav\r\nignored.wav\r\n\r\nsecond.wav\r\n");
        Assert.Equal(new SoundMatch(2, "sound\\attack.wav"), sounds.Find("勇者が攻撃した"));
        Assert.Equal(new SoundMatch(4, null), sounds.Find("[x] > priv"));
        Assert.Equal(new SoundRuleIssue("SoundRegexp.ini", 3), Assert.Single(sounds.Issues));
        Assert.Null(sounds.Find("通常のログ"));
    }

    [Theory]
    [InlineData("/abc/ig", "ABC")]
    [InlineData("/a.b/s", "a\nb")]
    [InlineData("/^b/m", "a\nb")]
    [InlineData("/ a \\s+ b /x", "a  b")]
    [InlineData("/a\\/b/", "a/b")]
    public void OriginalSupportedFlagsAndSlashDelimitersMatch(string pattern, string text)
    {
        var sounds = SoundRules.Parse("説明\n" + pattern, "説明\n");
        Assert.Empty(sounds.Issues);
        Assert.Equal(new SoundMatch(2, null), sounds.Find(text));
    }

    [Fact]
    public void DefaultExclusionsAndExplicitEmptyOverrideFollowOriginal()
    {
        var defaults = SoundRules.Parse("", "");
        Assert.True(defaults.SuppressTalkFlash("DM > ようこそ"));
        Assert.True(defaults.SuppressTalkFlash("/*cl=red*/DM > ようこそ"));
        Assert.True(defaults.SuppressTalkFlash("現在の接続者数 >> 3"));
        Assert.False(defaults.SuppressTalkFlash("勇者 > こんにちは"));
        Assert.False(SoundRules.Parse("", "", "説明\n").SuppressTalkFlash("DM > ようこそ"));
        var custom = SoundRules.Parse("", "", "説明\n/quiet/i\nbroken");
        Assert.True(custom.SuppressTalkFlash("QUIET"));
        Assert.False(custom.SuppressTalkFlash("DM > ようこそ"));
        Assert.Equal(new SoundRuleIssue("NotFlashRegexp.ini", 3), Assert.Single(custom.Issues));
    }

    [Fact]
    public void EmptyAndMissingWaveEntriesRemainFlashRulesAndHeadersAreIgnored()
    {
        var sounds = SoundRules.Parse("/must-not-match/\n/one/\n/two/", "ignored.wav\n  ");
        Assert.Null(sounds.Find("must-not-match"));
        Assert.Equal(new SoundMatch(2, null), sounds.Find("one"));
        Assert.Equal(new SoundMatch(3, null), sounds.Find("two"));
        Assert.Empty(sounds.Issues);
    }

    [Fact]
    public void ExcessiveBacktrackingIsAnExplicitFailureInsteadOfAnUnmatchedFallback()
    {
        var sounds = SoundRules.Parse("説明\n/^(a+)+$/", "説明");
        Assert.Throws<InvalidDataException>(() => sounds.Find(new string('a', 10000) + "!"));
    }
}
