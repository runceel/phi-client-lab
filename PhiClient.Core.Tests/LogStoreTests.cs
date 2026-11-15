using PhiClient.Core;

namespace PhiClient.Core.Tests;

public sealed class LogStoreTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), "phi-log-test-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    private static LogEntry Row(string text, DateTimeOffset? time = null) =>
        new(0, ProtocolParser.PlainText(text), text,
            time ?? new DateTimeOffset(2026, 5, 6, 7, 8, 0, TimeSpan.FromHours(9)));

    [Fact]
    public async Task DailyHtmlAndTextAppendInOrderAndAlwaysContainLocalTimes()
    {
        var store = new LogStore(folder);
        var options = new ClientOptions { LogDispTime = false };
        var writes = Enumerable.Range(0, 12).Select(index =>
            store.AppendAsync("冒険者", Row($"行 {index} /*r*/赤/*.*/"), options)).ToArray();
        await Task.WhenAll(writes);
        var path = await store.AppendAsync("冒険者", Row("最終行"), options);
        var html = await File.ReadAllTextAsync(path);
        Assert.Equal(1, html.Split("<!doctype html>").Length - 1);
        Assert.Contains("07:08 ", html);
        Assert.Contains("color:red;", html);
        Assert.True(html.IndexOf("行 0 ", StringComparison.Ordinal) < html.IndexOf("行 11 ", StringComparison.Ordinal));
        Assert.Contains("最終行", html);
        var textPath = await store.AppendAsync("冒険者", Row("/*r*/文字/*.*/ /*br*/ 続き"),
            options with { LogSaveHtml = false });
        Assert.Equal("07:08 文字   続き\n", await File.ReadAllTextAsync(textPath));
        var tomorrow = await store.AppendAsync("冒険者", Row("翌日", Row("").Time!.Value.AddDays(1)), options);
        Assert.NotEqual(path, tomorrow);
        Assert.EndsWith("20260507.html", tomorrow);
    }

    [Fact]
    public async Task BufferEscapesServerHtmlAndFontsAndCannotAutomaticallyFetchImages()
    {
        var store = new LogStore(folder);
        var file = await store.SaveBufferAsync("CON.log", new[] {
            Row("<script>untrusted</script> /*img=https://example.invalid/image.png*/"),
            new LogEntry(1, "/*r*/literal", "/*r*/literal", Row("").Time, Literal: true)
        }, new ClientOptions { LogFontName = "'</style><script>bad</script>", LogDispTime = false });
        Assert.StartsWith(Path.Combine(folder, "_CON.log") + Path.DirectorySeparatorChar, file);
        var html = await File.ReadAllTextAsync(file);
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("&lt;script&gt;untrusted&lt;/script&gt;", html);
        Assert.Equal(1, html.Split("</style>").Length - 1);
        Assert.DoesNotContain("<img", html);
        Assert.Contains("img-src 'none'", html);
        Assert.Contains("/*r*/literal", html);
        Assert.DoesNotContain("07:08", html);
        Assert.NotEqual(file, await store.SaveBufferAsync("CON.log", Array.Empty<LogEntry>(), new()));
    }

    [Fact]
    public async Task InvalidDestinationIsReportedAndDoesNotPoisonSubsequentWrites()
    {
        Directory.CreateDirectory(folder);
        var obstruction = Path.Combine(folder, "blocked");
        await File.WriteAllTextAsync(obstruction, "fixture");
        var store = new LogStore(obstruction);
        await Assert.ThrowsAnyAsync<IOException>(() => store.AppendAsync("name", Row("line"), new()));
        File.Delete(obstruction);
        var file = await store.AppendAsync("name", Row("retry"), new());
        Assert.Contains("retry", await File.ReadAllTextAsync(file));
    }
}
