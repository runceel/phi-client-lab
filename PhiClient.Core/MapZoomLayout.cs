namespace PhiClient.Core;

public static class MapZoomLayout
{
    public static double CompactHeight(double bodyHeight)
    {
        if (!double.IsFinite(bodyHeight) || bodyHeight < 0)
            throw new ArgumentOutOfRangeException(nameof(bodyHeight));
        return Math.Clamp(bodyHeight - 160, 0, 220);
    }

    public static double ViewportHeight(double paneHeight, double footerHeight)
    {
        if (!double.IsFinite(paneHeight) || paneHeight < 0 ||
            !double.IsFinite(footerHeight) || footerHeight < 0)
            throw new ArgumentOutOfRangeException(nameof(paneHeight));
        // The command row is 32 DIP with two 4 DIP gaps around the map.
        return Math.Max(4, paneHeight - footerHeight - 32 - 2 * 4);
    }

    public static double Resolve(int savedZoom, int dimension, bool eagle, double width, double height)
    {
        if (dimension is < 1 or > 15)
            throw new ArgumentOutOfRangeException(nameof(dimension));
        if (!double.IsFinite(width) || width <= 2) throw new ArgumentOutOfRangeException(nameof(width));
        if (!double.IsFinite(height) || height <= 2) throw new ArgumentOutOfRangeException(nameof(height));
        if (savedZoom != 0) return Math.Clamp(savedZoom, 50, 400) / 100.0;
        return Math.Min(4, Math.Min((width - 2) / (dimension * 32),
            (height - 2) / (dimension * 32 + (eagle ? 0 : 16))));
    }
}
