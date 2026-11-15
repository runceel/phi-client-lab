using System.Globalization;
using System.Text;

namespace PhiClient.Core;

public sealed class LogStore(string folder)
{
    private readonly SemaphoreSlim writes = new(1, 1);
    private long pendingBytes;
    public string Folder(string label) => Path.Combine(folder, SafeLabel(label));
    public async Task FlushAsync()
    {
        await writes.WaitAsync();
        writes.Release();
    }

    public async Task<string> AppendAsync(string label, LogEntry row, ClientOptions options,
        CancellationToken cancellationToken = default)
    {
        if (row.Time is null) throw new ArgumentException("保存するログには受信時刻が必要です。", nameof(row));
        var date = row.Time.Value.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var path = Path.Combine(Folder(label), date + (options.LogSaveHtml ? ".html" : ".txt"));
        var data = Encoding.UTF8.GetBytes(options.LogSaveHtml ? LogFormat.Html(row) : LogFormat.Text(row) + "\n");
        if (Interlocked.Add(ref pendingBytes, data.Length) > 8 * 1024 * 1024)
        {
            Interlocked.Add(ref pendingBytes, -data.Length);
            throw new IOException("ログ保存が受信に追いついていません。自動保存を停止します。");
        }
        try
        {
            await writes.WaitAsync(cancellationToken);
            try
            {
                Directory.CreateDirectory(Folder(label));
                await using var file = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, true);
                if (file.Length == 0 && options.LogSaveHtml)
                    await file.WriteAsync(Encoding.UTF8.GetBytes(LogFormat.Header(label + " " + date, options)), cancellationToken);
                await file.WriteAsync(data, cancellationToken);
                await file.FlushAsync(cancellationToken);
                return path;
            }
            finally { writes.Release(); }
        }
        finally { Interlocked.Add(ref pendingBytes, -data.Length); }
    }

    public async Task<string> SaveBufferAsync(string label, IReadOnlyList<LogEntry> rows, ClientOptions options,
        CancellationToken cancellationToken = default)
    {
        var path = Path.Combine(Folder(label), $"LogBuffer-{Guid.NewGuid():N}.html");
        var temporary = path + ".tmp";
        var data = Encoding.UTF8.GetBytes(LogFormat.Header(label, options) +
            string.Concat(rows.Select(row => LogFormat.Html(row, options.LogDispTime))));
        await writes.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Folder(label));
            await using (var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true))
            {
                await file.WriteAsync(data, cancellationToken);
                await file.FlushAsync(cancellationToken);
            }
            File.Move(temporary, path);
            return path;
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            finally { writes.Release(); }
        }
    }

    public static string SafeLabel(string label)
    {
        var name = new string(label.Select(character => char.IsControl(character) || "/\\:*?\"<>|".Contains(character)
            ? '_' : character).ToArray()).Trim().Trim('.').Trim();
        if (name.Length == 0) return "_";
        var stem = name.Split('.', 2)[0].ToUpperInvariant();
        if (stem is "CON" or "PRN" or "AUX" or "NUL" || stem.Length == 4 &&
            (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal)) &&
            char.IsAsciiDigit(stem[3]))
            name = "_" + name;
        if (name.Length > 100) name = name[..80] + "-" +
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(name)))[..16];
        return name;
    }
}
