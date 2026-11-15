using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using PhiClient;
using PhiClient.Core;
using static Microsoft.UI.Reactor.Factories;

partial class App
{
    private Microsoft.UI.Xaml.Controls.ListView? listControl;
    private sealed record MenuInputs(GameSession? Session, bool Busy, bool Follow, bool FullKeys, bool ShowMap,
        System.Collections.Immutable.ImmutableArray<SavedCharacter> Characters, bool Connected, bool Transferring,
        bool PreparingMagic, bool MasterRunning, bool LogSaving, bool HasTransport, bool KnownWorld,
        bool HasEagle, bool NorthFixed, PhiClient.Core.MasterMenu? Master, bool MasterVisible,
        int ConnectedTabs, int DisconnectedTabs, bool Loud, bool AllTabSpeech, bool CheckingRelease, bool TraceRecording,
        bool ShowPad);
    private MenuInputs? menuInputs;
    private Element? menuElement;

    private Element AppMenu()
    {
        var session = active;
        var inputs = new MenuInputs(session, busy, followLog, fullKeys, showMap, model.Characters,
            session?.Connected == true, session?.Model.Transferring == true, session?.PreparingMagic == true,
            session?.Model.Master is not null, session?.Model.LogSaving == true, session?.Connection is not null,
            session?.World is not null and not "" and not "???", session?.Eagle is not null,
            session?.NorthFixed ?? true, model.MasterMenu, MasterMagic.Visible(session?.Stats, model.Options),
            sessions.Count(item => item.Connection is not null), sessions.Count(item => item.Connection is null),
            session?.Model.Loud == true, session?.Model.AllTabSpeech == true, model.CheckingRelease, model.TraceRecording,
            model.Options.ShowMovementPad);
        // Preserve open native flyouts when only map animation or unrelated log content changes.
        if (inputs == menuInputs && menuElement is not null) return menuElement;
        MenuFlyoutItemData Action(string label, InputAction action) =>
            MenuItem(label, () => { if (session is not null) Execute(session, action); })
                with { IsEnabled = session?.Connected == true && !session.Model.Transferring && session.Model.Master is null };
        MenuFlyoutItemData Key(string label, ConsoleKey key, bool alt = false, bool shift = false) =>
            Action(label, GameInput.Resolve(key, session?.NorthFixed ?? true, false,
                alt: alt, shift: shift) ?? throw new InvalidOperationException("Missing menu binding."));
        var menus = new[]
        {
            Menu("File",
                MenuItem("Connect / disconnect", ToggleConnection) with { IsEnabled = session is not null },
                MenuItem("Force disconnect  |  Pause", () => session?.Connection?.Dispose())
                    with { IsEnabled = session?.Connection is not null },
                MenuSeparator(),
                MenuItem("全タブを接続", ConnectAll) with { IsEnabled = inputs.DisconnectedTabs > 0 },
                MenuItem("全タブを切断", DisconnectAll) with { IsEnabled = inputs.ConnectedTabs > 0 },
                MenuItem("全タブを強制切断  |  Alt+Pause", ForceDisconnectAll)
                    with { IsEnabled = inputs.ConnectedTabs > 0 },
                MenuSeparator(),
                MenuItem("Close tab", () => { if (session is not null) CloseTab(session); })
                    with { IsEnabled = session is not null },
                MenuItem("設定…", OpenSettings) with { IsEnabled = !busy },
                MenuItem("超魔法のXMLを読み込む…", ChooseMasterMenu) with { IsEnabled = !busy },
                MenuItem("Phillyの登録を取り込む…", ImportPhillyProfiles) with { IsEnabled = loaded && !busy },
                MenuItem("Characters / registration", () => { showCharacters = !showCharacters; refresh(); })),
            Menu("Character",
                MenuItem("priv（内緒話）", () => { if (session is not null) TogglePriv(session); })
                    with { IsEnabled = session is not null },
                MenuItem("Next tab  |  Ctrl+Tab", () => CycleTab(1)),
                MenuItem("Previous tab  |  Ctrl+Shift+Tab", () => CycleTab(-1)),
                MenuSeparator(),
                MenuSubItem("Saved characters", characters.Select(character => (MenuFlyoutItemBase)
                    MenuItem(character.Name, () => _ = PerformAsync(() => OpenAsync(character)))).ToArray())),
            Menu("Operation",
                ToggleMenuItem("Follow latest log", followLog, value => { followLog = value; refresh(); }),
                MenuItem("ログを検索  |  Ctrl+F", () => { if (session is not null) OpenLogSearch(session); })
                    with { IsEnabled = session is not null },
                session is null ? MenuItem("ログ自動保存（キャラクター未選択）", () => { }) with { IsEnabled = false } :
                    ToggleMenuItem("このキャラクターのログを自動保存", session.Model.LogSaving,
                        value => ToggleLogSaving(session, value)),
                ToggleMenuItem("Full-key movement (789 UIO JKL M)", fullKeys, value => { fullKeys = value; refresh(); }),
                session is null ? MenuItem("大声で発言", () => { }) with { IsEnabled = false } :
                    ToggleMenuItem("大声で発言", session.Model.Loud,
                        value => session.Dispatch(new GameMessage.LoudChanged(value))),
                session is null ? MenuItem("全タブへ発言", () => { }) with { IsEnabled = false } :
                    ToggleMenuItem("全タブへ発言", session.Model.AllTabSpeech,
                        value => session.Dispatch(new GameMessage.AllTabSpeechChanged(value))),
                MenuItem("発言の前に付ける言葉…", () => { if (session is not null) ChooseTalkPrefix(session); })
                    with { IsEnabled = session is not null },
                MenuItem("Shout current line...", () => { if (session is not null) Submit(session, shout: true); })
                    with { IsEnabled = session?.Connected == true },
                MenuItem("ログ欄の内容をHTMLに保存", SaveLog) with { IsEnabled = session is not null },
                MenuItem("ログの保存先を開く", () => { if (session is not null) OpenLogFolder(session); })
                    with { IsEnabled = session is not null }),
            Menu("Action",
                Key("Get  |  Z / numpad +", ConsoleKey.Z),
                Key("Put  |  X / numpad -", ConsoleKey.X),
                Key("Use  |  C / numpad *", ConsoleKey.C),
                Key("Sort  |  V", ConsoleKey.V),
                Key("Floor items  |  F", ConsoleKey.F),
                MenuSeparator(),
                Key("Read sign  |  A", ConsoleKey.A),
                Key("Write sign  |  S", ConsoleKey.S),
                Key("Erase sign  |  D", ConsoleKey.D),
                Key("Watch board  |  B", ConsoleKey.B),
                Key("Pay...  |  P", ConsoleKey.P),
                MenuSeparator(),
                Key("Equip  |  Q / numpad /", ConsoleKey.Q),
                Key("Unequip  |  W", ConsoleKey.W),
                Key("Check and look  |  M / numpad 0", ConsoleKey.M),
                Action("Skills", GameInput.Command("skills")),
                Action("Attack  |  Space", GameInput.Command("hit"))),
            Menu("Magic",
                Key("Spells  |  R", ConsoleKey.R),
                Action("Cast  |  numpad 3 / numpad Enter", GameInput.Command("cast")),
                MenuSeparator(),
                Key("Wizard eye  |  Alt+W", ConsoleKey.W, true),
                Key("Wizard light  |  Alt+Shift+W", ConsoleKey.W, true, true),
                Key("Eagle eye  |  Alt+E", ConsoleKey.E, true),
                Key("Identify  |  Alt+I", ConsoleKey.I, true),
                Key("Party eye  |  Alt+P", ConsoleKey.P, true),
                Key("Wizard lock  |  Alt+L", ConsoleKey.L, true),
                Key("Unlock  |  Alt+U", ConsoleKey.U, true),
                MenuSubItem("More magic",
                    Key("List  |  Alt+Shift+L", ConsoleKey.L, true, true),
                    Key("Detect  |  Alt+D", ConsoleKey.D, true),
                    Key("Create  |  Alt+C", ConsoleKey.C, true),
                    Key("Destroy  |  Alt+Shift+D", ConsoleKey.D, true, true),
                    Key("Analyze  |  Alt+A", ConsoleKey.A, true),
                    Key("Forget  |  Alt+F", ConsoleKey.F, true),
                    Key("Charge spell  |  Alt+Shift+C", ConsoleKey.C, true, true),
                    Key("Search...  |  Alt+S", ConsoleKey.S, true),
                    Key("Appear...  |  Alt+Shift+A", ConsoleKey.A, true, true),
                    Key("Disappear...  |  Alt+Shift+U", ConsoleKey.U, true, true)),
                MenuSeparator(),
                Key("Call...  |  Alt+B", ConsoleKey.B, true),
                Key("Return...  |  Alt+R", ConsoleKey.R, true)),
            Menu("View",
                ToggleMenuItem("操作パッドを表示", model.Options.ShowMovementPad,
                    value => SetMapOptions(model.Options with { ShowMovementPad = value })),
                MenuItem("画像素材フォルダー…", ShowGraphicsSettings),
                MenuItem("現在の世界の記録地図を消す…", () => _ = PerformAsync(ClearAutomapAsync))
                    with { IsEnabled = session?.World is not null and not "" and not "???" },
                ToggleMenuItem("Map pane  |  Shift+F5", showMap, value => { showMap = value; refresh(); }),
                Key("Rotating  |  T", ConsoleKey.T),
                Key("North up  |  Shift+T", ConsoleKey.T, shift: true),
                Key("7 x 7  |  Shift+7", ConsoleKey.D7, shift: true),
                Key("5 x 5  |  Shift+5", ConsoleKey.D5, shift: true),
                MenuItem("Local map / EagleEye", () =>
                {
                    session?.Dispatch(new GameMessage.EagleToggled());
                    refresh();
                }) with { IsEnabled = session?.Eagle is not null },
                Action("Refresh", GameInput.Command("see"))),
            Menu("Help",
                MenuItem("Keyboard controls", ShowKeyboardHelp),
                MenuItem("wphiの利用者ガイド（ブラウザー）",
                    () => OpenLogLink(new Uri("https://s7k.github.io/wphi/guide/"))),
                MenuItem("窓の位置を元に戻す…", () => _ = PerformAsync(ResetWindowLayoutAsync)),
                MenuItem(model.CheckingRelease ? "公開リリースを確認しています…" : "新しい版を確認する…",
                    () => _ = PerformAsync(CheckReleaseAsync)) with { IsEnabled = !model.CheckingRelease },
                MenuItem(model.TraceRecording ? "時刻の記録を停止する" : "時刻の記録を開始する",
                    () => _ = PerformAsync(() => { ToggleDiagnosticTrace(); return Task.CompletedTask; })),
                MenuItem("バージョン情報", () => _ = PerformAsync(ShowVersionAsync)))
        };
        if (MasterMenu(session) is { } master) menus = [.. menus, master];
        menuInputs = inputs;
        return menuElement = DropDownButton("メニュー", MenuItems(menus.Select(menu =>
                (MenuFlyoutItemBase)MenuSubItem(menu.Title, menu.Items)).ToArray()))
            .HorizontalAlignment(HorizontalAlignment.Left).SubtleButton().MinWidth(48).Height(32).Padding(6, 4)
            .ToolTip("メニュー").AutomationName("メニュー").AutomationId("app-menu").OnMount(element =>
            {
                menuControl = (Microsoft.UI.Xaml.Controls.DropDownButton)element;
                menuControl.Content = new Microsoft.UI.Xaml.Controls.FontIcon { Glyph = "\uE700", FontSize = 16 };
            })
            .WithKey($"menu-{session?.Character.Key ?? "offline"}");
    }

