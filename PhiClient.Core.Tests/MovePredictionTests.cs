using PhiClient.Core;

namespace PhiClient.Core.Tests;

public sealed class MovePredictionTests
{
    [Fact]
    public void GuessesRespectWaitAndServerCadenceAndAcknowledgmentsConsumeTheMatchingPrefix()
    {
        var original = new MovePrediction().OnMap(new(0, 0), 0).Sent("go N", 0).Sent("go e", 20);
        Assert.Same(original, original.Tick(119));
        var first = original.Tick(120);
        Assert.Equal(new MapPosition(0, -1), first.Offset);
        Assert.Same(first, first.Tick(219));
        var acknowledged = first.OnMap(new(0, -1), 200);
        Assert.Empty(acknowledged.Guessed);
        Assert.Single(acknowledged.Queue);
        Assert.Equal(230, acknowledged.Wait);
        Assert.Equal(new MapPosition(0, 0), original.Offset);
        var both = first.OnMap(new(1, -1), 1000);
        Assert.Empty(both.Queue);
        Assert.Empty(both.Guessed);
        Assert.Equal(1000, both.Wait);
        Assert.Same(original, original.Sent("go NN", 0));
        Assert.Same(original, original.Sent("say go N", 0));
    }

    [Fact]
    public void LateMapsSamePositionWarpsFailuresAndResetFollowSourceSemantics()
    {
        var pending = new MovePrediction().OnMap(new(0, 0), 0).Sent("go N", 0).Sent("go E", 10);
        var guessed = pending.Tick(120);
        var same = guessed.OnMap(new(0, 0), 150);
        Assert.Empty(same.Guessed);
        Assert.Single(same.Queue);
        var warp = guessed.OnMap(new(99, 99), 150);
        Assert.Empty(warp.Queue);
        Assert.Empty(warp.Guessed);
        var late = guessed.OnMap(new(0, -1), 1000);
        Assert.Empty(late.Samples);
        Assert.Single(late.Queue);
        var rejectedGuess = guessed.CannotGo(150);
        Assert.Empty(rejectedGuess.Guessed);
        Assert.Single(rejectedGuess.Queue);
        var rejectedWait = pending.CannotGo(300);
        Assert.Single(rejectedWait.Queue);
        Assert.Equal(330, rejectedWait.Wait);
        var reset = rejectedWait.Reset();
        Assert.Empty(reset.Queue);
        Assert.Null(reset.Base);
        Assert.Equal(330, reset.Wait);

        var adaptive = new MovePrediction().OnMap(new(0, 0), 0);
        for (var i = 0; i < 9; i++)
            adaptive = adaptive.Sent("go E", i * 1000).OnMap(new(i + 1, 0), i * 1000 + (i == 0 ? 800 : 50));
        Assert.Equal(120, adaptive.Wait);
        Assert.Equal(8, adaptive.Samples.Length);
    }

    [Fact]
    public void ModelRejectsLiteralMovementFailureAndResetsPredictionWhenDisabled()
    {
        var model = ClientUpdate.Apply(new(), new ClientMessage.GameOpened("a"));
        model = ClientUpdate.Apply(model, new ClientMessage.GameChanged("a",
            new GameMessage.CommandSent("go N", 0)));
        model = ClientUpdate.Apply(model, new ClientMessage.GameChanged("a", new GameMessage.PredictionTick(120)));
        var literal = ClientUpdate.Apply(model, new ClientMessage.GameChanged("a",
            new GameMessage.Received(new LogLine("DM > Can not go.") { Literal = true }, 150)));
        Assert.Equal(new MapPosition(0, -1), literal.Games["a"].Prediction.Offset);
        var failure = ClientUpdate.Apply(model, new ClientMessage.GameChanged("a",
            new GameMessage.Received(new LogLine("DM > Can not go."), 150)));
        Assert.Equal(new MapPosition(0, 0), failure.Games["a"].Prediction.Offset);
        var disabled = ClientUpdate.Apply(model, new ClientMessage.OptionsChanged(model.Options with { Automap = false }));
        Assert.Empty(disabled.Games["a"].Prediction.Guessed);
        Assert.Empty(disabled.Games["a"].Prediction.Queue);
    }
}
