using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using PhiClient.Core;
using Windows.System;
using Windows.UI.Core;

namespace PhiClient;

internal sealed class LogSearchPanel
{
    private readonly LogPresenter presenter;
    private readonly Action close;
    private readonly TextBlock count = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new(6, 0, 6, 0) };
    private LogSearchResult result = new([], null);
    private bool composing;
    private bool committing;
    public Grid Surface { get; } = new() { ColumnSpacing = 2, Margin = new(0, 2, 0, 2) };
    public TextBox Query { get; } = new() { PlaceholderText = "ログを検索", MinWidth = 64, MinHeight = 28, Padding = new(4) };
    public bool IsComposing => composing || committing && Down(VirtualKey.Enter);

    public LogSearchPanel(LogPresenter presenter, Action close)
    {
        this.presenter = presenter;
        this.close = close;
        for (var i = 0; i < 5; i++)
            Surface.ColumnDefinitions.Add(new() { Width = i == 0 ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
        Add(Query, 0);
        Add(count, 1);
        Add(Button("↑", "古い一致へ（Enter）", "log-search-older", () => Step(-1)), 2);
        Add(Button("↓", "新しい一致へ（Shift+Enter）", "log-search-newer", () => Step(1)), 3);
        Add(Button("×", "検索を閉じる（Esc）", "log-search-close", close), 4);
        AutomationProperties.SetAutomationId(Query, "log-search-query");
        AutomationProperties.SetName(Query, "保持済みの世界ログを検索");
        AutomationProperties.SetAutomationId(count, "log-search-count");
        Query.TextChanged += (_, _) => Refresh(reset: true);
        Query.TextCompositionStarted += (_, _) => composing = true;
        Query.TextCompositionEnded += (_, _) =>
        {
            composing = false;
            committing = Down(VirtualKey.Enter);
        };
        Query.PreviewKeyDown += OnKeyDown;
        Query.KeyUp += (_, args) => { if (args.Key == VirtualKey.Enter) committing = false; };
        Surface.Loaded += (_, _) =>
        {
            presenter.ContentUpdated += OnContentUpdated;
            presenter.SearchMode(true);
            Refresh(reset: false);
            Query.Focus(FocusState.Programmatic);
        };
        Surface.Unloaded += (_, _) =>
        {
            presenter.ContentUpdated -= OnContentUpdated;
            presenter.SearchMode(false);
            presenter.HighlightSearch(new([], null));
        };
    }

    public void Focus()
    {
        if (composing || committing) return;
        Query.Focus(FocusState.Keyboard);
        Query.SelectAll();
    }

    private static bool Down(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    private void OnKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (!Down(VirtualKey.Enter)) committing = false;
        if (composing || committing || Down(VirtualKey.Control) || Down(VirtualKey.Menu)) return;
        if (args.Key == VirtualKey.Escape) close();
        else if (args.Key == VirtualKey.Enter) Step(Down(VirtualKey.Shift) ? 1 : -1);
        else return;
        args.Handled = true;
    }

    private void OnContentUpdated() => Refresh(reset: false);

    private void Refresh(bool reset)
    {
        var previous = result.Current;
        result = LogSearch.Find(presenter.SearchRows(), Query.Text, reset ? null : result.Current);
        Present(reset ? null : previous);
    }

    private void Step(int direction)
    {
        result = result.Step(direction);
        Present(null);
    }

    private void Present(long? previous)
    {
        count.Text = Query.Text.Length == 0 ? "" : $"{result.Position}/{result.Hits.Length}";
        presenter.HighlightSearch(result);
        if (previous != result.Current && result.Current is { } id) presenter.ScrollToSearch(id);
    }

    private void Add(FrameworkElement element, int column)
    {
        Grid.SetColumn(element, column);
        Surface.Children.Add(element);
    }

    private static Button Button(string text, string name, string id, Action action)
    {
        var button = new Button { Content = text, Padding = new(6, 2, 6, 2), MinWidth = 28, MinHeight = 28 };
        AutomationProperties.SetName(button, name);
        AutomationProperties.SetAutomationId(button, id);
        ToolTipService.SetToolTip(button, name);
        button.Click += (_, _) => action();
        return button;
    }
}
