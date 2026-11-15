using PhiClient.Core;

namespace PhiClient.Core.Tests;

public sealed class LogMarkupTests
{
    [Fact]
    public void OverlineSurvivesNestedColorAndEndsAtTheOriginalClosingTag()
    {
        var parts = LogMarkup.Parse("plain/*style=over*/上/*color=red*/赤/*color=.*/上/*.*/plain");
        Assert.Equal("plain上赤上plain", string.Concat(parts.Select(part => part.Text)));
        Assert.False(parts[0].Style.Overline);
        Assert.True(parts[1].Style.Overline);
        Assert.True(parts[2].Style.Overline);
        Assert.Equal("red", parts[2].Style.Color);
        Assert.True(parts[3].Style.Overline);
        Assert.Null(parts[3].Style.Color);
        Assert.False(parts[4].Style.Overline);
    }

    [Fact]
    public void NestedTagsClosingResetSizesFontsAndStylesFollowOriginalSkinValues()
    {
        var parts = LogMarkup.Parse("/*r*/赤/*size=+3*/大/*size=.*/赤/*.*/通常/*br*/" +
            "/*font=ＭＳ ゴシック style=bold*/太字/*.*/はーと/*unknown*/終");
        Assert.Equal("赤大赤通常\n太字♥終", string.Concat(parts.Select(part => part.Text)));
        Assert.Equal("red", parts[0].Style.Color);
        Assert.Equal(26, parts[1].Style.Size);
        Assert.Null(parts[2].Style.Size);
        Assert.Equal("red", parts[2].Style.Color);
        Assert.Null(parts[3].Style.Color);
        Assert.Equal("ＭＳ ゴシック", parts[5].Style.Font);
        Assert.True(parts[5].Style.Bold);
        Assert.False(parts[6].Style.Bold);
        Assert.All(LogMarkup.Parse("(大声 /*size=9*/文字"), part => Assert.Null(part.Style.Size));
        var monalisa = Assert.Single(LogMarkup.Parse("もなりざ"));
        Assert.Equal("\u009f", monalisa.Text);
        Assert.Equal("Webdings", monalisa.Style.Font);
        Assert.Equal(5, monalisa.Style.Scale);
    }

    [Theory]
    [InlineData("http://localhost/image.png")]
    [InlineData("http://127.0.0.1/image.png")]
    [InlineData("http://2130706433/image.png")]
    [InlineData("http://0x7f.1/image.png")]
    [InlineData("http://[::1]/image.png")]
    [InlineData("http://[::ffff:127.0.0.1]/image.png")]
    [InlineData("http://192.168.0.1/image.png")]
    [InlineData("http://100.64.0.1/image.png")]
    [InlineData("http://[fc00::1]/image.png")]
    [InlineData("https://printer.local/image.png")]
    [InlineData("file:///C:/image.png")]
    [InlineData("https://user:password@example.invalid/image.png")]
    public void LocalCredentialsAndNonHttpImageUrlsAreNeverActionable(string url)
    {
        Assert.False(LogMarkup.PublicHttpUrl(url, out _));
        Assert.Empty(LogMarkup.Parse("/*img=" + url + "*/"));
    }

    [Fact]
    public void HtmlIsPlainTextAndOnlyValidatedPublicHttpUrlsBecomeLinksOrImages()
    {
        var parts = LogMarkup.Parse("<script>ignored()</script> https://example.invalid/a /*img=https://example.invalid/a.png*/");
        Assert.Contains("<script>ignored()</script>", string.Concat(parts.Select(part => part.Text)));
        Assert.Equal("https://example.invalid/a", Assert.Single(parts, part => part.Link is not null && part.Image is null).Link!.AbsoluteUri);
        Assert.Equal("https://example.invalid/a.png", Assert.Single(parts, part => part.Image is not null).Image!.AbsoluteUri);
        var badFont = Assert.Single(LogMarkup.Parse("/*font=bad;url(file)*/text"));
        Assert.Null(badFont.Style.Font);
    }
}
