namespace PhiClient.Core;

public static class MapPaneLayout
{
    public const int Minimum = 200;
    public const int Maximum = 1600;

    public static int MaximumFor(double availableWidth)
    {
        if (!double.IsFinite(availableWidth) || availableWidth < 0)
            throw new ArgumentOutOfRangeException(nameof(availableWidth));
        return (int)Math.Clamp(Math.Floor(availableWidth - 360), Minimum, Maximum);
    }

    public static int Width(int requested, double availableWidth) =>
        Math.Clamp(requested, Minimum, MaximumFor(availableWidth));
}
