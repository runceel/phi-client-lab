using PhiClient.Core;

namespace PhiClient.Core.Tests;

public class LogScrollStateTests
{
    [Fact]
    public void BrowsingKeepsAnchorUntilUserReturnsToLatest()
    {
        var state = new LogScrollState();
        Assert.Equal(500, state.Target(500, _ => null));
        state.UserScrolled(120, 500);
        state.Anchor = new(7, 3, -4);
        Assert.Equal(95, state.Target(600, anchor => anchor.Row == 7 ? 95 : null));
        Assert.False(state.Following);
        state.UserScrolled(600, 600);
        Assert.True(state.Following);
        Assert.Equal(700, state.Target(700, _ => 95));
    }

    [Fact]
    public void LayoutClampingDoesNotChangeBrowsingIntent()
    {
        var state = new LogScrollState();
        state.UserScrolled(100, 500);
        state.Anchor = new(2, 0, 0);
        Assert.Equal(0, state.Target(0, _ => -30));
        Assert.False(state.Following);
        Assert.Equal(100, state.Target(500, _ => 100));
        Assert.Equal(500, state.Target(500, _ => 900));
        Assert.False(state.Following);
    }

    [Fact]
    public void LatestButtonAndGlobalSettingDoNotSilentlyEnableEachOther()
    {
        var state = new LogScrollState();
        state.Configure(false);
        state.UserScrolled(120, 500);
        state.Latest();
        Assert.Equal(500, state.Target(500, _ => null));
        Assert.False(state.Enabled);
        state.Offset = 500;
        state.Anchor = new(20, 0, 0);
        Assert.Equal(500, state.Target(600, _ => 500));
        state.Configure(true);
        Assert.Equal(600, state.Target(600, _ => null));
        state.UserScrolled(200, 600);
        state.Latest();
        Assert.Equal(700, state.Target(700, _ => 200));
    }
}
