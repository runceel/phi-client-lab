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
                    Title = "PHI Client — 地図", Width = 400, Height = 700,
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
        TextBlock(session.Map is null ? "地図を待っています。" :
            $"{(session.NorthFixed ? "北固定（上が北）" : "回転マップ（上が前）")} / 向き: " +
            (session.Map.Direction switch { 'E' => "東", 'S' => "南", 'W' => "西", _ => "北" }))
            .TextWrapping(Microsoft.UI.Xaml.TextWrapping.Wrap).Width(width).AutomationId("map-state"),
        HStack(6,
            Button("−", () => SetMapOptions(model.Options with { MapZoom = Math.Max(50, model.Options.MapZoom - 25) }))
                .AutomationName("地図を縮小").AutomationId("map-zoom-out").IsEnabled(model.Options.MapZoom > 50),
            Button($"{model.Options.MapZoom}%", () => SetMapOptions(model.Options with { MapZoom = 100 }))
                .AutomationName($"地図の倍率 {model.Options.MapZoom}%。押すと100%に戻します。")
                .AutomationId("map-zoom-reset").ToolTip("100%に戻す"),
            Button("＋", () => SetMapOptions(model.Options with { MapZoom = Math.Min(400, model.Options.MapZoom + 25) }))
                .AutomationName("地図を拡大").AutomationId("map-zoom-in").IsEnabled(model.Options.MapZoom < 400)),
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
        }),
        CheckBox(model.Options.ShowMovementPad,
            value => SetMapOptions(model.Options with { ShowMovementPad = value }), "操作パッドを表示")
            .AutomationId("show-movement-pad"),
        model.Options.ShowMovementPad ? MovementPad(session, width) : Empty(),
        GameViews.Status(session, width),
        session.Eagle is null ? Empty() : Button(session.ShowEagle ? "周辺地図に戻る" : "EagleEyeを表示", () =>
            session.Dispatch(new GameMessage.EagleToggled())),
        Expander("地図情報・保存", VStack(8,
            GameViews.Compass(session),
            TextBlock("周囲のキャラクター: " + string.Join(", ", MapLabels.NameList(session.Map?.Actors ?? [])))
                .TextWrapping(Microsoft.UI.Xaml.TextWrapping.Wrap).AutomationId("map-character-names"),
            TextBlock(model.GraphicsNotice).TextWrapping(Microsoft.UI.Xaml.TextWrapping.Wrap).FontSize(12)
                .AutomationId("graphics-notice"),
            WrappedMapButton("表示中の地図をPNGに保存", () => SaveMapImage(session), Math.Max(120, width - 48))
                .IsEnabled(!busy && session.Map is not null).AutomationId("save-map-image"),
            WrappedMapButton("地図画像の保存先を開く", OpenMapImageFolder, Math.Max(120, width - 48))
                .AutomationId("open-map-images"),
            TextBlock("地図のクリックで移動、右クリックで攻撃します。\n簡易表示: ○はキャラクター、!は看板、*はアイテムです。")
                .TextWrapping(Microsoft.UI.Xaml.TextWrapping.Wrap).FontSize(12)).Width(Math.Max(120, width - 48)))
            .HorizontalAlignment(Microsoft.UI.Xaml.HorizontalAlignment.Stretch).AutomationId("map-details")
    ).Width(width).IsEnabled(!busy && !dialogOpen && !closingApplication && model.SettingsEditor is null);

    private double MapViewportHeight(GameSession session, double width)
    {
        // Recorded terrain uses the available viewport to select its surrounding ring.
        if (!session.ShowEagle && model.Options.Automap && session.NorthFixed &&
            session.Map is { Size: 7, Position: not null })
            return width;
        var dim = session.ShowEagle && session.Eagle is { } eagle
            ? EagleBoard.Build(eagle).Dim : session.Map?.Size ?? 7;
        var contentHeight = session.GraphicsImage is not null ? session.GraphicsHeight :
            dim * 32 * model.Options.MapZoom / 100.0;
        return Math.Min(width, contentHeight);
    }

    private Element MovementPad(GameSession session, double width)
    {
        var arrows = new[] { "↖", "↑", "↗", "←", "攻撃", "→", "↙", "↓", "↘" };
        var directions = session.NorthFixed
            ? new[] { "北西", "北", "北東", "西", "", "東", "南西", "南", "南東" }
            : new[] { "左前", "前", "右前", "左", "", "右", "左後ろ", "後ろ", "右後ろ" };
        var cells = Enumerable.Range(0, 9).Select(index =>
        {
            var x = index % 3 - 1;
            var y = index / 3 - 1;
            var name = index == 4 ? "攻撃" : directions[index] + "へ移動";
            return (Element)Button(arrows[index], () => Execute(session, GameInput.MapPad(x, y, session.NorthFixed)))
                .Width((width - 12) / 3).Height(36).ToolTip(name).AutomationName(name)
                .AutomationId($"movement-pad-{index / 3}-{index % 3}");
        }).ToArray();
        return VStack(4,
            VStack(4, HStack(6, cells[..3]), HStack(6, cells[3..6]), HStack(6, cells[6..])),
            HStack(6, Button("左回転", () => Execute(session, GameInput.Command("turn l"))),
                Button("右回転", () => Execute(session, GameInput.Command("turn r")))))
            .AutomationId("movement-pad");
    }

    private static ButtonElement WrappedMapButton(string text, Action action, double width) =>
        Button(text, action).Width(width).AutomationName(text).OnMount(element =>
            ((Microsoft.UI.Xaml.Controls.Button)element).Content = new Microsoft.UI.Xaml.Controls.TextBlock
            { Text = text, TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap });
}
