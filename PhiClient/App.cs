using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text.Json;
using DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;
using Microsoft.UI.Input;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PhiClient;
using PhiClient.Core;
using Windows.System;
using Windows.UI.Core;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<App>("PHI Client", width: 1200, height: 850);

class App : Component
{
    private readonly CharacterStore store = new();
    private readonly List<GameSession> sessions = [];
    private Microsoft.UI.Xaml.Controls.TextBox? inputControl;
    private Microsoft.UI.Xaml.Controls.Button? gameControl;
    private List<SavedCharacter> characters = [];
    private GameSession? active;
    private DispatcherQueue? dispatcher;
    private Action refresh = () => { };
    private string name = "";
    private string host = "";
    private string port = "2000";
    private string id = "";
    private string password = "";
    private string image = "0";
    private string email = "";
    private string notice = "Load or register a character to begin. IDs are stored in Windows Credential Manager.";
    private string? pendingDelete;
    private bool creating;
    private bool loaded;
    private bool busy;
    private bool disposed;

    public override Element Render()
    {
        var (_, update) = UseReducer(0);
        refresh = () => update(value => value + 1);
        UseEffect(() =>
        {
            dispatcher = DispatcherQueue.GetForCurrentThread();
            _ = PerformAsync(LoadAsync);
            return () =>
            {
                disposed = true;
                foreach (var session in sessions)
                    session.Connection?.Dispose();
            };
        }, Array.Empty<object>());

        var sidebar = ScrollViewer(VStack(10,
            Heading("Characters"),
            Caption("Saved profiles"),
            VStack(6, characters.Select(CharacterRow).ToArray()),
            SubHeading(creating ? "Create a new character" : "Register an existing ID"),
            TextBox(name, value => Set(ref name, value), header: "Display name").AutomationId("character-name"),
            TextBox(host, value => Set(ref host, value), header: "Server hostname / IP").AutomationId("server-host"),
            TextBox(port, value => Set(ref port, value), header: "TCP port").AutomationId("server-port"),
            creating
                ? VStack(8,
                    PasswordBox(password, value => Set(ref password, value), "Six-character password")
                        .AutomationName("Six-character password").AutomationId("new-password"),
                    TextBox(image, value => Set(ref image, value), header: "Initial image number").AutomationId("initial-image"),
                    TextBox(email, value => Set(ref email, value), header: "Email").AutomationId("registration-email"),
                    Caption("Creates a character on the specified server."))
                : PasswordBox(id, value => Set(ref id, value), "Character ID")
                    .AutomationName("Character ID").AutomationId("character-id"),
            Button(creating ? "Create on server and save" : "Save character",
                () => _ = PerformAsync(SaveCharacterAsync)).IsEnabled(loaded && !busy).AutomationId("save-character"),
            Button(creating ? "Use an existing ID instead" : "Create a new character instead", () =>
            {
                creating = !creating;
                password = "";
                refresh();
            }).IsEnabled(!busy),
            TextBlock("PHI sends credentials and game data over unencrypted TCP. Use only a trusted server.")
                .TextWrapping(TextWrapping.Wrap).Width(300),
            Button("Open settings folder", () => _ = PerformAsync(async () =>
            {
                Directory.CreateDirectory(store.Folder);
                var folder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(store.Folder);
                if (!await Launcher.LaunchFolderAsync(folder))
                    throw new IOException("Windows could not open the settings folder.");
            }))
        ).Width(300)).Width(300).Flex(shrink: 0);

        var content = active is null
            ? VStack(16, Heading("PHI Client"),
                TextBlock("A native WinUI client using Microsoft's experimental Reactor framework."),
                TextBlock("Save a character on the left, then choose Connect."),
                TextBlock("Map graphics use a built-in symbolic view; no upstream image assets are bundled."),
                TextBlock("Existing character IDs can also be found on the first line of your .phirc file."))
                .Padding(24).Flex(grow: 1, basis: 0)
            : GameView(active);

        return FlexColumn(
            TitleBar("PHI Client").Flex(shrink: 0),
            TextBlock(notice).TextWrapping(TextWrapping.Wrap).Padding(16, 8).AutomationId("notice").Flex(shrink: 0),
            FlexRow(sidebar, content).Flex(grow: 1, basis: 0).FlexPadding(16)
        ).Backdrop(BackdropKind.Mica);
    }

