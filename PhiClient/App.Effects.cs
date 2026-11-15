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

partial class App
{
    private readonly CharacterStore store = new();
    private readonly List<GameSession> sessions = [];
    private Microsoft.UI.Xaml.Controls.TextBox? inputControl;
    private Microsoft.UI.Xaml.Controls.Button? gameControl;
    private DispatcherQueue? dispatcher;
    private Action refresh = () => { };
    private bool disposed;
    private bool composing;
    private bool committingComposition;
    private readonly GraphicsStore graphics = new();
    private string graphicsFolder = "";
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? animation;

    private Action StartServices()
    {
        dispatcher = DispatcherQueue.GetForCurrentThread();
        animation = dispatcher.CreateTimer();
        animation.Interval = TimeSpan.FromMilliseconds(500);
        animation.Tick += (_, _) =>
        {
            if (active is null || graphics.Folder.Length == 0 || !showMap) return;
            graphics.AnimationFrame ^= 1;
            refresh();
        };
        animation.Start();
        _ = PerformAsync(LoadAsync);
        return () =>
        {
            disposed = true;
            worldLifetime.Cancel();
            worldClient?.Dispose();
            worldLifetime.Dispose();
            animation?.Stop();
            dialogCompletion?.TrySetResult(new(false, ""));
            foreach (var session in sessions)
                session.Connection?.Dispose();
        };
    }

    private async Task OpenSettingsFolderAsync()
    {
        Directory.CreateDirectory(store.Folder);
        var folder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(store.Folder);
        if (!await Launcher.LaunchFolderAsync(folder))
            throw new IOException("Windows could not open the settings folder.");
    }

    private void DeleteCharacter(SavedCharacter character)
    {
        if (pendingDelete != character.Key)
        {
            pendingDelete = character.Key;
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
    }

    private async Task LoadAsync()
    {
        characters = await store.LoadAsync();
        Dispatch(new ClientMessage.WorldsLoaded(await WorldsClient.LoadAsync(worldLifetime.Token)));
        loaded = true;
        var graphicsPath = Path.Combine(store.Folder, "graphics-folder.txt");
        if (File.Exists(graphicsPath))
        {
            graphicsFolder = (await File.ReadAllTextAsync(graphicsPath)).Trim();
            await graphics.SetFolderAsync(graphicsFolder);
        }
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
        showCharacters = false;
        notice = "PHI uses unencrypted TCP. Menus and game keys apply to the selected character.";
        if (active is not null)
            return Task.CompletedTask;
        Dispatch(new ClientMessage.GameOpened(character.Key));
        var session = new GameSession(character, () => model.Games[character.Key],
            message => Dispatch(new ClientMessage.GameChanged(character.Key, message)));
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
        session.Dispatch(new GameMessage.PhaseChanged(ConnectionPhase.Connecting));
        connection.Received += item => dispatcher?.TryEnqueue(() =>
        {
            if (disposed || session.Connection != connection)
                return;
            session.Dispatch(new GameMessage.Received(item));
            if (item is InputRequested && active == session) inputControl?.Focus(FocusState.Programmatic);
            if (item is MapFrame or EagleFrame or MapSetNotice)
                _ = SessionOperationAsync(session, () => graphics.PrepareAsync(session));
        });
        session.ConnectionTask = RunSessionAsync(session, connection, characterId);
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
            session.Dispatch(new GameMessage.PhaseChanged(ConnectionPhase.Offline));
            if (!disposed)
                refresh();
        }
    }

    private static async Task DisconnectAsync(GameSession session)
    {
        var connection = session.Connection;
        if (connection is null)
            return;
        var connected = session.Connected;
        session.Dispatch(new GameMessage.PhaseChanged(ConnectionPhase.Closing));
        try
        {
            if (connected)
                await connection.SendAsync(["#x"]);
        }
        finally
        {
            connection.Dispose();
            session.Append("[Disconnected]");
        }
    }

    private void Move(GameSession session, string direction) =>
        Send(session, ProtocolParser.Movement(direction, session.NorthFixed));

    private void Send(GameSession session, params string[] commands) =>
        Execute(session, GameInput.Command(commands));

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

}
