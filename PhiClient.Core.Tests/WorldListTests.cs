using System.Net;
using System.Net.Sockets;
using System.Text;
using PhiClient.Core;

namespace PhiClient.Core.Tests;

public sealed class WorldListTests
{
    private const string Ini = "[モック世界]\r\nAddress=127.0.0.1\r\nPort=12345\r\nLine1=説明は接続情報ではありません\r\n";

    [Fact]
    public void Cp932SectionsPreserveOrderAndLastKeyWinsWhileInvalidEndpointsAreReported()
    {
        var parsed = WorldList.Parse(PhiWire.Encoding.GetBytes(Ini +
            "[二番目]\nADDRESS=not a host\naddress=127.0.0.2\nPORT=6000\nPort=6001\n" +
            "[不正]\nAddress=https://example.com\nPort=2000\n[空]\nPort=2000\n" +
            "[上限外]\nAddress=127.0.0.1\nPort=65536\n"), "fixture");
        Assert.Equal(["モック世界", "二番目"], parsed.Items.Select(w => w.Name));
        Assert.Equal(new WorldEndpoint("二番目", "127.0.0.2", 6001), parsed.Items[1]);
        Assert.Contains("不正な 3 件", parsed.Message);
        Assert.Equal("fixture", parsed.Source);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html>not an INI list</html>")]
    [InlineData("[invalid]\nAddress=127.0.0.1\nPort=0")]
    public void EmptyHtmlAndAllInvalidListsFailExplicitly(string text) =>
        Assert.Throws<InvalidDataException>(() => WorldList.Parse(PhiWire.Encoding.GetBytes(text), "fixture"));

    [Fact]
    public void ListSizeLimitIsEnforced() =>
        Assert.Throws<InvalidDataException>(() => WorldList.Parse(new byte[WorldList.MaxBytes + 1], "fixture"));

    [Fact]
    public void ExcessSectionCountIsRejectedWithoutSilentlyTruncating() =>
        Assert.Throws<InvalidDataException>(() => WorldList.Parse(
            Encoding.ASCII.GetBytes(string.Concat(Enumerable.Range(0, 1025)
                .Select(i => $"[World {i}]\nAddress=127.0.0.1\nPort=12345\n"))), "fixture"));

    [Fact]
    public void SelectionEditsOnlyEndpointAndDoesNotCreateOrConnectCharacters()
    {
        var world = new WorldEndpoint("モック", "127.0.0.1", 12345);
        var initial = new ClientModel
        {
            Draft = new("name", "", "2000", "id", "secret", "3", "mail"),
            Creating = true, Worlds = new() { Items = [world], Visible = true }
        };
        var selected = ClientUpdate.Apply(initial, new ClientMessage.WorldSelected(world));
        Assert.Equal(initial.Draft with { Host = "127.0.0.1", Port = "12345" }, selected.Draft);
        Assert.True(selected.Creating);
        Assert.Empty(selected.Characters);
        Assert.Empty(selected.Games);
        Assert.Null(selected.ActiveKey);
        Assert.False(selected.Worlds.Visible);
        Assert.True(initial.Worlds.Visible);
        Assert.Equal("", initial.Draft.Host);
        Assert.Contains("選択だけでは接続", selected.Notice);
        Assert.Same(initial, ClientUpdate.Apply(initial, new ClientMessage.WorldSelected(new("unknown", "127.0.0.2", 9))));
        var busy = initial with { Busy = true };
        Assert.Same(busy, ClientUpdate.Apply(busy, new ClientMessage.WorldSelected(world)));
        var loading = initial with { Worlds = initial.Worlds with { Loading = true } };
        Assert.Same(loading, ClientUpdate.Apply(loading, new ClientMessage.WorldSelected(world)));
    }

    [Fact]
    public void FailedRefreshPreservesPreviousItemsAndSourceWithVisibleError()
    {
        var model = new ClientModel { Worlds = new() { Items = [new("fixture", "127.0.0.1", 99)], Source = "fixture" } };
        var loading = ClientUpdate.Apply(model, new ClientMessage.WorldsLoading());
        var failed = ClientUpdate.Apply(loading, new ClientMessage.WorldsFailed("HTTP 503"));
        Assert.False(model.Worlds.Loading);
        Assert.True(loading.Worlds.Loading);
        Assert.False(failed.Worlds.Loading);
        Assert.Equal(model.Worlds.Items, failed.Worlds.Items);
        Assert.Equal("fixture", failed.Worlds.Source);
        Assert.Contains("HTTP 503", failed.Worlds.Message);
    }

    [Fact]
    public async Task LocalImportCachesOriginalBytesAndCorruptCacheFallsBackVisibly()
    {
        var folder = Path.Combine(Path.GetTempPath(), "phi-worlds-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        using var client = new WorldDirectoryClient(folder);
        try
        {
            var input = Path.Combine(folder, "worlds.ini");
            var bytes = PhiWire.Encoding.GetBytes(Ini);
            await File.WriteAllBytesAsync(input, bytes);
            var imported = await client.ImportAsync(input);
            var restored = await client.LoadAsync();
            Assert.Equal(imported.Items.ToArray(), restored.Items.ToArray());
            Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(folder, "resist.ini")));
            await File.WriteAllTextAsync(Path.Combine(folder, "resist.ini"), "broken");
            var fallback = await client.LoadAsync();
            Assert.Equal(WorldList.Bundled, fallback.Items);
            Assert.Contains("読み込みに失敗", fallback.Message);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public async Task LoopbackHttpListUpdatesCacheAndHttpFailureDoesNotReplaceIt()
    {
        var folder = Path.Combine(Path.GetTempPath(), "phi-worlds-" + Guid.NewGuid().ToString("N"));
        using var client = new WorldDirectoryClient(folder);
        var bytes = PhiWire.Encoding.GetBytes(Ini);
        try
        {
            var result = await ServeAsync(bytes, 200, url => client.RefreshAsync(url));
            Assert.Equal("モック世界", Assert.Single(result.Items).Name);
            Assert.StartsWith("http://127.0.0.1:", result.Source);
            await Assert.ThrowsAsync<HttpRequestException>(() =>
                ServeAsync([], 503, url => client.RefreshAsync(url)));
            Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(folder, "resist.ini")));
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                ServeAsync(Encoding.ASCII.GetBytes("<html>error</html>"), 200, url => client.RefreshAsync(url)));
            Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(folder, "resist.ini")));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [Theory]
    [InlineData("file:///C:/resist.ini")]
    [InlineData("ftp://127.0.0.1/list")]
    [InlineData("http://user:secret@127.0.0.1/list")]
    public async Task InvalidDownloadLocationsAreRejectedWithoutConnecting(string url)
    {
        using var client = new WorldDirectoryClient(Path.GetTempPath());
        await Assert.ThrowsAsync<ArgumentException>(() => client.RefreshAsync(url));
    }

    [Fact]
    public async Task HttpRedirectDoesNotContactTheRedirectDestination()
    {
        using var destination = new TcpListener(IPAddress.Loopback, 0);
        destination.Start();
        var port = ((IPEndPoint)destination.LocalEndpoint).Port;
        using var client = new WorldDirectoryClient(Path.GetTempPath());
        await Assert.ThrowsAsync<HttpRequestException>(() => ServeAsync([], 302, url => client.RefreshAsync(url),
            $"Location: http://127.0.0.1:{port}/redirected\r\n"));
        Assert.False(destination.Pending());
    }

    [Fact]
    public async Task OversizeHttpContentLengthIsRejectedBeforeCaching()
    {
        var folder = Path.Combine(Path.GetTempPath(), "phi-worlds-" + Guid.NewGuid().ToString("N"));
        using var client = new WorldDirectoryClient(folder);
        await Assert.ThrowsAsync<InvalidDataException>(() => ServeAsync([], 200, url => client.RefreshAsync(url),
            declaredLength: 2 * WorldList.MaxBytes + 1));
        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public async Task LhaExtractsOnlyResistIniWithoutWritingArchivePaths()
    {
        var bytes = PhiWire.Encoding.GetBytes(Ini);
        var archive = Lha("sub\\resist.ini", bytes);
        if (!OperatingSystem.IsWindows())
        {
            await Assert.ThrowsAsync<PlatformNotSupportedException>(() => WorldDirectoryClient.ReadLhaAsync(archive));
            return;
        }
        Assert.Equal(bytes, await WorldDirectoryClient.ReadLhaAsync(archive));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            WorldDirectoryClient.ReadLhaAsync(Lha("other.ini", bytes)));
        archive[^2] ^= 1;
        await Assert.ThrowsAsync<InvalidDataException>(() => WorldDirectoryClient.ReadLhaAsync(archive));
    }

    [Fact]
    public async Task LoopbackHttpLhaUsesTheSameParserAndCacheAsIni()
    {
        if (!OperatingSystem.IsWindows())
        {
            await Assert.ThrowsAsync<PlatformNotSupportedException>(() => WorldDirectoryClient.ReadLhaAsync([]));
            return;
        }
        var folder = Path.Combine(Path.GetTempPath(), "phi-worlds-" + Guid.NewGuid().ToString("N"));
        using var client = new WorldDirectoryClient(folder);
        var bytes = PhiWire.Encoding.GetBytes(Ini);
        try
        {
            var parsed = await ServeAsync(Lha("resist.ini", bytes), 200, url => client.RefreshAsync(url));
            Assert.Equal("モック世界", Assert.Single(parsed.Items).Name);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(folder, "resist.ini")));
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    private static async Task<T> ServeAsync<T>(byte[] content, int status, Func<string, Task<T>> request,
        string additionalHeaders = "", int? declaredLength = null)
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var endpoint = (IPEndPoint)listener.LocalEndpoint;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var server = Task.Run(async () =>
        {
            using var socket = await listener.AcceptTcpClientAsync(timeout.Token);
            await using var stream = socket.GetStream();
            using var reader = new StreamReader(stream, leaveOpen: true);
            Assert.StartsWith("GET /resist.lzh HTTP/", await reader.ReadLineAsync(timeout.Token));
            while (await reader.ReadLineAsync(timeout.Token) is { Length: > 0 } header)
                Assert.False(header.StartsWith("Authorization:", StringComparison.OrdinalIgnoreCase));
            var headers = Encoding.ASCII.GetBytes($"HTTP/1.1 {status} Fixture\r\nContent-Length: {declaredLength ?? content.Length}\r\n{additionalHeaders}Connection: close\r\n\r\n");
            await stream.WriteAsync(headers, timeout.Token);
            await stream.WriteAsync(content, timeout.Token);
        }, timeout.Token);
        try { return await request($"http://127.0.0.1:{endpoint.Port}/resist.lzh"); }
        finally { await server; }
    }

    private static byte[] Lha(string name, byte[] bytes)
    {
        var filename = Encoding.ASCII.GetBytes(name);
        var header = new byte[24 + filename.Length];
        Encoding.ASCII.GetBytes("-lh0-").CopyTo(header, 2);
        BitConverter.GetBytes(bytes.Length).CopyTo(header, 7);
        BitConverter.GetBytes(bytes.Length).CopyTo(header, 11);
        header[19] = 0x20;
        header[21] = (byte)filename.Length;
        filename.CopyTo(header, 22);
        ushort crc = 0;
        foreach (var b in bytes)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++) crc = (ushort)((crc >> 1) ^ ((crc & 1) != 0 ? 0xa001 : 0));
        }
        BitConverter.GetBytes(crc).CopyTo(header, 22 + filename.Length);
        header[0] = (byte)(header.Length - 2);
        header[1] = (byte)header.Skip(2).Sum(b => b);
        return [.. header, .. bytes, 0];
    }
}
