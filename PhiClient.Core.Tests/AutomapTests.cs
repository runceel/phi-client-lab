using PhiClient.Core;
using System.Net;
using System.Net.Sockets;

namespace PhiClient.Core.Tests;

public sealed class AutomapTests
{
    private static MapFrame Frame(long x, long y, byte tile = (byte)' ') =>
        new('N', Enumerable.Repeat(tile, 49).ToArray(), new byte[49], [new(3, 3, "本人")])
            { Position = new(x, y) };

    [Fact]
    public void RecordingAndCompositionPreserveLiveCellsSkipUnknownsShiftActorsAndDimOnlyRecall()
    {
        var original = new AutomapWorld();
        var memory = original.Absorb(Frame(0, 0, (byte)'#'));
        Assert.Empty(original.Blocks);
        Assert.Equal(new MapCell((byte)'#', 0), memory.Cell(new(-3, -3)));
        Assert.Equal(new MapPosition(-1, -1), Automap.BlockOf(new(-1, -32)));
        Assert.Equal(new MapPosition(-2, -2), Automap.BlockOf(new(-33, -33)));
        var live = Frame(1, 0);
        live.Tiles[0] = (byte)'?';
        var next = memory.Absorb(live);
        Assert.Equal(new MapCell((byte)'#', 0), next.Cell(new(-2, -3)));
        var display = Automap.Compose(live, next, 1, new(0, 0));
        Assert.Equal(9, display.Frame.Size);
        Assert.Equal(new Actor(4, 4, "本人"), Assert.Single(display.Frame.Actors));
        Assert.Equal((byte)'#', display.Frame.Tiles[9 + 1]);
        Assert.True(display.Recalled[9 + 1]);
        Assert.Equal((byte)' ', display.Frame.Tiles[9 + 2]);
        Assert.False(display.Recalled[9 + 2]);
        Assert.Equal((byte)'?', display.Frame.Tiles[0]);
        Assert.False(display.Recalled[0]);
        var prediction = Automap.Compose(live, next, 1, new(10, 0));
        Assert.Equal(new MapPosition(11, 0), prediction.Frame.Position);
        Assert.Equal((byte)' ', prediction.Frame.Tiles[9 + 2]);
        Assert.Same(memory, memory.Absorb(Frame(0, 0) with { Position = null }));
        Assert.Same(memory, memory.Absorb(Frame(0, 0) with { Size = 5 }));
        Assert.Equal(0, Automap.RingFor(223, 300, 32));
        Assert.Equal(1, Automap.RingFor(284, 300, 32));
        Assert.Equal(4, Automap.RingFor(1000, 1000, 32));
        Assert.Equal(0, Automap.RingFor(344, 344, 128));

        var pixels = new byte[512 * 96 * 4];
        for (var y = 0; y < 96; y++)
        for (var x = 0; x < 512; x++)
        {
            var i = (y * 512 + x) * 4;
            pixels[i] = 120; pixels[i + 1] = 80; pixels[i + 2] = 40;
            pixels[i + 3] = y % 48 < 16 ? (byte)0 : (byte)255;
        }
        var image = MapGraphics.Draw(9, 9, display.Frame.Tiles, display.Frame.Attributes, [],
            _ => new(512, 96, pixels), null, _ => null, recalled: display.Recalled);
        var recalledPixel = ((16 + 32) * image.Width + 32) * 4;
        var livePixel = recalledPixel + 32 * 4;
        Assert.Equal(new byte[] { 90, 60, 30, 255 }, image.Pixels[recalledPixel..(recalledPixel + 4)]);
        Assert.Equal(new byte[] { 120, 80, 40, 255 }, image.Pixels[livePixel..(livePixel + 4)]);
        Assert.Equal((byte)0, image.Pixels[3]);
    }

