using PhiClient.Core;

namespace PhiClient.Core.Tests;

public sealed class GameInputTests
{
    [Theory]
    [InlineData(ConsoleKey.D8, true, false, false, "go N")]
    [InlineData(ConsoleKey.K, false, false, false, "go b")]
    [InlineData(ConsoleKey.U, true, false, true, "go w")]
    [InlineData(ConsoleKey.O, false, false, true, "turn e")]
    [InlineData(ConsoleKey.UpArrow, true, true, false, "go")]
    [InlineData(ConsoleKey.LeftArrow, true, true, false, "turn l")]
    [InlineData(ConsoleKey.NumPad8, true, true, false, "go N")]
    [InlineData(ConsoleKey.I, false, false, true, "turn n")]
    public void MovementUsesConfirmedMapStyleAndDistinguishesArrowsFromNumpad(
        ConsoleKey key, bool northUp, bool shift, bool control, string command)
    {
        var action = Assert.IsType<InputAction>(GameInput.Resolve(key, northUp, shift: shift, control: control));
        Assert.Equal([command], action.Commands);
        Assert.True(action.Repeatable);
    }

    [Fact]
    public void FullKeysDoNotStealAltMagicShiftMapSizesOrTextNavigation()
    {
        Assert.Equal(["hit"], GameInput.Resolve(ConsoleKey.J, true)!.Commands);
        Assert.Null(GameInput.Resolve(ConsoleKey.J, true, fullKeys: false));
        Assert.Equal(["cast", "unlock", "see"], GameInput.Resolve(ConsoleKey.U, true, alt: true)!.Commands);
        Assert.Equal(["#ex-map size=57"], GameInput.Resolve(ConsoleKey.D7, true, shift: true)!.Commands);
        Assert.Equal(["#ex-map size=40"], GameInput.Resolve(ConsoleKey.D5, true, shift: true)!.Commands);
        Assert.Equal(InputIntent.PreviousTab, GameInput.Resolve(ConsoleKey.Tab, true, control: true, shift: true)!.Intent);
        Assert.Null(GameInput.Resolve(ConsoleKey.UpArrow, true, control: true, shift: true));
        Assert.Null(GameInput.Resolve(ConsoleKey.Z, true, shift: true));
        Assert.Equal(["cast"], GameInput.Resolve(ConsoleKey.Enter, true, extended: true)!.Commands);
        Assert.Null(GameInput.Resolve(ConsoleKey.Enter, true));
    }

    [Fact]
    public void MagicAndDestructiveActionsRequirePreparationOrConfirmation()
    {
        var call = GameInput.Resolve(ConsoleKey.B, true, alt: true)!;
        Assert.True(call.AutoUnequip);
        Assert.NotEmpty(call.Confirmation!);
        Assert.Equal(1, call.HiddenLists);
        Assert.False(call.Repeatable);
        var search = GameInput.Resolve(ConsoleKey.S, true, alt: true)!;
        Assert.NotEmpty(search.Prompt!);
        Assert.False(search.AutoUnequip);
        Assert.Equal(["cast", "search", "{}"], search.Commands);
        Assert.Equal(["cast", "7"], GameInput.Resolve(ConsoleKey.F7, true)!.Commands);
        Assert.Equal(InputIntent.ForceDisconnectAll, GameInput.Resolve(ConsoleKey.Pause, true, alt: true)!.Intent);
    }

    [Fact]
    public void ListsTakePrecedenceOverMovementAndAttack()
    {
        Assert.Equal(InputIntent.ListNumber, GameInput.Resolve(ConsoleKey.D8, true, listing: true)!.Intent);
        Assert.Equal(8, GameInput.Resolve(ConsoleKey.D8, true, listing: true)!.Number);
        Assert.Equal(InputIntent.ListToggle, GameInput.Resolve(ConsoleKey.Spacebar, true, listing: true)!.Intent);
        Assert.Equal(InputIntent.ListDown, GameInput.Resolve(ConsoleKey.DownArrow, true, listing: true)!.Intent);
        Assert.Equal(["."], GameInput.Resolve(ConsoleKey.LeftArrow, true, listing: true)!.Commands);
        Assert.Equal(["-"], GameInput.Resolve(ConsoleKey.OemMinus, true, listing: true)!.Commands);
        Assert.Equal(["get"], GameInput.Resolve(ConsoleKey.Z, true, listing: true)!.Commands);
    }

