using System.Diagnostics;
using System.Text;

namespace PhiClient.Core;

public sealed class WorldDirectoryClient(string folder) : IDisposable
{
    private readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect = false, UseCookies = false })
        { Timeout = TimeSpan.FromSeconds(20) };
    private string Cache => Path.Combine(folder, "resist.ini");

    public async Task<WorldListResult> LoadAsync(CancellationToken token = default)
    {
        if (File.Exists(Cache))
        {
            try { return WorldList.Parse(await ReadFileAsync(Cache, token), $"保存済み一覧: {Cache}"); }
            catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException)
            {
                return new(WorldList.Bundled, WorldList.BundledSource,
                    $"保存済み一覧の読み込みに失敗しました: {ex.Message} wphiに同梱された一覧を表示しています。");
            }
        }
        return new(WorldList.Bundled, WorldList.BundledSource,
            "wphiに同梱された世界一覧です。更新には「HTTP 一覧を更新」または「INI ファイルから取り込む」を使います。各世界の接続可否は未確認です。");
    }

    public async Task<WorldListResult> RefreshAsync(string url, CancellationToken token = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") ||
            uri.UserInfo.Length > 0 || uri.Fragment.Length > 0)
            throw new ArgumentException("一覧の URL は資格情報を含まない HTTP または HTTPS にしてください。");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));
        using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > 2 * WorldList.MaxBytes)
            throw new InvalidDataException("一覧の配布ファイルが 2 MiB を超えています。");
        await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
        var bytes = await ReadLimitedAsync(stream, 2 * WorldList.MaxBytes, timeout.Token);
        if (bytes.Length >= 7 && Encoding.ASCII.GetString(bytes, 2, 3) == "-lh")
            bytes = await ReadLhaAsync(bytes, timeout.Token);
        var parsed = WorldList.Parse(bytes, uri.AbsoluteUri);
        await SaveAsync(bytes, timeout.Token);
        return parsed;
    }

    public async Task<WorldListResult> ImportAsync(string path, CancellationToken token = default)
    {
        if (!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\") || !File.Exists(path))
            throw new ArgumentException("既存のローカル resist.ini または worlds.ini の絶対パスを指定してください。");
        var bytes = await ReadFileAsync(path, token);
        var parsed = WorldList.Parse(bytes, path);
        await SaveAsync(bytes, token);
        return parsed;
    }

    private async Task SaveAsync(byte[] bytes, CancellationToken token)
    {
        Directory.CreateDirectory(folder);
        var temporary = Cache + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, token);
            File.Move(temporary, Cache, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static async Task<byte[]> ReadFileAsync(string path, CancellationToken token)
    {
        await using var stream = File.OpenRead(path);
        return await ReadLimitedAsync(stream, WorldList.MaxBytes, token);
    }

    private static async Task<byte[]> ReadLimitedAsync(Stream stream, int limit, CancellationToken token)
    {
        using var result = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await stream.ReadAsync(buffer, token)) > 0)
        {
            if (result.Length + count > limit) throw new InvalidDataException("世界一覧の読み込みサイズが上限を超えています。");
            result.Write(buffer, 0, count);
        }
        return result.ToArray();
    }

    public static async Task<byte[]> ReadLhaAsync(byte[] archive, CancellationToken token = default)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("LZH の読み込みには Windows の tar.exe が必要です。");
        if (archive.Length > 2 * WorldList.MaxBytes) throw new InvalidDataException("LZH が 2 MiB を超えています。");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        var path = Path.GetTempFileName();
        try
        {
            await File.WriteAllBytesAsync(path, archive, timeout.Token);
            var listing = Encoding.UTF8.GetString(await TarAsync(["-tf", path], 64 * 1024, timeout.Token));
            var entries = listing.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Where(name => name.Split('/', '\\')[^1].Equals("resist.ini", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (entries.Length != 1) throw new InvalidDataException("LZH に resist.ini が 1 つだけ必要です。");
            return await TarAsync(["-xOf", path, "--", entries[0]], WorldList.MaxBytes, timeout.Token);
        }
        finally { File.Delete(path); }
    }

    private static async Task<byte[]> TarAsync(string[] arguments, int limit, CancellationToken token)
    {
        var executable = Path.Combine(Environment.SystemDirectory, "tar.exe");
        if (!File.Exists(executable)) throw new IOException("Windows の tar.exe が見つかりません。INI ファイルから取り込んでください。");
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("LZH 読み込み処理を起動できません。");
        try
        {
            var output = ReadLimitedAsync(process.StandardOutput.BaseStream, limit, token);
            var error = ReadLimitedAsync(process.StandardError.BaseStream, 64 * 1024, token);
            await Task.WhenAll(output, error, process.WaitForExitAsync(token));
            if (process.ExitCode != 0)
                throw new InvalidDataException("LZH を読み込めません: " + Encoding.UTF8.GetString(await error).Trim());
            return await output;
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
        }
    }

    public void Dispose() => http.Dispose();
}
