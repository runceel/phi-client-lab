using System.Globalization;
using System.Net;
using System.Text;

namespace PhiClient.Core;

public static class LogFormat
{
    public static IReadOnlyList<LogPart> Parts(LogEntry row, ClientOptions? options = null) =>
        row.Raw is { } raw && !row.Literal ? LogMarkup.Parse(raw, options ?? new()) : new LogPart[] { new(row.Text, new()) };

    public static string Text(LogEntry row, bool time = true, ClientOptions? options = null) =>
        Time(row, time) + string.Concat(Parts(row, options).Select(part => part.Text)).Replace('\n', ' ').Replace('\r', ' ');

    public static string Html(LogEntry row, bool time = true, ClientOptions? options = null)
    {
        var output = new StringBuilder("<div>");
        if (Time(row, time) is { Length: > 0 } stamp)
            output.Append("<span class=\"time\">").Append(stamp).Append("</span>");
        foreach (var part in Parts(row, options))
        {
            var css = new StringBuilder();
            var style = part.Style;
            if (style.Color is { } color) css.Append("color:").Append(color).Append(';');
            if (style.Size is { } size)
                css.Append("font-size:").Append((size * style.Scale).ToString(CultureInfo.InvariantCulture)).Append("px;");
            else if (style.Scale != 1)
                css.Append("font-size:").Append((style.Scale * 100).ToString(CultureInfo.InvariantCulture)).Append("%;");
            if (style.Font is { } font) css.Append("font-family:'").Append(CssString(font)).Append("';");
            if (style.Bold) css.Append("font-weight:bold;");
            if (style.Italic) css.Append("font-style:italic;");
            if (style.Underline || style.Strike || style.Overline)
                css.Append("text-decoration:").Append(style.Underline ? "underline " : "")
                    .Append(style.Strike ? "line-through " : "").Append(style.Overline ? "overline " : "").Append(';');
            if (style.Baseline != 0) css.Append(style.Baseline > 0 ? "vertical-align:super;" : "vertical-align:sub;");
            if (part.Link is { } link)
                output.Append("<a href=\"").Append(WebUtility.HtmlEncode(link.AbsoluteUri)).Append("\">");
            output.Append("<span style=\"").Append(WebUtility.HtmlEncode(css.ToString())).Append("\">")
                .Append(WebUtility.HtmlEncode(part.Text).Replace("\r\n", "\n", StringComparison.Ordinal)
                    .Replace("\r", "\n", StringComparison.Ordinal).Replace("\n", "<br>", StringComparison.Ordinal))
                .Append("</span>");
            if (part.Link is not null) output.Append("</a>");
        }
        return output.Append("</div>\n").ToString();
    }

    public static string Header(string label, ClientOptions options) =>
        "<!doctype html>\n<meta charset=\"utf-8\">\n" +
        "<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'; img-src 'none'; script-src 'none'\">\n" +
        $"<title>{WebUtility.HtmlEncode(label)}</title>\n<style>" +
        $"body{{background:white;color:black;font-family:'{CssString(options.LogFontName.Length == 0 ? "ＭＳ ゴシック" : options.LogFontName)}',monospace;" +
        $"font-size:{(options.LogFontSize == 0 ? 14 : options.LogFontSize)}px;white-space:pre-wrap}}.time{{color:teal;font:90% sans-serif}}</style>\n";

    private static string Time(LogEntry row, bool include) =>
        include && row.Time is { } time ? time.ToString("HH:mm ", CultureInfo.InvariantCulture) : "";

    private static string CssString(string value)
    {
        var output = new StringBuilder();
        foreach (var rune in value.EnumerateRunes())
            if (rune.Value is '\'' or '"' or '\\' or '<' or '>' || Rune.IsControl(rune))
                output.Append('\\').Append(rune.Value.ToString("x", CultureInfo.InvariantCulture)).Append(' ');
            else output.Append(rune);
        return output.ToString();
    }
}