    [Fact]
    public void SelectionRetainsCheckedItemsAcrossPagesAndUsesServerNumbers()
    {
        var selection = new ChoiceSelection();
        selection.Receive(Enumerable.Range(1, 9).Select(n => new Choice(n.ToString(), $"Item {n}")).ToArray());
        selection.ToggleCursor();
        var next = Assert.IsType<SelectionCommand>(selection.Number(9));
        Assert.Equal(new SelectionCommand("9", true), next);
        selection.Begin(next);
        Assert.True(selection.Waiting);
        selection.Receive(Enumerable.Range(9, 3).Select(n => new Choice(n.ToString(), $"Item {n}")).ToArray());
        Assert.False(selection.Waiting);
        Assert.Equal(8, selection.PageStart);
        Assert.Equal("9", selection.Number(1)!.Text);
        Assert.Equal(".", selection.Number(4)!.Text);
        selection.ToggleCursor();
        Assert.Equal("1 9", selection.Confirm().Text);
        selection.Begin(selection.Confirm());
        Assert.False(selection.Active);
        Assert.Empty(selection.Checked);
    }

    [Fact]
    public void ListsPreserveUnnumberedRowsAndUseTheFirstRowOffsetForEveryWireNumber()
    {
        var parser = new ProtocolParser();
        parser.Feed("#list"u8.ToArray());
        parser.Feed(PhiWire.Encode("説明"));
        parser.Feed(PhiWire.Encode("[8] /*r*/Item/*.*/"));
        parser.Feed(PhiWire.Encode("備考"));
        var list = Assert.IsType<Choices>(Assert.Single(parser.Feed("#end-list"u8.ToArray())));
        Assert.Equal(new[] { "1", "2", "3" }, list.Items.Select(item => item.Number));
        Assert.Equal(new[] { "説明", "[8] /*r*/Item/*.*/", "備考" }, list.Items.Select(item => item.Raw));
        var selection = new ChoiceSelection();
        selection.Receive(list.Items);
        Assert.Equal("2", selection.Number(2)!.Text);
        Assert.Null(selection.Number(9));
        parser.Feed("#list"u8.ToArray());
        parser.Feed(PhiWire.Encode("Page /*r*/12/*.*/"));
        parser.Feed(PhiWire.Encode("not numbered"));
        list = Assert.IsType<Choices>(Assert.Single(parser.Feed("#end-list"u8.ToArray())));
        Assert.Equal(new[] { "12", "13" }, list.Items.Select(item => item.Number));
    }

    [Fact]
    public void OnlyMatchingPagesExtendAndOnlyTheNineKeyRequestsContinuation()
    {
        var selection = new ChoiceSelection();
        selection.Receive(Enumerable.Range(1, 9).Select(number => new Choice($"{number}", "row")).ToArray());
        Assert.False(selection.Pick(8).NextPage);
        selection.Toggle(8);
        Assert.Equal(new SelectionCommand("9"), selection.Confirm());
        selection.Begin(selection.Number(9)!);
        Assert.Null(selection.Number(9));
        selection.Receive([new("4", "unrelated")]);
        Assert.Single(selection.Items);
        Assert.Empty(selection.Checked);
        Assert.Equal(0, selection.PageStart);
        selection.Receive(Enumerable.Range(1, 9).Select(number => new Choice($"{number}", "row")).ToArray());
        selection.Toggle(0);
        selection.Receive([new("9", "matching without a wait flag")]);
        Assert.Equal(9, selection.Items.Count);
        Assert.Contains("1", selection.Checked);
        Assert.Equal(8, selection.PageStart);
    }

    [Fact]
    public void EmptyCompletedListsDoNotEraseSelectionButExplicitEndAndInvalidOffsetsDo()
    {
        var model = GameUpdate.Apply(new(), new GameMessage.Received(new Choices([new("1", "row")])));
        Assert.True(GameUpdate.Apply(model, new GameMessage.Received(new Choices([]))).Selection.Active);
        Assert.False(GameUpdate.Apply(model, new GameMessage.Received(new Choices([], false))).Selection.Active);
        var parser = new ProtocolParser();
        parser.Feed("#list"u8.ToArray());
        Assert.IsType<LogLine>(Assert.Single(parser.Feed(PhiWire.Encode("[" + new string('9', 100) + "] bad"))));
        var rejected = Assert.IsType<Choices>(Assert.Single(parser.Feed("#end-list"u8.ToArray())));
        Assert.False(rejected.Complete);
        Assert.Empty(rejected.Items);
    }

