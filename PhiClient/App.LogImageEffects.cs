using PhiClient;
using PhiClient.Core;

partial class App
{
    private LogImageClient? logImageClient;
    private readonly SemaphoreSlim imageDownloads = new(2, 2);
    private long imageRequest;

    private void ObserveLogImages(ClientMessage message)
    {
        if (disposed || !model.Options.LogImageDisp || message is not
            (ClientMessage.GameChanged or ClientMessage.OptionsChanged or ClientMessage.GameSelected))
            return;
        var candidates = model.Games.Values.SelectMany(game => game.LogEntries.Concat(
                game.Priv.Current?.Entries ?? []))
            .SelectMany(row => LogFormat.Parts(row, model.Options))
            .Where(part => part.Image is not null).Select(part => part.Image!)
            .Distinct().Where(uri => !model.LogImages.ContainsKey(uri.AbsoluteUri))
            .Take(LogImages.MaximumEntries - model.LogImages.Count).ToArray();
        foreach (var uri in candidates)
        {
            var request = ++imageRequest;
            Dispatch(new ClientMessage.LogImageLoading(uri.AbsoluteUri, request));
            _ = LoadLogImageAsync(uri, request);
        }
    }

    private async Task LoadLogImageAsync(Uri uri, long request)
    {
        var token = worldLifetime.Token;
        var acquired = false;
        try
        {
            await imageDownloads.WaitAsync(token);
            acquired = true;
            logImageClient ??= new();
            var data = await logImageClient.LoadAsync(uri, token);
            var decoded = await LogImageDecoder.DecodeAsync(data.Bytes, token);
            if (!disposed)
                Dispatch(new ClientMessage.LogImageLoaded(uri.AbsoluteUri, request,
                    decoded.Width, decoded.Height, decoded.Pixels, decoded.Animation));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch (Exception ex) when (ex is HttpRequestException or IOException or ArgumentException or
            OperationCanceledException or System.Runtime.InteropServices.COMException)
        {
            if (!disposed)
                Dispatch(new ClientMessage.LogImageFailed(uri.AbsoluteUri, request, "画像を表示できませんでした: " + ex.Message));
        }
        finally
        {
            if (acquired) imageDownloads.Release();
        }
    }

    private void ReportLogImageFailure(Uri uri, long request, string error)
    {
        if (!disposed)
            Dispatch(new ClientMessage.LogImagePresentationFailed(uri.AbsoluteUri, request,
                "画像の連続表示に失敗しました: " + error));
    }
}
