using System.Collections.Immutable;
using PhiClient.Core;

namespace PhiClient.Core.Tests;

public sealed class LogImagesTests
{
    [Fact]
    public void AnimationBudgetsUseTheGifCanvasAndPresentationErrorsBelongToTheirRequest()
    {
        const string url = "https://example.invalid/a.gif";
        var loading = ClientUpdate.Apply(new(), new ClientMessage.LogImageLoading(url, 7));
        LogAnimation Header(int width, int height, int count) =>
            new([.. "GIF89a"u8.ToArray(), (byte)width, (byte)(width >> 8), (byte)height, (byte)(height >> 8)], count);
        foreach (var animation in new[]
        {
            Header(1, 1, 65), Header(0, 1, 2), Header(4096, 4096, 2),
            Header(1024, 1024, 5), new LogAnimation([1, 2, 3], 2),
            new LogAnimation(default, 2)
        })
            Assert.Same(loading, ClientUpdate.Apply(loading,
                new ClientMessage.LogImageLoaded(url, 7, 1, 1, [1, 2, 3, 255], animation)));
        var ready = ClientUpdate.Apply(loading,
            new ClientMessage.LogImageLoaded(url, 7, 1, 1, [1, 2, 3, 255], Header(2, 2, 2)));
        Assert.Equal(2, ready.LogImages[url].Animation!.Frames);
        Assert.Same(ready, ClientUpdate.Apply(ready, new ClientMessage.LogImagePresentationFailed(url, 6, "old")));
        var failed = ClientUpdate.Apply(ready, new ClientMessage.LogImagePresentationFailed(url, 7, "native failed"));
        Assert.Equal("native failed", failed.LogImages[url].Error);
        Assert.Null(failed.LogImages[url].Animation);
        Assert.True(failed.LogImages[url].Pixels.IsDefault);
        Assert.Same(failed, ClientUpdate.Apply(failed, new ClientMessage.LogImagePresentationFailed(url, 7, "late")));
    }

    [Fact]
    public void OnlyTheOwnedPendingRequestCanPublishPixelsOrAnError()
    {
        const string url = "https://example.invalid/a.png";
        var empty = new ClientModel();
        var loading = ClientUpdate.Apply(empty, new ClientMessage.LogImageLoading(url, 7));
        Assert.True(loading.LogImages[url].Loading);
        Assert.Empty(empty.LogImages);
        Assert.Same(loading, ClientUpdate.Apply(loading,
            new ClientMessage.LogImageLoaded(url, 6, 1, 1, [1, 2, 3, 255])));
        Assert.Same(loading, ClientUpdate.Apply(loading,
            new ClientMessage.LogImageFailed(url, 6, "stale")));
        var ready = ClientUpdate.Apply(loading, new ClientMessage.LogImageLoaded(url, 7, 1, 1, [1, 2, 3, 255]));
        Assert.False(ready.LogImages[url].Loading);
        Assert.Equal<byte>([1, 2, 3, 255], ready.LogImages[url].Pixels);
        Assert.Same(ready, ClientUpdate.Apply(ready, new ClientMessage.LogImageFailed(url, 7, "late")));
        var failed = ClientUpdate.Apply(loading, new ClientMessage.LogImageFailed(url, 7, "decode failed"));
        Assert.False(failed.LogImages[url].Loading);
        Assert.Equal("decode failed", failed.LogImages[url].Error);
        Assert.True(failed.LogImages[url].Pixels.IsDefault);
    }

    [Fact]
    public void UntrustedPixelSizesAndExtraCacheEntriesAreRejected()
    {
        const string url = "https://example.invalid/a.png";
        var loading = ClientUpdate.Apply(new(), new ClientMessage.LogImageLoading(url, 1));
        foreach (var (width, height, pixels) in new (int, int, ImmutableArray<byte>)[]
        {
            (0, 1, []), (1, 0, []), (int.MaxValue, int.MaxValue, []),
            (LogImages.MaximumPixels + 1, 1, []), (1, 1, [1, 2, 3]), (1, 1, default)
        })
            Assert.Same(loading, ClientUpdate.Apply(loading,
                new ClientMessage.LogImageLoaded(url, 1, width, height, pixels)));
        for (var index = 1; index < LogImages.MaximumEntries; index++)
            loading = ClientUpdate.Apply(loading, new ClientMessage.LogImageLoading(url + index, index));
        Assert.Equal(LogImages.MaximumEntries, loading.LogImages.Count);
        Assert.Same(loading, ClientUpdate.Apply(loading, new ClientMessage.LogImageLoading(url + "/extra", 99)));
        Assert.Same(loading, ClientUpdate.Apply(loading, new ClientMessage.LogImageLoading(url, 99)));
        Assert.Empty(ClientUpdate.Apply(new(), new ClientMessage.LogImageLoading("http://127.0.0.1/a", 1)).LogImages);
    }
}
