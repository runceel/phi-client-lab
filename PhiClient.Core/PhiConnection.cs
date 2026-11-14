using System.Net.Sockets;

namespace PhiClient.Core;

public sealed class PhiConnection : IDisposable
{
    private readonly TcpClient client = new() { NoDelay = true };
    private readonly SemaphoreSlim sending = new(1, 1);
    private readonly CancellationTokenSource lifetime = new();
    private long lastReceive = Environment.TickCount64;
    private NetworkStream? stream;
    public event Action<ServerEvent>? Received;

    public static void ValidateEndpoint(string host, int port)
    {
        if (string.IsNullOrWhiteSpace(host) || host.Any(char.IsWhiteSpace) ||
            Uri.CheckHostName(host) == UriHostNameType.Unknown)
            throw new ArgumentException("Enter a DNS hostname or IP address, not a URL.");
        if (port is < 1 or > 65535)
            throw new ArgumentException("Port must be between 1 and 65535.");
    }

    private async Task OpenAsync(string host, int port, CancellationToken token)
    {
        ValidateEndpoint(host, port);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        await client.ConnectAsync(host, port, timeout.Token);
        stream = client.GetStream();
        if (OperatingSystem.IsWindows())
        {
            // SIO_TCP_SET_ACK_FREQUENCY: do not delay maps behind legacy servers' Nagle.
            const int setAckFrequency = unchecked((int)0x98000017);
            try
            {
                client.Client.IOControl(setAckFrequency, BitConverter.GetBytes(1), null);
            }
            catch (SocketException ex)
            {
                Received?.Invoke(new LogLine($"[Network] Delayed ACK could not be disabled: {ex.SocketErrorCode}."));
            }
        }
    }

    public async Task RunAsync(string host, int port, string id, CancellationToken token = default)
    {
        var login = PhiWire.Login(id);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        await OpenAsync(host, port, linked.Token);
        await SendAsync(login, linked.Token);
        Received?.Invoke(new ConnectionReady());
        Received?.Invoke(new LogLine("[Connected]"));
        lastReceive = Environment.TickCount64;
        var reader = ReadAsync(linked.Token);
        var keepAlive = KeepAliveAsync(linked.Token);
        try
        {
            var first = await Task.WhenAny(reader, keepAlive);
            await first;
        }
        finally
        {
            await linked.CancelAsync();
            try
            {
                await Task.WhenAll(reader, keepAlive);
            }
            catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
            client.Close();
        }
    }

    public async Task SendAsync(string[] commands, CancellationToken token = default)
    {
        var lines = commands.ToArray();
        foreach (var line in lines)
            PhiWire.ValidateLine(line);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        await sending.WaitAsync(timeout.Token);
        try
        {
            if (stream is null)
                throw new InvalidOperationException("Not connected.");
            await stream.WriteAsync(PhiWire.Encode(string.Join('\n', lines) + "\n"), timeout.Token);
        }
        finally
        {
            sending.Release();
        }
    }

    private async Task ReadAsync(CancellationToken token)
    {
        var framer = new LineFramer();
        var parser = new ProtocolParser();
        var bytes = new byte[8192];
        while (true)
        {
            var count = await stream!.ReadAsync(bytes, token);
            if (count == 0)
            {
                Received?.Invoke(new LogLine("[Disconnected by server]"));
                return;
            }
            Interlocked.Exchange(ref lastReceive, Environment.TickCount64);
            var dropped = framer.DroppedLines;
            foreach (var line in framer.Push(bytes.AsSpan(0, count)))
            {
                foreach (var item in parser.Feed(line))
                {
                    if (item is Reply reply)
                        await SendAsync([reply.Command], token);
                    else
                        Received?.Invoke(item);
                    if (item is Closed)
                        return;
                }
            }
            if (framer.DroppedLines != dropped)
                Received?.Invoke(new LogLine("[Protocol] A line over 64 KiB was discarded."));
        }
    }

    private async Task KeepAliveAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        var pinged = false;
        var warned = false;
        while (await timer.WaitForNextTickAsync(token))
        {
            var quiet = Environment.TickCount64 - Interlocked.Read(ref lastReceive);
            if (quiet < 120000)
            {
                pinged = false;
                warned = false;
            }
            if (quiet >= 120000 && !pinged)
            {
                await SendAsync(["#code-sjis"], token);
                pinged = true;
            }
            if (quiet >= 180000 && !warned)
            {
                Received?.Invoke(new LogLine("[Network] No response from the server for three minutes."));
                warned = true;
            }
        }
    }

    public static async Task<string> RegisterAsync(string host, int port, string name, string password,
        string image, string email, CancellationToken token = default)
    {
        var commands = PhiWire.Registration(name, password, image, email);
        using var connection = new PhiConnection();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        await connection.OpenAsync(host, port, timeout.Token);
        await connection.SendAsync(commands, timeout.Token);
        var framer = new LineFramer();
        var bytes = new byte[4096];
        while (true)
        {
            var count = await connection.stream!.ReadAsync(bytes, timeout.Token);
            if (count == 0)
                throw new IOException("The server disconnected before returning a character ID.");
            foreach (var line in framer.Push(bytes.AsSpan(0, count)))
            {
                var text = PhiWire.Encoding.GetString(line);
                if (text.StartsWith("#set-user-id ", StringComparison.Ordinal))
                {
                    var id = text[13..].Trim();
                    PhiWire.Login(id);
                    await connection.SendAsync(["#x"], timeout.Token);
                    return id;
                }
                if (text.StartsWith("#ex-register reject", StringComparison.Ordinal) ||
                    text is "#x" or "#close")
                    throw new IOException($"Registration rejected: {text}");
                if (text == "#lag")
                    await connection.SendAsync(["#end-lag"], timeout.Token);
            }
        }
    }

    public void Dispose()
    {
        lifetime.Cancel();
        client.Dispose();
    }
}
