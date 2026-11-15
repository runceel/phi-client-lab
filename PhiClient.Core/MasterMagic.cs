using System.Collections.Immutable;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace PhiClient.Core;

public enum MasterStepKind { Send, Confirm, Integer, String }
public sealed record MasterStep(MasterStepKind Kind, string Text, bool HideList = false);
public sealed record MasterItem(string Caption, ImmutableArray<MasterStep> Steps)
{
    public bool Separator => Caption == "-";
}
public sealed record MasterGroup(string Caption, ImmutableArray<MasterItem> Items);
public sealed record MasterMenu(string Caption, ImmutableArray<MasterGroup> Groups);
public sealed record MasterRun(ImmutableArray<MasterStep> Steps, int Index = 0,
    string Integer = "", string Text = "", MasterStep? Prompt = null);
public sealed record MasterAdvance(MasterRun Next, ImmutableArray<string> Lines, int HiddenLists);

public static partial class MasterMagic
{
    public static bool Visible(PlayerStatus? stats, ClientOptions options) =>
        options.MasterMagicMenu || stats?.Experience == 99999999;

    [GeneratedRegex(@"\(&.\)", RegexOptions.CultureInvariant)]
    private static partial Regex AccessKey();
    [GeneratedRegex(@"^-?[0-9]+$", RegexOptions.CultureInvariant | RegexOptions.NonBacktracking)]
    private static partial Regex Integer();

    public static MasterMenu Parse(string xml)
    {
        using var reader = XmlReader.Create(new StringReader(xml), new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1024 * 1024
        });
        var document = XDocument.Load(reader);
        var root = document.Descendants("mastermagic").FirstOrDefault() ??
            throw new XmlException("mastermagic要素がありません。");
        var caption = AccessKey().Replace((string?)root.Attribute("caption") ?? "超魔法", "", 1);
        var amp = caption.IndexOf('&');
        if (amp >= 0) caption = caption.Remove(amp, 1);
        var count = 0;
        var groups = root.Elements("menu").Select(menu => new MasterGroup((string?)menu.Attribute("caption") ?? "",
            menu.Elements("item").Select(item =>
            {
                if (++count > 2048) throw new XmlException("超魔法メニューの項目が2048件を超えています。");
                if ((string?)item.Attribute("caption") == "-") return new MasterItem("-", []);
                var steps = item.Elements().Select(element => element.Name.ToString() switch
                {
                    "send" => new MasterStep(MasterStepKind.Send, Text(element), (string?)element.Attribute("list") == "no"),
                    "ok_dialog" => new MasterStep(MasterStepKind.Confirm, Text(element)),
                    "int_dialog" => new MasterStep(MasterStepKind.Integer, Text(element)),
                    "string_dialog" => new MasterStep(MasterStepKind.String, Text(element)),
                    _ => null
                }).OfType<MasterStep>().ToImmutableArray();
                if (steps.Length > 4096) throw new XmlException("超魔法の手順が4096個を超えています。");
                return new MasterItem((string?)item.Attribute("caption") ?? "", steps);
            }).ToImmutableArray())).ToImmutableArray();
        return new(caption, groups);
    }

    private static string Text(XElement element) => element.Value.Replace("&#xa;", "\n", StringComparison.Ordinal);

    public static MasterAdvance Advance(MasterRun run)
    {
        if (run.Prompt is not null) throw new InvalidOperationException("先にダイアログへ回答してください。");
        var lines = ImmutableArray.CreateBuilder<string>();
        var hidden = 0;
        var length = 0L;
        while (run.Index < run.Steps.Length)
        {
            var step = run.Steps[run.Index];
            run = run with { Index = run.Index + 1 };
            if (step.Kind != MasterStepKind.Send)
                return new(run with { Prompt = step }, lines.ToImmutable(), hidden);
            var text = step.Text.Replace("%string", run.Text, StringComparison.Ordinal)
                .Replace("%int", run.Integer, StringComparison.Ordinal).Replace("\r", "").Replace("\n", "");
            PhiWire.ValidateLine(text);
            length += text.Length;
            if (length > 8 * 1024 * 1024)
                throw new ArgumentException("超魔法の一度の送信量が大きすぎます。入力か手順を短くしてください。");
            lines.Add(text);
            if (step.HideList) hidden++;
        }
        return new(run, lines.ToImmutable(), hidden);
    }

    public static MasterRun? Answer(MasterRun run, string? answer)
    {
        if (run.Prompt is not { } prompt) throw new InvalidOperationException("回答を待っているダイアログがありません。");
        if (answer is null) return null;
        return prompt.Kind switch
        {
            MasterStepKind.Confirm => run with { Prompt = null },
            MasterStepKind.Integer when Integer().IsMatch(answer.Trim()) =>
                run with { Integer = answer.Trim(), Prompt = null },
            MasterStepKind.Integer => throw new ArgumentException("整数を入力してください。超魔法の残りの手順は実行しませんでした。"),
            MasterStepKind.String => run with { Text = answer, Prompt = null },
            _ => throw new InvalidOperationException("送信手順にはダイアログの回答を適用できません。")
        };
    }
}
