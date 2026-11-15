using System.Globalization;
using System.Text;

namespace PhiClient.Core;

public sealed class PhillyProfile(string name, string host, int port, string id)
{
    public string Name { get; } = name;
    public string Host { get; } = host;
    public int Port { get; } = port;
    public string Id { get; } = id;
    public override string ToString() => Name;
}

public sealed record PhillyProfilesResult(IReadOnlyList<PhillyProfile> Profiles, int Rejected);

public static class PhillyProfiles
{
    private const int MaximumBytes = 64 * 1024;
    private const int MaximumProfiles = 1000;

    public static PhillyProfile Parse(string directoryName, ReadOnlySpan<byte> bytes)
    {
        if (!directoryName.EndsWith(".chara", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(directoryName[..^6]) || bytes.Length > MaximumBytes)
            throw new InvalidDataException("Phillyの登録フォルダー名またはファイルサイズを確認してください。");
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var text = Encoding.GetEncoding(932, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback)
            .GetString(bytes);
        using var reader = new StringReader(text);
        var id = reader.ReadLine()?.Trim() ?? "";
        var address = reader.ReadLine()?.Trim() ?? "";
        var separator = address.IndexOfAny([':', ' ', '\t']);
        if (separator <= 0 || !int.TryParse(address[(separator + 1)..].TrimStart(':', ' ', '\t'),
            NumberStyles.None, CultureInfo.InvariantCulture, out var port))
            throw new InvalidDataException("登録ファイルの2行目を「ホスト:ポート」の形式で確認してください。");
        PhiWire.Login(id);
        var host = address[..separator];
        PhiConnection.ValidateEndpoint(host, port);
        return new(directoryName[..^6], host, port, id);
    }

    public static async Task<PhillyProfilesResult> ReadAsync(string root, CancellationToken token = default)
    {
        if (!Path.IsPathFullyQualified(root) || root.StartsWith(@"\\", StringComparison.Ordinal))
            throw new ArgumentException("このPCにあるPhillyフォルダーの絶対パスを入力してください。");
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0 ||
            new DriveInfo(Path.GetPathRoot(root)!).DriveType == DriveType.Network)
            throw new ArgumentException("ローカルの通常フォルダーを選んでください。共有やリンクは取り込み対象外です。");
        var profiles = new List<PhillyProfile>();
        var rejected = 0;
        foreach (var directory in Directory.EnumerateDirectories(root))
        {
            token.ThrowIfCancellationRequested();
            var name = Path.GetFileName(directory);
            if (!name.EndsWith(".chara", StringComparison.OrdinalIgnoreCase)) continue;
            if (profiles.Count + rejected >= MaximumProfiles)
                throw new InvalidDataException("一度に取り込む登録フォルダーを1000件以下にしてください。");
            try
            {
                var path = Path.Combine(directory, ".phirc");
                if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0 ||
                    (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("リンクされた登録ファイルは取り込み対象外です。");
                await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                if (file.Length > MaximumBytes)
                    throw new InvalidDataException("登録ファイルが64 KiBを超えています。");
                var bytes = new byte[(int)file.Length];
                await file.ReadExactlyAsync(bytes, token);
                profiles.Add(Parse(name, bytes));
            }
            catch (Exception error) when (error is IOException or InvalidDataException or
                UnauthorizedAccessException or ArgumentException)
            {
                rejected++;
            }
        }
        profiles.Sort((left, right) => StringComparer.Ordinal.Compare(left.Name, right.Name));
        return new(profiles, rejected);
    }

    public static List<SavedCharacter> Plan(IReadOnlyList<SavedCharacter> existing,
        IReadOnlyList<PhillyProfile> imported)
    {
        if (imported.Count > MaximumProfiles ||
            imported.Select(profile => profile.Name).Distinct(StringComparer.Ordinal).Count() != imported.Count)
            throw new InvalidDataException("取り込み対象の数と表示名の重複を確認してください。");
        foreach (var profile in imported)
        {
            if (string.IsNullOrWhiteSpace(profile.Name)) throw new InvalidDataException("表示名を確認してください。");
            PhiConnection.ValidateEndpoint(profile.Host, profile.Port);
            PhiWire.Login(profile.Id);
        }
        var names = imported.Select(profile => profile.Name).ToHashSet(StringComparer.Ordinal);
        return existing.Where(character => !names.Contains(character.Name)).Concat(imported.Select(profile =>
            new SavedCharacter(Guid.NewGuid().ToString("N"), profile.Name, profile.Host, profile.Port))).ToList();
    }
}
