using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using PhiClient.Core;

namespace PhiClient.Core.Tests;

public sealed class WorldTransferTests
{
    [Theory]
    [InlineData("success")]
    [InlineData("reserve-no")]
    [InlineData("source-no")]
    [InlineData("source-no-literal")]
    public async Task TransferUsesReservationProtocolAndPreservesFailedSourceLines(string outcome)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var origin = new TcpListener(IPAddress.Loopback, 0);
        using var target = new TcpListener(IPAddress.Loopback, 0);
        origin.Start();
        target.Start();
        var originPort = ((IPEndPoint)origin.LocalEndpoint).Port;
        var targetPort = ((IPEndPoint)target.LocalEndpoint).Port;
        using var connection = new PhiConnection();
        var events = new ConcurrentQueue<ServerEvent>();
        connection.Received += events.Enqueue;
        var reserveStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var bufferedDuringReserve = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var sourceRejects = outcome.StartsWith("source-no", StringComparison.Ordinal);

        var destinationServer = Task.Run(async () =>
        {
            using var peer = await target.AcceptTcpClientAsync(timeout.Token);
            var stream = peer.GetStream();
            using var reader = new StreamReader(stream, PhiWire.Encoding, false, 1024, true);
            Assert.Equal("#reserve synthetic-transfer-id", await reader.ReadLineAsync(timeout.Token));
            reserveStarted.TrySetResult();
            if (outcome == "reserve-no") await bufferedDuringReserve.Task.WaitAsync(timeout.Token);
            await Write(stream, outcome == "reserve-no" ? "#rsv-no\n" : "#rsv-ok\ndestination-after-ack\n", timeout.Token);
            if (outcome != "success")
            {
                Assert.Null(await reader.ReadLineAsync(timeout.Token));
                return;
            }
            Assert.Equal("#ch-srv-ok", await reader.ReadLineAsync(timeout.Token));
            var settings = new[]
            {
                "#version-cli 05107100", "#map-iv 2", "#status-iv 2",
                "#ex-switch eagleeye=form", "#ex-map size=57", "#ex-map style=solid",
                "#ex-switch ex-move-recv=true", "#ex-switch ex-list-mode-end=true",
                "#ex-switch ex-disp-magic=false", "#ex-switch ex-abspos=true"
            };
            foreach (var line in settings) Assert.Equal(line, await reader.ReadLineAsync(timeout.Token));
            await Write(stream, "destination-ready\n#x\n", timeout.Token);
        }, timeout.Token);

        var originServer = Task.Run(async () =>
        {
            using var peer = await origin.AcceptTcpClientAsync(timeout.Token);
            var stream = peer.GetStream();
            using var reader = new StreamReader(stream, PhiWire.Encoding, false, 1024, true);
            for (var i = 0; i < 11; i++) Assert.NotNull(await reader.ReadLineAsync(timeout.Token));
            await Write(stream, $"#ch-srv 127.0.0.1 {targetPort}\norigin-buffered\n", timeout.Token);
            if (outcome == "reserve-no")
            {
                await reserveStarted.Task.WaitAsync(timeout.Token);
                await Write(stream, "origin-during-reserve\n", timeout.Token);
                bufferedDuringReserve.TrySetResult();
                Assert.Equal("#no-srv", await reader.ReadLineAsync(timeout.Token));
                await Write(stream, "origin-recovered\n#x\n", timeout.Token);
                return;
            }
            Assert.Equal($"#trans 127.0.0.1 {targetPort}", await reader.ReadLineAsync(timeout.Token));
            await Write(stream, "origin-during-wait\n" +
                (outcome == "source-no-literal" ? "#more\n#trs-ok\n#end-more\n" : "") +
                (sourceRejects ? "#trs-no\norigin-after-reject\n" : "#trs-ok\norigin-after-success\n"),
                timeout.Token);
            if (sourceRejects)
            {
                Assert.Equal("#ch-srv-no", await reader.ReadLineAsync(timeout.Token));
                await Write(stream, "origin-recovered\n#x\n", timeout.Token);
            }
            else Assert.Null(await reader.ReadLineAsync(timeout.Token));
        }, timeout.Token);

