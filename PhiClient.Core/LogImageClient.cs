using System.Net;
using System.Net.Sockets;

namespace PhiClient.Core;

public sealed record LogImageData(byte[] Bytes, string MediaType);

public sealed class LogImageClient : IDisposable
{
    public const int MaximumBytes = 1024 * 1024;
    private readonly HttpClient http;

    public LogImageClient(HttpMessageHandler? handler = null)
    {
        http = new(handler ?? new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            UseProxy = false,
            ConnectCallback = ConnectPublicAsync
        }) { Timeout = Timeout.InfiniteTimeSpan };
    }

    public async Task<LogImageData> LoadAsync(Uri uri, CancellationToken token = default)
    {
        if (!LogMarkup.PublicHttpUrl(uri.AbsoluteUri, out _))
            throw new ArgumentException("ログ画像には、資格情報を含まない公開HTTP／HTTPSアドレスを指定してください。", nameof(uri));
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        if ((int)response.StatusCode is >= 300 and < 400)
            throw new InvalidDataException("ログ画像の転送先へは自動接続しません。元の画像URLを確認してください。");
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaximumBytes)
            throw new InvalidDataException("ログ画像が1MiBを超えています。");
        await using var input = await response.Content.ReadAsStreamAsync(timeout.Token);
        using var output = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var count = await input.ReadAsync(buffer, timeout.Token);
            if (count == 0) break;
            if (output.Length + count > MaximumBytes)
                throw new InvalidDataException("ログ画像が1MiBを超えています。");
            output.Write(buffer, 0, count);
        }
        var bytes = output.ToArray();
        var type = MediaType(bytes);
        if (type is null)
            throw new InvalidDataException("ログ画像はPNG・JPEG・GIF・BMPの形式を使用してください。");
        return new(bytes, type);
    }

    public static bool PublicAddress(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
        if (!LogMarkup.PublicHttpUrl($"http://{(address.AddressFamily == AddressFamily.InterNetworkV6 ? $"[{address}]" : address.ToString())}/", out _))
            return false;
        var bytes = address.GetAddressBytes();
        if (bytes.Length == 4)
            return !(bytes[0] == 198 && bytes[1] is 18 or 19 ||
                bytes[0] == 192 && bytes[1] == 0 && bytes[2] is 0 or 2 ||
                bytes[0] == 192 && bytes[1] == 88 && bytes[2] == 99 ||
                bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100 ||
                bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113);
        return (bytes[0] & 0xe0) == 0x20 &&
            !(bytes[0] == 0x20 && bytes[1] == 2 ||
                bytes[0] == 0x20 && bytes[1] == 1 &&
                    (bytes[2] == 0 && bytes[3] == 0 || bytes[2] == 0x0d && bytes[3] == 0xb8));
    }

    private static async ValueTask<Stream> ConnectPublicAsync(SocketsHttpConnectionContext context, CancellationToken token)
    {
        var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, token);
        if (addresses.Length == 0 || addresses.Any(address => !PublicAddress(address)))
            throw new HttpRequestException("ログ画像の接続先にローカルまたは非公開アドレスが含まれています。");
        var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
        try
        {
            await socket.ConnectAsync(addresses, context.DnsEndPoint.Port, token);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    private static string? MediaType(ReadOnlySpan<byte> bytes)
    {
        if (bytes.StartsWith(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return "image/png";
        if (bytes.Length >= 3 && bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255) return "image/jpeg";
        if (bytes.StartsWith("GIF87a"u8) || bytes.StartsWith("GIF89a"u8)) return "image/gif";
        if (bytes.StartsWith("BM"u8)) return "image/bmp";
        return null;
    }

    public void Dispose() => http.Dispose();
}