    private Element GameView(GameSession session)
    {
        var selection = session.Selection;
        var choices = !selection.Active ? Empty() : VStack(4,
            TextBlock(selection.Waiting ? "Waiting for next page..." :
                "Selection: 1-9 choose; Space check; Enter send; Esc cancel").TextWrapping(TextWrapping.Wrap),
            ListView(selection.Cursor, index =>
                {
                    session.Dispatch(new GameMessage.SelectionMoved(index - selection.Cursor));
                }, selection.Items.Select((choice, index) => (Element)HStack(6,
                    CheckBox(selection.Checked.Contains(choice.Number), value =>
                    {
                        if (selection.Checked.Contains(choice.Number) == value) return;
                        session.Dispatch(new GameMessage.SelectionToggled(index));
                    }, ""),
                    TextBlock($"{(index >= selection.PageStart ? $"({index - selection.PageStart + 1}) " : "")}" +
                        (choice.Raw is { } raw ? ProtocolParser.PlainText(raw) : $"[{choice.Number}] {choice.Text}"))
                        .TextWrapping(TextWrapping.Wrap))).ToArray())
                .ItemClick(index => Choose(session, selection.Pick(index)))
                .MaxHeight(220).IsEnabled(!selection.Waiting).AutomationId("selection-list")
                .OnMount(element => listControl = (Microsoft.UI.Xaml.Controls.ListView)element),
            HStack(6,
                Button("Send selection", () => Choose(session, selection.Confirm())).IsEnabled(!selection.Waiting),
                Button("All", () => Send(session, "-")),
                Button("Cancel selection", () => Send(session, "."))));

        var log = Border(Empty())
            .OnMount(element =>
            {
                var presenter = new LogPresenter((rows, toInput) => CopyLog(session, rows, toInput),
                    OpenLogLink, ReportLogImageFailure);
                presenter.Attach();
                var viewport = presenter.CreateViewport(session.LogScroll, "world-log");
                var viewer = viewport.Viewer;
                presenter.Update(session.Model.LogEntries, model.Options, model.LogImages);
                ((Microsoft.UI.Xaml.Controls.Border)element).Child = viewport.Surface;
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(viewer, "世界ログ・ゲーム操作");
                Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(viewer,
                    "矢印・テンキーでゲーム操作。Tabでチャット入力へ。Shift+Tabで通常のUIフォーカス移動。");
                gameControl = viewer;
                viewer.Loaded += (_, _) =>
                {
                    if (!pendingGameFocus || active != session) return;
                    pendingGameFocus = false;
                    viewer.Focus(FocusState.Programmatic);
                };
                session.LogPresenter = presenter;
            }).MinHeight(80).Flex(grow: 1, basis: 0);

        var chat = FlexColumn(
            session.Model.Master is null ? Empty() :
                HStack(8, TextBlock("超魔法の手順を実行しています…").AutomationId("master-status"),
                    Button("残りの手順を中止", () => CancelMaster(session)).AutomationId("master-cancel")).Flex(shrink: 0),
            Caption($"{session.World} {session.Area}").AutomationId("world-location").Flex(shrink: 0),
            log, LogSearchView(session).Flex(shrink: 0), choices.Flex(shrink: 0), SignEditor(session).Flex(shrink: 0),
            FlexRow(InputBox(session.Input, value =>
                {
                    if (session.Input == value) return;
                    session.Dispatch(new GameMessage.InputChanged(value));
                }).PlaceholderText("発言またはPHIコマンド").AutomationName("発言またはPHIコマンド")
                .AcceptsReturn(true).Height(Math.Clamp(40 + 20 * session.Input.Count(c => c == '\n'), 40, 80))
                .AutomationId("command-input")
                .OnGotFocus((_, _) => Dispatch(new ClientMessage.ChatFocusChanged(true)))
                .OnLostFocus((_, _) => Dispatch(new ClientMessage.ChatFocusChanged(false)))
                .OnMount(element =>
                {
                    var input = (Microsoft.UI.Xaml.Controls.TextBox)element;
                    AttachInput(input, session);
                    AttachTagInput(input, session);
                })
                .OnPreviewKeyDown((_, args) => OnInputKeyDown(session, args))
                .OnKeyUp((_, args) =>
                {
                    if (args.Key == Windows.System.VirtualKey.Enter) committingComposition = false;
                }).Flex(grow: 1, basis: 0),
                Button("送信", () => Submit(session)).IsEnabled(session.Connected && session.Model.Master is null)
                    .AutomationId("send-command").Flex(shrink: 0),
                model.GameGuideVisible ? MenuFlyout(Button("送信方法"),
                    MenuItem("パーティーへ（Ctrl+Enter）", () => Submit(session, party: true)),
                    MenuItem("タグを入力…", () => ShowInputTags(session)),
                    MenuItem("改行: Shift+Enter", () => { }) with { IsEnabled = false }).Flex(shrink: 0) : Empty()
            ).Flex(shrink: 0)
        ).Flex(grow: 1, basis: 0);

        var integratedMap = showMap && model.Options.SingleWindow && !showCharacters;
        var compact = windowWidth < 760;
        var replaceChatWithPriv = session.Model.Priv.Visible && windowWidth < 1100;
        var mapWidth = IntegratedMapWidth;
        var map = MapPane(session, mapWidth - 16);
        var mapHeight = Math.Max(120, model.GameBodyHeight);
        var gameBody = compact
            ? replaceChatWithPriv ? (Element)PrivView(session, fill: true).Flex(grow: 1, basis: 0) : FlexColumn(
                integratedMap ? (ScrollViewer(map) with
                {
                    VerticalScrollBarVisibility = Microsoft.UI.Xaml.Controls.ScrollBarVisibility.Visible
                }).AutomationId("compact-map-scroll").Height(CompactMapHeight).Flex(shrink: 0) : Empty(), chat)
            : FlexRow(integratedMap
                    ? ScrollViewer(map).Width(mapWidth).Height(mapHeight).Flex(shrink: 0) : Empty(),
                integratedMap ? MapSplitter(mapHeight) : Empty(),
                replaceChatWithPriv ? PrivView(session, fill: true).Flex(grow: 1, basis: 0) : chat,
                !replaceChatWithPriv ? PrivView(session).Flex(shrink: 0) : Empty());
        var connectionButton = Button(session.Connecting ? "接続しています…" : session.Connected ? "切断" : "再接続", ToggleConnection)
            .IsEnabled(!session.Connecting && !session.Closing).AutomationId("connection-toggle");
        var movementButton = Button("キーボード操作に戻る", () => gameControl?.Focus(FocusState.Programmatic))
            .AutomationId("movement-mode");
        var charactersButton = Button("キャラクター一覧", () => { showCharacters = !showCharacters; }).AutomationId("toggle-characters");
        var closeButton = Button("タブを閉じる", () => CloseTab(session)).IsEnabled(!session.Connecting && !session.Closing)
            .AutomationId("close-tab");
        var toolbar = compact ? (Element)VStack(6, HStack(6, connectionButton, movementButton), HStack(6, charactersButton, closeButton))
            : HStack(6, connectionButton, movementButton, charactersButton, closeButton);
        return FlexColumn(
            model.GameGuideVisible ? Expander("接続・タブ操作", toolbar).Flex(shrink: 0) : Empty(),
            model.GameGuideVisible || session.Model.Transferring || session.PreparingMagic ?
            Caption(session.Model.Transferring ? "世界移動中です。操作は送信しません。「切断」で移動を中止できます。" :
                session.PreparingMagic ? "Preparing magic: waiting for equipment response; other input is paused." :
                model.ChatFocused ? "チャット入力中 / Enter: 送信 / Tab・Esc: ログでゲーム操作 / Shift+Tab: UI移動" :
                model.NativeInputFocused ? "文字入力・UI操作中 / 入力欄やメニューの操作が優先されます" :
                model.GameGuideVisible ? "ログでゲーム操作: 矢印・テンキー / Tab: チャット / Shift+Tab: UI移動 / Ctrl+Tab: 次のキャラクター" :
                "ゲーム操作 / Tab: チャット")
                .AutomationId("input-mode").Flex(shrink: 0) : Empty(),
            gameBody.Flex(grow: 1, basis: 0).OnSizeChanged((_, args) =>
            {
                if (args.NewSize.Height > 0 && Math.Abs(model.GameBodyHeight - args.NewSize.Height) > 1)
                    Dispatch(new ClientMessage.GameBodyHeightChanged(args.NewSize.Height));
            })
        ).Flex(grow: 1, basis: 0).FlexPadding(4);
    }

