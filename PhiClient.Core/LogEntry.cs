namespace PhiClient.Core;

public enum LogChannel { Log, Talk, Priv, System }
public sealed record LogEntry(long Id, string Text, string? Raw, DateTimeOffset? Time,
    LogChannel Channel = LogChannel.Log, string? From = null, bool Literal = false)
{
    public string Copy(ClientOptions options, bool toInput = false)
    {
        var text = Raw ?? Text;
        if (options.LogNameDelete)
        {
            var end = text.IndexOf(" > ", StringComparison.Ordinal);
            if (end > 0 && !text.AsSpan(0, end).ContainsAny('\n', '>')) text = text[(end + 3)..];
        }
        if (options.LogTagDeleteCopy || toInput && options.LogTagDeleteAlways)
            text = ProtocolParser.PlainText(text);
        if (options.LogTagCancel) text = text.Replace("/*", "/＊", StringComparison.Ordinal);
        return (options.LogTimeDelete || !options.LogDispTime || Time is null ? "" : Time.Value.ToString("HH:mm ",
            System.Globalization.CultureInfo.InvariantCulture)) + text;
    }
}
