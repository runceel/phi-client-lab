using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PhiClient.Core;
using Windows.Foundation;
using static Microsoft.UI.Reactor.Factories;

partial class App
{
    private Element MainTitleBar()
    {
        var title = active is null ? "PHI Client" : $"PHI Client — {active.Character.Name}";
        var gameVisible = active is not null && !showCharacters && !model.Worlds.Visible &&
            model.SettingsEditor is null && model.Registration.Stage == RegistrationStage.None;
        var session = active;
        var status = session?.Connected == true ? model.ChatFocused ? "接続・入力" :
            model.NativeInputFocused ? "接続・UI" : "接続・移動" :
            session?.Connecting == true ? "接続中…" : "未接続";
        var unread = session?.Model.Priv.Conversations.Count(item => item.Unread) ?? 0;
        var privLabel = session?.Model.Priv.Visible == true ? "会話に戻る" : "内緒話";
        if (unread > 0) privLabel += $"（未読 {unread} 件）";
        var shortStatus = session?.Connected == true ? "接続" :
            session?.Connecting == true ? "接続中…" : "未接続";

        var actions = HStack(2,
            model.Registration.Stage == RegistrationStage.None ? AppMenu().IsDragRegion(false) : Empty(),
            gameVisible ? Button(Icon(FontIcon("\uE897", fontSize: 16)),
                    () => Dispatch(new ClientMessage.GameGuideChanged(!model.GameGuideVisible)))
                .SubtleButton().Width(32).Height(32).Padding(4)
                .ToolTip("操作ガイド").AutomationName("操作ガイド").AutomationId("game-guide")
                .IsDragRegion(false) : Empty(),
            gameVisible && session is not null ? Button(HStack(2,
                    Icon(FontIcon(session.Model.Priv.Visible ? "\uE72B" : "\uE8F2", fontSize: 16)),
                    unread > 0 ? TextBlock(unread.ToString()).FontSize(12) : Empty()),
                    () => TogglePriv(session))
                .SubtleButton().MinWidth(32).Height(32).Padding(6, 4)
                .ToolTip(privLabel).AutomationName(privLabel).AutomationId("toggle-priv")
                .IsDragRegion(false) : Empty(),
            gameVisible ? Caption(windowWidth < 760 ? shortStatus : status)
                .VerticalAlignment(VerticalAlignment.Center).Margin(6, 0)
                .ToolTip(status).AutomationName(status).AutomationId("connection-status") : Empty()
        ).HorizontalAlignment(HorizontalAlignment.Left).VerticalAlignment(VerticalAlignment.Center)
            .AutomationId("titlebar-actions");

        return Grid(columns: [], rows: [], children: [
            TitleBar("").NoIcon().Tall(false).AutoRefreshDragRegions()
                .Content(actions)
                .Resources(resources => resources.Set("TitleBarExpandedHeight", 32.0))
                .Set(bar => bar.Resources["TitleBarContentHorizontalAlignment"] = HorizontalAlignment.Stretch)
                .AutomationName(title).AutomationId("main-titlebar"),
            TextBlock(title).FontSize(12).TextTrimming(TextTrimming.CharacterEllipsis)
                .HorizontalAlignment(HorizontalAlignment.Center).VerticalAlignment(VerticalAlignment.Center)
                .IsHitTestVisible(false).AutomationId("window-title")
        ]).OnMount(element => AttachCenteredTitle((Microsoft.UI.Xaml.Controls.Grid)element));
    }

    private static void AttachCenteredTitle(Microsoft.UI.Xaml.Controls.Grid chrome)
    {
        var bar = (Microsoft.UI.Xaml.Controls.TitleBar)chrome.Children[0];
        var title = (Microsoft.UI.Xaml.Controls.TextBlock)chrome.Children[1];
        void Layout(object? sender, object args)
        {
            if (chrome.XamlRoot is not { } root || bar.Content is not FrameworkElement actions) return;
            var window = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(root.ContentIslandEnvironment.AppWindowId);
            var actionRight = actions.TransformToVisual(chrome).TransformPoint(new Point(actions.ActualWidth, 0)).X;
            // Mirror the larger occupied edge, not the remaining space between toolbar and caption.
            var inset = Math.Max(actionRight + 8, window.TitleBar.RightInset / root.RasterizationScale + 8);
            var width = Math.Max(0, chrome.ActualWidth - 2 * inset);
            if (Math.Abs(title.MaxWidth - width) > 0.5) title.MaxWidth = width;
        }
        chrome.Loaded += (_, _) =>
        {
            chrome.LayoutUpdated -= Layout;
            chrome.LayoutUpdated += Layout;
        };
        chrome.LayoutUpdated += Layout;
        chrome.Unloaded += (_, _) => chrome.LayoutUpdated -= Layout;
    }
}
