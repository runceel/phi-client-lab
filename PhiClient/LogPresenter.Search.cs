using System.Text;
using Microsoft.UI;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using PhiClient.Core;

namespace PhiClient;

internal sealed partial class LogPresenter
{
    public event Action? ContentUpdated;

    public IEnumerable<(long Id, string Text)> SearchRows()
    {
        foreach (var row in entries)
        {
            if (!paragraphs.TryGetValue(row.Id, out var paragraph)) continue;
            var text = new StringBuilder();
            Append(paragraph.Inlines);
            yield return (row.Id, text.ToString());
            void Append(IEnumerable<Inline> inlines)
            {
                foreach (var inline in inlines)
                    switch (inline)
                    {
                        case Run run: text.Append(run.Text); break;
                        case LineBreak: text.Append('\n'); break;
                        case Span span: Append(span.Inlines); break;
                    }
            }
        }
    }

    public void SearchMode(bool enabled) => viewport?.SetSearching(enabled);

    public void ScrollToSearch(long id) => viewport?.ScrollToSearch(id);

    public void HighlightSearch(LogSearchResult result)
    {
        Control.TextHighlighters.Clear();
        var accessible = new Windows.UI.ViewManagement.AccessibilitySettings().HighContrast;
        var colors = new Windows.UI.ViewManagement.UISettings();
        foreach (var (id, start, length) in LogSearch.HighlightRanges(SearchRows(), result))
        {
            var highlight = new TextHighlighter
            {
                Background = new SolidColorBrush(accessible
                    ? colors.UIElementColor(Windows.UI.ViewManagement.UIElementType.Highlight)
                    : result.Current == id ? Colors.Orange : Colors.LightGoldenrodYellow),
                Foreground = new SolidColorBrush(accessible
                    ? colors.UIElementColor(Windows.UI.ViewManagement.UIElementType.HighlightText) : Colors.Black)
            };
            highlight.Ranges.Add(new TextRange
            {
                StartIndex = start,
                Length = length
            });
            Control.TextHighlighters.Add(highlight);
        }
    }
}
