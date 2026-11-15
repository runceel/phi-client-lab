using System.Net;
using System.Net.Sockets;
using PhiClient.Core;

namespace PhiClient.Core.Tests;

public sealed class RegistrationGraphicsTests
{
    [Fact]
    public async Task LookupSendsOnlyReginfoAndPreservesCp932NamesEmptyRowsDuplicatesAndIndices()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var server = Task.Run(async () =>
        {
            using var peer = await listener.AcceptTcpClientAsync(timeout.Token);
            var stream = peer.GetStream();
            using var reader = new StreamReader(stream, PhiWire.Encoding, false, 1024, true);
            Assert.Equal("#ex-get REGINFO IMG", await reader.ReadLineAsync(timeout.Token));
            var bytes = PhiWire.Encode("ignored banner\n#ex-put REGINFO IMG\n t_man \n\n \n怪物\nt_man\n#ex-put .\n");
            foreach (var value in bytes) await stream.WriteAsync(new byte[] { value }, timeout.Token);
            Assert.Null(await reader.ReadLineAsync(timeout.Token));
        }, timeout.Token);
        var names = await PhiConnection.RegistrationGraphicsAsync("127.0.0.1",
            ((IPEndPoint)listener.LocalEndpoint).Port, timeout.Token);
        await server;
        Assert.Equal(["t_man", "", "怪物", "t_man"], names);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IncompleteOrOversizedCandidateRowsNeverProduceShiftedImageNumbers(bool oversized)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var server = Task.Run(async () =>
        {
            using var peer = await listener.AcceptTcpClientAsync(timeout.Token);
            var stream = peer.GetStream();
            using var reader = new StreamReader(stream, PhiWire.Encoding, false, 1024, true);
            await reader.ReadLineAsync(timeout.Token);
            await stream.WriteAsync(PhiWire.Encode("#ex-put REGINFO IMG\nt_man\n" +
                (oversized ? new string('x', 65537) + "\nt_elf\n#ex-put .\n" : "")), timeout.Token);
        }, timeout.Token);
        await Assert.ThrowsAsync<IOException>(() => PhiConnection.RegistrationGraphicsAsync("127.0.0.1",
            ((IPEndPoint)listener.LocalEndpoint).Port, timeout.Token));
        await server;
    }

    [Fact]
    public void EditingOrSelectingAnotherEndpointDiscardsPendingResultsAndSelectionUsesZeroBasedIndices()
    {
        var initial = new ClientModel { Draft = new(Host: "127.0.0.1", Port: "2000") };
        var waiting = ClientUpdate.Apply(initial, new ClientMessage.RegistrationGraphicsLoading(1));
        var changed = ClientUpdate.Apply(waiting, new ClientMessage.DraftChanged(waiting.Draft with { Port = "2001" }));
        var stale = ClientUpdate.Apply(changed, new ClientMessage.RegistrationGraphicsLoaded(1, ["old"]));
        Assert.Empty(stale.RegistrationGraphics.Names);
        var newer = ClientUpdate.Apply(waiting, new ClientMessage.RegistrationGraphicsLoading(2));
        Assert.True(ClientUpdate.Apply(newer, new ClientMessage.RegistrationGraphicsLoaded(1, ["old"]))
            .RegistrationGraphics.Loading);
        var loaded = ClientUpdate.Apply(newer, new ClientMessage.RegistrationGraphicsLoaded(2, ["t_man", "t_elf"]));
        var selected = ClientUpdate.Apply(loaded, new ClientMessage.RegistrationGraphicSelected(1));
        Assert.Equal("1", selected.Draft.Image);
        Assert.Equal("0", loaded.Draft.Image);
        Assert.Same(selected, ClientUpdate.Apply(selected, new ClientMessage.RegistrationGraphicSelected(2)));
    }

    [Fact]
    public void FrontLargeFrameCropPreservesExactPixelsAndTransparencyWithoutChangingTheSheet()
    {
        var pixels = new byte[96 * 160 * 4];
        for (var y = 0; y < 160; y++)
        for (var x = 0; x < 96; x++)
        {
            var offset = (y * 96 + x) * 4;
            pixels[offset] = (byte)x;
            pixels[offset + 1] = (byte)y;
            pixels[offset + 3] = 255;
        }
        pixels[(64 * 96 + 32) * 4 + 3] = 0;
        var sheet = new PixelImage(96, 160, pixels);
        var crop = sheet.Crop(32, 64, 32, 32);
        Assert.Equal(new byte[] {32, 64, 0, 0}, crop.Pixels[..4]);
        Assert.Equal(new byte[] {63, 64, 0, 255}, crop.Pixels[(31 * 4)..(32 * 4)]);
        Assert.Equal(new byte[] {32, 95, 0, 255}, crop.Pixels[(31 * 32 * 4)..(31 * 32 * 4 + 4)]);
        Assert.Equal(new byte[] {63, 95, 0, 255}, crop.Pixels[^4..]);
        Assert.Equal((96, 160), (sheet.Width, sheet.Height));
        Assert.Throws<ArgumentOutOfRangeException>(() => sheet.Crop(95, 64, 32, 32));
    }
}