    [Theory]
    [InlineData("first\r\nsecond", 9, false, "second")]
    [InlineData("first\rsecond", 9, true, "+second")]
    [InlineData("first\r\nsecond", 6, false, "first")]
    [InlineData("first\r", 6, false, null)]
    [InlineData("first\r\nsecond", 2, true, "+first")]
    [InlineData("*not a shout", 3, false, "/**/*not a shout")]
    [InlineData("*not a shout", 3, true, "/**/*not a shout")]
    [InlineData("one\n\nthree", 4, false, "/**/")]
    [InlineData("one\n", 4, false, null)]
    public void ChatSendsOnlyCurrentLineAndProtectsAccidentalShouts(string text, int caret, bool party, string? expected) =>
        Assert.Equal(expected, GameInput.ChatLine(text, caret, party));

    [Theory]
    [InlineData("hello", 2, false, "*hello")]
    [InlineData("*hello", 2, false, "**hello")]
    [InlineData("hello", 2, true, "*hello")]
    [InlineData("one\n\nlast", 4, false, "/**/")]
    [InlineData("one\n", 4, false, null)]
    public void ExplicitShoutingUsesTheUnescapedCurrentLineAndOriginalBlankLineRules(
        string text, int caret, bool party, string? expected) =>
        Assert.Equal(expected, GameInput.ChatLine(text, caret, party, loud: true));

    [Theory]
    [InlineData("hello", 2, "", "", 0)]
    [InlineData("hello", 5, "say ", "say ", 4)]
    [InlineData("one\r\ntwo", 2, "", "two", 0)]
    [InlineData("one\r\ntwo", 4, "", "two", 0)]
    [InlineData("one\rtwo", 2, "", "two", 0)]
    [InlineData("one\ntwo\nthree", 5, "", "one\nthree", 4)]
    [InlineData("one\n\nthree", 4, "", "one\nthree", 4)]
    [InlineData("one\r\ntwo", 7, "next", "one\r\nnext", 9)]
    [InlineData("one\ntwo", 7, "", "one\n", 4)]
    public void SuccessfulChatRemovesOnlyTheSentLineAndKeepsUnsentLines(
        string text, int caret, string prefix, string expected, int expectedCaret) =>
        Assert.Equal((expected, expectedCaret), GameInput.AfterChatSend(text, caret, prefix));

    [Fact]
    public void SuccessfulChatDoesNotTruncateOtherUnsentLines()
    {
        var draft = string.Join('\n', Enumerable.Range(1, 100));
        var after = GameInput.AfterChatSend(draft, draft.Length);
        var lines = after.Text.Split('\n');
        Assert.Equal(100, lines.Length);
        Assert.Equal("1", lines[0]);
        Assert.Equal("99", lines[^2]);
        Assert.Empty(lines[^1]);
        Assert.Equal(after.Text.Length, after.Caret);
    }

    [Fact]
    public void CompletedChatClearsTheCapturedDraftDespiteLogUpdatesAndNativeTextEchoes()
    {
        var draft = GameUpdate.Apply(new(), new GameMessage.InputChanged("hello"));
        var current = GameUpdate.Apply(draft, new GameMessage.Logged("unrelated log"));
        current = GameUpdate.Apply(current, new GameMessage.InputChanged("hello"));
        var sent = GameUpdate.Apply(current, new GameMessage.ChatSent(draft.InputRevision, 5));
        Assert.Empty(sent.Input);
        Assert.Equal(draft.InputRevision + 1, sent.InputRevision);
        Assert.Equal("hello", draft.Input);
    }

    [Theory]
    [InlineData("new draft")]
    [InlineData("hello")]
    public void CompletedChatNeverClearsAnEditedDraftEvenWhenTheTextMatchesAgain(string replacement)
    {
        var draft = GameUpdate.Apply(new(), new GameMessage.InputChanged("hello"));
        var current = GameUpdate.Apply(draft, new GameMessage.InputChanged("temporary edit"));
        current = GameUpdate.Apply(current, new GameMessage.InputChanged(replacement));
        var sent = GameUpdate.Apply(current, new GameMessage.ChatSent(draft.InputRevision, 5));
        Assert.Equal(replacement, sent.Input);
        Assert.Equal(current.InputRevision, sent.InputRevision);
    }

