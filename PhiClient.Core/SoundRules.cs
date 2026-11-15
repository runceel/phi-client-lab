using System.Text.RegularExpressions;

namespace PhiClient.Core;

public sealed record SoundMatch(int Line, string? WavePath);
public sealed record SoundRuleIssue(string File, int Line);

public sealed class SoundRules
{
    private sealed record Rule(Regex Pattern, SoundMatch Action);
    private readonly IReadOnlyList<Rule> rules;
    private readonly IReadOnlyList<Regex> exclusions;
    public IReadOnlyList<SoundRuleIssue> Issues { get; }

    private SoundRules(IReadOnlyList<Rule> rules, IReadOnlyList<Regex> exclusions,
        IReadOnlyList<SoundRuleIssue> issues)
    {
        this.rules = rules;
        this.exclusions = exclusions;
        Issues = issues;
    }

    public static SoundRules Parse(string expressions, string files, string? notFlash = null)
    {
        if (expressions.Length > 1024 * 1024 || files.Length > 1024 * 1024 || notFlash?.Length > 1024 * 1024)
            throw new InvalidDataException("効果音の設定ファイルを1Mi文字以内に収めてから読み込んでください。");
        var rules = new List<Rule>();
        var issues = new List<SoundRuleIssue>();
        var patterns = Lines(expressions);
        var paths = Lines(files);
        for (var index = 1; index < patterns.Length; index++)
        {
            if (string.IsNullOrWhiteSpace(patterns[index])) continue;
            var pattern = Compile(patterns[index]);
            if (pattern is null)
            {
                issues.Add(new("SoundRegexp.ini", index + 1));
                continue;
            }
            var path = index < paths.Length ? paths[index].Trim() : "";
            rules.Add(new(pattern, new(index + 1, path.Length == 0 ? null : path)));
        }
        var exclusions = new List<Regex>();
        var excluded = notFlash is null ? ["説明", @"/^(?:\/\*.*?\*\/)?DM >/", "/現在の接続者数 >>/"] :
            Lines(notFlash);
        for (var index = 1; index < excluded.Length; index++)
        {
            if (string.IsNullOrWhiteSpace(excluded[index])) continue;
            var pattern = Compile(excluded[index]);
            if (pattern is not null) exclusions.Add(pattern);
            else issues.Add(new("NotFlashRegexp.ini", index + 1));
        }
        return new(rules, exclusions, issues);
    }

    public SoundMatch? Find(string text)
    {
        foreach (var rule in rules)
            if (Matches(rule.Pattern, text, "SoundRegexp.ini", rule.Action.Line)) return rule.Action;
        return null;
    }

    public bool SuppressTalkFlash(string text)
    {
        foreach (var pattern in exclusions)
            if (Matches(pattern, text, "NotFlashRegexp.ini", null)) return true;
        return false;
    }

    private static string[] Lines(string text) => text.Split('\n').Select(line => line.TrimEnd('\r')).ToArray();

    private static Regex? Compile(string text)
    {
        text = text.Trim();
        var end = text.LastIndexOf('/');
        if (!text.StartsWith('/') || end < 1 || text.Length > 4096) return null;
        var options = RegexOptions.CultureInvariant;
        foreach (var flag in text[(end + 1)..])
            options |= flag switch
            {
                'i' => RegexOptions.IgnoreCase, 's' => RegexOptions.Singleline,
                'm' => RegexOptions.Multiline, 'x' => RegexOptions.IgnorePatternWhitespace,
                _ => RegexOptions.None
            };
        try { return new(text[1..end], options, TimeSpan.FromMilliseconds(50)); }
        catch (ArgumentException) { return null; }
    }

    private static bool Matches(Regex pattern, string text, string file, int? line)
    {
        try { return pattern.IsMatch(text); }
        catch (RegexMatchTimeoutException error)
        {
            throw new InvalidDataException(
                $"{file}{(line is { } number ? $" の{number}行目" : "")}の照合時間が上限を超えました。規則を確認してください。",
                error);
        }
    }
}