    private Element CharacterRow(SavedCharacter character) =>
        VStack(4,
            TextBlock(character.Name),
            Caption($"{character.Host}:{character.Port}"),
            HStack(6,
                Button(sessions.Any(s => s.Character.Key == character.Key) ? "Show" : "Connect", () =>
                {
                    _ = PerformAsync(() => OpenAsync(character));
                }).IsEnabled(!busy).AutomationId($"connect-{character.Key}"),
                Button(pendingDelete == character.Key ? "Confirm delete" : "Delete", () =>
                {
                    if (pendingDelete != character.Key)
                    {
                        pendingDelete = character.Key;
                        refresh();
                        return;
                    }
                    _ = PerformAsync(async () =>
                    {
                        if (sessions.Any(s => s.Character.Key == character.Key))
                            throw new InvalidOperationException("Close this character's tab before deleting it.");
                        var remaining = characters.Where(c => c.Key != character.Key).ToList();
                        await store.RemoveAsync(character, remaining);
                        characters = remaining;
                        pendingDelete = null;
                        notice = "Character removed.";
                    });
                }).IsEnabled(!busy).AutomationId($"delete-{character.Key}")
            ));

    private Element GameView(GameSession session)
    {
        var tabs = HStack(6, sessions.Select(s => (Element)Button(
            (s == active ? "* " : "") + s.Character.Name, () =>
            {
                active = s;
                refresh();
            }).WithKey(s.Character.Key).AutomationId($"tab-{s.Character.Key}")).ToArray());
        var controls = VStack(8,
            TextBlock(session.Status).TextWrapping(TextWrapping.Wrap).AutomationId("game-status"),
            HStack(8,
                Button(session.Connecting ? "Connecting..." : session.Connected ? "Disconnect" : "Reconnect", () =>
                {
                    if (session.Connected)
                        _ = SessionOperationAsync(session, () => DisconnectAsync(session));
                    else
                        _ = PerformAsync(() => StartAsync(session));
                }).IsEnabled(!session.Connecting && !session.Closing).AutomationId("connection-toggle"),
                Button("Close tab", () => _ = SessionOperationAsync(session, async () =>
                {
                    await DisconnectAsync(session);
                    sessions.Remove(session);
                    active = sessions.LastOrDefault();
                })).IsEnabled(!session.Connecting && !session.Closing).AutomationId("close-tab"),
                Button("Refresh view", () => Send(session, "see")),
                Button(session.NorthUp ? "Use rotating map" : "Use north-up map", () =>
                    Send(session, session.NorthUp ? "#ex-map style=turn" : "#ex-map style=solid")))
        );
        var map = VStack(8,
            SubHeading("Local map (7 x 7)").AutomationId("local-map"),
            MapView(session),
            TextBlock("Tiles: # wall, . floor, ? unknown; @ you, o other characters.")
                .TextWrapping(TextWrapping.Wrap).Width(340).FontSize(12),
            TextBlock(session.Map is null ? "Waiting for a map." :
                $"Facing {session.Map.Direction} | {(session.NorthUp ? "North up" : "Rotating")}")
                .AutomationId("map-state"),
            TextBlock(session.Map is null ? "" : string.Join(", ", session.Map.Actors.Select(a => a.Name)))
                .TextWrapping(TextWrapping.Wrap),
            HStack(6,
                Button("Turn left", () => Send(session, "turn l")),
                Button("North / Forward", () => Move(session, "N")),
                Button("Turn right", () => Send(session, "turn r"))),
            HStack(6,
                Button("West / Left", () => Move(session, "W")),
                Button("Attack", () => Send(session, "hit")),
                Button("East / Right", () => Move(session, "E"))),
            Button("South / Back", () => Move(session, "S")),
            HStack(6,
                Button("Get", () => Send(session, "get")),
                Button("Put", () => Send(session, "put")),
                Button("Use", () => Send(session, "use"))),
            HStack(6,
                Button("Items", () => Send(session, "items")),
                Button("Equip", () => Send(session, "equip")),
                Button("Spells", () => Send(session, "spells"))),
            HStack(6,
                Button("Read sign", () => Send(session, "read")),
                Button("Write sign", () =>
                {
                    session.Editing = true;
                    Send(session, "write");
                })),
            Button("Keyboard movement mode", () => gameControl?.Focus(FocusState.Programmatic))
                .OnMount(element => gameControl = (Microsoft.UI.Xaml.Controls.Button)element)
                .AutomationId("movement-mode"),
            TextBlock("Esc: movement mode; Tab: command field.\nArrows / numpad: move; Z/X/C: get/put/use; Space: hit.")
                .TextWrapping(TextWrapping.Wrap).Width(340).FontSize(12)
        ).Width(340).Flex(shrink: 0);

        var log = ScrollViewer(TextBlock(string.Join('\n', session.Log))
            .TextWrapping(TextWrapping.Wrap).FontFamily("Consolas").AutomationId("game-log"))
            .OnMount(element =>
            {
                if (element is ScrollViewer viewer)
                    viewer.ChangeView(null, viewer.ScrollableHeight, null);
            }).Flex(grow: 1, basis: 0).WithKey($"log-{session.Character.Key}-{session.Log.Count}-{session.LogCharacters}");

        var choices = session.Choices.Count == 0 ? Empty() :
            ScrollViewer(VStack(4,
                session.Choices.Select(choice => (Element)Button($"[{choice.Number}] {choice.Text}",
                    () => Send(session, choice.Number))).Append(
                    Button("Cancel selection", () => Send(session, "."))).ToArray())).MaxHeight(180);

        var editor = !session.Editing ? Empty() : VStack(6,
            TextBox(session.BoardText, text =>
                {
                    if (session.BoardText == text) return;
                    session.BoardText = text;
                    refresh();
                }, header: "Sign text")
                .AcceptsReturn(true).Height(120).AutomationId("sign-text"),
            HStack(6,
                Button("Send sign", () =>
                {
                    var lines = session.BoardText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
                    if (lines.Any(line => line is "." or ".!" || line.StartsWith('#')))
                    {
                        notice = "Sign lines must not be protocol commands, '.' or '.!'.";
                        refresh();
                        return;
                    }
                    Send(session, lines.Select(line => line.Length == 0 ? " " : line).Append(".").ToArray());
                }),
                Button("Cancel sign", () => Send(session, ".!"))));

        var chat = FlexColumn(
            SubHeading("Server log"),
            log,
            choices,
            editor,
            HStack(8,
                TextBox(session.Input, value =>
                    {
                        if (session.Input == value) return;
                        session.Input = value;
                        refresh();
                    }, "Message or raw PHI command")
                    .OnMount(element => inputControl = (Microsoft.UI.Xaml.Controls.TextBox)element)
                    .AutomationName("Message or raw PHI command").Width(330).AutomationId("command-input")
                    .OnKeyDown((_, args) =>
                    {
                        if (args.Key == VirtualKey.Enter)
                        {
                            Submit(session);
                            args.Handled = true;
                        }
                        else if (args.Key == VirtualKey.Escape)
                        {
                            gameControl?.Focus(FocusState.Programmatic);
                            args.Handled = true;
                        }
                    }),
                Button("Send", () => Submit(session)).IsEnabled(session.Connected).AutomationId("send-command"))
        ).Flex(grow: 1, basis: 0);

        return FlexColumn(
            ScrollViewer(tabs).Flex(shrink: 0),
            controls.Flex(shrink: 0),
            FlexRow(ScrollViewer(map), chat).Flex(grow: 1, basis: 0)
        ).Flex(grow: 1, basis: 0).FlexPadding(16, 0).WithKey(session.Character.Key)
            .OnKeyDown((_, args) =>
            {
                var focused = Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(
                    gameControl?.XamlRoot);
                if (focused is Microsoft.UI.Xaml.Controls.TextBox or Microsoft.UI.Xaml.Controls.PasswordBox)
                    return;
                if (args.Key == VirtualKey.Tab)
                {
                    inputControl?.Focus(FocusState.Programmatic);
                    args.Handled = true;
                    return;
                }
                var key = args.Key;
                var direction = key switch
                {
                    VirtualKey.Up or VirtualKey.NumberPad8 => "N",
                    VirtualKey.Down or VirtualKey.NumberPad2 => "S",
                    VirtualKey.Left or VirtualKey.NumberPad4 => "W",
                    VirtualKey.Right or VirtualKey.NumberPad6 => "E",
                    _ => null
                };
                var ctrl = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
                    .HasFlag(CoreVirtualKeyStates.Down);
                var shift = InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
                    .HasFlag(CoreVirtualKeyStates.Down);
                if (shift && key is VirtualKey.Left or VirtualKey.Right)
                    Send(session, key == VirtualKey.Left ? "turn l" : "turn r");
                else if (direction is not null)
                    Send(session, ProtocolParser.Movement(direction, session.NorthUp, ctrl));
                else
                {
                    var command = key switch
                    {
                        VirtualKey.Space or VirtualKey.NumberPad1 => "hit",
                        VirtualKey.Z or VirtualKey.Add => "get",
                        VirtualKey.X or VirtualKey.Subtract => "put",
                        VirtualKey.C or VirtualKey.Multiply => "use",
                        VirtualKey.NumberPad7 => "turn l",
                        VirtualKey.NumberPad9 => "turn r",
                        VirtualKey.NumberPad5 => "turn b",
                        _ => null
                    };
                    if (command is null)
                        return;
                    Send(session, command);
                }
                args.Handled = true;
            });
    }

