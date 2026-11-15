using PhiClient;

namespace PhiClient.Core.Tests;

public sealed class TitleBarLayoutTests
{
    [Theory]
    [InlineData(1200, 180, 138, 824)]
    [InlineData(760, 180, 138, 384)]
    [InlineData(600, 180, 138, 224)]
    [InlineData(480, 220, 138, 24)]
    [InlineData(320, 180, 138, 0)]
    [InlineData(1200, 70, 200, 784)]
    public void TitleWidthMirrorsTheLargerOccupiedEdgeAndNeverBecomesNegative(
        double windowWidth, double actionsRight, double captionInset, double expected)
    {
        var width = TitleBarLayout.AvailableTitleWidth(windowWidth, actionsRight, captionInset);

        Assert.Equal(expected, width);
        if (width > 0)
        {
            var left = (windowWidth - width) / 2;
            Assert.True(left >= actionsRight + 8);
            Assert.True(windowWidth - (left + width) >= captionInset + 8);
        }
    }
}
