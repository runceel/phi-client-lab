using PhiClient.Core;

namespace PhiClient.Core.Tests;

public sealed class LogSearchTests
{
    [Fact]
    public void HighlightRangesUseVisibleUtf16TextWithoutParagraphMarkers()
    {
        (long Id, string Text)[] rows = [(1, "00:01 prefix"), (2, "00:02 猫😀 link"), (3, "00:03 wrapped\n猫")];
        var result = LogSearch.Find(rows, "猫");
        var ranges = LogSearch.HighlightRanges(rows, result).ToArray();
        var plain = string.Concat(rows.Select(row => row.Text));
        Assert.Equal([2L, 3L], ranges.Select(range => range.Id).ToArray());
        Assert.Equal("00:02 猫😀 link", plain.Substring(ranges[0].Start, ranges[0].Length));
        Assert.Equal("00:03 wrapped\n猫", plain.Substring(ranges[1].Start, ranges[1].Length));
        Assert.Equal(rows[0].Text.Length, ranges[0].Start);
        Assert.Equal(rows[0].Text.Length + rows[1].Text.Length, ranges[1].Start);
    }

    [Fact]
    public void SearchStartsAtNewestAndCyclesInBothDirections()
    {
        var result = LogSearch.Find([(1L, "HELLO"), (2L, "different"), (3L, "hello again")], "Hello");
        Assert.Equal([1L, 3L], result.Hits.ToArray());
        Assert.Equal((3L, 2), (result.Current, result.Position));
        result = result.Step(-1);
        Assert.Equal(1L, result.Current);
        Assert.Equal(3L, result.Step(-1).Current);
        Assert.Equal(3L, result.Step(1).Current);
    }

    [Fact]
    public void ArrivalPreservesCurrentButEvictionFallsBackToNewestMatch()
    {
        var current = LogSearch.Find([(1L, "猫"), (2L, "猫")], "猫").Step(-1).Current;
        Assert.Equal(1L, LogSearch.Find([(1L, "猫"), (2L, "猫"), (3L, "猫")], "猫", current).Current);
        Assert.Equal(3L, LogSearch.Find([(2L, "猫"), (3L, "猫")], "猫", current).Current);
        Assert.Null(LogSearch.Find([(2L, "犬")], "猫", current).Current);
    }

    [Theory]
    [InlineData("")]
    [InlineData("missing")]
    public void EmptyAndUnmatchedQueriesNeverSelectOrStepToARow(string query)
    {
        var result = LogSearch.Find([(7L, "retained log")], query, 7);
        Assert.Empty(result.Hits);
        Assert.Null(result.Step(-1).Step(1).Current);
        Assert.Equal(0, result.Position);
    }

    [Fact]
    public void SearchVisibilityIsPerCharacterAndSurvivesReconnectWithoutChangingDraft()
    {
        var model = ClientUpdate.Apply(new(), new ClientMessage.GameOpened("a"));
        model = ClientUpdate.Apply(model, new ClientMessage.GameOpened("b"));
        model = ClientUpdate.Apply(model, new ClientMessage.GameChanged("a", new GameMessage.InputChanged("下書き")));
        var changed = ClientUpdate.Apply(model,
            new ClientMessage.GameChanged("a", new GameMessage.LogSearchVisibilityChanged(true)));
        Assert.NotEqual(model, changed);
        Assert.True(changed.Games["a"].LogSearchVisible);
        Assert.False(changed.Games["b"].LogSearchVisible);
        var reconnected = GameUpdate.Apply(changed.Games["a"],
            new GameMessage.PhaseChanged(ConnectionPhase.Connecting));
        Assert.True(reconnected.LogSearchVisible);
        Assert.Equal("下書き", reconnected.Input);
    }
}
