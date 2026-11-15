using System.Collections.Immutable;
using System.Globalization;

namespace PhiClient.Core;

public sealed record WorldEndpoint(string Name, string Host, int Port);
public sealed record WorldListResult(ImmutableArray<WorldEndpoint> Items, string Source, string Message);
public sealed record WorldDirectoryModel
{
    public ImmutableArray<WorldEndpoint> Items { get; init; } = WorldList.Bundled;
    public string Source { get; init; } = WorldList.BundledSource;
    public string Url { get; init; } = WorldList.DownloadUrl;
    public string Filter { get; init; } = "";
    public string Message { get; init; } = "原典同梱の世界一覧です。接続できるかは確認していません。";
    public bool Loading { get; init; }
    public bool Visible { get; init; }
}

public static class WorldList
{
    public const int MaxBytes = 1024 * 1024;
    public const string DownloadUrl = "http://www.tofup.net/raphi/resist.lzh";
    public const string BundledSource = "wphi d8f11bf / Config/worlds.ini";

    // Only endpoint facts from the original snapshot; no world descriptions or credentials.
    public static ImmutableArray<WorldEndpoint> Bundled { get; } =
    [
        new("Metro Planetarium", "59.106.178.128", 20033),
        new("The United Kingdom NEO", "59.106.178.128", 20019),
        new("Kuzu", "59.106.178.128", 20029),
        new("Jimble-Jamble Immensity", "59.106.178.128", 20022),
        new("LE-SOLEIL", "182.48.40.254", 20035),
        new("Ryur", "182.48.40.254", 20032),
        new("Gaiearth", "182.48.40.254", 20029),
        new("No One Individual", "49.212.76.90", 20037),
        new("Capital Ruins", "49.212.76.90", 20029),
        new("Ketty Ridge", "49.212.76.90", 20018),
        new("Remnant of Sacred Era", "59.106.177.144", 20018),
        new("Centuries old Tower［GURAN］", "59.106.177.144", 20031),
        new("Silent Lake", "59.106.177.144", 20023),
        new("International Space Station", "59.106.177.144", 20024),
        new("Ranzaia Continent World", "59.106.178.128", 20037),
        new("Closed Worship", "59.106.178.128", 20018),
        new("Metro Planetarium Gateway", "59.106.178.128", 20023)
    ];

    public static WorldListResult Parse(byte[] bytes, string source)
    {
        if (bytes.Length > MaxBytes) throw new InvalidDataException("世界一覧は 1 MiB 以下にしてください。");
        var text = PhiWire.Encoding.GetString(bytes);
        var items = ImmutableArray.CreateBuilder<WorldEndpoint>();
        var warnings = new List<string>();
        string? name = null;
        var host = "";
        var port = "";
        var sections = 0;
        void Finish()
        {
            if (name is null) return;
            if (name.Length == 0 || name.Any(char.IsControl) ||
                !int.TryParse(port, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ||
                number is < 1 or > 65535 || string.IsNullOrWhiteSpace(host) ||
                host.Any(char.IsWhiteSpace) || Uri.CheckHostName(host) == UriHostNameType.Unknown)
                warnings.Add($"「{name}」の Address または Port が不正です。");
            else items.Add(new(name, host, number));
        }
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
            if (line.StartsWith('[') && line.EndsWith(']'))
            {
                Finish();
                if (++sections > 1024) throw new InvalidDataException("世界一覧の項目数が上限を超えています。");
                name = line[1..^1];
                host = port = "";
            }
            else if (name is not null)
            {
                var parts = line.Split('=', 2);
                if (parts.Length != 2) continue;
                switch (parts[0].Trim().ToLowerInvariant())
                {
                    case "address": host = parts[1].Trim(); break;
                    case "port": port = parts[1].Trim(); break;
                }
            }
        }
        Finish();
        if (items.Count == 0) throw new InvalidDataException("Address と Port を持つ有効な世界がありません。");
        var message = $"{items.Count} 件の世界を読み込みました。";
        if (warnings.Count > 0) message += $" 不正な {warnings.Count} 件を除外しました。{warnings[0]}";
        return new(items.ToImmutable(), source, message);
    }
}
