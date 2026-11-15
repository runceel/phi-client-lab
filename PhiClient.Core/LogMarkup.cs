using System.Net;
using System.Text.RegularExpressions;

namespace PhiClient.Core;

public sealed record LogStyle(string? Color = null, double? Size = null, string? Font = null,
    bool Bold = false, bool Italic = false, bool Underline = false, bool Strike = false,
    bool Overline = false, int Baseline = 0, double Scale = 1);
public sealed record LogPart(string Text, LogStyle Style, Uri? Link = null, Uri? Image = null);

public static partial class LogMarkup
{
    private static readonly Dictionary<string, string> Colors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["a"] = "aqua", ["b"] = "blue", ["c"] = "#00e0e0", ["f"] = "fuchsia", ["g"] = "green",
        ["h"] = "gray", ["k"] = "black", ["l"] = "lime", ["m"] = "maroon", ["n"] = "navy",
        ["o"] = "olive", ["p"] = "purple", ["r"] = "red", ["s"] = "silver", ["t"] = "teal",
        ["w"] = "#d0d0ff", ["y"] = "#ffd200", ["+hp"] = "green", ["-hp"] = "red",
        ["+mp"] = "blue", ["-mp"] = "red", ["#777"] = "#777777",
        ["white"] = "#d0d0ff", ["cyan"] = "#00e0e0", ["yellow"] = "#ffd200"
    };
    private static readonly double[] Sizes = [14, 16, 20, 26, 34, 44, 56, 70, 88, 100];

    [GeneratedRegex(@"(?i)\b(color|cl|c|size|font|ft|style)=", RegexOptions.CultureInvariant)]
    private static partial Regex Attributes();
    [GeneratedRegex(@"https?://[-_.!~*'()a-zA-Z0-9;/?:@&=+$,%#]+", RegexOptions.CultureInvariant)]
    private static partial Regex Links();

    public static bool PublicHttpUrl(string text, out Uri? uri)
    {
        uri = null;
        if (!Uri.TryCreate(text, UriKind.Absolute, out var candidate) ||
            candidate.Scheme is not "http" and not "https" || candidate.UserInfo.Length > 0)
            return false;
        var host = candidate.DnsSafeHost.TrimEnd('.').ToLowerInvariant();
        if (host.Length == 0 || !host.Contains('.') && candidate.HostNameType == UriHostNameType.Dns ||
            host is "localhost" or "localhost.localdomain" ||
            host.EndsWith(".localhost", StringComparison.Ordinal) ||
            host.EndsWith(".local", StringComparison.Ordinal) || host.EndsWith(".lan", StringComparison.Ordinal))
            return false;
        if (IPAddress.TryParse(host.Trim('[', ']'), out var address))
        {
            if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
            var bytes = address.GetAddressBytes();
            if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) ||
                address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast ||
                bytes.Length == 16 && (bytes[0] & 0xfe) == 0xfc ||
                bytes.Length == 4 && (bytes[0] is 0 or 10 or >= 224 ||
                    bytes[0] == 169 && bytes[1] == 254 || bytes[0] == 172 && bytes[1] is >= 16 and <= 31 ||
                    bytes[0] == 192 && bytes[1] == 168 || bytes[0] == 100 && bytes[1] is >= 64 and < 128))
                return false;
        }
        else if (host.All(character => char.IsAsciiHexDigit(character) || character is '.' or 'x'))
            return false;
        uri = candidate;
        return true;
    }

    public static IReadOnlyList<LogPart> Parse(string raw, bool allowLargeShout = false)
    {
        var parts = new List<LogPart>();
        var stack = new Stack<LogStyle>();
        var style = new LogStyle();
        var loud = raw.StartsWith('(');
        var position = 0;
        void Text(string text)
        {
            text = text.Replace("はーと", "♥", StringComparison.Ordinal);
            var fragments = text.Split("もなりざ", StringSplitOptions.None);
            for (var i = 0; i < fragments.Length; i++)
            {
                if (i > 0) parts.Add(new("\u009f", style with { Font = "Webdings", Scale = 5 }));
                var fragment = fragments[i];
                var offset = 0;
                foreach (Match match in Links().Matches(fragment))
                {
                    if (match.Index > offset) parts.Add(new(fragment[offset..match.Index], style));
                    parts.Add(new(match.Value, style, PublicHttpUrl(match.Value, out var link) ? link : null));
                    offset = match.Index + match.Length;
                }
                if (offset < fragment.Length) parts.Add(new(fragment[offset..], style));
            }
        }
        while (position < raw.Length)
        {
            var open = raw.IndexOf("/*", position, StringComparison.Ordinal);
            if (open < 0) { Text(raw[position..]); break; }
            Text(raw[position..open]);
            var close = raw.IndexOf("*/", open + 2, StringComparison.Ordinal);
            if (close < 0) { Text(raw[open..]); break; }
            var tag = raw[(open + 2)..close];
            position = close + 2;
            if (tag == ".")
            {
                stack.Clear();
                style = new();
            }
            else if (tag.Equals("br", StringComparison.OrdinalIgnoreCase))
                parts.Add(new("\n", style));
            else if (tag.Length == 1 && char.IsAsciiLetter(tag[0]))
            {
                stack.Push(style);
                style = style with { Color = Colors.GetValueOrDefault(tag) ?? style.Color };
            }
            else if (tag.StartsWith("img=", StringComparison.OrdinalIgnoreCase))
            {
                var url = tag[4..].Split(' ', 2)[0];
                if (PublicHttpUrl(url, out var image))
                    parts.Add(new("[画像]", style, image, image));
            }
            else
            {
                var fields = Attributes().Matches(tag);
                if (fields.Count == 0 || fields[0].Index != 0) continue;
                if (tag[(fields[0].Index + fields[0].Length)..] == ".")
                {
                    if (stack.TryPop(out var previous)) style = previous;
                    continue;
                }
                stack.Push(style);
                for (var i = 0; i < fields.Count; i++)
                {
                    var field = fields[i];
                    var end = i + 1 < fields.Count ? fields[i + 1].Index : tag.Length;
                    var value = tag[(field.Index + field.Length)..end].Trim();
                    switch (field.Groups[1].Value.ToLowerInvariant())
                    {
                        case "c" or "cl" or "color":
                            if (value.All(character => char.IsAsciiLetterOrDigit(character) || character is '#' or '+' or '-'))
                                style = style with { Color = Colors.GetValueOrDefault(value) ?? value };
                            break;
                        case "size":
                            var size = value.ToLowerInvariant() switch
                            {
                                "large" => 32.0, "small" => 10.0,
                                _ => int.TryParse(value, System.Globalization.NumberStyles.AllowLeadingSign,
                                    System.Globalization.CultureInfo.InvariantCulture, out var number) &&
                                     number is >= -9 and <= 9 ? number >= 0 ? Sizes[number] : 14 + number : (double?)null
                            };
                            if (size is { } fontSize && (!loud || allowLargeShout || fontSize < 14))
                                style = style with { Size = fontSize };
                            break;
                        case "font" or "ft":
                            if (value.Length is > 0 and <= 128 && !value.Any(character => ";:(){}\\/".Contains(character)))
                                style = style with { Font = value.Trim('"', '\'') };
                            break;
                        case "style":
                            style = value.ToLowerInvariant() switch
                            {
                                "bold" => style with { Bold = true }, "italic" => style with { Italic = true },
                                "under" => style with { Underline = true }, "strike" => style with { Strike = true },
                                "over" => style with { Overline = true }, "sup" => style with { Baseline = 1 },
                                "sub" => style with { Baseline = -1 }, _ => style
                            };
                            break;
                    }
                }
            }
        }
        return parts;
    }
}
