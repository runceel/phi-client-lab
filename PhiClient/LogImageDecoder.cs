using System.Collections.Immutable;
using System.Buffers.Binary;
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
        LogAnimation? animation = null;
        if (decoder.DecoderInformation.CodecId == BitmapDecoder.GifDecoderId && decoder.FrameCount > 1)
        {
            if (decoder.FrameCount > LogImages.MaximumAnimationFrames)
                throw new InvalidDataException("GIFの表示上限は64コマです。");
            animation = new(bytes.ToImmutableArray(), checked((int)decoder.FrameCount));
            if (!LogImages.ValidAnimation((int)decoder.PixelWidth, (int)decoder.PixelHeight, animation))
                throw new InvalidDataException("GIFの表示上限は64コマ・全コマ合計4194304画素・1MiBです。");
            var canvasWidth = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(6, 2));
            var canvasHeight = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(8, 2));
            for (uint index = 0; index < decoder.FrameCount; index++)
            {
                var frame = await decoder.GetFrameAsync(index).AsTask(token);
                if (frame.PixelWidth == 0 || frame.PixelHeight == 0 ||
                    frame.PixelWidth > canvasWidth || frame.PixelHeight > canvasHeight)
                    throw new InvalidDataException("GIFのコマの寸法を確認してください。");
            }
        }
        var pixels = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied,
            new BitmapTransform(), ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage)
            .AsTask(token);
        return new(0, false, Width: (int)decoder.PixelWidth, Height: (int)decoder.PixelHeight,
            Pixels: pixels.DetachPixelData().ToImmutableArray(), Animation: animation);
    }
}
