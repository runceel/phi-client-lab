using PhiClient.Core;

namespace PhiClient.Core.Tests;

public sealed class PrivTests
{
    [Fact]
    public void IncomingGroupsSortDeduplicateExcludeSelfAndRequireFirstGroupReplyConfirmation()
    {
        var raw = "[Alice] > /*to=Self*//*to=Bob*//*to=Alice*/hello";
        var entry = new LogEntry(0, "hello", raw, null, LogChannel.Priv, "Alice");
        var empty = new PrivState();
        var state = empty.Received(entry, "Self");
        Assert.Empty(empty.Conversations);
        Assert.True(state.Visible);
        var conversation = Assert.Single(state.Conversations);
        Assert.Equal(new[] { "Alice", "Bob" }, conversation.To.ToArray());
        Assert.True(conversation.Confirm);
        Assert.Equal(Priv.Key(new[] { "Bob", "Alice" }), state.Active);
        var explicitOpen = empty.Open(new[] { "Bob", "Alice" }).Received(entry, "Self");
        Assert.False(Assert.Single(explicitOpen.Conversations).Confirm);
        Assert.False(Assert.Single(state.Confirmed(state.Active!).Conversations).Confirm);
        Assert.Single(state.Received(entry with { From = "Bob" }, "Self").Conversations);
        Assert.Empty(empty.Received(entry with { Literal = true }, "Self").Conversations);
    }

    [Fact]
    public void SendUsesResolvedNumbersTagsAndSelfEchoAndReportsOnlyMissingRecipients()
    {
        var result = Priv.Send(new[] { "Alice", "Absent", "Bob" }, "本文",
            new[] { new User(1, "Self"), new User(4, "Alice"), new User(9, "Bob") }, "Self");
        Assert.Equal(new[] {
            "priv 4 /*to=Alice*//*to=Bob*/本文",
            "priv 9 /*to=Alice*//*to=Bob*/本文",
            "priv 1 /*to=Alice*//*to=Bob*/本文"
        }, result.Lines);
        Assert.Equal(new[] { "Absent" }, result.Failed);
        Assert.Empty(Priv.Send(new[] { "Absent" }, "本文", Array.Empty<User>(), "Self").Lines);
        Assert.Throws<ArgumentException>(() => Priv.Send(new[] { "bad*/name" }, "本文", Array.Empty<User>(), "Self"));
        Assert.Throws<ArgumentException>(() => Priv.Send(new[] { "Alice" }, "本文\ninjected", Array.Empty<User>(), "Self"));
    }

    [Fact]
    public void ConversationCapRetainsActiveConversationAndDropsOldestInactive()
    {
        var state = new PrivState().Open(new[] { "active" });
        for (var i = 0; i < 60; i++)
            state = state.Received(new(i, "message", null, null, LogChannel.Priv, $"other-{i}"), "self");
        Assert.Equal(50, state.Conversations.Length);
        Assert.Equal("active", state.Active);
        Assert.Contains(state.Conversations, conversation => conversation.Key == "active");
        Assert.DoesNotContain(state.Conversations, conversation => conversation.Key == "other-0");
        var selected = state.Select("other-59");
        Assert.False(selected.Current!.Unread);
        Assert.True(state.Conversations.Single(conversation => conversation.Key == "other-59").Unread);
    }

    [Fact]
    public void RosterWaitUsesNewResponsesThreeHundredMillisecondsQuietTenSecondRateLimitAndOneMinuteTimeout()
    {
        var wait = new UserListRefresh();
        Assert.True(wait.CanRequest(0));
        wait = wait.Begin(1000, 3);
        Assert.False(wait.CanRequest(10_000));
        Assert.False(wait.Complete(59_000, 3, 0));
        Assert.False(wait.Complete(1599, 4, 1300));
        Assert.True(wait.Complete(1600, 4, 1300));
        Assert.True(wait.Complete(61_000, 3, 0));
        Assert.False(wait.Finish().CanRequest(10_999));
        Assert.True(wait.Finish().CanRequest(11_000));
    }

    [Fact]
    public void MvuKeepsGroupMessagesIsolatedRetainsChangedDraftAndExcludesRosterClearFromResponseRevision()
    {
        var before = GameUpdate.Apply(new(), new GameMessage.Received(new PlayerName("Self")));
        var received = GameUpdate.Apply(before, new GameMessage.Received(new LogLine("[Alice] > hello")
            { Raw = "[Alice] > /*to=Bob*/hello", Channel = LogChannel.Priv, From = "Alice" }));
        Assert.Empty(before.Priv.Conversations);
        Assert.Equal(new[] { "Alice", "Bob" }, Assert.Single(received.Priv.Conversations).To.ToArray());
        received = GameUpdate.Apply(received, new GameMessage.PrivDraftChanged("new draft"));
        Assert.Equal("new draft", GameUpdate.Apply(received, new GameMessage.PrivSent("old draft")).Priv.Draft);
        Assert.Empty(GameUpdate.Apply(received, new GameMessage.PrivSent("new draft")).Priv.Draft);
        var listed = GameUpdate.Apply(received, new GameMessage.Received(new UserList([new(1, "Self")]), 123));
        var cleared = GameUpdate.Apply(listed, new GameMessage.Received(new UserList([]) { Cleared = true }, 456));
        Assert.Empty(cleared.Users);
        Assert.Equal(listed.UsersRevision, cleared.UsersRevision);
        Assert.Equal(123, cleared.UsersUpdatedAt);
        var refreshing = GameUpdate.Apply(cleared, new GameMessage.UsersRefreshStarted(1000));
        Assert.Null(GameUpdate.Apply(refreshing, new GameMessage.PhaseChanged(ConnectionPhase.Offline)).UserRefresh.Started);
        Assert.True(GameUpdate.Apply(refreshing, new GameMessage.Received(new WorldMoved("127.0.0.1", 2000)))
            .UserRefresh.CanRequest(1001));
    }
}
