using System.Text;

namespace PhiClient.Core;

public static class SoundFiles
{
    public static async Task<SoundRules> LoadAsync(string root, CancellationToken token = default)
    {
        if (root.Length == 0) return SoundRules.Parse("", "");
        LocalPath(root, directory: true);
        var config = Path.Combine(root, "Config");
        var expressions = await ReadAsync(Path.Combine(config, "SoundRegexp.ini"), token);
        var files = await ReadAsync(Path.Combine(config, "SoundFile.ini"), token);
        var exclusion = Path.Combine(config, "NotFlashRegexp.ini");
        string? notFlash;
        try { notFlash = await ReadAsync(exclusion, token); }
        catch (FileNotFoundException) { notFlash = null; }
        return await Task.Run(() => SoundRules.Parse(expressions, files, notFlash), token);
    }

    public static string WavePath(string root, string value)
    {
        if (!Path.IsPathFullyQualified(root))
            throw new ArgumentException("効果音を使うPhillyフォルダーの絶対パスを入力してください。");
        var path = Path.GetFullPath(Path.IsPathFullyQualified(value) ? value : Path.Combine(root, value));
        LocalPath(path, directory: false);
        if (!Path.GetExtension(path).Equals(".wav", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("効果音はWAVファイルを指定してください。");
        if (new FileInfo(path).Length > 16 * 1024 * 1024)
            throw new InvalidDataException("効果音のWAVを16MiB以内に収めてください。");
        return path;
    }

    private static void LocalPath(string path, bool directory)
    {
        if (!Path.IsPathFullyQualified(path) || path.StartsWith(@"\\", StringComparison.Ordinal) ||
            new DriveInfo(Path.GetPathRoot(path)!).DriveType == DriveType.Network)
            throw new ArgumentException("このPCの通常フォルダーまたはファイルを指定してください。");
        var full = Path.GetFullPath(path);
        var attributes = File.GetAttributes(full);
        if (attributes.HasFlag(FileAttributes.Directory) != directory)
            throw new ArgumentException("指定した効果音フォルダーまたはファイルの種類を確認してください。");
        for (var current = full; current is not null; current = Path.GetDirectoryName(current))
            if (File.GetAttributes(current).HasFlag(FileAttributes.ReparsePoint))
                throw new ArgumentException("効果音の読み込みにはリンクを含まないローカルパスを指定してください。");
    }

    private static async Task<string> ReadAsync(string path, CancellationToken token)
    {
        LocalPath(path, directory: false);
        await using var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, true);
        using var bytes = new MemoryStream();
        var buffer = new byte[8192];
        int count;
        while ((count = await input.ReadAsync(buffer, token)) > 0)
        {
            if (bytes.Length + count > 1024 * 1024)
                throw new InvalidDataException("効果音の設定ファイルを1MiB以内に収めてください。");
            bytes.Write(buffer, 0, count);
        }
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(932, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)
            .GetString(bytes.GetBuffer(), 0, checked((int)bytes.Length));
    }
}
