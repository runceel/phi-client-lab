using PhiClient.Core;

namespace PhiClient.Core.Tests;

public sealed class MapZoomLayoutTests
{
    [Theory]
    [InlineData(600, 220)]
    [InlineData(320, 160)]
    [InlineData(200, 40)]
    [InlineData(160, 0)]
    [InlineData(100, 0)]
    public void ShortCompactWindowsPrioritizeTheChatAllocation(double body, double expectedMap)
    {
        Assert.Equal(expectedMap, MapZoomLayout.CompactHeight(body));
    }

    [Theory]
    [InlineData(184, 60, 7, false)]
    [InlineData(344, 400, 7, false)]
    [InlineData(900, 200, 15, false)]
    [InlineData(344, 144, 15, true)]
    [InlineData(1600, 1200, 5, false)]
    [InlineData(4, 4, 15, false)]
    public void AutoFitsRenderedDimensionsIncludingTheNormalMapTopMargin(
        double width, double height, int dimension, bool eagle)
    {
        var zoom = MapZoomLayout.Resolve(0, dimension, eagle, width, height);
        Assert.InRange(zoom, double.Epsilon, 4);
        Assert.True(Math.Round(dimension * 32 * zoom) + 2 <= width);
        Assert.True(Math.Round((dimension * 32 + (eagle ? 0 : 16)) * zoom) + 2 <= height);
    }

    [Theory]
    [InlineData(600, 150, 410)]
    [InlineData(220, 128, 52)]
    [InlineData(112, 100, 4)]
    public void MapBudgetExcludesMeasuredVitalsAndOtherChrome(double pane, double footer, double expected)
    {
        Assert.Equal(expected, MapZoomLayout.ViewportHeight(pane, footer));
    }

    [Fact]
    public void ManualPreferenceIsNotOverwrittenByASmallWindow()
    {
        Assert.Equal(1.75, MapZoomLayout.Resolve(175, 7, false, 32, 32));
        Assert.Equal(1.75, MapZoomLayout.Resolve(175, 7, false, 1000, 1000));
        Assert.True(MapZoomLayout.Resolve(0, 7, false, 32, 32) < 0.5);
    }

    [Theory]
    [InlineData(184, 40)]
    [InlineData(344, 360)]
    [InlineData(900, 200)]
    public void FittedScaleUsesExistingMapHitCoordinates(double width, double height)
    {
        var zoom = MapZoomLayout.Resolve(0, 7, false, width, height);
        Assert.Equal((-2, -2), MapLabels.Hit(48 * zoom, 64 * zoom, 7, zoom));
        Assert.Null(MapLabels.Hit(48 * zoom, 8 * zoom, 7, zoom));
        Assert.Null(MapLabels.Hit(224 * zoom, 64 * zoom, 7, zoom));
    }
}