    [Fact]
    public async Task BlocksPersistByWorldAndLateSaveDoesNotClearNewDirtyCells()
    {
        var folder = Path.Combine(Path.GetTempPath(), "phi-maps-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var store = new AutomapStore(folder);
            var memory = new AutomapWorld { Loaded = true }.Absorb(Frame(0, 0));
            foreach (var (block, cells) in memory.Blocks) await store.SaveBlockAsync("../世界:A", block, cells);
            var loaded = await store.LoadAsync("../世界:A");
            Assert.Equal(new MapCell((byte)' ', 0), loaded.Cell(new(-3, -3)));
            Assert.Empty((await store.LoadAsync("世界:B")).Blocks);
            foreach (var (block, cells) in memory.Blocks) await store.SaveBlockAsync("世界:B", block, cells);
            var updated = memory.Absorb(Frame(0, 0, (byte)'#'));
            var model = new ClientModel { Automaps = new ClientModel().Automaps.Add("世界", updated) };
            var first = memory.Blocks.First();
            var saved = ClientUpdate.Apply(model, new ClientMessage.AutomapSaved("世界", first.Key, first.Value));
            Assert.Contains(first.Key, saved.Automaps["世界"].Dirty);
            var current = saved.Automaps["世界"].Blocks[first.Key];
            saved = ClientUpdate.Apply(saved, new ClientMessage.AutomapSaved("世界", first.Key, current));
            Assert.DoesNotContain(first.Key, saved.Automaps["世界"].Dirty);
            var merged = updated.MergeLoaded(loaded);
            Assert.Equal(new MapCell((byte)'#', 0), merged.Cell(new(-3, -3)));
            await store.ClearAsync("../世界:A");
            Assert.Empty((await store.LoadAsync("../世界:A")).Blocks);
            Assert.NotEmpty((await store.LoadAsync("世界:B")).Blocks);
            var corrupt = Directory.EnumerateFiles(folder, "*.json", SearchOption.AllDirectories).First();
            await File.WriteAllTextAsync(corrupt, "{\"World\":\"世界:B\",\"X\":0,\"Y\":0,\"Cells\":[{\"X\":32,\"Y\":0,\"Tile\":32,\"Attribute\":0}]}");
            await Assert.ThrowsAsync<InvalidDataException>(() => store.LoadAsync("世界:B"));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void ClientRecordsSharedWorldOnlyForSolidSightedFramesAndKeepsSnapshotsImmutable()
    {
        var initial = ClientUpdate.Apply(new(), new ClientMessage.GameOpened("a"));
        initial = ClientUpdate.Apply(initial, new ClientMessage.GameChanged("a",
            new GameMessage.Received(new LocationNotice("world", "世界"))));
        var recorded = ClientUpdate.Apply(initial, new ClientMessage.GameChanged("a", new GameMessage.Received(Frame(1, 2))));
        Assert.Empty(initial.Automaps);
        Assert.NotEmpty(recorded.Automaps["世界"].Blocks);
        var blind = ClientUpdate.Apply(initial, new ClientMessage.GameChanged("a",
            new GameMessage.Received(new Conditions("      *"))));
        blind = ClientUpdate.Apply(blind, new ClientMessage.GameChanged("a", new GameMessage.Received(Frame(1, 2))));
        Assert.Empty(blind.Automaps);
        var rotating = ClientUpdate.Apply(initial, new ClientMessage.GameChanged("a",
            new GameMessage.Received(new MapStyle(false))));
        rotating = ClientUpdate.Apply(rotating, new ClientMessage.GameChanged("a", new GameMessage.Received(Frame(1, 2))));
        Assert.Empty(rotating.Automaps);
    }

    [Fact]
    public async Task OwnedLoopbackFramesDriveMappingPredictionAndAcknowledgmentWithoutExtraMovement()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var connection = new PhiConnection();
        var gate = new object();
        var model = ClientUpdate.Apply(new(), new ClientMessage.GameOpened("a"));
        long clock = 0;
        var firstMap = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var moved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        connection.Received += item =>
        {
            lock (gate)
            {
                model = ClientUpdate.Apply(model, new ClientMessage.GameChanged("a",
                    new GameMessage.Received(item, Volatile.Read(ref clock))));
                if (item is MapFrame) firstMap.TrySetResult();
            }
        };
        connection.Sending += (line, _) =>
        {
            lock (gate)
                model = ClientUpdate.Apply(model, new ClientMessage.GameChanged("a",
                    new GameMessage.CommandSent(line, Volatile.Read(ref clock))));
        };
        static byte[] Terrain()
        {
            var bytes = Enumerable.Repeat((byte)' ', 116).ToArray();
            "#m57 M N 00:      "u8.CopyTo(bytes);
            for (var i = 0; i < 49; i++) bytes[19 + i * 2] = 0;
            return bytes;
        }
        var server = Task.Run(async () =>
        {
            using var peer = await listener.AcceptTcpClientAsync(timeout.Token);
            var stream = peer.GetStream();
            using var reader = new StreamReader(stream, PhiWire.Encoding, false, 1024, true);
            for (var i = 0; i < 11; i++) await reader.ReadLineAsync(timeout.Token);
            await stream.WriteAsync(PhiWire.Encode("#ex-notice land=Owned fixture\n#ex-pos 100 100\n"), timeout.Token);
            await stream.WriteAsync(Terrain(), timeout.Token);
            await stream.WriteAsync("\n#m57 .\n"u8.ToArray(), timeout.Token);
            Assert.Equal("go N", await reader.ReadLineAsync(timeout.Token));
            moved.SetResult();
            Assert.Equal("inspect", await reader.ReadLineAsync(timeout.Token));
            await stream.WriteAsync(PhiWire.Encode("#ex-pos 100 99\n"), timeout.Token);
            await stream.WriteAsync(Terrain(), timeout.Token);
            await stream.WriteAsync("\n#m57 .\n#close\n"u8.ToArray(), timeout.Token);
        }, timeout.Token);
        var running = connection.RunAsync("127.0.0.1", ((IPEndPoint)listener.LocalEndpoint).Port,
            "test-character", timeout.Token);
        await firstMap.Task.WaitAsync(timeout.Token);
        Volatile.Write(ref clock, 1000);
        await connection.SendAsync(["go N"], timeout.Token);
        await moved.Task.WaitAsync(timeout.Token);
        lock (gate)
        {
            model = ClientUpdate.Apply(model, new ClientMessage.GameChanged("a", new GameMessage.PredictionTick(1119)));
            Assert.Equal(new MapPosition(0, 0), model.Games["a"].Prediction.Offset);
            model = ClientUpdate.Apply(model, new ClientMessage.GameChanged("a", new GameMessage.PredictionTick(1120)));
            Assert.Equal(new MapPosition(0, -1), model.Games["a"].Prediction.Offset);
            Assert.Equal(new MapPosition(100, 100), model.Games["a"].Map!.Position);
            Assert.NotEmpty(model.Automaps["Owned fixture"].Blocks);
        }
        Volatile.Write(ref clock, 1300);
        await connection.SendAsync(["inspect"], timeout.Token);
        await Task.WhenAll(server, running);
        Assert.Equal(new MapPosition(100, 99), model.Games["a"].Map!.Position);
        Assert.Empty(model.Games["a"].Prediction.Queue);
        Assert.Empty(model.Games["a"].Prediction.Guessed);
    }
}
