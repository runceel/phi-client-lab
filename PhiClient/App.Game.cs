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
        int ConnectedTabs, int DisconnectedTabs, bool Loud, bool AllTabSpeech);
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
            session?.Model.Loud == true, session?.Model.AllTabSpeech == true);
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
                MenuItem("Graphics folder...", ShowGraphicsSettings),
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
                MenuItem("About", () => _ = PerformAsync(async () => await SessionDialogAsync("PHI Client",
                    "C# / WinUI 3 with official Microsoft.UI.Reactor preview. " +
                    "Newly drawn schematic maps; no wphi image assets are bundled. " +
                    "PHI uses unencrypted TCP. This is not yet a complete wphi replacement."))))
        };
        if (MasterMenu(session) is { } master) menus = [.. menus, master];
        menuInputs = inputs;
        return menuElement = MenuBar(menus).WithKey($"menu-{session?.Character.Key ?? "offline"}");
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

        var log = ScrollViewer(Empty())
            .OnMount(element =>
            {
                var viewer = (Microsoft.UI.Xaml.Controls.ScrollViewer)element;
                var presenter = new LogPresenter((rows, toInput) => CopyLog(session, rows, toInput), OpenLogLink);
                presenter.Attach();
                presenter.Update(session.Model.LogEntries, model.Options);
                viewer.Content = presenter.Control;
                viewer.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
                session.LogPresenter = presenter;
                var scrollQueued = false;
                viewer.LayoutUpdated += (_, _) =>
                {
                    if (scrollQueued || !followLog || active != session || !viewer.IsLoaded ||
                        viewer.ScrollableHeight - viewer.VerticalOffset <= 1) return;
                    scrollQueued = true;
                    dispatcher?.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
                    {
                        scrollQueued = false;
                        if (followLog && active == session && viewer.IsLoaded && viewer.XamlRoot is not null)
                            viewer.ChangeView(null, viewer.ScrollableHeight, null);
                    });
                };
            }).Flex(grow: 1, basis: 0);

        var chat = FlexColumn(
            session.Model.Master is null ? Empty() :
                HStack(8, TextBlock("超魔法の手順を実行しています…").AutomationId("master-status"),
                    Button("残りの手順を中止", () => CancelMaster(session)).AutomationId("master-cancel")).Flex(shrink: 0),
            HStack(8, SubHeading("世界からのログ"),
                Caption($"{session.World} {session.Area}").AutomationId("world-location")).Flex(shrink: 0),
            log, choices.Flex(shrink: 0), SignEditor(session).Flex(shrink: 0),
            TextBox(session.Input, value =>
                {
                    if (session.Input == value) return;
                    session.Dispatch(new GameMessage.InputChanged(value));
                }, header: "Message or raw PHI command")
                .AcceptsReturn(true).Height(82).AutomationId("command-input")
                .OnMount(element => AttachInput((Microsoft.UI.Xaml.Controls.TextBox)element, session))
                .OnPreviewKeyDown((_, args) => OnInputKeyDown(session, args))
                .OnKeyUp((_, args) =>
                {
                    if (args.Key == Windows.System.VirtualKey.Enter) committingComposition = false;
                }).Flex(shrink: 0),
            HStack(8,
                Button("Send", () => Submit(session)).IsEnabled(session.Connected && session.Model.Master is null).AutomationId("send-command"),
                Button("Party", () => Submit(session, party: true)).IsEnabled(session.Connected),
                Caption("Enter: current line  |  Shift+Enter: newline  |  Ctrl+Enter: party"))
                .Flex(shrink: 0)
        ).Flex(grow: 1, basis: 0);

        var map = VStack(8,
            SubHeading(session.ShowEagle && session.Eagle is not null ? "EagleEye" :
                $"Local map ({session.Map?.Size ?? 7} x {session.Map?.Size ?? 7})").AutomationId("local-map"),
            HStack(8, GameViews.Compass(session), TextBlock(session.Map is null ? "地図を待っています。" :
                session.NorthFixed ? "北固定（上が北）" : "回転マップ（方位は矢印で確認）")
                .TextWrapping(TextWrapping.Wrap)),
            (ScrollViewer(GraphicMap(session)) with
            {
                HorizontalScrollBarVisibility = Microsoft.UI.Xaml.Controls.ScrollBarVisibility.Auto,
                HorizontalScrollMode = Microsoft.UI.Xaml.Controls.ScrollMode.Enabled
            }).AutomationId("map-viewport").MaxWidth(344).Height(344)
                .OnMount(element =>
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
            TextBlock("Characters: " + string.Join(", ", MapLabels.NameList(session.Map?.Actors ?? [])))
                .TextWrapping(TextWrapping.Wrap).Width(336).AutomationId("map-character-names"),
            TextBlock(model.GraphicsNotice).TextWrapping(TextWrapping.Wrap).FontSize(12).Width(336)
                .AutomationId("graphics-notice"),
            TextBlock(session.Map is null ? "Waiting for a map." :
                $"Facing {session.Map.Direction} | {(session.NorthFixed ? "North up" : "Rotating")}")
                .AutomationId("map-state"),
            session.Eagle is null ? Empty() : Button(session.ShowEagle ? "Back to local map" : "Show EagleEye", () =>
            {
                session.Dispatch(new GameMessage.EagleToggled());
            }),
            GameViews.Status(session),
            TextBlock("Click: move toward tile. Right-click: attack.\nWithout graphics: circle = character, ! = sign, * = item.")
                .TextWrapping(TextWrapping.Wrap).FontSize(12).Width(336),
            HStack(6,
                Button("Turn left", () => Send(session, "turn l")),
                Button("Forward", () => Move(session, "N")),
                Button("Turn right", () => Send(session, "turn r"))),
            HStack(6,
                Button("Left", () => Move(session, "W")),
                Button("Attack", () => Send(session, "hit")),
                Button("Right", () => Move(session, "E")),
                Button("Back", () => Move(session, "S")))
        ).Width(344);

        return FlexColumn(
            HStack(6,
                Button(session.Connecting ? "Connecting..." : session.Connected ? "Disconnect" : "Reconnect", ToggleConnection)
                    .IsEnabled(!session.Connecting && !session.Closing).AutomationId("connection-toggle"),
                Button("Keyboard movement mode", () => gameControl?.Focus(FocusState.Programmatic))
                    .OnMount(element => gameControl = (Microsoft.UI.Xaml.Controls.Button)element)
                    .AutomationId("movement-mode"),
                Button("Close tab", () => CloseTab(session)).IsEnabled(!session.Connecting && !session.Closing)
                    .AutomationId("close-tab")).Flex(shrink: 0),
            Caption(session.Model.Transferring ? "世界移動中です。操作は送信しません。「切断」で移動を中止できます。" :
                session.PreparingMagic ? "Preparing magic: waiting for equipment response; other input is paused." :
                $"Esc: movement  |  Tab: input  |  Ctrl+Tab: next character  |  {(fullKeys ? "Full-key movement on" : "Full-key movement off")}")
                .AutomationId("input-mode").Flex(shrink: 0),
            FlexRow(showMap && !showCharacters ? ScrollViewer(map).Width(360).Flex(shrink: 0) : Empty(), chat,
                PrivView(session).Flex(shrink: 0))
                .Flex(grow: 1, basis: 0)
        ).Flex(grow: 1, basis: 0).FlexPadding(12, 8);
    }

    private Element GraphicMap(GameSession session)
    {
        var bitmap = session.GraphicsImage;
        if (bitmap is null) return GameViews.Map(session, (x, y) => MapClick(session, x, y), () => Send(session, "hit"),
            DisplayMap(session), model.Options.MapZoom / 100.0);
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
        return Canvas(layers.ToArray()).Width(session.GraphicsWidth).Height(session.GraphicsHeight)
            .WithKey($"map-surface-{session.GraphicsWidth}-{session.GraphicsHeight}")
            .AutomationId("sprite-map").AutomationName("Character sprites and map chips: " +
                string.Join(", ", MapLabels.NameList(session.Map?.Actors ?? [])))
            .OnMount(element => ((Microsoft.UI.Xaml.UIElement)element).Clip =
                new Microsoft.UI.Xaml.Media.RectangleGeometry { Rect = new Windows.Foundation.Rect(0, 0,
                    session.GraphicsWidth, session.GraphicsHeight) })
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
        TextBox(session.BoardText, text =>
            {
                if (session.BoardText == text) return;
                session.Dispatch(new GameMessage.BoardChanged(text));
            }, header: "Sign text").AcceptsReturn(true).Height(120).AutomationId("sign-text"),
        HStack(6,
            Button("Send sign", () => SendSign(session)),
            Button("Cancel sign", () => Send(session, ".!"))));
}
