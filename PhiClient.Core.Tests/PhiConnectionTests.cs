using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using PhiClient.Core;

namespace PhiClient.Core.Tests;

public sealed class PhiConnectionTests
{
    [Fact]
    public async Task RunAsync_ExchangesLoginAndGameFrames()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var connection = new PhiConnection();
        var events = new ConcurrentQueue<ServerEvent>();
        var receivedMap = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Received += item =>
        {
            events.Enqueue(item);
            if (item is MapFrame)
                receivedMap.TrySetResult();
        };
        var server = Task.Run(async () =>
        {
            using var peer = await listener.AcceptTcpClientAsync(timeout.Token);
            var stream = peer.GetStream();
            using var reader = new StreamReader(stream, PhiWire.Encoding, false, 1024, true);
            var expected = new[]
            {
                "#open test-character", "#version-cli 05107100", "#map-iv 2", "#status-iv 2",
                "#ex-switch eagleeye=form", "#ex-map size=57", "#ex-map style=solid",
                "#ex-switch ex-move-recv=true", "#ex-switch ex-list-mode-end=true",
                "#ex-switch ex-disp-magic=false", "#ex-switch ex-abspos=true"
            };
            foreach (var line in expected)
                Assert.Equal(line, await reader.ReadLineAsync(timeout.Token));

            var text = PhiWire.Encoding.GetBytes(
                "#lag\n#remap\n#more\n#lag\n#ch-srv untrusted.invalid 2000\n#end-more\n" +
                "こんにちは\n#status Alice : 10 : 20 : 3 : 5\n#ex-map style=turn\n" +
                "#list\n [/*r*/ 9/*.*/] Potion\n[10] Next page\n#end-list\n");
            // Deliberately split every CP932 character across separate network writes.
            foreach (var b in text)
                await stream.WriteAsync(new[] { b }, timeout.Token);
            var terrain = Enumerable.Repeat((byte)' ', 116).ToArray();
            "#m57 M N 00:      "u8.CopyTo(terrain);
            for (var i = 0; i < 49; i++)
            {
                terrain[18 + i * 2] = (byte)(i == 0 ? '#' : ' ');
                terrain[19 + i * 2] = 0x81;
            }
            await stream.WriteAsync(terrain, timeout.Token);
            await stream.WriteAsync("\n#m57 .\n"u8.ToArray(), timeout.Token);
            Assert.Equal("#end-lag", await reader.ReadLineAsync(timeout.Token));
            Assert.Equal("#map", await reader.ReadLineAsync(timeout.Token));
            Assert.Equal("go N", await reader.ReadLineAsync(timeout.Token));
            Assert.Equal("hello ?", await reader.ReadLineAsync(timeout.Token));
            await stream.WriteAsync("#close\n"u8.ToArray(), timeout.Token);
        }, timeout.Token);

        var running = connection.RunAsync("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port,
            "test-character", timeout.Token);
        await receivedMap.Task.WaitAsync(timeout.Token);
        await connection.SendAsync(["go N", "hello \U0001F600"], timeout.Token);
        await Task.WhenAll(server, running).WaitAsync(timeout.Token);

