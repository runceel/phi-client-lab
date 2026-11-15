namespace PhiClient;

internal static class TitleBarLayout
{
    internal static double AvailableTitleWidth(double windowWidth, double actionsRight, double captionInset) =>
        Math.Max(0, windowWidth - 2 * (Math.Max(actionsRight, captionInset) + 8));
}