        await Task.WhenAll(originServer, destinationServer,
            connection.RunAsync("127.0.0.1", originPort, "synthetic-transfer-id", timeout.Token))
            .WaitAsync(timeout.Token);
        var lines = events.OfType<LogLine>().Select(line => line.Text).ToArray();
        if (outcome == "success")
        {
            Assert.Single(events.OfType<WorldMoved>());
            Assert.Contains("destination-after-ack", lines);
            Assert.Contains("destination-ready", lines);
            Assert.DoesNotContain(lines, line => line.StartsWith("origin-", StringComparison.Ordinal));
        }
        else
        {
            Assert.Empty(events.OfType<WorldMoved>());
            var expected = sourceRejects
                ? new[] { "origin-buffered", "origin-during-wait", "origin-after-reject", "origin-recovered" }
                : ["origin-buffered", "origin-during-reserve", "origin-recovered"];
            Assert.Equal(expected, lines.Where(line => line.StartsWith("origin-", StringComparison.Ordinal)));
            if (outcome == "source-no-literal") Assert.Contains("#trs-ok", lines);
        }
        Assert.Equal([new TransferProgress(true), new TransferProgress(false)], events.OfType<TransferProgress>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnapprovedDifferentHostNeverReceivesCharacterId(bool explicitDenial)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var origin = new TcpListener(IPAddress.Loopback, 0);
        origin.Start();
        using var connection = new PhiConnection();
        var confirmations = 0;
        if (explicitDenial) connection.ConfirmTransferAsync = (host, port, _) =>
        {
            confirmations++;
            Assert.Equal("untrusted.invalid", host);
            Assert.Equal(2000, port);
            return Task.FromResult(false);
        };
        var server = Task.Run(async () =>
        {
            using var peer = await origin.AcceptTcpClientAsync(timeout.Token);
            var stream = peer.GetStream();
            using var reader = new StreamReader(stream, PhiWire.Encoding, false, 1024, true);
            for (var i = 0; i < 11; i++) await reader.ReadLineAsync(timeout.Token);
            await Write(stream, "#ch-srv untrusted.invalid 2000\n", timeout.Token);
            Assert.Equal("#no-srv", await reader.ReadLineAsync(timeout.Token));
            await Write(stream, "#x\n", timeout.Token);
        }, timeout.Token);
        await Task.WhenAll(server, connection.RunAsync("127.0.0.1",
            ((IPEndPoint)origin.LocalEndpoint).Port, "synthetic-transfer-id", timeout.Token));
        Assert.Equal(explicitDenial ? 1 : 0, confirmations);
    }

