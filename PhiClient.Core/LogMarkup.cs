using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace PhiClient.Core;

public sealed record LogStyle(string? Color = null, double? Size = null, string? Font = null,
    bool Bold = false, bool Italic = false, bool Underline = false, bool Strike = false,
    bool Overline = false, int Baseline = 0, double Scale = 1);
public sealed record LogPart(string Text, LogStyle Style, Uri? Link = null, Uri? Image = null,
    int? Width = null, int? Height = null, string? Align = null);

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
    [GeneratedRegex(@"/\*img_wnd=.*?\*/", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex ImageWindow();
    [GeneratedRegex(@"/\*img=.+?\*/", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex ImageTag();
    [GeneratedRegex(@"(?:color|cl|c)=([^ ]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex MetricColor();
    [GeneratedRegex(@"(?:font|ft)=([^=]+?)(?: +[^ ]+=| *$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex MetricFont();

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

    public static IReadOnlyList<LogPart> Parse(string raw, bool allowLargeShout = false) =>
        Parse(raw, new ClientOptions { LogLargeDispLoud = allowLargeShout });

    public static IReadOnlyList<LogPart> Parse(string raw, ClientOptions options)
    {
        var parts = new List<LogPart>();
        var stack = new Stack<LogStyle>();
        var style = new LogStyle();
        var loud = raw.StartsWith('(');
        var imageDisplay = options.LogImageDisp && (!loud || options.LogImageDispLoud);
        var imageWindow = ImageWindow().IsMatch(raw);
        var explicitImage = imageDisplay && !imageWindow && ImageTag().IsMatch(raw);
        var urlImages = imageDisplay && !imageWindow && !explicitImage && options.LogImageDispURL &&
            PlainSegments(raw).Any(segment => Links().Matches(segment).Any(match => ImageUrl(match.Value, out _)));
        var suppressLinks = imageWindow || explicitImage || urlImages;
        Uri? lastImage = null;
        var position = 0;
        var body = false;
        var marker = 0;
        var column = 0;
        // Philly expands tabs after HTML conversion, so even generated tags/entities count as columns.
        void Metric(string html)
        {
            foreach (var rune in html.EnumerateRunes())
            {
                if (!body)
                {
                    const string separator = "&gt; ";
                    marker = rune.Value == separator[marker] ? marker + 1 : rune.Value == '&' ? 1 : 0;
                    if (marker == separator.Length) body = true;
                }
                else column += rune.Value == '\t' ? 8 - column % 8 : rune.IsAscii ? 1 : 2;
            }
        }
        void Visible(string text, LogStyle current, Uri? link = null)
        {
            var value = new StringBuilder();
            void Flush()
            {
                if (value.Length == 0) return;
                parts.Add(new(value.ToString().Replace("  ", "　", StringComparison.Ordinal), current, link));
                value.Clear();
            }
            for (var index = 0; index < text.Length;)
            {
                if (text.AsSpan(index).StartsWith("はーと", StringComparison.Ordinal))
                {
                    value.Append('♥'); Metric("&hearts;"); index += 3;
                }
                else if (text.AsSpan(index).StartsWith("もなりざ", StringComparison.Ordinal))
                {
                    Flush();
                    parts.Add(new("\u009f", current with { Font = "Webdings", Scale = 5 }));
                    Metric("<font style=\"font-family:webdings;font-size:500%;line-height:100%\">&#0159;</font>");
                    index += 4;
                }
                else if (text[index] == '\t' && body)
                {
                    var spaces = 8 - column % 8;
                    value.Append(' ', spaces); Metric(new string(' ', spaces)); index++;
                }
                else if (text[index] == '\n' || text[index] == '\r' && index + 1 < text.Length && text[index + 1] == '\n')
                {
                    if (text[index] == '\r') index++;
                    value.Append('\n'); Metric("<br />"); index++;
                }
                else
                {
                    var rune = Rune.GetRuneAt(text, index);
                    value.Append(rune.ToString()); Metric(Escape(rune.ToString()));
                    index += rune.Utf16SequenceLength;
                }
            }
            Flush();
        }
        void Text(string text)
        {
            var offset = 0;
            foreach (Match match in Links().Matches(text))
            {
                Visible(text[offset..match.Index], style);
                var valid = PublicHttpUrl(match.Value, out var link);
                var escaped = Escape(match.Value);
                if (urlImages && ImageUrl(match.Value, out var image))
                {
                    Metric((options.LogImageLinkDisp ? "<div>" : "") + $"<img src=\"{escaped}\" align=\"top\" />");
                    parts.Add(new("[画像]", style, Image: image));
                    lastImage = image;
                }
                else
                {
                    var actionable = valid && options.LogUrlLink && !suppressLinks ? link : null;
                    if (actionable is not null) Metric($"<a href=\"{escaped}\">");
                    Visible(match.Value, style, actionable);
                    if (actionable is not null) Metric("</a>");
                }
                offset = match.Index + match.Length;
            }
            Visible(text[offset..], style);
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
                Metric("</font>" + string.Concat(Enumerable.Repeat("</font><!--font_none-->", Math.Max(0, stack.Count - 1))));
                stack.Clear();
                style = new();
            }
            else if (tag.Equals("br", StringComparison.OrdinalIgnoreCase))
            {
                Metric("<br class=\"phi_tag\">");
                parts.Add(new("\n", style));
            }
            else if (tag.Length == 1 && char.IsAsciiLetter(tag[0]))
            {
                Metric($"<font class=\"color_{tag}_\">");
                stack.Push(style);
                style = style with { Color = Colors.GetValueOrDefault(tag) ?? style.Color };
            }
            else if (tag.StartsWith("img_wnd=", StringComparison.OrdinalIgnoreCase))
                Metric("<!--" + Escape(tag) + "-->");
            else if (tag.StartsWith("img=", StringComparison.OrdinalIgnoreCase))
            {
                var url = tag[4..].Split(' ', 2)[0];
                if (explicitImage && PublicHttpUrl(url, out var image))
                {
                    var attributes = tag.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                        .Select(field => field.Split('=', 2)).Where(field => field.Length == 2).ToArray();
                    string? Value(string key) => attributes.FirstOrDefault(field =>
                        field[0].Equals(key, StringComparison.OrdinalIgnoreCase))?[1];
                    int? Dimension(string key) => Value(key) is { } value
                        ? int.TryParse(value, System.Globalization.NumberStyles.Integer,
                            System.Globalization.CultureInfo.InvariantCulture, out var number) ? Math.Clamp(number, 0, 1000) : 0
                        : null;
                    parts.Add(new("[画像]", style, Image: image, Width: Dimension("width"),
                        Height: Dimension("height"), Align: Value("align")));
                    var html = new StringBuilder($"<img src=\"{Escape(url)}\"");
                    if (Value("align") is { Length: > 0 } align) html.Append($" align=\"{Escape(align)}\"");
                    foreach (var key in new[] { "width", "height" })
                        if (Dimension(key) is { } dimension) html.Append($" {key}=\"{dimension}\"");
                    Metric(html.Append(" />").ToString());
                }
                else if (!explicitImage)
                {
                    if (options.NotInterpretedTagDisp) Text("/*" + tag + "*/");
                    else Metric("<!--" + Escape(tag) + "-->");
                }
            }
            else
            {
                var fields = Attributes().Matches(tag);
                if (fields.Count == 0 || fields[0].Index != 0 || fields[0].Length == tag.Length)
                {
                    if (options.NotInterpretedTagDisp && tag.Length > 0 && !tag.StartsWith("to=", StringComparison.Ordinal))
                        Text("/*" + tag + "*/");
                    else Metric("<!--" + Escape(tag) + "-->");
                    continue;
                }
                if (tag[(fields[0].Index + fields[0].Length)..] == ".")
                {
                    Metric("</font><!--" + fields[0].Groups[1].Value + "-->");
                    if (stack.TryPop(out var previous)) style = previous;
                    continue;
                }
                stack.Push(style);
                Metric(FontMetric(tag, loud && !options.LogLargeDispLoud));
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
                            if (size is { } fontSize && (!loud || options.LogLargeDispLoud || fontSize < 14))
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
        if (lastImage is not null && options.LogImageLinkDisp)
            parts.Add(new("\n" + lastImage.AbsoluteUri, style, lastImage));
        return parts;
    }

    private static bool ImageUrl(string text, out Uri? uri)
    {
        var valid = PublicHttpUrl(text, out uri);
        var value = uri;
        return valid && new[] { ".png", ".jpg", ".jpeg", ".gif", ".bmp" }
            .Any(extension => value!.AbsolutePath.EndsWith(extension, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> PlainSegments(string raw)
    {
        var position = 0;
        while (position < raw.Length)
        {
            var open = raw.IndexOf("/*", position, StringComparison.Ordinal);
            if (open < 0) { yield return raw[position..]; yield break; }
            yield return raw[position..open];
            var close = raw.IndexOf("*/", open + 2, StringComparison.Ordinal);
            if (close < 0) { yield return raw[open..]; yield break; }
            position = close + 2;
        }
    }

    private static string Escape(string value) => value.Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal).Replace("'", "&#39;", StringComparison.Ordinal);

    private static string FontMetric(string tag, bool loud)
    {
        tag = Escape(tag);
        var classes = Attributes().Replace(tag, "${1}_");
        classes = Regex.Replace(classes, @"size_\+", "size__", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        classes = Regex.Replace(classes, @"(color|cl|c)_\+(hp|mp)", "${1}__$2", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        classes = Regex.Replace(classes, @"(color|cl|c)_#777", "${1}_777", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (loud) classes = Regex.Replace(classes, @"size_(_\d|\d|large)", "size_${1}_",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        var css = new StringBuilder();
        var color = MetricColor().Match(tag);
        if (color.Success)
        {
            var value = color.Groups[1].Value;
            if (!new[] { "+hp", "-hp", "+mp", "-mp", "#777", "white", "cyan", "yellow" }
                .Any(item => value.Contains(item, StringComparison.OrdinalIgnoreCase)) &&
                value.All(character => char.IsAsciiLetterOrDigit(character) || character == '#'))
                css.Append($"color:{value};");
        }
        var font = MetricFont().Match(tag);
        if (font.Success && !font.Groups[1].Value.Any(character => ";:(){}\\/".Contains(character)))
            css.Append($"font-family:{font.Groups[1].Value};");
        return $"<font class=\"{classes}\"" + (css.Length > 0 ? $" style=\"{css}\"" : "") + ">";
    }
}
