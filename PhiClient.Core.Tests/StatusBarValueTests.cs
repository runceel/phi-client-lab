using PhiClient.Core;

namespace PhiClient.Core.Tests;

public sealed class StatusBarValueTests
{
    [Theory]
    [InlineData(30L, 100L, 30d)]
    [InlineData(10L, 200L, 5d)]
    [InlineData(200L, 100L, 100d)]
    [InlineData(-1L, 100L, 0d)]
    [InlineData(30L, 0L, 0d)]
    [InlineData(30L, -1L, 0d)]
    [InlineData(null, 100L, 0d)]
    [InlineData(30L, null, 0d)]
    [InlineData(long.MaxValue, long.MaxValue, 100d)]
    public void BarClampsFillWithoutChangingReportedValues(long? current, long? maximum, double expected)
    {
        var value = new StatusBarValue(current, maximum);
        Assert.Equal(expected, value.Percentage, precision: 8);
    }

    [Theory]
    [InlineData(200, 100, "200 / 100")]
    [InlineData(-1, 100, "-1 / 100")]
    public void ClampedFillStillReportsActualServerValues(long current, long maximum, string expectedText)
    {
        Assert.Equal(expectedText, new StatusBarValue(current, maximum).Text);
    }

    [Fact]
    public void UnknownMaximumAndZeroRemainDistinctWhileBothHaveEmptyFill()
    {
        var unknown = new StatusBarValue(30, null);
        var zero = new StatusBarValue(30, 0);
        Assert.Equal("30 / --", unknown.Text);
        Assert.Equal("30 / 0", zero.Text);
        Assert.Equal(0, unknown.Percentage);
        Assert.Equal(0, zero.Percentage);
    }
}
