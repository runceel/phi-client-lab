using System.Collections.Immutable;

namespace PhiClient.Core;

public sealed record LogImageState(long Request, bool Loading = true, string Error = "",
    int Width = 0, int Height = 0, ImmutableArray<byte> Pixels = default);

public static class LogImages
{
    public const int MaximumEntries = 16;
    public const int MaximumPixels = 1024 * 1024;

    public static bool ValidPixels(int width, int height, ImmutableArray<byte> pixels) =>
        width > 0 && height > 0 && (long)width * height <= MaximumPixels &&
        !pixels.IsDefault && pixels.Length == (long)width * height * 4;
}