    private Element GraphicMap(GameSession session)
    {
        var bitmap = session.GraphicsImage;
        if (bitmap is null) return GameViews.Map(session, (x, y) => MapClick(session, x, y), () => Send(session, "hit"),
            DisplayMap(session), EffectiveMapZoom(session))
                .OnMount(element => AttachMapSurface((FrameworkElement)element, session));
        // The preview's Image descriptor requires a URI even when OnMount supplies a live bitmap.
        var picture = Image("ms-appx:///Assets/Square44x44Logo.scale-200.png")
            .AccessibilityHidden()
            .Width(session.GraphicsWidth).Height(session.GraphicsHeight)
            .WithKey($"pixels-{bitmap.PixelWidth}-{bitmap.PixelHeight}")
            .OnMount(element => MountMapImage((Microsoft.UI.Xaml.Controls.Image)element, bitmap));
        Element Label(MapLabel label, int dx, int dy, string color) => TextBlock(label.Text)
            .AccessibilityHidden()
            .FontFamily("Segoe UI").FontSize((label.Missing ? 14 : 9) * session.GraphicsZoom)
            .FontWeight(Microsoft.UI.Text.FontWeights.Bold)
            .Foreground(BrushHelper.Parse(color)).Width(session.GraphicsWidth).TextAlignment(TextAlignment.Center)
            .IsHitTestVisible(false).Canvas(label.X - session.GraphicsWidth / 2.0 + dx * session.GraphicsZoom,
                label.Y - (label.Missing ? 9 * session.GraphicsZoom : 0) + dy * session.GraphicsZoom);
        var layers = new List<Element> { picture.Canvas(0, 0) };
        foreach (var label in session.GraphicsLabels)
        {
            if (!label.Missing)
                foreach (var (dx, dy) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) })
                    layers.Add(Label(label, dx, dy, "#000000"));
            layers.Add(Label(label, 0, 0, label.Missing ? "#FFFF00" : "#FFFFFF"));
        }
        if (model.Options.MapEffects && !session.ShowEagle)
            layers.Add(MapEffectLayer(session).Canvas(0, 0));
        return Canvas(layers.ToArray()).Width(session.GraphicsWidth).Height(session.GraphicsHeight)
            .WithKey($"map-surface-{session.GraphicsWidth}-{session.GraphicsHeight}")
            .AutomationId("sprite-map").AutomationName("Character sprites and map chips: " +
                string.Join(", ", MapLabels.NameList(session.Map?.Actors ?? [])))
            .OnMount(element =>
            {
                var surface = (FrameworkElement)element;
                AttachMapSurface(surface, session);
                surface.Clip = new Microsoft.UI.Xaml.Media.RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0,
                    session.GraphicsWidth, session.GraphicsHeight) };
            })
            .OnPointerPressed((sender, args) =>
            {
                if (session.ShowEagle || sender is not Microsoft.UI.Xaml.UIElement view) return;
                var point = args.GetCurrentPoint(view);
                if (point.Properties.IsRightButtonPressed) Send(session, "hit");
                else if (point.Properties.IsLeftButtonPressed &&
                    MapLabels.Hit(point.Position.X, point.Position.Y, session.GraphicsDim, session.GraphicsZoom) is { } hit)
                    MapClick(session, hit.X, hit.Y);
                else return;
                args.Handled = true;
            });
    }

    private Element SignEditor(GameSession session) => !session.Editing ? Empty() : VStack(6,
        InputBox(session.BoardText, text =>
            {
                if (session.BoardText == text) return;
                session.Dispatch(new GameMessage.BoardChanged(text));
            }, header: "Sign text").AcceptsReturn(true).Height(120).AutomationId("sign-text")
                .OnMount(element => AttachTagInput((Microsoft.UI.Xaml.Controls.TextBox)element, session, true)),
        HStack(6,
            Button("Send sign", () => SendSign(session)),
            Button("Cancel sign", () => Send(session, ".!")),
            Button("タグ…", () => ShowInputTags(session, true)).AutomationId("sign-tags")));
}