        Assert.Contains(new LogLine("こんにちは"), events);
        Assert.Contains(new LogLine("#lag"), events);
        Assert.Contains(new LogLine("#ch-srv untrusted.invalid 2000"), events);
        Assert.Contains(new StatusLine("Alice : 10 : 20 : 3 : 5"), events);
        Assert.Contains(new MapStyle(false), events);
        var map = Assert.Single(events.OfType<MapFrame>());
        Assert.Equal('N', map.Direction);
        Assert.Equal(49, map.Tiles.Length);
        Assert.Equal((byte)'#', map.Tiles[0]);
        Assert.All(map.Attributes, value => Assert.Equal(0x81, value));
        var list = Assert.Single(events.OfType<Choices>());
        Assert.Equal(new[] { new Choice("9", "Potion"), new Choice("10", "Next page") }, list.Items);
        Assert.Single(events.OfType<Closed>());
    }

    [Fact]
    public async Task RunAsync_DiscardsOversizedLineAndRecovers()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var connection = new PhiConnection();
        var events = new ConcurrentQueue<ServerEvent>();
        connection.Received += events.Enqueue;
        var server = Task.Run(async () =>
        {
            using var peer = await listener.AcceptTcpClientAsync(timeout.Token);
            var stream = peer.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
            for (var i = 0; i < 11; i++)
                await reader.ReadLineAsync(timeout.Token);
            await stream.WriteAsync(Enumerable.Repeat((byte)'a', 65536).ToArray(), timeout.Token);
            await stream.WriteAsync("\r\n"u8.ToArray(), timeout.Token);
            // CR is part of the 64 KiB wire limit, so the preceding row is too long.
            await stream.WriteAsync(Enumerable.Repeat((byte)'b', 65536).ToArray(), timeout.Token);
            await stream.WriteAsync("\n"u8.ToArray(), timeout.Token);
            await stream.WriteAsync(Enumerable.Repeat((byte)'c', 65537).ToArray(), timeout.Token);
            await stream.WriteAsync("\nrecovered\r\n#x\n"u8.ToArray(), timeout.Token);
        }, timeout.Token);
        await Task.WhenAll(server, connection.RunAsync("127.0.0.1",
            ((IPEndPoint)listener.LocalEndpoint).Port, "test-character", timeout.Token));
        Assert.Contains(new LogLine(new string('b', 65536)), events);
        Assert.DoesNotContain(events.OfType<LogLine>(), line => line.Text.StartsWith('a') || line.Text.StartsWith('c'));
        Assert.Contains(new LogLine("recovered"), events);
        Assert.Contains(events.OfType<LogLine>(), line => line.Text.Contains("64 KiB"));
    }

    [Fact]
    public async Task SendAsync_RejectsWholeBatchBeforeWritingInjectedCommand()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var connection = new PhiConnection();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Received += item => { if (item is ConnectionReady) ready.TrySetResult(); };
        var server = Task.Run(async () =>
        {
            using var peer = await listener.AcceptTcpClientAsync(timeout.Token);
            var stream = peer.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
            for (var i = 0; i < 11; i++)
                await reader.ReadLineAsync(timeout.Token);
            Assert.Equal("safe", await reader.ReadLineAsync(timeout.Token));
            await stream.WriteAsync("#x\n"u8.ToArray(), timeout.Token);
        }, timeout.Token);
        var running = connection.RunAsync("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port,
            "test-character", timeout.Token);
        await ready.Task.WaitAsync(timeout.Token);
        await Assert.ThrowsAsync<ArgumentException>(() => connection.SendAsync(["must-not-be-sent", "bad\n#x"], timeout.Token));
        await connection.SendAsync(["safe"], timeout.Token);
        await Task.WhenAll(server, running);
    }

    [Fact]
    public async Task RegisterAsync_ReturnsIdAndClosesRegistrationConnection()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var server = Task.Run(async () =>
        {
            using var peer = await listener.AcceptTcpClientAsync(timeout.Token);
            var stream = peer.GetStream();
            using var reader = new StreamReader(stream, PhiWire.Encoding, false, 1024, true);
            Assert.Equal("#ex-register start", await reader.ReadLineAsync(timeout.Token));
            Assert.Equal("name=\"Tester\" pass=abc123 image=2 mail=test@example.invalid",
                await reader.ReadLineAsync(timeout.Token));
            Assert.Equal("#ex-register end", await reader.ReadLineAsync(timeout.Token));
            await stream.WriteAsync("#set-user-id newly-created-id\n"u8.ToArray(), timeout.Token);
            Assert.Equal("#x", await reader.ReadLineAsync(timeout.Token));
        }, timeout.Token);
        var id = await PhiConnection.RegisterAsync("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port,
            "Tester", "abc123", "2", "test@example.invalid", timeout.Token);
        await server;
        Assert.Equal("newly-created-id", id);
    }

    [Fact]
    public async Task RegisterAsync_SurfacesServerRejection()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var server = Task.Run(async () =>
        {
            using var peer = await listener.AcceptTcpClientAsync(timeout.Token);
            var stream = peer.GetStream();
            using var reader = new StreamReader(stream, PhiWire.Encoding, false, 1024, true);
            for (var i = 0; i < 3; i++)
                await reader.ReadLineAsync(timeout.Token);
            await stream.WriteAsync("#ex-register reject name\n"u8.ToArray(), timeout.Token);
        }, timeout.Token);
        var error = await Assert.ThrowsAsync<IOException>(() => PhiConnection.RegisterAsync("127.0.0.1",
            ((IPEndPoint)listener.LocalEndpoint).Port, "Tester", "abc123", "2", "test@example.invalid", timeout.Token));
        await server;
        Assert.Contains("reject name", error.Message);
    }

    [Fact]
    public async Task RunAsync_ParsesPackedActorsAtByteOffsets()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var connection = new PhiConnection();
        var frames = new ConcurrentQueue<MapFrame>();
        connection.Received += item => { if (item is MapFrame frame) frames.Enqueue(frame); };
        var server = Task.Run(async () =>
        {
            using var peer = await listener.AcceptTcpClientAsync(timeout.Token);
            var stream = peer.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
            for (var i = 0; i < 11; i++)
                await reader.ReadLineAsync(timeout.Token);
            var map = Enumerable.Repeat((byte)' ', 116).ToArray();
            "#m57 M N 00:      "u8.CopyTo(map);
            await stream.WriteAsync(map, timeout.Token);
            await stream.WriteAsync("\n"u8.ToArray(), timeout.Token);
            var packed = Enumerable.Repeat((byte)' ', 142).ToArray();
            "#m57 W "u8.CopyTo(packed);
            packed[13] = (byte)'1';
            packed[15] = (byte)'2';
            PhiWire.Encoding.GetBytes("太郎").CopyTo(packed, 19);
            packed[81] = (byte)'5';
            packed[83] = (byte)'4';
            "NPC"u8.CopyTo(packed.AsSpan(87));
            await stream.WriteAsync(packed, timeout.Token);
            await stream.WriteAsync("\n#m57 .\n#m57 .\n#x\n"u8.ToArray(), timeout.Token);
        }, timeout.Token);
        await Task.WhenAll(server, connection.RunAsync("127.0.0.1",
            ((IPEndPoint)listener.LocalEndpoint).Port, "test-character", timeout.Token));
        Assert.Equal(2, frames.Count);
        foreach (var frame in frames)
            Assert.Equal(new[] { new Actor(1, 2, "太郎"), new Actor(5, 4, "NPC") }, frame.Actors);
    }

    [Theory]
    [InlineData("Wpn", "1")]
    [InlineData("Arm", ".")]
    [InlineData("unrecognized", ".")]
    [InlineData("none", null)]
    public async Task SendActionAsync_PreparesEquipmentHidesOnlyIntermediateListsAndSerializesInput(string kind, string? selection)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var connection = new PhiConnection();
        var events = new ConcurrentQueue<ServerEvent>();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var unequip = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var queued = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Received += item =>
        {
            events.Enqueue(item);
            if (item is ConnectionReady) ready.TrySetResult();
        };
        var server = Task.Run(async () =>
        {
            using var peer = await listener.AcceptTcpClientAsync(timeout.Token);
            var stream = peer.GetStream();
            using var reader = new StreamReader(stream, PhiWire.Encoding, false, 1024, true);
            for (var i = 0; i < 11; i++) await reader.ReadLineAsync(timeout.Token);
            Assert.Equal("unequip", await reader.ReadLineAsync(timeout.Token));
            unequip.SetResult();
            await queued.Task.WaitAsync(timeout.Token);
            // A sign's literal text must not be mistaken for an equipment response.
            await stream.WriteAsync(PhiWire.Encode("#more\nCan not unequip.\n#end-more\n"), timeout.Token);
            var reply = kind == "none" ? "Can not unequip.\n" :
                $"#list\n     [/*color=cyan*/ 1/*.*/] Sword Wp : 5 [/*r*/{kind}/*.*/]\n[2] Armor [Wpn]\n#end-list\n";
            await stream.WriteAsync(PhiWire.Encode(reply), timeout.Token);
            if (selection is not null) Assert.Equal(selection, await reader.ReadLineAsync(timeout.Token));
            Assert.Equal("cast", await reader.ReadLineAsync(timeout.Token));
            Assert.Equal("detect", await reader.ReadLineAsync(timeout.Token));
            Assert.Equal("go N", await reader.ReadLineAsync(timeout.Token));
            await stream.WriteAsync(PhiWire.Encode("#list\n[1] detect\n#end-list\n" +
                "#ex-list-mode-end\n#list\n[9] Choose target\n#end-list\n#close\n"), timeout.Token);
        }, timeout.Token);
        var running = connection.RunAsync("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port, "test-character", timeout.Token);
        await ready.Task.WaitAsync(timeout.Token);
        var casting = connection.SendActionAsync(GameInput.Spell("detect"), timeout.Token);
        await unequip.Task.WaitAsync(timeout.Token);
        var moving = connection.SendActionAsync(GameInput.Command("go N"), timeout.Token);
        queued.SetResult();
        await Task.WhenAll(server, running, casting, moving).WaitAsync(timeout.Token);
        var displayed = Assert.Single(events.OfType<Choices>(), item => item.Complete);
        Assert.Equal([new Choice("9", "Choose target")], displayed.Items);
        Assert.Contains(new LogLine("Can not unequip."), events);
    }

    [Fact]
    public async Task SendActionAsync_TimesOutWithoutCastingAndDoesNotHideTheNextUserList()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var connection = new PhiConnection();
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lists = new ConcurrentQueue<Choices>();
        connection.Received += item =>
        {
            if (item is ConnectionReady) ready.TrySetResult();
            if (item is Choices list) lists.Enqueue(list);
        };
        var server = Task.Run(async () =>
        {
            using var peer = await listener.AcceptTcpClientAsync(timeout.Token);
            var stream = peer.GetStream();
            using var reader = new StreamReader(stream, PhiWire.Encoding, false, 1024, true);
            for (var i = 0; i < 11; i++) await reader.ReadLineAsync(timeout.Token);
            Assert.Equal("unequip", await reader.ReadLineAsync(timeout.Token));
            Assert.Equal("get", await reader.ReadLineAsync(timeout.Token));
            await stream.WriteAsync("#list\n[1] Potion\n#end-list\n#x\n"u8.ToArray(), timeout.Token);
        }, timeout.Token);
        var running = connection.RunAsync("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port, "test-character", timeout.Token);
        await ready.Task.WaitAsync(timeout.Token);
        var error = await Assert.ThrowsAsync<IOException>(() => connection.SendActionAsync(GameInput.Spell("wizard eye"), timeout.Token));
        Assert.Contains("spell was not cast", error.Message);
        await connection.SendActionAsync(GameInput.Command("get"), timeout.Token);
        await Task.WhenAll(server, running).WaitAsync(timeout.Token);
        Assert.Equal([new Choice("1", "Potion")], Assert.Single(lists).Items);
    }
}
