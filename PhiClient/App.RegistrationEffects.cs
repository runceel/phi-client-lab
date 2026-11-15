using System.Runtime.InteropServices.WindowsRuntime;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using PhiClient.Core;

partial class App
{
    private long registrationGraphicsRequest;
    private (long Request, string Number) registrationPreviewKey;
    private WriteableBitmap? registrationPreview;
    private string registrationPreviewMessage = "";
    private double registrationRasterScale = 1;
    private CancellationTokenSource? registrationGraphicsCancellation;

    private async Task LoadRegistrationGraphicsAsync()
    {
        if (model.RegistrationGraphics.Loading) return;
        var request = ++registrationGraphicsRequest;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(worldLifetime.Token);
        registrationGraphicsCancellation = cancellation;
        Dispatch(new ClientMessage.RegistrationGraphicsLoading(request));
        try
        {
            if (!int.TryParse(port, out var number))
                throw new ArgumentException("TCPポートを数字で入力してください。");
            PhiConnection.ValidateEndpoint(host, number);
            var names = await PhiConnection.RegistrationGraphicsAsync(host.Trim(), number, cancellation.Token);
            Dispatch(new ClientMessage.RegistrationGraphicsLoaded(request, names));
            if (model.RegistrationGraphics.Request == request) await PrepareRegistrationPreviewAsync();
        }
        catch (Exception error) when (IsExpected(error))
        {
            Dispatch(new ClientMessage.RegistrationGraphicsFailed(request,
                error is OperationCanceledException ? "30秒以内に一覧の返事がありませんでした。接続先を確認して再試行してください。" : error.Message));
        }
        finally
        {
            if (ReferenceEquals(registrationGraphicsCancellation, cancellation)) registrationGraphicsCancellation = null;
        }
    }

    private void SelectRegistrationGraphic(int index)
    {
        Dispatch(new ClientMessage.RegistrationGraphicSelected(index));
        _ = PrepareRegistrationPreviewAsync();
    }

    private async Task PrepareRegistrationPreviewAsync()
    {
        var candidates = model.RegistrationGraphics;
        var number = model.Draft.Image;
        registrationPreviewKey = (candidates.Request, number);
        registrationPreview = null;
        registrationPreviewMessage = "";
        if (!int.TryParse(number, out var index) || index < 0 || index >= candidates.Names.Length) { refresh(); return; }
        var key = registrationPreviewKey;
        var raster = registrationRasterScale;
        var folder = graphics.Folder;
        try
        {
            var pixels = await graphics.RegistrationPreviewAsync(candidates.Names[index]);
            if (model.RegistrationGraphics.Request != key.Request || model.Draft.Image != key.Number ||
                raster != registrationRasterScale || folder != graphics.Folder) return;
            pixels = pixels.ScaleNearest((int)Math.Round(32 * raster), (int)Math.Round(32 * raster));
            var bitmap = new WriteableBitmap(pixels.Width, pixels.Height);
            using (var buffer = bitmap.PixelBuffer.AsStream()) buffer.Write(pixels.Pixels);
            bitmap.Invalidate();
            registrationPreview = bitmap;
            registrationPreviewMessage = $"{candidates.Names[index]}：正面の大きい絵・コマ0";
        }
        catch (Exception error) when (IsExpected(error))
        {
            if (model.RegistrationGraphics.Request != key.Request || model.Draft.Image != key.Number) return;
            registrationPreviewMessage = error.Message;
        }
        refresh();
    }

    private void AttachRegistrationPreview(Image image, WriteableBitmap bitmap)
    {
        image.Source = bitmap;
        Microsoft.UI.Xaml.XamlRoot? mountedRoot = null;
        void UpdateScale()
        {
            if (image.XamlRoot is not { } root || registrationRasterScale == root.RasterizationScale) return;
            registrationRasterScale = root.RasterizationScale;
            _ = PrepareRegistrationPreviewAsync();
        }
        void RootChanged(Microsoft.UI.Xaml.XamlRoot _, Microsoft.UI.Xaml.XamlRootChangedEventArgs __) => UpdateScale();
        image.Loaded += (_, _) =>
        {
            mountedRoot = image.XamlRoot;
            if (mountedRoot is not null) mountedRoot.Changed += RootChanged;
            UpdateScale();
        };
        image.Unloaded += (_, _) =>
        {
            if (mountedRoot is not null) mountedRoot.Changed -= RootChanged;
        };
    }
}
