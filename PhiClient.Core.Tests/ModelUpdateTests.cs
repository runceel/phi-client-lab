using PhiClient.Core;

namespace PhiClient.Core.Tests;

public sealed class ModelUpdateTests
{
    [Fact]
    public void ServerMessagesUpdateOnlyTheirCharacterAndDoNotMutatePriorSnapshots()
    {
        var empty = new ClientModel();
        var opened = ClientUpdate.Apply(ClientUpdate.Apply(empty, new ClientMessage.GameOpened("alpha")),
            new ClientMessage.GameOpened("beta"));
        var connected = ClientUpdate.Apply(opened, new ClientMessage.GameChanged("alpha",
            new GameMessage.Received(new ConnectionReady())));
        var changed = ClientUpdate.Apply(connected, new ClientMessage.GameChanged("alpha",
            new GameMessage.Received(new StatusLine("Alpha : 1 : 2 : 3 : 4 : 5 : 6 : 7 : 8 : 9 : 10"))));
        Assert.Empty(empty.Games);
        Assert.Null(opened.Games["alpha"].Stats);
        Assert.Equal(ConnectionPhase.Offline, opened.Games["alpha"].Phase);
        Assert.Equal(ConnectionPhase.Connected, changed.Games["alpha"].Phase);
        Assert.Equal("Alpha", changed.Games["alpha"].Stats!.Name);
        Assert.Null(changed.Games["beta"].Stats);
        Assert.Equal("beta", changed.ActiveKey);
    }

    [Fact]
    public void SelectionTransitionsAreCopyOnWriteAcrossPagesAndCancellation()
    {
        var initial = new GameModel();
        var listed = GameUpdate.Apply(initial, new GameMessage.Received(
            new Choices(Enumerable.Range(1, 9).Select(i => new Choice($"{i}", $"Item {i}")).ToArray())));
        var checkedFirst = GameUpdate.Apply(listed, new GameMessage.SelectionToggled(0));
        var waiting = GameUpdate.Apply(checkedFirst, new GameMessage.SelectionBegan(checkedFirst.Selection.Number(9)));
        var next = GameUpdate.Apply(waiting, new GameMessage.Received(new Choices([new("9", "Last item")])));
        var checkedLast = GameUpdate.Apply(next, new GameMessage.SelectionToggled(8));
        Assert.Empty(initial.Selection.Items);
        Assert.Empty(listed.Selection.Checked);
        Assert.False(checkedFirst.Selection.Waiting);
        Assert.True(waiting.Selection.Waiting);
        Assert.Equal("1 9", checkedLast.Selection.Confirm().Text);
        Assert.Equal("1", next.Selection.Confirm().Text);
        var cancelled = GameUpdate.Apply(checkedLast, new GameMessage.SelectionCleared());
        Assert.False(cancelled.Selection.Active);
        Assert.True(checkedLast.Selection.Active);
    }

    [Fact]
    public void ReconnectionResetsServerStateButRetainsDraftsAndHistory()
    {
        var model = GameUpdate.Apply(new(), new GameMessage.InputChanged("retained chat"));
        model = GameUpdate.Apply(model, new GameMessage.BoardChanged("retained sign"));
        model = GameUpdate.Apply(model, new GameMessage.Received(new MapSetNotice(2, "castle")));
        model = GameUpdate.Apply(model, new GameMessage.Received(new Conditions("*******")));
        model = GameUpdate.Apply(model, new GameMessage.Received(new EagleFrame(1, 1, 0, 0, [(byte)' '], [0])));
        var restarted = GameUpdate.Apply(model, new GameMessage.PhaseChanged(ConnectionPhase.Connecting));
        Assert.Equal("retained chat", restarted.Input);
        Assert.Equal("retained sign", restarted.BoardText);
        Assert.Equal("default", restarted.MapSets[2]);
        Assert.Null(restarted.Eagle);
        Assert.False(restarted.ShowEagle);
        Assert.Empty(restarted.Conditions.Active);
        Assert.Equal("[Connecting...]", Assert.Single(restarted.Log));
        Assert.Equal("castle", model.MapSets[2]);
    }

    [Fact]
    public void ClosedTabsCannotBeResurrectedByLateEffectCompletions()
    {
        var model = ClientUpdate.Apply(new(), new ClientMessage.GameOpened("alpha"));
        model = ClientUpdate.Apply(model, new ClientMessage.GameOpened("beta"));
        model = ClientUpdate.Apply(model, new ClientMessage.GameClosed("beta", "alpha"));
        var late = ClientUpdate.Apply(model, new ClientMessage.GameChanged("beta", new GameMessage.Received(new ConnectionReady())));
        Assert.Equal("alpha", late.ActiveKey);
        Assert.False(late.Games.ContainsKey("beta"));
        Assert.Same(model, late);
        var closing = new GameModel { Phase = ConnectionPhase.Closing };
        Assert.Equal(ConnectionPhase.Closing,
            GameUpdate.Apply(closing, new GameMessage.Received(new ConnectionReady())).Phase);
    }

    [Fact]
    public void DialogEditsAndProfileDraftsAreDataNotNativeControlsOrEffects()
    {
        var old = new ClientModel();
        var draft = ClientUpdate.Apply(old, new ClientMessage.DraftChanged(new(Name: "New character", Host: "localhost")));
        var dialog = ClientUpdate.Apply(draft, new ClientMessage.DialogChanged(new("Pay", "", "Send", "Amount")));
        var typed = ClientUpdate.Apply(dialog, new ClientMessage.DialogEdited("123"));
        Assert.Equal("", old.Draft.Name);
        Assert.Equal("", dialog.Dialog!.Value);
        Assert.Equal("123", typed.Dialog!.Value);
        var closed = ClientUpdate.Apply(typed, new ClientMessage.DialogChanged(null));
        Assert.Null(closed.Dialog);
        Assert.Equal("New character", closed.Draft.Name);
        Assert.Equal("123", typed.Dialog.Value);
    }

    [Fact]
    public void LogRetentionBoundsAreEnforcedWithoutChangingPriorHistory()
    {
        var empty = new GameModel();
        var model = empty;
        for (var i = 0; i < 510; i++) model = GameUpdate.Apply(model, new GameMessage.Logged($"{i}"));
        Assert.Equal(500, model.Log.Length);
        Assert.Equal("10", model.Log[0]);
        Assert.Equal("509", model.Log[^1]);
        for (var i = 0; i < 20; i++) model = GameUpdate.Apply(model, new GameMessage.Logged(new string('x', 9000)));
        Assert.True(model.LogCharacters <= 100000);
        Assert.Equal(model.Log.Sum(line => line.Length), model.LogCharacters);
        Assert.EndsWith(" [display truncated]", model.Log[^1]);
        Assert.Empty(empty.Log);
    }
}
