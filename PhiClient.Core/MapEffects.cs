using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace PhiClient.Core;

public enum MapEffectKind { Talk, HpLost, HpGained, MpLost, MpGained, Miss, Poison, Palsy, Dead, Buff, Debuff }

public readonly record struct MapEffectBounds(double X, double Y, double Width, double Height)
{
    public bool Overlaps(MapEffectBounds other, double gap = 2) =>
        X < other.X + other.Width + gap && X + Width + gap > other.X &&
        Y < other.Y + other.Height + gap && Y + Height + gap > other.Y;
}

public static class MapEffectLayout
{
    public static MapEffectBounds? Place(double x, double y, double width, double height,
        double mapWidth, double mapHeight, IReadOnlyList<MapEffectBounds> occupied)
    {
        if (width > mapWidth || height > mapHeight) return null;
        var maxX = mapWidth - width;
        var maxY = mapHeight - height;
        x = Math.Clamp(x, 0, maxX);
        y = Math.Clamp(y, 0, maxY);
        var preferred = new MapEffectBounds(x, y, width, height);
        if (!occupied.Any(item => preferred.Overlaps(item))) return preferred;
        var rows = new[] { y, 0, maxY }.Concat(occupied.SelectMany(item =>
            new[] { item.Y - height - 2, item.Y + item.Height + 2 }));
        MapEffectBounds? best = null;
        var distance = double.PositiveInfinity;
        foreach (var top in rows.Where(top => top >= 0 && top <= maxY).Distinct())
        foreach (var left in new[] { x, 0, maxX }.Distinct())
        {
            var nextDistance = (left - x) * (left - x) + (top - y) * (top - y);
            if (nextDistance >= distance) continue;
            var candidate = new MapEffectBounds(left, top, width, height);
            if (occupied.Any(item => candidate.Overlaps(item))) continue;
            best = candidate;
            distance = nextDistance;
        }
        return best;
    }
}

public sealed record MapEffect(long Id, string Name, MapEffectKind Kind, string Text, long Started,
    string? By = null, bool Magic = false, (int X, int Y)? Anchor = null)
{
    public int Duration => Kind == MapEffectKind.Talk ? 4000 : 1200;
    public bool Alive(long now) => now - Started < Duration;
}

public sealed record MapEffectState
{
    public ImmutableArray<MapEffect> Items { get; init; } = [];
    public ImmutableDictionary<string, string> Spoken { get; init; } = ImmutableDictionary<string, string>.Empty;
    public string? CastTarget { get; init; }
    public long NextId { get; init; }

    public MapEffectState Receive(LogLine line, long now, MapFrame? map)
    {
        var live = Items.Where(item => item.Alive(now)).ToImmutableArray();
        if (line.Literal) return this with { Items = live };
        var text = MapLabels.Clean(line.Raw ?? line.Text);
        var split = text.IndexOf(" > ", StringComparison.Ordinal);
        if (split <= 0) return this with { Items = live };
        var name = text[..split].Trim();
        var body = text[(split + 3)..].Trim();
        var state = this with { Items = live };
        MapEffect? effect;
        if (name == "DM")
        {
            effect = MapEffectMessages.Parse(body, state, now);
            state = state with { CastTarget = MapEffectMessages.CastTarget(body) };
        }
        else
        {
            if (name.StartsWith('[') && name.EndsWith(']')) name = name[1..^1];
            if (name.Length == 0 || body.Length == 0) return state;
            var spoken = state.Spoken;
            if (spoken.Count >= 128 && !spoken.ContainsKey(name)) spoken = spoken.Clear();
            state = state with { Spoken = spoken.SetItem(name, body),
                Items = state.Items.Where(item => item.Kind != MapEffectKind.Talk || item.Name != name).ToImmutableArray() };
            effect = new(NextId, name, MapEffectKind.Talk, body, now);
        }
        if (effect is null) return state;
        if (effect.Kind == MapEffectKind.Dead)
        {
            var actor = FindActor(map?.Actors ?? [], effect.Name, (map?.Size ?? 7) / 2);
            if (actor is null) return state;
            var center = (map?.Size ?? 7) / 2;
            effect = effect with { Anchor = (actor.X - center, actor.Y - center) };
        }
        return state with { Items = state.Items.TakeLast(127).Append(effect).ToImmutableArray(), NextId = NextId + 1 };
    }

    public static Actor? FindActor(IReadOnlyList<Actor> actors, string name, int center) =>
        string.IsNullOrEmpty(name) ? actors.FirstOrDefault(actor => actor.X == center && actor.Y == center) :
        actors.Where(actor => MapLabels.Clean(actor.Name) == name)
            .MinBy(actor => Math.Abs(actor.X - center) + Math.Abs(actor.Y - center));
}