    private static Element MapView(GameSession session)
    {
        var map = session.Map;
        var rows = new List<Element>();
        for (var y = 0; y < 7; y++)
        {
            var cells = new List<Element>();
            for (var x = 0; x < 7; x++)
            {
                var tile = map is null ? '?' : (char)map.Tiles[y * 7 + x];
                var symbol = tile == ' ' ? "." : tile.ToString();
                if (map?.Actors.Any(a => a.X == x && a.Y == y) == true)
                    symbol = "o";
                if (x == 3 && y == 3)
                    symbol = "@";
                cells.Add(Border(TextBlock(symbol).FontFamily("Consolas").FontSize(20).Padding(4))
                    .Width(32).Height(32));
            }
            rows.Add(HStack(0, cells.ToArray()));
        }
        return VStack(0, rows.ToArray());
    }

    private async Task LoadAsync()
    {
        characters = await store.LoadAsync();
        loaded = true;
        notice = "Ready. Choose a saved character or register a new one.";
    }

    private async Task SaveCharacterAsync()
    {
        if (!int.TryParse(port, out var portNumber))
            throw new ArgumentException("Enter a numeric TCP port.");
        PhiConnection.ValidateEndpoint(host, portNumber);
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Enter a display name.");
        var character = new SavedCharacter(Guid.NewGuid().ToString("N"), name.Trim(), host.Trim(), portNumber);
        var characterId = id;
        if (creating)
        {
            notice = "Creating character on server...";
            refresh();
            characterId = await PhiConnection.RegisterAsync(character.Host, portNumber, character.Name, password, image, email);
            // Keep the new ID available for retry if local persistence fails.
            id = characterId;
            creating = false;
            password = "";
            notice = "Character created on server. Saving the local profile...";
            refresh();
        }
        PhiWire.Login(characterId);
        var updated = characters.Append(character).ToList();
        await store.AddAsync(character, characterId, updated);
        characters = updated;
        id = "";
        password = "";
        notice = "Character saved. Choose Connect.";
    }

