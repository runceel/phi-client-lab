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
    private double CompactMapHeight => Math.Clamp(model.GameBodyHeight - 160, 112, 220);

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
                    Title = "PHI Client — 地図", Width = 400, Height = 520,
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

    private Element MapPane(GameSession session, double width) => VStack(4,
        TextBlock(session.ShowEagle && session.Eagle is not null ? "EagleEye" :
            $"周辺地図（{session.Map?.Size ?? 7} × {session.Map?.Size ?? 7}）")
            .TextWrapping(Microsoft.UI.Xaml.TextWrapping.Wrap).AutomationId("local-map"),
        HStack(6,
            Button("−", () => SetMapOptions(model.Options with { MapZoom = Math.Max(50, model.Options.MapZoom - 25) }))
                .AutomationName("地図を縮小").AutomationId("map-zoom-out").IsEnabled(model.Options.MapZoom > 50),
            NumberBox(model.Options.MapZoom).Width(88).AutomationName("地図の倍率（50から400パーセント・整数）")
                .AutomationId("map-zoom-value")
                .OnMount(element =>
                {
                    var box = (Microsoft.UI.Xaml.Controls.NumberBox)element;
                    box.Minimum = 50;
                    box.Maximum = 400;
                    box.SmallChange = 1;
                    var normalizing = false;
                    box.ValueChanged += (_, _) =>
                    {
                        if (normalizing) return;
                        var zoom = double.IsFinite(box.Value)
                            ? (int)Math.Clamp(Math.Round(box.Value, MidpointRounding.AwayFromZero), 50, 400)
                            : model.Options.MapZoom;
                        if (box.Value != zoom)
                        {
                            normalizing = true;
                            try { box.Value = zoom; }
                            finally { normalizing = false; }
                        }
                        if (model.Options.MapZoom != zoom) SetMapOptions(model.Options with { MapZoom = zoom });
                    };
                }),
            TextBlock("%"),
            Button("＋", () => SetMapOptions(model.Options with { MapZoom = Math.Min(400, model.Options.MapZoom + 25) }))
                .AutomationName("地図を拡大").AutomationId("map-zoom-in").IsEnabled(model.Options.MapZoom < 400)),
        model.Options.MapZoom != 100 ? Button("100%に戻す",
            () => SetMapOptions(model.Options with { MapZoom = 100 })).AutomationId("map-zoom-reset") : Empty(),
        (ScrollViewer(GraphicMap(session)) with
        {
            HorizontalScrollBarVisibility = Microsoft.UI.Xaml.Controls.ScrollBarVisibility.Auto,
            HorizontalScrollMode = Microsoft.UI.Xaml.Controls.ScrollMode.Enabled
        }).AutomationId("map-viewport").MaxWidth(width).Height(MapViewportHeight(session, width)).OnMount(element =>
        {
            var viewer = (Microsoft.UI.Xaml.Controls.ScrollViewer)element;
            viewer.SizeChanged += (_, args) =>
            {
                if (args.NewSize.Width <= 0 || args.NewSize.Height <= 0) return;
                session.MapViewportWidth = args.NewSize.Width;
                session.MapViewportHeight = args.NewSize.Height;
                refresh();
            };
            viewer.Loaded += (_, _) => dispatcher?.TryEnqueue(
                Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    if (viewer.IsLoaded && !session.ShowEagle)
                        viewer.ChangeView(viewer.ScrollableWidth / 2, viewer.ScrollableHeight / 2, null, disableAnimation: true);
                });
        }),
        model.GameGuideVisible ? CheckBox(model.Options.ShowMovementPad,
            value => SetMapOptions(model.Options with { ShowMovementPad = value }), "操作パッドを表示")
            .AutomationId("show-movement-pad") : Empty(),
        model.Options.ShowMovementPad ? MovementPad(session, width) : Empty(),
        GameViews.Status(session, width, details: false),
        session.Eagle is null ? Empty() : Button(session.ShowEagle ? "周辺地図に戻る" : "EagleEyeを表示", () =>
            session.Dispatch(new GameMessage.EagleToggled())),
        Expander("地図情報・保存", VStack(8,
            GameViews.Status(session, Math.Max(120, width - 48)),
            TextBlock(session.Map is null ? "地図を待っています。" :
                $"{(session.NorthFixed ? "北固定（上が北）" : "回転マップ（上が前）")} / 向き: " +
                (session.Map.Direction switch { 'E' => "東", 'S' => "南", 'W' => "西", _ => "北" }))
                .TextWrapping(Microsoft.UI.Xaml.TextWrapping.Wrap).AutomationId("map-state"),
            GameViews.Compass(session),
            TextBlock("周囲のキャラクター: " + string.Join(", ", MapLabels.NameList(session.Map?.Actors ?? [])))
                .TextWrapping(Microsoft.UI.Xaml.TextWrapping.Wrap).AutomationId("map-character-names"),
            TextBlock(model.GraphicsNotice).TextWrapping(Microsoft.UI.Xaml.TextWrapping.Wrap).FontSize(12)
                .AutomationId("graphics-notice"),
            WrappedMapButton("表示中の地図をPNGに保存", () => SaveMapImage(session), Math.Max(120, width - 48))
                .IsEnabled(!busy && session.Map is not null).AutomationId("save-map-image"),
            WrappedMapButton("地図画像の保存先を開く", OpenMapImageFolder, Math.Max(120, width - 48))
                .AutomationId("open-map-images"),
            TextBlock("地図のクリックで移動、右クリックで攻撃します。\n同梱の基本画像は、外部素材の固有の姿を再現するものではありません。")
                .TextWrapping(Microsoft.UI.Xaml.TextWrapping.Wrap).FontSize(12)).Width(Math.Max(120, width - 48)))
            .HorizontalAlignment(Microsoft.UI.Xaml.HorizontalAlignment.Stretch).AutomationId("map-details")
    ).Width(width).IsEnabled(!busy && !dialogOpen && !closingApplication && model.SettingsEditor is null);

    private double MapViewportHeight(GameSession session, double width)
    {
        // Recorded terrain uses the available viewport to select its surrounding ring.
        var maximum = model.Options.SingleWindow && windowWidth < 760
            ? Math.Clamp(CompactMapHeight - 92, 64, 144) : 360;
        if (!session.ShowEagle && model.Options.Automap && session.NorthFixed &&
            session.Map is { Size: 7, Position: not null })
            return Math.Min(width, maximum);
        var dim = session.ShowEagle && session.Eagle is { } eagle
            ? EagleBoard.Build(eagle).Dim : session.Map?.Size ?? 7;
        var contentHeight = session.GraphicsImage is not null ? session.GraphicsHeight :
            dim * 32 * model.Options.MapZoom / 100.0;
        return Math.Min(maximum, Math.Min(width, contentHeight));
    }

    private Element MovementPad(GameSession session, double width)
    {
        var arrows = new[] { "左旋回", "↑", "右旋回", "←", "攻撃", "→", "", "↓", "" };
        var directions = session.NorthFixed
            ? new[] { "左を向く", "北へ移動", "右を向く", "西へ移動", "攻撃", "東へ移動", "", "南へ移動", "" }
            : new[] { "左を向く", "前へ移動", "右を向く", "左へ移動", "攻撃", "右へ移動", "", "後ろへ移動", "" };
        var keys = new[] { "NumPad7", "NumPad8", "NumPad9", "NumPad4", "Space", "NumPad6", "", "NumPad2", "" };
        var cells = Enumerable.Range(0, 9).Select(index =>
        {
            var x = index % 3 - 1;
            var y = index / 3 - 1;
            if (index is 6 or 8) return (Element)Empty().Width((width - 12) / 3).Height(36);
            var name = $"{directions[index]}（{keys[index]}）";
            return (Element)Button(arrows[index], () => Execute(session, GameInput.MapPad(x, y, session.NorthFixed)))
                .Width((width - 12) / 3).Height(36).ToolTip(name).AutomationName(name)
                .AutomationId($"movement-pad-{index / 3}-{index % 3}");
        }).ToArray();
        return VStack(4, HStack(6, cells[..3]), HStack(6, cells[3..6]), HStack(6, cells[6..]))
            .AutomationId("movement-pad");
    }

    private static ButtonElement WrappedMapButton(string text, Action action, double width) =>
        Button(text, action).Width(width).AutomationName(text).OnMount(element =>
            ((Microsoft.UI.Xaml.Controls.Button)element).Content = new Microsoft.UI.Xaml.Controls.TextBlock
            { Text = text, TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap });
}
