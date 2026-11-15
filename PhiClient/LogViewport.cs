using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using PhiClient.Core;

namespace PhiClient;

internal sealed class LogViewport
{
    private readonly LogPresenter presenter;
    private readonly LogScrollState state;
    private readonly Button latest;
    private bool pendingLayout = true;
    private bool queued;
    private bool applying;
    private bool userScrolling;
    private bool inputPending;
    private int inputVersion;
    private (double Width, double Height, double Extent) layout;

    public ScrollViewer Viewer { get; } = new()
    {
        HorizontalScrollMode = ScrollMode.Disabled,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White),
        IsTabStop = true
    };
    public Grid Surface { get; } = new();

    public LogViewport(LogPresenter presenter, LogScrollState state, string id)
    {
        this.presenter = presenter;
        this.state = state;
        Viewer.Content = presenter.Surface;
        AutomationProperties.SetAutomationId(Viewer, id + "-scroll");
        latest = new Button
        {
            Content = "最新へ ↓", Visibility = Visibility.Collapsed,
            RequestedTheme = ElementTheme.Light,
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(8, 8, 24, 8), Padding = new Thickness(10, 4, 10, 4),
            MinHeight = 28
        };
        AutomationProperties.SetAutomationId(latest, id + "-latest");
        AutomationProperties.SetName(latest, "ログの最新へ移動");
        AutomationProperties.SetHelpText(latest, "末尾へ戻ります。新着への自動追随が有効なら追随を再開します。");
        latest.Click += (_, _) =>
        {
            // Only return focus when the disappearing button actually owns it.
            if (ReferenceEquals(FocusManager.GetFocusedElement(latest.XamlRoot), latest))
                Viewer.Focus(FocusState.Programmatic);
            state.Latest();
            userScrolling = false;
            ContentChanged();
        };
        Surface.Children.Add(Viewer);
        Surface.Children.Add(latest);
        Viewer.SizeChanged += (_, _) => ContentChanged();
        presenter.Control.SizeChanged += (_, _) => ContentChanged();
        Viewer.Loaded += (_, _) => ContentChanged();
        Viewer.AddHandler(UIElement.PointerWheelChangedEvent,
            new PointerEventHandler((_, _) => MarkUserInput()), true);
        Viewer.AddHandler(UIElement.PointerPressedEvent, new PointerEventHandler((_, args) =>
        {
            for (var parent = args.OriginalSource as DependencyObject; parent is not null && parent != Viewer;
                parent = VisualTreeHelper.GetParent(parent))
                if (parent is Microsoft.UI.Xaml.Controls.Primitives.ScrollBar)
                {
                    MarkUserInput();
                    break;
                }
        }), true);
        Viewer.DirectManipulationStarted += (_, _) => MarkUserInput();
        Viewer.PreviewKeyDown += (_, args) =>
        {
            if (args.Key is Windows.System.VirtualKey.PageUp or Windows.System.VirtualKey.PageDown or
                Windows.System.VirtualKey.Home or Windows.System.VirtualKey.End or
                Windows.System.VirtualKey.Up or Windows.System.VirtualKey.Down)
                MarkUserInput();
        };
        Viewer.LayoutUpdated += (_, _) =>
        {
            if (layout != Metrics()) pendingLayout = true;
            Schedule();
        };
        Viewer.ViewChanging += (_, args) =>
        {
            // Extent/viewport changes and our own ChangeView are not user intent.
            if (!inputPending && (applying || pendingLayout || layout != Metrics())) return;
            if (inputPending)
            {
                applying = pendingLayout = false;
                layout = Metrics();
            }
            userScrolling = true;
            state.UserScrolled(args.FinalView.VerticalOffset, Viewer.ScrollableHeight);
        };
        Viewer.ViewChanged += (_, args) =>
        {
            if (args.IsIntermediate) return;
            var wasApplying = applying;
            applying = false;
            if (userScrolling && !wasApplying)
                state.UserScrolled(Viewer.VerticalOffset, Viewer.ScrollableHeight);
            userScrolling = false;
            if (!pendingLayout && layout == Metrics()) Remember();
            UpdateButton();
            Schedule();
        };
        Viewer.Unloaded += (_, _) =>
        {
            if (!pendingLayout && !applying) Remember();
        };
    }

    public void Configure(bool enabled)
    {
        if (state.Enabled == enabled) return;
        state.Configure(enabled);
        ContentChanged();
    }

    public void ContentChanged()
    {
        pendingLayout = true;
        Schedule();
    }

    public void SetSearching(bool enabled)
    {
        state.Searching = enabled;
        ContentChanged();
    }

    public void ScrollToSearch(long id)
    {
        state.Anchor = new(id, 0, 0);
        ContentChanged();
    }

    private (double, double, double) Metrics() =>
        (Viewer.ViewportWidth, Viewer.ViewportHeight, Viewer.ExtentHeight);

    private void MarkUserInput()
    {
        inputPending = true;
        var version = ++inputVersion;
        Viewer.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            if (version == inputVersion) inputPending = false;
        });
    }

    private void Schedule()
    {
        if (!pendingLayout || queued || applying || userScrolling || !Viewer.IsLoaded ||
            Viewer.ViewportHeight <= 0) return;
        queued = Viewer.DispatcherQueue.TryEnqueue(DispatcherQueuePriority.Low, () =>
        {
            queued = false;
            if (!pendingLayout || !Viewer.IsLoaded || Viewer.XamlRoot is null || applying || userScrolling) return;
            layout = Metrics();
            pendingLayout = false;
            var target = state.Target(Viewer.ScrollableHeight, presenter.ResolveAnchor);
            if (Math.Abs(target - Viewer.VerticalOffset) > .5)
            {
                applying = true;
                if (!Viewer.ChangeView(null, target, null, disableAnimation: true))
                {
                    applying = false;
                    Remember();
                }
            }
            else Remember();
            UpdateButton();
        });
    }

    private void Remember()
    {
        if (Viewer.ViewportHeight <= 0 || !Viewer.IsLoaded) return;
        state.Offset = Viewer.VerticalOffset;
        state.Anchor = presenter.CaptureAnchor(Viewer.VerticalOffset);
    }

    private void UpdateButton() => latest.Visibility =
        LogScrollState.AtLatest(Viewer.VerticalOffset, Viewer.ScrollableHeight)
            ? Visibility.Collapsed : Visibility.Visible;
}