    private Task OpenAsync(SavedCharacter character)
    {
        active = sessions.FirstOrDefault(s => s.Character.Key == character.Key);
        if (active is not null)
            return Task.CompletedTask;
        var session = new GameSession(character);
        sessions.Add(session);
        active = session;
        return StartAsync(session);
    }

    private Task StartAsync(GameSession session)
    {
        if (session.Connection is not null)
            throw new InvalidOperationException("The previous connection is still closing.");
        var characterId = store.GetId(session.Character);
        var connection = new PhiConnection();
        session.Connection = connection;
        session.Connecting = true;
        session.Closing = false;
        session.Map = null;
        session.Choices = [];
        session.Append("[Connecting...]");
        connection.Received += item => dispatcher?.TryEnqueue(() =>
        {
            if (disposed || session.Connection != connection)
                return;
            switch (item)
            {
                case ConnectionReady: session.Connected = true; session.Connecting = false; break;
                case LogLine line: session.Append(line.Text); break;
                case StatusLine status: session.Status = status.Text; break;
                case MapFrame map: session.Map = map; break;
                case Choices choices: session.Choices = choices.Items; break;
                case MapStyle style: session.NorthUp = style.NorthUp; break;
                case EditMode edit: session.Editing = edit.Active; break;
                case Closed: session.Append("[Server closed the session]"); break;
            }
            refresh();
        });
        _ = RunSessionAsync(session, connection, characterId);
        return Task.CompletedTask;
    }

