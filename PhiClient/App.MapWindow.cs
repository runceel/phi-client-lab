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
    private (string Character, int Saved, int Percent)? mapZoomMenuInputs;
    private Element? mapZoomMenuElement;
    private double CompactMapHeight => MapZoomLayout.CompactHeight(model.GameBodyHeight);

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
                    Title = $"{ProductName} — 地図", Width = 400, Height = 520,
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
        TitleBar($"{ProductName} — 地図"),
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
                        var height = Math.Max(4, args.NewSize.Height - viewer.Padding.Top - viewer.Padding.Bottom);
                        if (Math.Abs(height - model.DetachedMapHeight) > 0.5)
                            Dispatch(new ClientMessage.DetachedMapHeightChanged(height));
                    };
                })
            : TextBlock("キャラクターに接続すると地図を表示します。").TextWrapping(Microsoft.UI.Xaml.TextWrapping.Wrap)
    ).Backdrop(BackdropKind.Mica).OnPreviewKeyDown(OnGameKeyDown).AutomationId("detached-map");

    private Element MapPane(GameSession session, double width) => VStack(4,
        FlexRow(
        TextBlock(session.ShowEagle && session.Eagle is not null ? "EagleEye" :
            $"周辺地図（{session.Map?.Size ?? 7} × {session.Map?.Size ?? 7}）")
            .TextTrimming(Microsoft.UI.Xaml.TextTrimming.CharacterEllipsis)
            .VerticalAlignment(Microsoft.UI.Xaml.VerticalAlignment.Center).AutomationId("local-map").Flex(grow: 1, basis: 0),
        MapZoomMenu(session).Flex(shrink: 0)).Height(32),
        (ScrollViewer(GraphicMap(session)) with
        {
            HorizontalScrollBarVisibility = model.Options.MapZoom == 0
                ? Microsoft.UI.Xaml.Controls.ScrollBarVisibility.Disabled : Microsoft.UI.Xaml.Controls.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = model.Options.MapZoom == 0
                ? Microsoft.UI.Xaml.Controls.ScrollBarVisibility.Disabled : Microsoft.UI.Xaml.Controls.ScrollBarVisibility.Auto,
            HorizontalScrollMode = model.Options.MapZoom == 0
                ? Microsoft.UI.Xaml.Controls.ScrollMode.Disabled : Microsoft.UI.Xaml.Controls.ScrollMode.Enabled,
            VerticalScrollMode = model.Options.MapZoom == 0
                ? Microsoft.UI.Xaml.Controls.ScrollMode.Disabled : Microsoft.UI.Xaml.Controls.ScrollMode.Enabled
        }).AutomationId("map-viewport").MaxWidth(width).Height(MapViewportHeight(session, width)).OnMount(element =>
        {
            var viewer = (Microsoft.UI.Xaml.Controls.ScrollViewer)element;
            viewer.SizeChanged += (_, args) =>
            {
                if (args.NewSize.Width <= 0 || args.NewSize.Height <= 0) return;
                session.MapViewportWidth = args.NewSize.Width;
                session.MapViewportHeight = args.NewSize.Height;
                if (Math.Abs(model.MapViewportWidth - args.NewSize.Width) > 0.5 ||
                    Math.Abs(model.MapViewportHeight - args.NewSize.Height) > 0.5)
                    Dispatch(new ClientMessage.MapViewportSizeChanged(args.NewSize.Width, args.NewSize.Height));
            };
            viewer.Loaded += (_, _) => dispatcher?.TryEnqueue(
                Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                {
                    if (viewer.IsLoaded && !session.ShowEagle)
                        viewer.ChangeView(viewer.ScrollableWidth / 2, viewer.ScrollableHeight / 2, null, disableAnimation: true);
                });
        }),
        VStack(4, GameViews.Status(session, width, details: false),
        model.GameGuideVisible ? CheckBox(model.Options.ShowMovementPad,
            value => SetMapOptions(model.Options with { ShowMovementPad = value }), "操作パッドを表示")
            .AutomationId("show-movement-pad") : Empty(),
        model.Options.ShowMovementPad ? MovementPad(session, width) : Empty(),
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
            .HorizontalAlignment(Microsoft.UI.Xaml.HorizontalAlignment.Stretch).AutomationId("map-details"))
            .OnSizeChanged((_, args) =>
            {
                var height = Math.Ceiling(args.NewSize.Height);
                if (height > 0 && Math.Abs(model.MapFooterHeight - height) > 0.5)
                    Dispatch(new ClientMessage.MapFooterHeightChanged(height));
            })
    ).Width(width).IsEnabled(!busy && !dialogOpen && !closingApplication && model.SettingsEditor is null);

    private double MapHeightBudget => MapZoomLayout.ViewportHeight(
        !model.Options.SingleWindow ? model.DetachedMapHeight :
        windowWidth < 760 ? CompactMapHeight : Math.Max(4, model.GameBodyHeight), model.MapFooterHeight);

    private double IntegratedMapWidth => windowWidth < 760
        ? Math.Min(model.Options.MapPaneWidth, Math.Max(200, windowWidth - 48))
        : MapPaneLayout.Width(model.Options.MapPaneWidth, MapAvailableWidth);

    private double MapContentWidth => model.Options.SingleWindow ? IntegratedMapWidth - 16 : model.DetachedMapWidth;

    private int MapDimension(GameSession session) => session.ShowEagle && session.Eagle is { } eagle
        ? EagleBoard.Build(eagle).Dim : DisplayMap(session)?.Frame.Size ?? session.Map?.Size ?? 7;

    private double EffectiveMapZoom(GameSession session) => MapZoomLayout.Resolve(model.Options.MapZoom,
        MapDimension(session), session.ShowEagle && session.Eagle is not null,
        Math.Max(4, MapContentWidth), MapHeightBudget);

    private double MapViewportHeight(GameSession session, double width)
    {
        if (model.Options.MapZoom != 0 && !session.ShowEagle && model.Options.Automap && session.NorthFixed &&
            session.Map is { Size: 7, Position: not null })
            return Math.Min(width, MapHeightBudget);
        var dimension = MapDimension(session);
        var eagle = session.ShowEagle && session.Eagle is not null;
        var zoom = MapZoomLayout.Resolve(model.Options.MapZoom, dimension, eagle, Math.Max(4, width), MapHeightBudget);
        return Math.Min(MapHeightBudget, (dimension * 32 + (eagle ? 0 : 16)) * zoom + 2);
    }

    private Element MapZoomMenu(GameSession session)
    {
        var inputs = (session.Character.Key, model.Options.MapZoom, (int)Math.Round(EffectiveMapZoom(session) * 100));
        if (inputs == mapZoomMenuInputs && mapZoomMenuElement is not null) return mapZoomMenuElement;
        var caption = model.Options.MapZoom == 0 ? $"Auto ({EffectiveMapZoom(session):P0})" : $"{model.Options.MapZoom}%";
        var items = new List<MenuFlyoutItemBase>
        {
            MenuItem("Auto — 地図とチャットに合わせる",
                () => SetMapOptions(model.Options with { MapZoom = 0 })),
            MenuSeparator()
        };
        foreach (var zoom in new[] { 50, 75, 100, 125, 150, 175, 200, 250, 300, 400 })
        {
            var value = zoom;
            items.Add(MenuItem($"{value}%", () => SetMapOptions(model.Options with { MapZoom = value })));
        }
        items.Add(MenuSeparator());
        items.Add(MenuItem("倍率を入力（設定）…", OpenSettings));
        mapZoomMenuInputs = inputs;
        return mapZoomMenuElement = MenuFlyout(Button(caption).Height(32).AutomationName($"地図の倍率: {caption}")
            .AutomationId("map-zoom-menu"), items.ToArray());
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
            if (index is 6 or 8) return (Element)Canvas().Width((width - 12) / 3).Height(36)
                .IsHitTestVisible(false).AccessibilityHidden();
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
