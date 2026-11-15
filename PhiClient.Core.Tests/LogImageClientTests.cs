using System.Net;
using PhiClient.Core;

namespace PhiClient.Core.Tests;

public sealed class LogImageClientTests
{
    [Theory]
    [InlineData("http://127.0.0.1/image.png")]
    [InlineData("https://user:password@example.invalid/image.png")]
    [InlineData("file:///C:/image.png")]
    public async Task UnsafeUrlsAreRejectedBeforeRequestingAnyBytes(string url)
    {
        using var handler = new ResponseHandler(() => new(HttpStatusCode.OK));
        using var client = new LogImageClient(handler);
        await Assert.ThrowsAsync<ArgumentException>(() => client.LoadAsync(new(url)));
        Assert.Equal(0, handler.Requests);
    }

    [Fact]
    public async Task SmallRasterResponseIsBoundedAndReturnedWithoutTrustingItsContentType()
    {
        byte[] png = [137, 80, 78, 71, 13, 10, 26, 10, 0, 1];
        using var client = new LogImageClient(new ResponseHandler(() => new(HttpStatusCode.OK)
            { Content = new ByteArrayContent(png) }));
        var image = await client.LoadAsync(new("https://example.invalid/image.png"));
        Assert.Equal(png, image.Bytes);
        Assert.Equal("image/png", image.MediaType);
    }

    [Fact]
    public async Task RedirectIsAnExplicitFailureAndNeverFollowed()
    {
        using var handler = new ResponseHandler(() =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Redirect);
            response.Headers.Location = new("http://127.0.0.1/private.png");
            return response;
        });
        using var client = new LogImageClient(handler);
        await Assert.ThrowsAsync<InvalidDataException>(() => client.LoadAsync(new("https://example.invalid/image.png")));
        Assert.Equal(1, handler.Requests);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OversizeBodyIsRejectedWithAndWithoutAContentLength(bool declaredLength)
    {
        using var client = new LogImageClient(new ResponseHandler(() =>
        {
            HttpContent content = declaredLength
                ? new ByteArrayContent(new byte[LogImageClient.MaximumBytes + 1])
                : new StreamContent(new UnmeasuredStream(new byte[LogImageClient.MaximumBytes + 1]));
            return new(HttpStatusCode.OK) { Content = content };
        }));
        await Assert.ThrowsAsync<InvalidDataException>(() => client.LoadAsync(new("https://example.invalid/image.png")));
    }

    [Fact]
    public async Task MarkupPayloadIsNotAcceptedAsAnImage()
    {
        using var client = new LogImageClient(new ResponseHandler(() => new(HttpStatusCode.OK)
            { Content = new StringContent("<svg><script>ignored()</script></svg>") }));
        await Assert.ThrowsAsync<InvalidDataException>(() => client.LoadAsync(new("https://example.invalid/image.png")));
    }

    [Theory]
    [InlineData("8.8.8.8", true)]
    [InlineData("2606:4700:4700::1111", true)]
    [InlineData("127.0.0.1", false)]
    [InlineData("::ffff:10.0.0.1", false)]
    [InlineData("192.168.1.1", false)]
    [InlineData("100.64.0.1", false)]
    [InlineData("198.18.0.1", false)]
    [InlineData("192.0.2.1", false)]
    [InlineData("fe80::1", false)]
    [InlineData("fc00::1", false)]
    [InlineData("64:ff9b::a00:1", false)]
    [InlineData("2002:0a00:0001::", false)]
    [InlineData("2001:db8::1", false)]
    public void ResolvedNonPublicAndTunnelAddressesAreRejected(string address, bool allowed) =>
        Assert.Equal(allowed, LogImageClient.PublicAddress(IPAddress.Parse(address)));

    private sealed class ResponseHandler(Func<HttpResponseMessage> response) : HttpMessageHandler
    {
        public int Requests { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            Requests++;
            return Task.FromResult(response());
        }
    }

    private sealed class UnmeasuredStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override bool CanSeek => false;
    }
}
