using System.Collections.Immutable;
using System.Buffers.Binary;

namespace PhiClient.Core;

public sealed record LogImageState(long Request, bool Loading = true, string Error = "",
    int Width = 0, int Height = 0, ImmutableArray<byte> Pixels = default, LogAnimation? Animation = null);
public sealed record LogAnimation(ImmutableArray<byte> Bytes, int Frames);

public static class LogImages
{
    public const int MaximumEntries = 16;
    public const int MaximumPixels = 1024 * 1024;
    public const int MaximumAnimationFrames = 64;
    public const int MaximumAnimationPixels = 4 * 1024 * 1024;

    public static bool ValidPixels(int width, int height, ImmutableArray<byte> pixels) =>
        width > 0 && height > 0 && (long)width * height <= MaximumPixels &&
        !pixels.IsDefault && pixels.Length == (long)width * height * 4;

    public static bool ValidAnimation(int width, int height, LogAnimation? animation)
    {
        if (animation is null) return true;
        if (animation.Frames is < 2 or > MaximumAnimationFrames || animation.Bytes.IsDefault ||
            animation.Bytes.Length is < 10 or > LogImageClient.MaximumBytes ||
            !(animation.Bytes.AsSpan().StartsWith("GIF87a"u8) || animation.Bytes.AsSpan().StartsWith("GIF89a"u8)))
            return false;
        var canvasWidth = BinaryPrimitives.ReadUInt16LittleEndian(animation.Bytes.AsSpan(6, 2));
        var canvasHeight = BinaryPrimitives.ReadUInt16LittleEndian(animation.Bytes.AsSpan(8, 2));
        return width > 0 && height > 0 && width <= canvasWidth && height <= canvasHeight &&
            (long)canvasWidth * canvasHeight <= MaximumPixels &&
            (long)canvasWidth * canvasHeight * animation.Frames <= MaximumAnimationPixels;
    }
}
