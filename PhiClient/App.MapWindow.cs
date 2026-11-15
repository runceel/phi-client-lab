using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using PhiClient;
using PhiClient.Core;
using static Microsoft.UI.Reactor.Factories;

partial class App
{
    private ReactorWindow? mapWindow;
    private Action? refreshMapWindow;
    private bool openingMapWindow;

    private sealed class MapWindowView(App owner) : Component
    {
        public override Element Render()
        {
            var (_, update) = UseReducer(owner.model);
            owner.refreshMapWindow = () => update(_ => owner.model);
            return owner.DetachedMapView();
        }
    }

    private void UpdateMapWindow()
    {
        if (openingMapWindow) return;
        var visible = loaded && active is not null && showMap && !model.Options.SingleWindow && !closingApplication;
        if (!visible)
        {
            if (mapWindow?.IsVisible == true)
            {
                mapWindow.SavePlacement();
                mapWindow.Hide();
            }
            return;
        }
        if (mapWindow is null)
        {
            openingMapWindow = true;
            try
            {
                mapWindow = ReactorApp.OpenWindow(new WindowSpec
                {
                    Title = "PHI Client — 地図", Width = 400, Height = 780,
                    MinWidth = 380, MinHeight = 280,
                    PersistenceId = "phi-map", PersistPlacement = true, ActivateOnOpen = false
                }, () => new MapWindowView(this));
            }
            finally { openingMapWindow = false; }
            mapWindow.Closing += (_, args) =>
            {
                if (disposed || closingApplication) return;
                args.Cancel = true;
                mapWindow.SavePlacement();
                showMap = false;
            };
            mapWindow.DpiChanged += (_, _) => refresh();
        }
        if (!mapWindow.IsVisible) mapWindow.Show();
        refreshMapWindow?.Invoke();
        TraceDiagnostic(DiagnosticEvent.MapWindowRefresh);
    }

    private Element DetachedMapView() => FlexColumn(
        TitleBar("PHI Client — 地図"),
        active is { } session
            ? ScrollViewer(MapPane(session, model.DetachedMapWidth)).Flex(grow: 1, basis: 0).Padding(12)
                .AutomationId("detached-map-scroll")
                .OnMount(element =>
                {
                    var viewer = (Microsoft.UI.Xaml.Controls.ScrollViewer)element;
                    viewer.SizeChanged += (_, args) =>
                    {
                        var width = (int)Math.Clamp(args.NewSize.Width - viewer.Padding.Left - viewer.Padding.Right, 184, 1600);
                        if (width != model.DetachedMapWidth)
                            Dispatch(new ClientMessage.DetachedMapWidthChanged(width));
                    };
                })
            : TextBlock("キャラクターに接続すると地図を表示します。").TextWrapping(Microsoft.UI.Xaml.TextWrapping.Wrap)
    ).Backdrop(BackdropKind.Mica).OnPreviewKeyDown(OnGameKeyDown).AutomationId("detached-map");

    private Element MapPane(GameSession session, double width) => VStack(8,
        SubHeading(session.ShowEagle && session.Eagle is not null ? "EagleEye" :
            $"周辺地図（{session.Map?.Size ?? 7} × {session.Map?.Size ?? 7}）")
            .TextWrapping(Microsoft.UI.Xaml.TextWrapping.Wrap).AutomationId("local-map"),
        HStack(8, GameViews.Compass(session), TextBlock(session.Map is null ? "地図を待っています。" :
            session.NorthFixed ? "北固定（上が北）" : "回転マップ（方位は矢印で確認）")
            .TextWrapping(Microsoft.UI.Xaml.TextWrapping.Wrap).Width(Math.Max(80, width - 90))),
        (ScrollViewer(GraphicMap(session)) with
        {
            HorizontalScrollBarVisibility = Microsoft.UI.Xaml.Controls.ScrollBarVisibility.Auto,
            HorizontalScrollMode = Microsoft.UI.Xaml.Controls.ScrollMode.Enabled
        }).AutomationId("map-viewport").MaxWidth(width).Height(width).OnMount(element =>
        {
            var viewer = (Microsoft.UI.Xaml.Controls.ScrollViewer)element;
            viewer.SizeChanged += (_, args) =>
            {
                if (args.NewSize.Width <= 0 || args.NewSize.Height <= 0) return;
                session.MapViewportWidth = args.NewSize.Width;
                session.MapViewportHeight = args.NewSize.Height;
                refresh();
            };
        }),
        TextBlock("周囲のキャラクター: " + string.Join(", ", MapLabels.NameList(session.Map?.Actors ?? [])))
            .TextWrapping(Microsoft.UI.Xaml.TextWrapping.Wrap).Width(width).AutomationId("map-character-names"),
        TextBlock(model.GraphicsNotice).TextWrapping(Microsoft.UI.Xaml.TextWrapping.Wrap).FontSize(12).Width(width)
            .AutomationId("graphics-notice"),
        TextBlock(session.Map is null ? "地図を待っています。" :
            $"向き: {session.Map.Direction} | {(session.NorthFixed ? "北固定" : "回転マップ")}")
            .TextWrapping(Microsoft.UI.Xaml.TextWrapping.Wrap).Width(width).AutomationId("map-state"),
        session.Eagle is null ? Empty() : Button(session.ShowEagle ? "周辺地図に戻る" : "EagleEyeを表示", () =>
            session.Dispatch(new GameMessage.EagleToggled())),
        WrappedMapButton("表示中の地図をPNGに保存", () => SaveMapImage(session), width)
            .IsEnabled(!busy && session.Map is not null).AutomationId("save-map-image"),
        WrappedMapButton("地図画像の保存先を開く", OpenMapImageFolder, width).AutomationId("open-map-images"),
        GameViews.Status(session),
        TextBlock("地図のクリックで移動、右クリックで攻撃します。\n簡易表示: ○はキャラクター、!は看板、*はアイテムです。")
            .TextWrapping(Microsoft.UI.Xaml.TextWrapping.Wrap).FontSize(12).Width(width),
        width < 300 ? VStack(6,
            HStack(6, Button("左を向く", () => Send(session, "turn l")), Button("前へ", () => Move(session, "N"))),
            HStack(6, Button("右を向く", () => Send(session, "turn r")), Button("後ろへ", () => Move(session, "S"))),
            HStack(6, Button("左へ", () => Move(session, "W")), Button("右へ", () => Move(session, "E"))),
            Button("攻撃", () => Send(session, "hit"))) : VStack(6, HStack(6,
            Button("左を向く", () => Send(session, "turn l")),
            Button("前へ", () => Move(session, "N")),
            Button("右を向く", () => Send(session, "turn r"))),
        HStack(6,
            Button("左へ", () => Move(session, "W")),
            Button("攻撃", () => Send(session, "hit")),
            Button("右へ", () => Move(session, "E")),
            Button("後ろへ", () => Move(session, "S"))))
    ).Width(width).IsEnabled(!busy && !dialogOpen && !closingApplication && model.SettingsEditor is null);

    private static ButtonElement WrappedMapButton(string text, Action action, double width) =>
        Button(text, action).Width(width).AutomationName(text).OnMount(element =>
            ((Microsoft.UI.Xaml.Controls.Button)element).Content = new Microsoft.UI.Xaml.Controls.TextBlock
            { Text = text, TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap });
}
