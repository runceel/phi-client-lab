using PhiClient.Core;

namespace PhiClient.Core.Tests;

public sealed class LogEntryTests
{
    [Fact]
    public void ParserPreservesRawTagsAndDistinguishesKnownTalkPrivNpcAndLiteral()
    {
        var parser = new ProtocolParser();
        parser.Feed(PhiWire.Encode($"#user {1,4} {"Alice",-31}"));
        var talk = Assert.IsType<LogLine>(Assert.Single(parser.Feed(PhiWire.Encode("Alice > /*color=red*/hello/*.*/"))));
        Assert.Equal(LogChannel.Talk, talk.Channel);
        Assert.Equal("Alice", talk.From);
        Assert.Equal("Alice > hello", talk.Text);
        Assert.Equal("Alice > /*color=red*/hello/*.*/", talk.Raw);
        var npc = Assert.IsType<LogLine>(Assert.Single(parser.Feed(PhiWire.Encode("NPC > hello"))));
        Assert.Equal(LogChannel.Log, npc.Channel);
        Assert.Null(npc.From);
        var priv = Assert.IsType<LogLine>(Assert.Single(parser.Feed(PhiWire.Encode("[Alice] > /*to=Bob*/hello"))));
        Assert.Equal(LogChannel.Priv, priv.Channel);
        Assert.Equal("Alice", priv.From);
        parser.Feed("#more"u8.ToArray());
        var literal = Assert.IsType<LogLine>(Assert.Single(parser.Feed(PhiWire.Encode("[Alice] > #ch-srv hidden"))));
        Assert.True(literal.Literal);
        Assert.Equal(LogChannel.Log, literal.Channel);
        Assert.Null(literal.From);
    }

    [Fact]
    public void ImmutableLogHistoryKeepsWholeRawRowsTimeAndCopyOptionsDespiteDisplayTruncation()
    {
        var time = new DateTimeOffset(2026, 1, 1, 12, 34, 0, TimeSpan.FromHours(9));
        var raw = "Alice > /*color=red*/hello/*.*/";
        var model = GameUpdate.Apply(new(), new GameMessage.Received(new LogLine("Alice > hello")
            { Raw = raw, Channel = LogChannel.Talk, From = "Alice" }, 0, time));
        var entry = Assert.Single(model.LogEntries);
        Assert.Equal(raw, entry.Copy(new()));
        Assert.Equal("12:34 " + raw, entry.Copy(new ClientOptions { LogTimeDelete = false }));
        Assert.Equal("hello", entry.Copy(new ClientOptions { LogTagDeleteCopy = true, LogNameDelete = true }));
        Assert.Equal("Alice > /＊color=red*/hello/＊.*/", entry.Copy(new ClientOptions { LogTagCancel = true }));
        Assert.Equal(raw, entry.Copy(new ClientOptions { LogTagDeleteAlways = true }));
        Assert.Equal("Alice > hello", entry.Copy(new ClientOptions { LogTagDeleteAlways = true }, true));
        var longRaw = "Alice > " + new string('x', 9000);
        var later = GameUpdate.Apply(model, new GameMessage.Received(new LogLine(longRaw) { Raw = longRaw }, 1, time));
        Assert.EndsWith(" [display truncated]", later.Log[^1]);
        Assert.Equal(longRaw, later.LogEntries[^1].Copy(new()));
        Assert.Single(model.LogEntries);
        Assert.NotEqual(entry.Id, later.LogEntries[^1].Id);
        Assert.Equal(time, later.LogEntries[^1].Time);
    }
}