    private async Task RunSessionAsync(GameSession session, PhiConnection connection, string characterId)
    {
        try
        {
            await connection.RunAsync(session.Character.Host, session.Character.Port, characterId);
        }
        catch (OperationCanceledException)
        {
            if (!session.Closing && !disposed)
                session.Append("[Network] Connection timed out or was cancelled.");
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            session.Append($"[Error] {ex.Message}");
        }
        finally
        {
            connection.Dispose();
            session.Connection = null;
            session.Connected = false;
            session.Connecting = false;
            session.Closing = false;
            session.Choices = [];
            session.Editing = false;
            if (!disposed)
                refresh();
        }
    }

    private static async Task DisconnectAsync(GameSession session)
    {
        var connection = session.Connection;
        if (connection is null)
            return;
        session.Closing = true;
        try
        {
            if (session.Connected)
                await connection.SendAsync(["#x"]);
        }
        finally
        {
            connection.Dispose();
            session.Append("[Disconnected]");
        }
    }

    private void Move(GameSession session, string direction) =>
        Send(session, ProtocolParser.Movement(direction, session.NorthUp));

    private void Submit(GameSession session)
    {
        var text = session.Input;
        if (string.IsNullOrWhiteSpace(text))
            return;
        _ = SessionOperationAsync(session, async () =>
        {
            if (!session.Connected || session.Connection is null)
                throw new InvalidOperationException("Connect before sending a command.");
            await session.Connection.SendAsync([text]);
            session.Input = "";
            session.Choices = [];
        });
    }

    private void Send(GameSession session, params string[] commands) =>
        _ = SessionOperationAsync(session, async () =>
        {
            if (!session.Connected || session.Connection is null)
                throw new InvalidOperationException("Connect before sending a command.");
            await session.Connection.SendAsync(commands);
            session.Choices = [];
        });

    private async Task SessionOperationAsync(GameSession session, Func<Task> operation)
    {
        try { await operation(); }
        catch (Exception ex) when (IsExpected(ex))
        {
            session.Append($"[Error] {ex.Message}");
        }
        if (!disposed)
            refresh();
    }

    private async Task PerformAsync(Func<Task> operation)
    {
        if (busy)
            return;
        busy = true;
        refresh();
        try { await operation(); }
        catch (Exception ex) when (IsExpected(ex))
        {
            notice = $"Error: {ex.Message}";
        }
        finally
        {
            busy = false;
            if (!disposed)
                refresh();
        }
    }

    private static bool IsExpected(Exception ex) =>
        ex is IOException or SocketException or ArgumentException or InvalidOperationException
            or UnauthorizedAccessException or JsonException or COMException or OperationCanceledException;

    private void Set(ref string field, string value)
    {
        if (field == value)
            return;
        field = value;
        refresh();
    }
}
