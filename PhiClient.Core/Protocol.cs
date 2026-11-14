using System.Text;
using System.Text.RegularExpressions;

namespace PhiClient.Core;

public abstract record ServerEvent;
public sealed record ConnectionReady : ServerEvent;
public sealed record LogLine(string Text) : ServerEvent;
public sealed record StatusLine(string Text) : ServerEvent;
public sealed record Reply(string Command) : ServerEvent;
public sealed record Closed : ServerEvent;
public sealed record EditMode(bool Active) : ServerEvent;
public sealed record Choice(string Number, string Text);
public sealed record Choices(IReadOnlyList<Choice> Items) : ServerEvent;
public sealed record Actor(int X, int Y, string Name);
public sealed record MapFrame(char Direction, byte[] Tiles, byte[] Attributes, IReadOnlyList<Actor> Actors) : ServerEvent;
public sealed record MapStyle(bool NorthUp) : ServerEvent;

public static class PhiWire
{
    public const int MaximumLineBytes = 65536;
    public static Encoding Encoding { get; } = CreateEncoding();

    private static Encoding CreateEncoding()
    {
        System.Text.Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return System.Text.Encoding.GetEncoding(932, new EncoderReplacementFallback("?"),
            new DecoderReplacementFallback("\uFFFD"));
    }

    public static void ValidateLine(string line)
    {
        if (line.IndexOfAny(['\r', '\n', '\0']) >= 0)
            throw new ArgumentException("A command must not contain a line break or NUL.");
        if (Encode(line).Length > MaximumLineBytes)
            throw new ArgumentException("The command exceeds 64 KiB in CP932.");
    }

    public static byte[] Encode(string text)
    {
        if (!text.Any(char.IsSurrogate))
            return Encoding.GetBytes(text);
        // CP932's default fallback replaces both halves of a surrogate pair.
        // PHI uses one '?' per unrepresentable Unicode scalar instead.
        var normalized = new StringBuilder(text.Length);
        foreach (var rune in text.EnumerateRunes())
            normalized.Append(rune.IsBmp ? (char)rune.Value : '?');
        return Encoding.GetBytes(normalized.ToString());
    }

    public static string[] Login(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Any(char.IsWhiteSpace))
            throw new ArgumentException("A character ID must be a non-empty single token.");
        ValidateLine(id);
        return [
            $"#open {id}", "#version-cli 05107100", "#map-iv 2", "#status-iv 2",
            "#ex-switch eagleeye=form", "#ex-map size=57", "#ex-map style=solid",
            "#ex-switch ex-move-recv=true", "#ex-switch ex-list-mode-end=true",
            "#ex-switch ex-disp-magic=false", "#ex-switch ex-abspos=true"
        ];
    }

    public static string[] Registration(string name, string password, string image, string email)
    {
        foreach (var value in new[] { name, password, image, email })
            ValidateLine(value);
        if (string.IsNullOrWhiteSpace(name) || name.Contains('"') || name.Contains('\\'))
            throw new ArgumentException("Enter a name without quotes or backslashes.");
        if (password.Length != 6 || password.Any(c => !char.IsAsciiLetterOrDigit(c)))
            throw new ArgumentException("The password must contain exactly six ASCII letters or digits.");
        if (!int.TryParse(image, out var number) || number < 0)
            throw new ArgumentException("The initial image must be a non-negative number.");
        if (string.IsNullOrWhiteSpace(email) || email.Any(char.IsWhiteSpace))
            throw new ArgumentException("Enter an email address without spaces.");
        return ["#ex-register start", $"name=\"{name}\" pass={password} image={number} mail={email}", "#ex-register end"];
    }
}

public sealed class LineFramer
{
    private readonly List<byte> buffer = [];
    private bool dropping;
    public int DroppedLines { get; private set; }

    public IReadOnlyList<byte[]> Push(ReadOnlySpan<byte> bytes)
    {
        var lines = new List<byte[]>();
        foreach (var b in bytes)
        {
            if (b == (byte)'\n')
            {
                if (!dropping)
                {
                    if (buffer.Count > 0 && buffer[^1] == (byte)'\r')
                        buffer.RemoveAt(buffer.Count - 1);
                    if (buffer.Count > 0)
                        lines.Add(buffer.ToArray());
                }
                buffer.Clear();
                dropping = false;
            }
            else if (!dropping)
            {
                if (buffer.Count == PhiWire.MaximumLineBytes)
                {
                    buffer.Clear();
                    dropping = true;
                    DroppedLines++;
                }
                else
                    buffer.Add(b);
            }
        }
        return lines;
    }
}

