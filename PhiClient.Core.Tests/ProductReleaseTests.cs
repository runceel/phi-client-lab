using System.Net;
using System.Text;
using PhiClient.Core;

namespace PhiClient.Core.Tests;

public sealed class ProductReleaseTests
{
    [Theory]
    [InlineData("v1.2", "1.2.0.0")]
    [InlineData("1", "1.0.0.0")]
    [InlineData("1.2.3.4", "1.2.3.4")]
    [InlineData("1..2", null)]
    [InlineData("v1.2-beta", null)]
    [InlineData("vv1.2", null)]
    [InlineData("1.-2", null)]
    [InlineData("2147483648", null)]
    public void ParseVersion_UsesTheOriginalNumericComparison(string text, string? expected) =>
        Assert.Equal(expected, ProductReleaseClient.ParseVersion(text)?.ToString());

    [Fact]
    public async Task Latest_UsesOnlyThisProductsApiAndAcceptsItsReleasePage()
    {
        using var client = new ProductReleaseClient(new Handler(request =>
        {
            Assert.Equal("https://api.github.com/repos/runceel/phi-client-lab/releases/latest",
                request.RequestUri!.AbsoluteUri);
            Assert.Null(request.Headers.Authorization);
            Assert.NotEmpty(request.Headers.UserAgent);
            return new(HttpStatusCode.OK) { Content = new StringContent(
                """{"tag_name":"v1.2.3","html_url":"https://github.com/runceel/phi-client-lab/releases/tag/v1.2.3"}""") };
        }));
        var result = await client.LatestAsync();
        Assert.Equal("1.2.3", result!.Version);
        Assert.Equal("https://github.com/runceel/phi-client-lab/releases/tag/v1.2.3", result.Url.AbsoluteUri);
    }

    [Theory]
    [InlineData("https://github.com/s7k/wphi/releases/tag/v1.2")]
    [InlineData("https://example.invalid/runceel/phi-client-lab/releases/tag/v1.2")]
    [InlineData("http://github.com/runceel/phi-client-lab/releases/tag/v1.2")]
    [InlineData("https://user@github.com/runceel/phi-client-lab/releases/tag/v1.2")]
    public async Task Latest_RejectsOtherProductAndUntrustedLinks(string url)
    {
        using var client = new ProductReleaseClient(new Handler(_ => new(HttpStatusCode.OK) {
            Content = new StringContent($$"""{"tag_name":"v1.2","html_url":"{{url}}"}""")
        }));
        await Assert.ThrowsAsync<InvalidDataException>(() => client.LatestAsync());
    }

    [Fact]
    public async Task Latest_SeparatesNoPublishedReleaseFromHttpAndOversizeFailures()
    {
        using var missing = new ProductReleaseClient(new Handler(_ => new(HttpStatusCode.NotFound)));
        Assert.Null(await missing.LatestAsync());
        using var denied = new ProductReleaseClient(new Handler(_ => new(HttpStatusCode.Forbidden)));
        await Assert.ThrowsAsync<HttpRequestException>(() => denied.LatestAsync());
        using var huge = new ProductReleaseClient(new Handler(_ => new(HttpStatusCode.OK) {
            Content = new StringContent(new string(' ', 128 * 1024 + 1), Encoding.UTF8)
        }));
        await Assert.ThrowsAsync<InvalidDataException>(() => huge.LatestAsync());
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) =>
            Task.FromResult(respond(request));
    }
}