internal static class MapEffectMessages
{
    private static Regex Pattern(string value) => new(value, RegexOptions.CultureInvariant | RegexOptions.NonBacktracking,
        TimeSpan.FromMilliseconds(100));
    private static readonly Regex cast = Pattern(@"^.+? は (.+?) に .+ をかけた\.");
    private static readonly Regex amount = Pattern(@"\[(\d+) ([hm])p\]");
    private static readonly Regex gain = Pattern("回復した|吸収した|absorbed|cured");
    private static readonly Regex absorb = Pattern(@"^(.+?) は (.+?) の (.+?) (?:による攻撃を|を)吸収した");
    private static readonly Regex absorbEnglish = Pattern(@"^(.+?) absorbed from (.+?)'s ");
    private static readonly Regex attack = Pattern(@"^(.+?) は (.+?) を (.+?) で攻撃した");
    private static readonly Regex attackEnglish = Pattern(@"^(.+?) attacked(?: to)? (.+?) by (.+?)\. \[");
    private static readonly Regex cure = Pattern(@"^.+? は (.+?) を |^.+? cured (.+?) by ");
    private static readonly Regex target = Pattern(@"^(.+?) (?:は|が|is )");
    private static readonly Regex debuff = Pattern(@"^(.+?) の(攻撃力|魔力|防御率|魔法抵抗率|速度|魔法回避率)が \d+(?: point|%) .*によって下がった");
    private static readonly (Regex Pattern, MapEffectKind Kind, string Text)[] notices =
    [
        (Pattern(@"^(.+?) ?はミスした\.|^(.+?) miss\."), MapEffectKind.Miss, "MISS"),
        (Pattern(@"^(.+?) は .+?(?:に殺された|で死んだ)\.|^(.+?) is killed by "), MapEffectKind.Dead, ""),
        (Pattern(@"^(.+?) は \d+ lv の毒を|^(.+?) is \d+ lv poisoned"), MapEffectKind.Poison, "毒"),
        (Pattern(@"^(.+?) は \d+ 秒間 .*麻痺|^(.+?) is paralyzed in "), MapEffectKind.Palsy, "麻痺")
    ];
    private static readonly Regex buff = Pattern(@"^(攻撃力|魔力|防御率|魔法抵抗率|攻撃速度|魔法回避率)が \d+(?: point|%) (?:\d+ 秒間)?上がった");
    private static readonly Regex buffEnglish = Pattern(@"^(Attack point|Spell point|Defence rate|Magic resistance rate|Attack speed|Magic rejection rate) is \d+%? up");
    private static readonly Regex field = Pattern(@"^(.+?) が \d+ 秒間張られる|^(.+?) is appeared in \d+ sec");
    private static string Group(Match match) => match.Groups.Cast<Group>().Skip(1).First(group => group.Success).Value;

    public static string? CastTarget(string body) => cast.Match(body) is { Success: true } match ? Group(match) : null;

    public static MapEffect? Parse(string body, MapEffectState state, long now)
    {
        MapEffect Effect(string name, MapEffectKind kind, string text, string? by = null, string? weapon = null) =>
            new(state.NextId, name, kind, text, now, by,
                by is not null && weapon is not null && state.Spoken.GetValueOrDefault(by) == weapon);
        foreach (var (pattern, kind, label) in notices)
            if (pattern.Match(body) is { Success: true } notice) return Effect(Group(notice), kind, label);
        if (debuff.Match(body) is { Success: true } weaker)
            return Effect(weaker.Groups[1].Value, MapEffectKind.Debuff, weaker.Groups[2].Value + "↓");
        if (buff.Match(body) is { Success: true } stronger)
            return Effect(state.CastTarget ?? "", MapEffectKind.Buff, Group(stronger) + "↑");
        if (buffEnglish.Match(body) is { Success: true } strongerEnglish)
        {
            var label = Group(strongerEnglish) switch
            {
                "Attack point" => "攻撃力", "Spell point" => "魔力", "Defence rate" => "防御率",
                "Magic resistance rate" => "魔法抵抗率", "Attack speed" => "攻撃速度", _ => "魔法回避率"
            };
            return Effect(state.CastTarget ?? "", MapEffectKind.Buff, label + "↑");
        }
        if (field.Match(body) is { Success: true } shield)
            return Effect(state.CastTarget ?? "", MapEffectKind.Buff, Group(shield));
        var value = amount.Match(body);
        if (!value.Success) return null;
        string? who = null, by = null, weapon = null;
        if (absorb.Match(body) is { Success: true } absorbed)
            (who, by, weapon) = (absorbed.Groups[1].Value, absorbed.Groups[2].Value, absorbed.Groups[3].Value);
        else if (absorbEnglish.Match(body) is { Success: true } absorbedEnglish)
            (who, by) = (absorbedEnglish.Groups[1].Value, absorbedEnglish.Groups[2].Value);
        else if ((attack.Match(body) is { Success: true } hit ? hit : attackEnglish.Match(body)) is { Success: true } attacked)
            (who, by, weapon) = (attacked.Groups[2].Value, attacked.Groups[1].Value, attacked.Groups[3].Value);
        else if (cure.Match(body) is { Success: true } cured) who = Group(cured);
        else if (target.Match(body) is { Success: true } affected) who = Group(affected);
        if (who is null) return null;
        var gained = gain.IsMatch(body);
        var kindValue = (value.Groups[2].Value == "h", gained) switch
        {
            (true, false) => MapEffectKind.HpLost, (true, true) => MapEffectKind.HpGained,
            (false, false) => MapEffectKind.MpLost, _ => MapEffectKind.MpGained
        };
        return Effect(who, kindValue, (gained ? "+" : "") + value.Groups[1].Value, by, weapon);
    }
}
