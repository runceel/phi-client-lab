using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace PhiClient.Core;

public sealed record PrivConversation(ImmutableArray<string> To, ImmutableArray<LogEntry> Entries,
    bool Unread = false, bool Confirm = false)
{
    public string Key => Priv.Key(To);
}

public enum PrivSendStage { Idle, Preparing, Confirming, Transmitting }

public sealed record PrivState
{
    public ImmutableArray<PrivConversation> Conversations { get; init; } = [];
    public ImmutableHashSet<string> Picked { get; init; } = ImmutableHashSet<string>.Empty.WithComparer(StringComparer.Ordinal);
    public bool Visible { get; init; }
    public string? Active { get; init; }
    public string Draft { get; init; } = "";
    public PrivSendStage SendStage { get; init; }
    public bool Sending => SendStage != PrivSendStage.Idle;
    public PrivConversation? Current => Conversations.FirstOrDefault(conversation => conversation.Key == Active);

    public PrivState Select(string key) => Conversations.Any(conversation => conversation.Key == key)
        ? this with { Active = key, Conversations = Conversations.Select(conversation =>
            conversation.Key == key ? conversation with { Unread = false } : conversation).ToImmutableArray() } : this;

    public PrivState Open(IEnumerable<string> names)
    {
        var to = names.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray();
        if (to.IsEmpty) return this;
        var key = Priv.Key(to);
        var conversations = Conversations.Any(conversation => conversation.Key == key) ? Conversations :
            Conversations.Add(new(to, []));
        return (this with { Conversations = Cap(conversations, key), Visible = true, Picked = [] }).Select(key);
    }

    public PrivState Received(LogEntry entry, string self)
    {
        if (entry.Channel != LogChannel.Priv || entry.From is null || entry.Literal) return this;
        var to = Priv.Partners(entry.From, entry.Raw ?? entry.Text, self);
        if (to.IsEmpty) return this;
        var key = Priv.Key(to);
        var existing = Conversations.FirstOrDefault(conversation => conversation.Key == key);
        var rows = (existing?.Entries ?? []).Add(entry);
        var size = rows.Sum(row => (long)(row.Raw?.Length ?? row.Text.Length));
        var remove = 0;
        while (rows.Length - remove > 2000 || size > 2 * 1024 * 1024)
        {
            size -= rows[remove].Raw?.Length ?? rows[remove].Text.Length;
            remove++;
        }
        var updated = new PrivConversation(to, rows.RemoveRange(0, remove), Active != key,
            existing?.Confirm ?? to.Length > 1);
        var conversations = existing is null ? Conversations.Add(updated) :
            Conversations.Select(conversation => conversation.Key == key ? updated : conversation).ToImmutableArray();
        return this with { Conversations = Cap(conversations, Active ?? key), Visible = true, Active = Active ?? key };
    }

    public PrivState Confirmed(string key) => this with
    {
        Conversations = Conversations.Select(conversation =>
            conversation.Key == key ? conversation with { Confirm = false } : conversation).ToImmutableArray()
    };

    private static ImmutableArray<PrivConversation> Cap(ImmutableArray<PrivConversation> conversations, string? active)
    {
        while (conversations.Length > 50)
        {
            var index = 0;
            while (index < conversations.Length && conversations[index].Key == active) index++;
            conversations = conversations.RemoveAt(index);
        }
        return conversations;
    }
}

public sealed record PrivSend(IReadOnlyList<string> Lines, IReadOnlyList<string> Failed);

public static partial class Priv
{
    [GeneratedRegex(@"/\*to=(.*?)\*/", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex ToTags();

    public static string Key(IEnumerable<string> names) =>
        string.Join('\n', names.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));

    public static ImmutableArray<string> Partners(string from, string raw, string self) =>
        ToTags().Matches(raw).Select(match => match.Groups[1].Value).Append(from)
            .Where(name => name != self).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray();

    public static PrivSend Send(IReadOnlyList<string> to, string text, IReadOnlyList<User> users, string self)
    {
        PhiWire.ValidateLine(text);
        if (to.Count > 1000) throw new ArgumentException("privの宛先が多すぎます。1000人以下にしてください。", nameof(to));
        var resolved = new List<User>();
        var failed = new List<string>();
        foreach (var name in to.Distinct(StringComparer.Ordinal))
        {
            if (name.Length == 0 || name.Any(char.IsControl) ||
                name.Contains("/*", StringComparison.Ordinal) || name.Contains("*/", StringComparison.Ordinal))
                throw new ArgumentException("宛先名に制御文字やタグの区切りが含まれるため、privを送信できません。", nameof(to));
            var user = users.FirstOrDefault(user => user.Name == name);
            if (user is null) failed.Add(name);
            else resolved.Add(user);
        }
        var body = string.Concat(resolved.Select(user => $"/*to={user.Name}*/")) + text;
        var own = users.FirstOrDefault(user => user.Name == self);
        var length = resolved.Sum(user => (long)body.Length + 6 +
            user.Number.ToString(System.Globalization.CultureInfo.InvariantCulture).Length);
        if (resolved.Count > 0 && own is not null)
            length += body.Length + 6 + own.Number.ToString(System.Globalization.CultureInfo.InvariantCulture).Length;
        if (length > 8 * 1024 * 1024)
            throw new ArgumentException("privの送信量が大きすぎます。宛先か本文を減らしてください。", nameof(text));
        var lines = resolved.Select(user => $"priv {user.Number} {body}").ToList();
        if (lines.Count > 0 && own is not null)
            lines.Add($"priv {own.Number} {body}");
        foreach (var line in lines) PhiWire.ValidateLine(line);
        return new(lines, failed);
    }
}
