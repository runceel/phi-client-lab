using System.Net.Sockets;

namespace PhiClient.Core;

public sealed partial class PhiConnection
{
    private volatile bool transferring;
    private int transportGeneration;
    public int TransportVersion => Volatile.Read(ref transportGeneration);
    public Func<string, int, CancellationToken, Task<bool>>? ConfirmTransferAsync { get; set; }

    private void RejectTransferInput(int generation)
    {
        if (transferring || generation != Volatile.Read(ref transportGeneration))
            throw new InvalidOperationException("世界移動中の操作は送信しません。移動が終わってから再度操作してください。");
    }

    private static async Task WriteTransferAsync(NetworkStream target, string[] lines, CancellationToken token)
    {
        foreach (var line in lines) PhiWire.ValidateLine(line);
        await target.WriteAsync(PhiWire.Encode(string.Join('\n', lines) + "\n"), token);
    }

    private async Task<bool> WaitTransferAsync(NetworkStream source, LineFramer framer, Queue<byte[]> pending,
        string success, string[] failures, List<byte[]> skipped, CancellationToken token)
    {
        var bytes = new byte[8192];
        var literal = false;
        var skippedBytes = 0;
        while (true)
        {
            while (pending.TryDequeue(out var line))
            {
                var text = PhiWire.Encoding.GetString(line);
                if (literal)
                {
                    if (text == "#end-more") literal = false;
                }
                else if (text == "#more") literal = true;
                else
                {
                    if (text == success) return true;
                    if (failures.Contains(text, StringComparer.Ordinal))
                    {
                        if (text is "#x" or "#close") skipped.Add(line);
                        return false;
                    }
                }
                skipped.Add(line);
                skippedBytes += line.Length;
                if (skipped.Count > 4096 || skippedBytes > 2 * 1024 * 1024)
                    throw new IOException("世界移動中の受信行が上限を超えました。");
            }
            var count = await source.ReadAsync(bytes, token);
            if (count == 0) return false;
            Interlocked.Exchange(ref lastReceive, Environment.TickCount64);
            var dropped = framer.DroppedLines;
            foreach (var line in framer.Push(bytes.AsSpan(0, count))) pending.Enqueue(line);
            if (framer.DroppedLines != dropped)
                Received?.Invoke(new LogLine("[Protocol] A line over 64 KiB was discarded during transfer."));
        }
    }

    private async Task<(LineFramer Framer, Queue<byte[]> Pending)?> TransferAsync(string currentHost,
        string id, WorldTransferRequest request, LineFramer sourceFramer, Queue<byte[]> sourcePending,
        CancellationToken token)
    {
        transferring = true;
        Interlocked.Increment(ref transportGeneration);
        Received?.Invoke(new TransferProgress(true));
        var locked = false;
        var accepted = false;
        var sourceRequested = false;
        var skipped = new List<byte[]>();
        TcpClient? destination = null;
        var source = stream!;
        try
        {
            await sending.WaitAsync(token);
            locked = true;
            lock (automation)
            {
                equipment?.TrySetException(new IOException("世界移動のため魔法の準備を中止しました。"));
                hiddenLists = 0;
                equipmentPromptUntil = 0;
            }
            try { ValidateEndpoint(request.Host, request.Port); }
            catch (ArgumentException)
            {
                await WriteTransferAsync(source, ["#no-srv"], token);
                Received?.Invoke(new LogLine("世界移動先の指定が不正なため、元の世界に留まります。"));
                return null;
            }
            if (!string.Equals(currentHost, request.Host, StringComparison.OrdinalIgnoreCase) &&
                (ConfirmTransferAsync is null || !await ConfirmTransferAsync(request.Host, request.Port, token)))
            {
                await WriteTransferAsync(source, ["#no-srv"], token);
                Received?.Invoke(new LogLine("別ホストへの世界移動を取り消しました。キャラクターIDは送信していません。"));
                return null;
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            destination = new() { NoDelay = true };
            using (var connecting = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token))
            {
                connecting.CancelAfter(TimeSpan.FromSeconds(10));
                await destination.ConnectAsync(request.Host, request.Port, connecting.Token);
            }
            Tune(destination);
            var target = destination.GetStream();
            var targetFramer = new LineFramer();
            var targetPending = new Queue<byte[]>();
            Received?.Invoke(new LogLine("転送先世界に転送準備を要求しています…"));
            await WriteTransferAsync(target, [$"#reserve {id}"], timeout.Token);
            if (!await WaitTransferAsync(target, targetFramer, targetPending, "#rsv-ok",
                ["#rsv-no", "#x", "#close"], [], timeout.Token))
            {
                await WriteTransferAsync(source, ["#no-srv"], token);
                Received?.Invoke(new LogLine("転送先が準備を拒否しました。元の世界に留まります。"));
                return null;
            }
            sourceRequested = true;
            await WriteTransferAsync(source, [$"#trans {request.Host} {request.Port}"], timeout.Token);
            if (!await WaitTransferAsync(source, sourceFramer, sourcePending, "#trs-ok",
                ["#trs-no", "#x", "#close"], skipped, timeout.Token))
            {
                await WriteTransferAsync(source, ["#ch-srv-no"], token);
                Received?.Invoke(new LogLine("転送元が世界移動を拒否しました。元の世界の受信を再開します。"));
                return null;
            }
            accepted = true;
            await WriteTransferAsync(target, ["#ch-srv-ok", .. PhiWire.LoginSettings()], timeout.Token);
            var previous = client;
            client = destination;
            stream = target;
            previous.Dispose();
            Interlocked.Exchange(ref lastReceive, Environment.TickCount64);
            Received?.Invoke(new WorldMoved(request.Host, request.Port));
            Received?.Invoke(new LogLine("世界移動が完了しました。"));
            return (targetFramer, targetPending);
        }
        catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException &&
            !token.IsCancellationRequested)
        {
            if (accepted)
                throw new IOException("転送元の承認後、転送先への送信に失敗しました。接続を終了します。", ex);
            await WriteTransferAsync(source, [sourceRequested ? "#ch-srv-no" : "#no-srv"], token);
            Received?.Invoke(new LogLine("転送先への接続または応答に失敗しました。元の世界に留まります。"));
            return null;
        }
        finally
        {
            if (destination is not null && client != destination) destination.Dispose();
            if (ReferenceEquals(stream, source) && skipped.Count > 0)
            {
                var remaining = sourcePending.ToArray();
                sourcePending.Clear();
                foreach (var line in skipped) sourcePending.Enqueue(line);
                foreach (var line in remaining) sourcePending.Enqueue(line);
            }
            transferring = false;
            if (locked) sending.Release();
            Received?.Invoke(new TransferProgress(false));
        }
    }
}
