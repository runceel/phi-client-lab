using System.Net.Sockets;

namespace PhiClient.Core;

public sealed partial class PhiConnection : IDisposable
{
    private TcpClient client = new() { NoDelay = true };
    private readonly SemaphoreSlim sending = new(1, 1);
    private readonly SemaphoreSlim actions = new(1, 1);
    private readonly object automation = new();
    private readonly object parserGate = new();
    private ProtocolParser? currentParser;
    private readonly CancellationTokenSource lifetime = new();
    private TaskCompletionSource<Choices?>? equipment;
    private int hiddenLists;
    private int scriptHiddenLists;
    private long hideUntil;
    private long equipmentPromptUntil;
    private long equipmentConfirmationUntil;
    private bool reequipRequested;
    private long lastReceive = Environment.TickCount64;
    private NetworkStream? stream;
    public event Action<ServerEvent>? Received;
    public event Action<string, long>? Sending;

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
        Tune(client);
    }

    private void Tune(TcpClient target)
    {
        if (OperatingSystem.IsWindows())
        {
            // SIO_TCP_SET_ACK_FREQUENCY: do not delay maps behind legacy servers' Nagle.
            const int setAckFrequency = unchecked((int)0x98000017);
            try
            {
                target.Client.IOControl(setAckFrequency, BitConverter.GetBytes(1), null);
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
        var reader = ReadAsync(host, id, linked.Token);
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

    public Task SendAsync(string[] commands, CancellationToken token = default, int suppressLists = 0) =>
        SendBatchAsync(commands, token, suppressLists, Volatile.Read(ref transportGeneration));

    public Task SendPinnedAsync(string[] commands, int generation, CancellationToken token = default) =>
        SendBatchAsync(commands, token, 0, generation);

    public Task SendScriptAsync(string[] commands, int suppressLists, int generation, CancellationToken token = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(suppressLists);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(suppressLists, 4096);
        return SendBatchAsync(commands, token, 0, generation, suppressLists);
    }

    private async Task SendBatchAsync(string[] commands, CancellationToken token, int suppressLists, int generation,
        int scriptSuppressLists = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(suppressLists);
        var lines = commands.ToArray();
        foreach (var line in lines)
            PhiWire.ValidateLine(line);
        RejectTransferInput(generation);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        await sending.WaitAsync(timeout.Token);
        try
        {
            if (stream is null)
                throw new InvalidOperationException("Not connected.");
            RejectTransferInput(generation);
            if (scriptSuppressLists > 0)
                lock (automation)
                {
                    if (scriptHiddenLists > 4096 - scriptSuppressLists)
                        throw new InvalidOperationException("超魔法の非表示リストの返事が多く残っています。再接続してから実行してください。");
                    scriptHiddenLists += scriptSuppressLists;
                }
            if (suppressLists > 0)
                lock (automation)
                {
                    if (Environment.TickCount64 >= hideUntil)
                        hiddenLists = 0;
                    hiddenLists += suppressLists;
                    hideUntil = Environment.TickCount64 + 5000;
                }
            var at = Environment.TickCount64;
            if (lines.Contains("#ulist", StringComparer.Ordinal))
            {
                lock (parserGate) currentParser?.ClearUsers();
                Received?.Invoke(new UserList([]) { Cleared = true });
            }
            foreach (var line in lines) Sending?.Invoke(line, at);
            await stream.WriteAsync(PhiWire.Encode(string.Join('\n', lines) + "\n"), timeout.Token);
        }
        finally
        {
            sending.Release();
        }
    }

    public async Task SendActionAsync(InputAction action, CancellationToken token = default)
    {
        var generation = Volatile.Read(ref transportGeneration);
        foreach (var line in action.Commands)
            PhiWire.ValidateLine(line);
        RejectTransferInput(generation);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, lifetime.Token);
        await actions.WaitAsync(linked.Token);
        try
        {
            RejectTransferInput(generation);
            var commands = action.Commands;
            if (action.AutoUnequip)
            {
                var response = new TaskCompletionSource<Choices?>(TaskCreationOptions.RunContinuationsAsynchronously);
                lock (automation)
                {
                    equipment = response;
                    reequipRequested = action.AutoReequip;
                }
                Choices? list;
                try
                {
                    await SendBatchAsync(["unequip"], linked.Token, 0, generation);
                    list = await response.Task.WaitAsync(TimeSpan.FromSeconds(5), linked.Token);
                }
                catch (TimeoutException ex)
                {
                    throw new IOException("No equipment response within five seconds; the spell was not cast.", ex);
                }
                finally
                {
                    lock (automation)
                    {
                        equipment = null;
                        reequipRequested = false;
                    }
                }
                if (list is not null)
                {
                    var weaponName = GameInput.FirstWeaponName(list);
                    commands = [weaponName is not null ? "1" : ".", .. commands];
                    if (action.AutoReequip && weaponName is not null)
                        commands = [.. commands, "equip " + weaponName];
                }
            }
            await SendBatchAsync(commands, linked.Token, action.HiddenLists, generation);
        }
        finally
        {
            actions.Release();
        }
    }

    private bool ConsumeAutomation(ServerEvent item)
    {
        lock (automation)
        {
            var now = Environment.TickCount64;
            if (equipment is not null)
            {
                if (item is Choices { Complete: true } list && equipment.TrySetResult(list))
                {
                    equipmentPromptUntil = now + 5000;
                    if (reequipRequested && GameInput.FirstWeaponName(list) is not null)
                        equipmentConfirmationUntil = now + 5000;
                    return true;
                }
                if (item is LogLine line && (line.Text.Contains("装備を外せません", StringComparison.Ordinal) ||
                    line.Text.Contains("なにも装備していません", StringComparison.Ordinal) ||
                    line.Text.Contains("何も装備していません", StringComparison.Ordinal) ||
                    line.Text.Contains("Can not unequip.", StringComparison.OrdinalIgnoreCase)))
                    return equipment.TrySetResult(null);
            }
            if (item is LogLine prompt && now < equipmentPromptUntil &&
                (prompt.Text.Contains("外す装備を選択", StringComparison.Ordinal) ||
                    prompt.Text.Contains("Input item number to unequip.", StringComparison.Ordinal) ||
                    prompt.Text.Contains("Select equipment to unequip", StringComparison.OrdinalIgnoreCase)))
            {
                equipmentPromptUntil = 0;
                return true;
            }
            if (item is LogLine equipped && now < equipmentConfirmationUntil &&
                equipped.Text.Contains("を装備した", StringComparison.Ordinal))
            {
                equipmentConfirmationUntil = 0;
                return true;
            }
            if (item is Choices { Complete: true } && scriptHiddenLists > 0)
            {
                scriptHiddenLists--;
                return true;
            }
            if (item is Choices { Complete: true } && hiddenLists > 0)
            {
                if (now < hideUntil)
                {
                    hiddenLists--;
                    return true;
                }
                hiddenLists = 0;
            }
            return false;
        }
    }

    private async Task ReadAsync(string host, string id, CancellationToken token)
    {
        var framer = new LineFramer();
        var parser = new ProtocolParser();
        lock (parserGate) currentParser = parser;
        var pending = new Queue<byte[]>();
        var bytes = new byte[8192];
        while (true)
        {
            if (pending.Count == 0)
            {
                var count = await stream!.ReadAsync(bytes, token);
                if (count == 0)
                {
                    Received?.Invoke(new LogLine("[Disconnected by server]"));
                    return;
                }
                Interlocked.Exchange(ref lastReceive, Environment.TickCount64);
                var dropped = framer.DroppedLines;
                foreach (var line in framer.Push(bytes.AsSpan(0, count))) pending.Enqueue(line);
                if (framer.DroppedLines != dropped)
                    Received?.Invoke(new LogLine("[Protocol] A line over 64 KiB was discarded."));
                continue;
            }
            IReadOnlyList<ServerEvent> items;
            lock (parserGate) items = parser.Feed(pending.Dequeue());
            foreach (var item in items)
            {
                if (item is WorldTransferRequest request)
                {
                    if (await TransferAsync(host, id, request, framer, pending, token) is { } destination)
                    {
                        (framer, pending) = destination;
                        parser = new ProtocolParser();
                        lock (parserGate) currentParser = parser;
                        host = request.Host;
                    }
                }
                else if (item is Reply reply)
                    await SendAsync([reply.Command], token);
                else if (parser.InLiteralBlock || !ConsumeAutomation(item))
                    Received?.Invoke(item);
                if (item is Closed) return;
            }
        }
    }

    private async Task KeepAliveAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
        var pinged = false;
        var warned = false;
        while (await timer.WaitForNextTickAsync(token))
        {
            if (transferring) continue;
            var quiet = Environment.TickCount64 - Interlocked.Read(ref lastReceive);
            if (quiet < 120000)
            {
                pinged = false;
                warned = false;
            }
            if (quiet >= 120000 && !pinged)
            {
                var generation = Volatile.Read(ref transportGeneration);
                try { await SendBatchAsync(["#code-sjis"], token, 0, generation); }
                catch (InvalidOperationException) when (transferring ||
                    generation != Volatile.Read(ref transportGeneration))
                {
                    // A transport change restarts the idle clock; do not terminate the session over an obsolete ping.
                    continue;
                }
                pinged = true;
            }
            if (quiet >= 180000 && !warned)
            {
                Received?.Invoke(new LogLine("[Network] No response from the server for three minutes."));
                warned = true;
            }
        }
    }

    public static async Task<IReadOnlyList<string>> RegistrationGraphicsAsync(string host, int port,
        CancellationToken token = default)
    {
        using var connection = new PhiConnection();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        await connection.OpenAsync(host, port, timeout.Token);
        await connection.SendAsync(["#ex-get REGINFO IMG"], timeout.Token);
        var framer = new LineFramer();
        var bytes = new byte[4096];
        var started = false;
        var names = new List<string>();
        var total = 0;
        while (true)
        {
            var count = await connection.stream!.ReadAsync(bytes, timeout.Token);
            if (count == 0)
                throw new IOException("初期画像の一覧を受け取る前にサーバーから切断されました。");
            var lines = framer.Push(bytes.AsSpan(0, count));
            if (framer.DroppedLines > 0)
                throw new IOException("初期画像の候補に64KiBを超える行が含まれています。番号のずれを防ぐため取得を中止しました。");
            foreach (var raw in lines)
            {
                var line = PhiWire.Encoding.GetString(raw).Trim();
                if (!started)
                {
                    started = line == "#ex-put REGINFO IMG";
                    continue;
                }
                if (line == "#ex-put .") return names;
                total += line.Length;
                if (names.Count >= 4096 || total > 1024 * 1024)
                    throw new IOException("初期画像の候補が4096件または1Mi文字を超えました。");
                names.Add(line);
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
                if (text.StartsWith("#ex-register reject", StringComparison.Ordinal))
                {
                    var fields = text[19..].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                        .Where(field => field is "name" or "pass" or "image" or "mail").Distinct();
                    throw new IOException("キャラクターの作成が拒否されました。確認する項目: " +
                        string.Join(", ", fields.Select(field => field switch
                        {
                            "name" => "名前", "pass" => "パスワード", "image" => "初期画像", _ => "メールアドレス"
                        })));
                }
                if (text is "#x" or "#close")
                    throw new IOException("キャラクターの作成結果を受け取る前にサーバーから切断されました。");
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
