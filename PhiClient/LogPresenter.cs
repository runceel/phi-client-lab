using System.Collections.Immutable;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using PhiClient.Core;

namespace PhiClient;

internal sealed class LogPresenter(Action<IReadOnlyList<LogEntry>, bool> copy, Action<Uri> openLink)
{
    public RichTextBlock Control { get; } = new()
    {
        IsTextSelectionEnabled = true,
        TextWrapping = TextWrapping.Wrap,
        FontFamily = new FontFamily("ＭＳ ゴシック, Consolas"),
        FontSize = 14,
        Foreground = new SolidColorBrush(Microsoft.UI.Colors.Black),
        RequestedTheme = ElementTheme.Light
    };
    private readonly Dictionary<DependencyObject, long> owners = [];
    private readonly Dictionary<long, Paragraph> paragraphs = [];
    private ImmutableArray<LogEntry> entries = [];
    private (bool, string, uint)? presentation;
    private long? pressedRow;

    public void Attach()
    {
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(Control, "game-log");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(Control, "世界からのログ");
        var menu = new MenuFlyout();
        var clipboard = new MenuFlyoutItem { Text = "行全体をコピー" };
        var input = new MenuFlyoutItem { Text = "行全体を入力欄へコピー" };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(clipboard, "log-copy-clipboard");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(input, "log-copy-input");
        clipboard.Click += (_, _) => Copy(false);
        input.Click += (_, _) => Copy(true);
        menu.Items.Add(clipboard);
        menu.Items.Add(input);
        menu.Opening += (_, _) => clipboard.IsEnabled = input.IsEnabled = Selected().Count > 0;
        Control.ContextFlyout = menu;
        Control.AddHandler(UIElement.PointerPressedEvent, new Microsoft.UI.Xaml.Input.PointerEventHandler((_, args) =>
        {
            var point = args.GetCurrentPoint(Control);
            if (point.Properties.IsRightButtonPressed)
                pressedRow = Owner(Control.GetPositionFromPoint(point.Position));
        }), true);
        Control.AddHandler(UIElement.KeyDownEvent, new Microsoft.UI.Xaml.Input.KeyEventHandler((_, args) =>
        {
            if (args.Key != Windows.System.VirtualKey.C ||
                !Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(Windows.System.VirtualKey.Control)
                    .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down) || Control.SelectedText.Length == 0) return;
            Copy(false);
            args.Handled = true;
        }), true);
    }

    public void Update(ImmutableArray<LogEntry> value, ClientOptions options)
    {
        var key = (options.LogDispTime, options.LogFontName, options.LogFontSize);
        if (presentation != key)
        {
            Control.Blocks.Clear();
            paragraphs.Clear();
            owners.Clear();
            presentation = key;
            Control.FontFamily = new(options.LogFontName.Length == 0 ? "ＭＳ ゴシック, Consolas" : options.LogFontName);
            Control.FontSize = options.LogFontSize == 0 ? 14 : options.LogFontSize;
        }
        if (entries == value && paragraphs.Count == value.Length) return;
        entries = value;
        var retained = value.Select(row => row.Id).ToHashSet();
        foreach (var stale in paragraphs.Keys.Where(id => !retained.Contains(id)).ToArray())
        {
            Control.Blocks.Remove(paragraphs[stale]);
            paragraphs.Remove(stale);
            foreach (var owner in owners.Where(pair => pair.Value == stale).Select(pair => pair.Key).ToArray())
                owners.Remove(owner);
        }
        foreach (var row in value)
        {
            if (paragraphs.ContainsKey(row.Id)) continue;
            var paragraph = new Paragraph { Margin = new Thickness(0), LineHeight = Math.Max(18, Control.FontSize * 1.3) };
            owners.Add(paragraph, row.Id);
            if (options.LogDispTime && row.Time is { } time)
            {
                var stamp = new Run { Text = time.ToString("HH:mm ", System.Globalization.CultureInfo.InvariantCulture),
                    FontFamily = new("Segoe UI"), FontSize = Control.FontSize * .9,
                    Foreground = new SolidColorBrush(Microsoft.UI.Colors.Teal) };
                paragraph.Inlines.Add(stamp);
                owners.Add(stamp, row.Id);
            }
            var parts = LogFormat.Parts(row);
            foreach (var part in parts)
            {
                var run = new Run { Text = part.Text, FontWeight = part.Style.Bold ? FontWeights.Bold : FontWeights.Normal,
                    FontStyle = part.Style.Italic ? Windows.UI.Text.FontStyle.Italic : Windows.UI.Text.FontStyle.Normal };
                if (part.Style.Size is { } size) run.FontSize = size;
                if (part.Style.Scale != 1) run.FontSize = (part.Style.Size ?? Control.FontSize) * part.Style.Scale;
                if (part.Style.Font is { } font) run.FontFamily = new(font);
                if (part.Style.Baseline != 0)
                    Typography.SetVariants(run, part.Style.Baseline > 0 ? FontVariants.Superscript : FontVariants.Subscript);
                if (Color(part.Style.Color) is { } color) run.Foreground = new SolidColorBrush(color);
                run.TextDecorations = (part.Style.Underline ? Windows.UI.Text.TextDecorations.Underline : 0) |
                    (part.Style.Strike ? Windows.UI.Text.TextDecorations.Strikethrough : 0);
                owners.Add(run, row.Id);
                if (part.Link is { } uri)
                {
                    var link = new Hyperlink();
                    link.Inlines.Add(run);
                    link.Click += (_, _) => openLink(uri);
                    owners.Add(link, row.Id);
                    paragraph.Inlines.Add(link);
                }
                else paragraph.Inlines.Add(run);
            }
            paragraphs.Add(row.Id, paragraph);
            Control.Blocks.Add(paragraph);
        }
    }

    private long? Owner(TextPointer? pointer)
    {
        if (pointer is null) return null;
        if (owners.TryGetValue(pointer.Parent, out var id)) return id;
        foreach (var pair in paragraphs)
            if (pointer.Offset >= pair.Value.ElementStart.Offset && pointer.Offset <= pair.Value.ElementEnd.Offset)
                return pair.Key;
        return null;
    }

    private IReadOnlyList<LogEntry> Selected()
    {
        if (Control.SelectedText.Length > 0 &&
            Owner(Control.SelectionStart) is { } start && Owner(Control.SelectionEnd) is { } end)
            return entries.Where(row => row.Id >= Math.Min(start, end) && row.Id <= Math.Max(start, end)).ToArray();
        return pressedRow is { } rowId ? entries.Where(row => row.Id == rowId).ToArray() : [];
    }

    private void Copy(bool toInput)
    {
        var rows = Selected();
        if (rows.Count > 0) copy(rows, toInput);
    }

    private static Windows.UI.Color? Color(string? value)
    {
        if (value is null) return null;
        if (value.StartsWith('#') && value.Length is 4 or 7 &&
            uint.TryParse(value[1..], System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var rgb))
        {
            if (value.Length == 4) rgb = ((rgb & 0xf00) << 8 | (rgb & 0xf0) << 4 | rgb & 0xf) * 17;
            return Windows.UI.Color.FromArgb(255, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
        }
        var known = System.Drawing.Color.FromName(value.Replace("grey", "gray", StringComparison.OrdinalIgnoreCase));
        return known.IsKnownColor && !known.IsSystemColor ?
            Windows.UI.Color.FromArgb(known.A, known.R, known.G, known.B) : null;
    }
}
