using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics.Imaging;
using PhiClient;

partial class App
{
    private string MapImageFolder => Path.Combine(store.Folder, "MapImages");

    private void SaveMapImage(GameSession session) => _ = PerformAsync(async () =>
    {
        if (session.MapSurface is not FrameworkElement { IsLoaded: true, ActualWidth: > 0, ActualHeight: > 0 } surface)
            throw new InvalidOperationException("地図欄を表示してから画像を保存してください。");
        var eagle = session.ShowEagle;
        var map = session.Map;
        var eagleFrame = session.Eagle;
        var image = new RenderTargetBitmap();
        await image.RenderAsync(surface);
        if (image.PixelWidth is < 1 or > 4096 || image.PixelHeight is < 1 or > 4096)
            throw new InvalidOperationException("地図画像を4096ピクセル以内に収めてから保存してください。");
        var pixels = (await image.GetPixelsAsync()).ToArray();
        if (session.MapSurface != surface || session.ShowEagle != eagle || session.Map != map ||
            session.Eagle != eagleFrame || !surface.IsLoaded)
            throw new InvalidOperationException("保存中に地図の表示が変わりました。表示を確認して再試行してください。");
        if (pixels.Length != checked(image.PixelWidth * image.PixelHeight * 4))
            throw new IOException("地図画像のピクセルを取得できませんでした。表示を確認して再試行してください。");
        Directory.CreateDirectory(MapImageFolder);
        var name = $"{(eagle ? "eagle" : "map")}-{DateTime.Now:yyyyMMdd-HHmmssfff}-{Guid.NewGuid().ToString("N")[..8]}.png";
        var path = Path.Combine(MapImageFolder, name);
        var temporary = path + ".tmp";
        try
        {
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
            {
                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, file.AsRandomAccessStream());
                encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
                    (uint)image.PixelWidth, (uint)image.PixelHeight, 96, 96, pixels);
                await encoder.FlushAsync();
                file.Flush(true);
            }
            File.Move(temporary, path);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        notice = $"地図画像を保存しました: {name}";
    });

    private void OpenMapImageFolder() => _ = PerformAsync(async () =>
    {
        Directory.CreateDirectory(MapImageFolder);
        var folder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(MapImageFolder);
        if (!await Windows.System.Launcher.LaunchFolderAsync(folder))
            throw new IOException("地図画像の保存先を開けませんでした。");
    });

    private void AttachMapSurface(FrameworkElement element, GameSession session)
    {
        session.MapSurface = element;
        element.Unloaded += (_, _) =>
        {
            if (session.MapSurface == element) session.MapSurface = null;
        };
    }

    private void MountMapImage(Microsoft.UI.Xaml.Controls.Image image, WriteableBitmap bitmap)
    {
        image.Source = bitmap;
        XamlRoot? root = null;
        void Changed(XamlRoot _, XamlRootChangedEventArgs __) => refresh();
        image.Loaded += (_, _) =>
        {
            root = image.XamlRoot;
            if (root is not null) root.Changed += Changed;
            refresh();
        };
        image.Unloaded += (_, _) =>
        {
            if (root is not null) root.Changed -= Changed;
        };
    }
}
