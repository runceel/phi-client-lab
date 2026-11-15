using System.Net;
using System.Text.Json;

namespace PhiClient.Core;

public sealed record ProductRelease(string Version, Uri Url);

public sealed class ProductReleaseClient : IDisposable
{
    public const string Repository = "runceel/phi-client-lab";
    private readonly HttpClient http;

    public ProductReleaseClient(HttpMessageHandler? handler = null)
    {
        http = new(handler ?? new HttpClientHandler
        {
            AllowAutoRedirect = false, UseCookies = false, UseProxy = false
        }) { Timeout = TimeSpan.FromSeconds(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("PhiClient/1.0");
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    public async Task<ProductRelease?> LatestAsync(CancellationToken token = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        using var response = await http.GetAsync($"https://api.github.com/repos/{Repository}/releases/latest",
            HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        const int limit = 128 * 1024;
        if (response.Content.Headers.ContentLength > limit)
            throw new InvalidDataException("リリース情報が128KiBを超えています。");
        await using var input = await response.Content.ReadAsStreamAsync(deadline.Token);
        using var output = new MemoryStream();
        var buffer = new byte[4096];
        while (true)
        {
            var count = await input.ReadAsync(buffer, deadline.Token);
            if (count == 0) break;
            if (output.Length + count > limit) throw new InvalidDataException("リリース情報が128KiBを超えています。");
            output.Write(buffer, 0, count);
        }
        using var json = JsonDocument.Parse(output.ToArray());
        var root = json.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("tag_name", out var tag) || tag.ValueKind != JsonValueKind.String ||
            !root.TryGetProperty("html_url", out var url) || url.ValueKind != JsonValueKind.String ||
            !Uri.TryCreate(url.GetString(), UriKind.Absolute, out var link) ||
            link.Scheme != "https" || link.Host != "github.com" || link.UserInfo.Length != 0 ||
            !link.IsDefaultPort || !link.AbsolutePath.StartsWith("/" + Repository + "/releases/",
                StringComparison.Ordinal) || ParseVersion(tag.GetString()!) is null)
            throw new InvalidDataException("このアプリのリリース情報を確認できませんでした。");
        var version = tag.GetString()!;
        return new(version.StartsWith('v') ? version[1..] : version, link);
    }

    public static Version? ParseVersion(string text)
    {
        var parts = (text.StartsWith('v') ? text[1..] : text).Split('.');
        if (parts.Length is < 1 or > 4 || parts.Any(part => part.Length == 0 ||
            !part.All(char.IsAsciiDigit) || !int.TryParse(part, out _))) return null;
        var numbers = parts.Select(int.Parse).Concat([0, 0, 0, 0]).Take(4).ToArray();
        return new(numbers[0], numbers[1], numbers[2], numbers[3]);
    }

    public void Dispose() => http.Dispose();
}