public sealed partial class ProtocolParser
{
    private bool more;
    private bool listing;
    private readonly List<Choice> choices = [];
    private byte[]? tiles;
    private byte[]? attributes;
    private char direction = 'N';
    private readonly List<Actor> actors = [];
    private bool clearActors = true;

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex TagPattern();
    [GeneratedRegex(@"\[\s*(\d+)\]\s*(.*)", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex ChoicePattern();

    public static string PlainText(string text) => TagPattern().Replace(text.Replace("/*br*/", "\n"), "");

    public IReadOnlyList<ServerEvent> Feed(byte[] raw)
    {
        var text = PhiWire.Encoding.GetString(raw);
        if (more)
        {
            if (text == "#end-more")
            {
                more = false;
                return [];
            }
            return [new LogLine(PlainText(text))];
        }
        if (raw.AsSpan().StartsWith("#m57 M"u8))
        {
            if (raw.Length < 18 + 98)
                return [new LogLine("[Protocol] Incomplete 7x7 terrain frame ignored.")];
            direction = (char)raw[7];
            tiles = new byte[49];
            attributes = new byte[49];
            for (var i = 0; i < 49; i++)
            {
                tiles[i] = raw[18 + i * 2];
                attributes[i] = raw[19 + i * 2];
            }
            return [];
        }
        if (text.StartsWith("#m57 O", StringComparison.Ordinal) ||
            text.StartsWith("#m57 W", StringComparison.Ordinal))
        {
            if (clearActors)
            {
                actors.Clear();
                clearActors = false;
            }
            ParseActor(raw, 0);
            if (raw[5] == 'W')
                ParseActor(raw, 68);
            return [];
        }
        if (text == "#m57 .")
        {
            clearActors = true;
            return tiles is null || attributes is null ? [] :
                [new MapFrame(direction, (byte[])tiles.Clone(), (byte[])attributes.Clone(), actors.ToArray())];
        }
        switch (text)
        {
            case "#more": more = true; return [];
            case "#lag": return [new Reply("#end-lag")];
            case "#remap": return [new Reply("#map")];
            case "#close" or "#x": return [new Closed()];
            case "#list": listing = true; choices.Clear(); return [];
            case "#end-list": listing = false; return [new Choices(choices.ToArray())];
            case "#ex-list-mode-end": listing = false; return [new Choices([])];
            case "#m-edit": return [new EditMode(true)];
            case "#.": return [new EditMode(false)];
            case "#ex-map style=solid": return [new MapStyle(true)];
            case "#ex-map style=turn": return [new MapStyle(false)];
        }
        if (text.StartsWith("#status ", StringComparison.Ordinal))
            return [new StatusLine(PlainText(text[8..]))];
        if (text.StartsWith("#ch-srv ", StringComparison.Ordinal))
            return [new Reply("#no-srv"), new LogLine("[World transfer] Not supported yet; staying on the current server.")];
        if (text.StartsWith("#cond ", StringComparison.Ordinal))
            return [new LogLine($"[Conditions] {text[6..]}")];
        if (text.StartsWith("#map M", StringComparison.Ordinal))
            return [new LogLine("[Protocol] Legacy 5x5 maps are not supported; request 7x7 with #ex-map size=57.")];
        if (text.Length > 1 && text[0] == '#' && text[1] is not '#' and not ' ')
            return [];
        var plain = PlainText(text);
        if (listing)
        {
            var match = ChoicePattern().Match(plain);
            if (match.Success && choices.Count < 1000)
                choices.Add(new Choice(match.Groups[1].Value, match.Groups[2].Value));
            return [];
        }
        return [new LogLine(plain)];
    }

    private void ParseActor(byte[] raw, int offset)
    {
        if (raw.Length < offset + 74 || actors.Count >= 1000)
            return;
        var x = raw[offset + 13] - (byte)'0';
        var y = raw[offset + 15] - (byte)'0';
        if (x is < 0 or > 6 || y is < 0 or > 6)
            return;
        actors.Add(new Actor(x, y, PhiWire.Encoding.GetString(raw, offset + 19, 31).Trim()));
    }

    public static string Movement(string direction, bool northUp, bool strafe = false) =>
        northUp ? $"go {(strafe ? direction.ToLowerInvariant() : direction)}" :
        strafe ? $"turn {direction.ToLowerInvariant()}" :
        direction switch { "N" => "go", "S" => "go b", "W" => "go l", "E" => "go r", _ => throw new ArgumentException("Invalid direction.") };
}
