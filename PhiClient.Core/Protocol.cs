using System.Text;
using System.Text.RegularExpressions;
using System.Globalization;

namespace PhiClient.Core;

public abstract record ServerEvent;
public sealed record ConnectionReady : ServerEvent;
public sealed record LogLine(string Text) : ServerEvent;
public sealed record StatusLine(string Text) : ServerEvent;
public sealed record Reply(string Command) : ServerEvent;
public sealed record Closed : ServerEvent;
public sealed record EditMode(bool Active) : ServerEvent;
public sealed record InputRequested : ServerEvent;
public sealed record Choice(string Number, string Text);
public sealed record Choices(IReadOnlyList<Choice> Items, bool Complete = true) : ServerEvent;
public sealed record Actor(int X, int Y, string Name, string Graphic = "", char Direction = 'F',
    char Size = '#', int Layer = 0, int DefaultGraphic = 1, int Status = 0);
public sealed record MapSetNotice(int Index, string Name) : ServerEvent;
public sealed record MapFrame(char Direction, byte[] Tiles, byte[] Attributes, IReadOnlyList<Actor> Actors, int Size = 7) : ServerEvent;
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
    internal bool InLiteralBlock => more;
    private bool listing;
    private readonly List<Choice> choices = [];
    private byte[]? tiles;
    private byte[]? attributes;
    private char direction = 'N';
    private int size = 7;
    private readonly List<Actor> actors = [];
    private bool clearActors = true;
    private byte[]? eagleTiles;
    private byte[]? eagleAttributes;
    private int eagleWidth, eagleHeight, eagleX = -1, eagleY = -1;

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
        if (raw.AsSpan().StartsWith("#m57 M"u8) || raw.AsSpan().StartsWith("#map M"u8))
        {
            var nextSize = raw[2] == '5' ? 7 : 5;
            if (size != nextSize)
            {
                actors.Clear();
                tiles = attributes = null;
                size = nextSize;
            }
            if (size == 5 && raw.Length >= 7 && raw.AsSpan(7).SequenceEqual("-"u8))
                return [];
            var cells = size * size;
            if (raw.Length < 18 + cells * 2)
            {
                tiles = attributes = null;
                return [new LogLine("[Protocol] Incomplete terrain frame ignored.")];
            }
            direction = (char)raw[7];
            tiles = new byte[cells];
            attributes = new byte[cells];
            for (var i = 0; i < cells; i++)
            {
                tiles[i] = raw[18 + i * 2];
                attributes[i] = size == 7 ? raw[19 + i * 2] :
                    (byte)((raw.Length > 68 + i * 2 && raw[68 + i * 2] == '.' ? 0x60 : 0) |
                        (raw.Length > 69 + i * 2 && raw[69 + i * 2] == '-' ? 0x08 : 0));
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
        if (text is "#m57 ." or "#map .")
        {
            clearActors = true;
            var expectedSize = text == "#m57 ." ? 7 : 5;
            return tiles is null || attributes is null || size != expectedSize ? [] :
                [new MapFrame(direction, (byte[])tiles.Clone(), (byte[])attributes.Clone(), actors.ToArray(), size)];
        }
        if (text.StartsWith("#ex-eagleeye ", StringComparison.Ordinal))
            return ParseEagle(raw, text);
        if (text.StartsWith("#mapset ", StringComparison.Ordinal))
            return [new MapSetNotice(0, text[8..].Trim())];
        if (text.StartsWith("#mapset-define ", StringComparison.Ordinal))
        {
            var fields = text[15..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return fields.Length == 2 && int.TryParse(fields[0], out var slot) && slot is >= 0 and <= 255
                ? [new MapSetNotice(slot % 4, fields[1])] : [new LogLine("[Protocol] Invalid mapset definition ignored.")];
        }
        switch (text)
        {
            case "#more": more = true; return [];
            case "#lag": return [new Reply("#end-lag")];
            case "#remap": return [new Reply("#map")];
            case "#close" or "#x": return [new Closed()];
            case "#list": listing = true; choices.Clear(); return [];
            case "#end-list": listing = false; return [new Choices(choices.ToArray())];
            case "#ex-list-mode-end": listing = false; return [new Choices([], false)];
            case "#m-edit": return [new EditMode(true)];
            case "#s-edit": return [new InputRequested()];
            case "#.": return [new EditMode(false)];
            case "#attack": return [new ActionMode(false, true)];
            case "#end-at": return [new ActionMode(false, false)];
            case "#magic": return [new ActionMode(true, true)];
            case "#end-mg": return [new ActionMode(true, false)];
            case "#ex-map style=solid": return [new MapStyle(true)];
            case "#ex-map style=turn": return [new MapStyle(false)];
        }
        if (text.StartsWith("#status ", StringComparison.Ordinal))
            return [new StatusLine(PlainText(text[8..]))];
        if (text.StartsWith("#ch-srv ", StringComparison.Ordinal))
            return [new Reply("#no-srv"), new LogLine("[World transfer] Not supported yet; staying on the current server.")];
        if (text.StartsWith("#cond ", StringComparison.Ordinal))
            return [new Conditions(text[6..])];
        foreach (var prefix in new[] { "#ex-notice land=", "#ex-notice area=" })
            if (text.StartsWith(prefix, StringComparison.Ordinal))
                return [new LocationNotice(prefix.Contains("land") ? "world" : "area", PlainText(text[prefix.Length..]))];
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

    private IReadOnlyList<ServerEvent> ParseEagle(byte[] raw, string text)
    {
        if (text == "#ex-eagleeye start")
        {
            eagleTiles = Enumerable.Repeat((byte)'?', 225).ToArray();
            eagleAttributes = new byte[225];
            eagleWidth = eagleHeight = 0;
            eagleX = eagleY = -1;
            return [];
        }
        if (eagleTiles is null || eagleAttributes is null)
            return [new LogLine("[Protocol] EagleEye data arrived without a start frame.")];
        if (text == "#ex-eagleeye end")
        {
            var indices = Enumerable.Range(0, eagleWidth * eagleHeight)
                .Select(i => i / eagleWidth * 15 + i % eagleWidth).ToArray();
            var frame = new EagleFrame(eagleWidth, eagleHeight, eagleX, eagleY,
                indices.Select(i => eagleTiles[i]).ToArray(), indices.Select(i => eagleAttributes[i]).ToArray());
            eagleTiles = eagleAttributes = null;
            return frame.Width == 0 || frame.Height == 0
                ? [new LogLine("[Protocol] Empty EagleEye frame ignored.")] : [frame];
        }
        if (text.StartsWith("#ex-eagleeye pos ", StringComparison.Ordinal))
        {
            var fields = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length != 4 || !int.TryParse(fields[2], out var x) || x is < 0 or > 14 ||
                !int.TryParse(fields[3], out var y) || y is < 0 or > 14)
                return [new LogLine("[Protocol] Invalid EagleEye position ignored.")];
            eagleX = x;
            eagleY = y;
            return [];
        }
        if (text.StartsWith("#ex-eagleeye M ", StringComparison.Ordinal))
        {
            var header = System.Text.Encoding.ASCII.GetString(raw.AsSpan(0, Math.Min(21, raw.Length)))
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (header.Length != 4 || !int.TryParse(header[2], out var lastX) || lastX is < 0 or > 14 ||
                !int.TryParse(header[3], out var y) || y is < 0 or > 14 || raw.Length < 21 + (lastX + 1) * 2)
                return [new LogLine("[Protocol] Invalid EagleEye row ignored.")];
            eagleWidth = Math.Max(eagleWidth, lastX + 1);
            eagleHeight = Math.Max(eagleHeight, y + 1);
            for (var x = 0; x <= lastX; x++)
            {
                eagleTiles[y * 15 + x] = raw[21 + x * 2];
                eagleAttributes[y * 15 + x] = raw[22 + x * 2];
            }
        }
        return [];
    }

    private void ParseActor(byte[] raw, int offset)
    {
        if (raw.Length < offset + 74 || actors.Count >= 1000)
            return;
        var x = raw[offset + 13] - (byte)'0';
        var y = raw[offset + 15] - (byte)'0';
        if (x is < 0 or > 6 || y is < 0 or > 6)
            return;
        int Hex(int start, int count, int empty) =>
            int.TryParse(Encoding.ASCII.GetString(raw, offset + start, count).Trim(),
                NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value) ? value : empty;
        var facing = (char)raw[offset + 17];
        actors.Add(new Actor(x, y, PhiWire.Encoding.GetString(raw, offset + 19, 31).Trim(),
            PhiWire.Encoding.GetString(raw, offset + 54, 15).Trim(),
            "BRFL".Contains(facing) ? facing : 'F', raw[offset + 70] == '*' ? '*' : '#',
            Hex(7, 1, 0), Hex(72, 2, 1), Hex(51, 2, 0)));
    }

    public static string Movement(string direction, bool northUp, bool strafe = false) =>
        northUp ? $"go {(strafe ? direction.ToLowerInvariant() : direction)}" :
        strafe ? $"turn {direction.ToLowerInvariant()}" :
        direction switch { "N" => "go", "S" => "go b", "W" => "go l", "E" => "go r", _ => throw new ArgumentException("Invalid direction.") };
}