    [Fact]
    public async Task InvalidDestinationAndCommandsDuringConfirmationCannotLeakToEitherWorld()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var origin = new TcpListener(IPAddress.Loopback, 0);
        origin.Start();
        using var connection = new PhiConnection();
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var answer = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.ConfirmTransferAsync = (_, _, _) => { reached.TrySetResult(); return answer.Task; };
        var server = Task.Run(async () =>
        {
            using var peer = await origin.AcceptTcpClientAsync(timeout.Token);
            var stream = peer.GetStream();
            using var reader = new StreamReader(stream, PhiWire.Encoding, false, 1024, true);
            for (var i = 0; i < 11; i++) await reader.ReadLineAsync(timeout.Token);
            await Write(stream, "#ch-srv 127.0.0.1 65536\n", timeout.Token);
            Assert.Equal("#no-srv", await reader.ReadLineAsync(timeout.Token));
            await Write(stream, "#ch-srv untrusted.invalid 2000\n", timeout.Token);
            Assert.Equal("#no-srv", await reader.ReadLineAsync(timeout.Token));
            await Write(stream, "#x\n", timeout.Token);
        }, timeout.Token);
        var run = connection.RunAsync("127.0.0.1", ((IPEndPoint)origin.LocalEndpoint).Port,
            "synthetic-transfer-id", timeout.Token);
        await reached.Task.WaitAsync(timeout.Token);
        await Assert.ThrowsAsync<InvalidOperationException>(() => connection.SendAsync(["must-not-send"], timeout.Token));
        await Assert.ThrowsAsync<InvalidOperationException>(() => connection.SendActionAsync(GameInput.Command("hit"), timeout.Token));
        answer.TrySetResult(false);
        await Task.WhenAll(server, run);
    }

    [Fact]
    public void NamesUsersAndAbsolutePositionsUseOriginalFieldsAndSingleFrameConsumption()
    {
        var parser = new ProtocolParser();
        Assert.Equal(new PlayerName("勇者"), Assert.Single(parser.Feed(PhiWire.Encode("#name 勇者"))));
        var user = Enumerable.Repeat((byte)' ', 42).ToArray();
        "#user 0007 "u8.CopyTo(user);
        PhiWire.Encode("相手").CopyTo(user, 11);
        var first = Assert.IsType<UserList>(Assert.Single(parser.Feed(user)));
        Assert.Equal(new User(7, "相手"), Assert.Single(first.Users));
        "0009"u8.CopyTo(user.AsSpan(6));
        var next = Assert.IsType<UserList>(Assert.Single(parser.Feed(user)));
        Assert.Equal(new User(9, "相手"), Assert.Single(next.Users));
        Assert.Equal(7, first.Users[0].Number);
        Assert.Empty(parser.Feed("#ex-pos 123 456"u8.ToArray()));
        var frame = Assert.IsType<MapFrame>(Assert.Single(parser.Feed("#m57 ."u8.ToArray())));
        Assert.Equal(new MapPosition(123, 456), frame.Position);
        var following = Assert.IsType<MapFrame>(Assert.Single(parser.Feed("#map ."u8.ToArray())));
        Assert.Null(following.Position);
        Assert.Equal(5, following.Size);
        Assert.Equal(new LogLine("3重状態で消えています。移動してください"),
            Assert.Single(parser.Feed("#ex-chr-conflict"u8.ToArray())));
    }

    [Fact]
    public async Task CancellingConfirmationClosesWithoutContactingUnapprovedDestination()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var origin = new TcpListener(IPAddress.Loopback, 0);
        origin.Start();
        using var connection = new PhiConnection();
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var events = new ConcurrentQueue<ServerEvent>();
        connection.Received += events.Enqueue;
        connection.ConfirmTransferAsync = async (_, _, token) =>
        {
            reached.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return true;
        };
        var server = Task.Run(async () =>
        {
            using var peer = await origin.AcceptTcpClientAsync(timeout.Token);
            var stream = peer.GetStream();
            using var reader = new StreamReader(stream, PhiWire.Encoding, false, 1024, true);
            for (var i = 0; i < 11; i++) await reader.ReadLineAsync(timeout.Token);
            await Write(stream, "#ch-srv untrusted.invalid 2000\n", timeout.Token);
            Assert.Null(await reader.ReadLineAsync(timeout.Token));
        }, timeout.Token);
        var running = connection.RunAsync("127.0.0.1", ((IPEndPoint)origin.LocalEndpoint).Port,
            "synthetic-transfer-id", timeout.Token);
        await reached.Task.WaitAsync(timeout.Token);
        connection.Dispose();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        await server;
        Assert.Equal([new TransferProgress(true), new TransferProgress(false)], events.OfType<TransferProgress>());
        Assert.Empty(events.OfType<WorldMoved>());
    }

    [Fact]
    public void UserSnapshotsAndLiveMapAreResetOnMoveWithoutLosingDraftOrHistory()
    {
        var users = new[] { new User(1, "相手") };
        var initial = GameUpdate.Apply(new GameModel { Input = "未送信", World = "元世界", Area = "元地域" },
            new GameMessage.Received(new UserList(users)));
        users[0] = new(2, "後から変更");
        Assert.Equal(new User(1, "相手"), Assert.Single(initial.Users));
        initial = GameUpdate.Apply(initial, new GameMessage.Logged("履歴"));
        initial = GameUpdate.Apply(initial, new GameMessage.Received(
            new MapFrame('N', [], [], [new Actor(2, 2, "描画する相手")], 5)));
        var moved = GameUpdate.Apply(initial, new GameMessage.Received(new WorldMoved("127.0.0.1", 2001)));
        Assert.Null(moved.Map);
        Assert.Empty(moved.Users);
        Assert.Equal("未送信", moved.Input);
        Assert.Equal(new[] { "履歴" }, moved.Log.ToArray());
        Assert.Equal("", moved.World);
        Assert.Equal("", moved.Area);
        Assert.NotNull(initial.Map);
        Assert.Single(initial.Users);
    }

    private static Task Write(NetworkStream stream, string text, CancellationToken token) =>
        stream.WriteAsync(PhiWire.Encode(text), token).AsTask();
}
