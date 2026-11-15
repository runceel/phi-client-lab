using System.Collections.Immutable;
using System.Runtime.InteropServices.WindowsRuntime;
using PhiClient.Core;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace PhiClient;

internal static class LogImageDecoder
{
    public static async Task<LogImageState> DecodeAsync(byte[] bytes, CancellationToken token)
    {
        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(bytes.AsBuffer()).AsTask(token);
        stream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(token);
        if (decoder.PixelWidth == 0 || decoder.PixelHeight == 0 ||
            (long)decoder.PixelWidth * decoder.PixelHeight > LogImages.MaximumPixels)
            throw new InvalidDataException("ログ画像の画素数が上限の1048576画素を超えています。");
        var pixels = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage)
            .AsTask(token);
        return new(0, false, Width: (int)decoder.PixelWidth, Height: (int)decoder.PixelHeight,
            Pixels: pixels.DetachPixelData().ToImmutableArray());
    }
}