    [Fact]
    public void ChatThrottleAndPrefixEditingRespectTheOriginalBoundaries()
    {
        Assert.True(GameInput.ChatReady(0, long.MinValue));
        Assert.False(GameInput.ChatReady(1099, 1000));
        Assert.True(GameInput.ChatReady(1100, 1000));
        Assert.Equal(("one\nprefix", 10), GameInput.WithChatPrefix("one\n", 4, "prefix"));
        Assert.Equal(("one\ntext", 5), GameInput.WithChatPrefix("one\ntext", 5, "prefix"));
        Assert.Equal(("one\nprefix", 10), GameInput.WithChatPrefix("one\ntext", 5, "prefix", replaceLast: true));
        Assert.Equal(("one\ntext", 2), GameInput.WithChatPrefix("one\ntext", 2, "prefix", replaceLast: true));
    }

    [Fact]
    public void ChatModesAreTabLocalAndReconnectPreservesModesWhileResettingTheAttemptTimer()
    {
        var original = new GameModel();
        var edited = GameUpdate.Apply(original, new GameMessage.InputChanged("unsent"));
        edited = GameUpdate.Apply(edited, new GameMessage.LoudChanged(true, true));
        edited = GameUpdate.Apply(edited, new GameMessage.AllTabSpeechChanged(true));
        edited = GameUpdate.Apply(edited, new GameMessage.TalkPrefixChanged("prefix"));
        edited = GameUpdate.Apply(edited, new GameMessage.ChatAttempted(1000));
        var reconnected = GameUpdate.Apply(edited, new GameMessage.PhaseChanged(ConnectionPhase.Connecting));
        Assert.False(original.Loud);
        Assert.True(reconnected.Loud);
        Assert.True(reconnected.RepeatLoud);
        Assert.True(reconnected.AllTabSpeech);
        Assert.Equal("prefix", reconnected.TalkPrefix);
        Assert.Equal("unsent", reconnected.Input);
        Assert.Equal(edited.InputRevision, reconnected.InputRevision);
        Assert.True(GameInput.ChatReady(1001, reconnected.LastChatAttempt));
        var cancelled = GameUpdate.Apply(edited, new GameMessage.LoudChanged(false));
        Assert.False(cancelled.Loud);
        Assert.False(cancelled.RepeatLoud);
        Assert.True(edited.RepeatLoud);
    }

    [Fact]
    public void MapMouseUsesEightAngularSectorsRatherThanAlwaysMovingDiagonally()
    {
        Assert.Equal(["go N"], GameInput.MapMovement(1, -3, true));
        Assert.Equal(["go E"], GameInput.MapMovement(3, -1, true));
        Assert.Equal(["go", "go r"], GameInput.MapMovement(2, -2, false));
        Assert.Equal(["check", "look"], GameInput.MapMovement(0, 0, true));
    }

    [Theory]
    [InlineData(-1, -1, "turn l", "turn l")]
    [InlineData(0, -1, "go N", "go")]
    [InlineData(1, -1, "turn r", "turn r")]
    [InlineData(-1, 0, "go W", "go l")]
    [InlineData(0, 0, "hit", "hit")]
    [InlineData(1, 0, "go E", "go r")]
    [InlineData(0, 1, "go S", "go b")]
    public void MovementPadKeepsScreenPositionsAndMapOrientation(int x, int y, string north, string rotating)
    {
        Assert.Equal(north.Split('|'), GameInput.MapPad(x, y, true).Commands);
        Assert.Equal(rotating.Split('|'), GameInput.MapPad(x, y, false).Commands);
    }

    [Fact]
    public void MovementPadRejectsCoordinatesOutsideItsThreeRowsAndColumns()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => GameInput.MapPad(-2, 0, true));
        Assert.Throws<ArgumentOutOfRangeException>(() => GameInput.MapPad(0, 2, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => GameInput.MapPad(-1, 1, false));
        Assert.Throws<ArgumentOutOfRangeException>(() => GameInput.MapPad(1, 1, true));
    }
}
