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
    private Microsoft.UI.Xaml.Controls.Control? gameControl;
    private Microsoft.UI.Xaml.Controls.DropDownButton? menuControl;
    private bool pendingGameFocus;
    private DispatcherQueue? dispatcher;
    private Action refresh = () => { };
    private bool disposed;
    private bool composing;
    private bool committingComposition;
    private readonly GraphicsStore graphics = new();
    private readonly Windows.UI.ViewManagement.UISettings visualPreferences = new();
    private string graphicsFolder = "";
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? animation;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? predictionTimer;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? mapSaveTimer;

    private Action StartServices()
    {
        dispatcher = DispatcherQueue.GetForCurrentThread();
        animation = dispatcher.CreateTimer();
        animation.Interval = TimeSpan.FromMilliseconds(500);
        animation.Tick += (_, _) =>
        {
            if (active is null || !showMap) return;
            graphics.AnimationFrame ^= 1;
            refresh();
        };
        animation.Start();
        StartMapEffectAnimation(dispatcher);
        predictionTimer = dispatcher.CreateTimer();
        predictionTimer.Interval = TimeSpan.FromMilliseconds(20);
        predictionTimer.Tick += (_, _) =>
        {
            if (active is { NorthFixed: true } session)
            {
                var now = Environment.TickCount64;
                if (model.Options.Automap && !ReferenceEquals(session.Model.Prediction, session.Model.Prediction.Tick(now)) ||
                    model.Options.SmoothMove && visualPreferences.AnimationsEnabled && session.Model.Motion.Offset != (0, 0))
                    session.Dispatch(new GameMessage.PredictionTick(now));
            }
        };
        predictionTimer.Start();
        mapSaveTimer = dispatcher.CreateTimer();
        mapSaveTimer.Interval = TimeSpan.FromSeconds(5);
        mapSaveTimer.Tick += (_, _) => _ = FlushAutomapsAsync();
        mapSaveTimer.Start();
        _ = PerformAsync(LoadAsync);
        return () =>
        {
            disposed = true;
            mapWindow?.Close();
            worldLifetime.Cancel();
            worldClient?.Dispose();
            logImageClient?.Dispose();
            worldLifetime.Dispose();
            animation?.Stop();
            mapEffectTimer?.Stop();
            predictionTimer?.Stop();
            mapSaveTimer?.Stop();
            if (mainWindow is not null) mainWindow.Closing -= OnWindowClosing;
            dialogCompletion?.TrySetResult(new(false, ""));
            foreach (var session in sessions)
                session.Connection?.Dispose();
            DisposeSounds();
        };
    }

    private async Task OpenSettingsFolderAsync()
    {
        Directory.CreateDirectory(store.Folder);
        var folder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(store.Folder);
        if (!await Launcher.LaunchFolderAsync(folder))
            throw new IOException("設定フォルダーを開けませんでした。Windowsのフォルダーへのアクセスを確認してください。");
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
            await store.RemoveAsync(character);
            characters = characters.Where(c => c.Key != character.Key).ToList();
            pendingDelete = null;
            notice = "このPCからキャラクターの接続情報を削除しました。";
        });
    }

    private async Task LoadAsync()
    {
        settingsStore ??= new(Path.Combine(store.Folder, "settings.json"));
        Dispatch(new ClientMessage.OptionsChanged(await settingsStore.LoadAsync(worldLifetime.Token)));
        characters = await store.LoadAsync();
        Dispatch(new ClientMessage.WorldsLoaded(await WorldsClient.LoadAsync(worldLifetime.Token)));
        loaded = true;
        var graphicsPath = Path.Combine(store.Folder, "graphics-folder.txt");
        if (File.Exists(graphicsPath))
        {
            graphicsFolder = (await File.ReadAllTextAsync(graphicsPath)).Trim();
            await graphics.SetFolderAsync(graphicsFolder);
        }
        await LoadMasterMenuAsync();
        notice = "保存済みキャラクターに接続するか、キャラクターIDを登録して始めます。";
        try
        {
            var rules = await SoundFiles.LoadAsync(model.Options.SoundFolder, worldLifetime.Token);
            SetSoundRules(rules);
            if (rules.Issues.Count > 0) notice = SoundIssues(rules);
        }
        catch (Exception error) when (IsExpected(error))
        {
            soundRules = null;
            notice = "通知の設定を読み込めませんでした。「設定」でPhillyフォルダーを確認してください: " + error.Message;
        }
    }

    private async Task SaveCharacterAsync()
    {
        var form = model.Registration;
        if (form.Stage != RegistrationStage.SavePending && !form.CanEdit) return;
        if (!int.TryParse(port, out var portNumber))
            throw new ArgumentException("TCPポートを数値で入力してください。");
        PhiConnection.ValidateEndpoint(host, portNumber);
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("表示名を入力してください。");
        var character = form.Character ??
            new SavedCharacter(Guid.NewGuid().ToString("N"), name.Trim(), host.Trim(), portNumber);
        var characterId = id;
        var createdPassword = form.NewCharacter ? password : "";
        if (form.NewCharacter && form.Stage != RegistrationStage.SavePending)
        {
            if (password.Length == 0) password = PhiWire.CreatePassword();
            createdPassword = password;
            EditRegistration(form with { Stage = RegistrationStage.Creating });
            notice = "指定した世界にキャラクターを作成しています…";
            refresh();
            try
            {
                characterId = await PhiConnection.RegisterAsync(character.Host, portNumber, character.Name, password, image, email);
            }
            catch (RegistrationRejectedException error)
            {
                EditRegistration(form with { Stage = RegistrationStage.Rejected, Message = error.Message });
                notice = error.Message;
                return;
            }
            catch (Exception error) when (IsExpected(error))
            {
                EditRegistration(form with { Stage = RegistrationStage.Unknown,
                    Message = "サーバーから作成結果を受け取れませんでした。入力と作成時のパスワードを保持しています。" });
                notice = "作成結果は不明です。作成済みの可能性があるため、再送しません。";
                return;
            }
            Dispatch(new ClientMessage.RegistrationCreated(character, characterId));
            notice = "世界での作成が完了しました。接続情報をこのPCに保存しています…";
            refresh();
        }
        PhiWire.Login(characterId);
        try { await store.AddAsync(character, characterId); }
        catch (Exception ex) when (createdPassword.Length > 0 && IsExpected(ex))
        {
            EditRegistration(model.Registration with {
                Message = "このPCへの保存に失敗しました。「このPCへの保存を再試行」で保存だけをやり直せます。" });
            notice = "世界での作成は完了していますが、このPCへの保存に失敗しました。IDとパスワードを保持しています。";
            return;
        }
        characters = characters.Append(character).ToList();
        EditRegistration(model.Registration with { Stage = RegistrationStage.Complete, Character = character, Message = "" });
        id = "";
        password = "";
        notice = "接続情報を保存しました。「接続」でゲームを開始できます。";
        if (createdPassword.Length > 0)
            await RequestDialogAsync(new("キャラクターを作成しました",
                "作成時のパスワードを安全な場所に控えてください。この確認画面を閉じると、アプリはパスワードを保持しません。" +
                "\nキャラクターIDはWindows資格情報マネージャーに保存しました。まだゲームには接続していません。",
                InputLabel: "作成時のパスワード", Value: createdPassword,
                InputId: "created-password", Secret: true));
    }

    private Task OpenAsync(SavedCharacter character)
    {
        pendingGameFocus = true;
        active = sessions.FirstOrDefault(s => s.Character.Key == character.Key);
        showCharacters = false;
        notice = "";
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
        connection.ConfirmTransferAsync = (targetHost, targetPort, token) =>
            ConfirmWorldTransferAsync(session, connection, targetHost, targetPort, token);
        session.Connection = connection;
        session.Dispatch(new GameMessage.PhaseChanged(ConnectionPhase.Connecting));
        connection.Sending += (line, at) => dispatcher?.TryEnqueue(() =>
        {
            if (disposed || session.Connection != connection || !model.Options.Automap || !session.NorthFixed) return;
            _ = SessionOperationAsync(session, () =>
            {
                session.Dispatch(new GameMessage.CommandSent(line, at));
                return Task.CompletedTask;
            });
        });
        connection.Received += item => dispatcher?.TryEnqueue(() =>
        {
            if (disposed || session.Connection != connection)
                return;
            if (item is TransferProgress { Active: true } or WorldMoved)
                session.MasterCancellation?.Cancel();
            var privWasVisible = session.Model.Priv.Visible;
            session.Dispatch(new GameMessage.Received(item, Environment.TickCount64, DateTimeOffset.Now));
            if (item is LogLine log) ObserveSound(session, connection, log);
            if (item is LogLine { Channel: LogChannel.Priv, Literal: false } && !privWasVisible &&
                session.Model.Priv.Visible)
                _ = SessionOperationAsync(session, () => RefreshUsersAsync(session));
            if (item is MapFrame && session.World is not "" and not "???")
                _ = LoadAutomapAsync(session, session.World);
            if (item is WorldMoved moved)
            {
                session.Character = session.Character with { Host = moved.Host, Port = moved.Port };
                characters = characters.Select(character => character.Key == session.Character.Key
                    ? session.Character : character).ToList();
                _ = SessionOperationAsync(session, () => store.UpdateEndpointAsync(session.Character), allowClosing: true);
            }
            if (item is InputRequested && active == session) inputControl?.Focus(FocusState.Programmatic);
            if (item is MapFrame or EagleFrame or MapSetNotice or WorldMoved)
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
            session.MasterCancellation?.Cancel();
            connection.Dispose();
            session.Connection = null;
            session.Dispatch(new GameMessage.PhaseChanged(ConnectionPhase.Offline));
            await FlushAutomapsAsync();
            if (!disposed)
                refresh();
        }
    }

    private void ConnectAll()
    {
        foreach (var session in sessions.Where(item => item.Connection is null).ToArray())
            _ = SessionOperationAsync(session, () => StartAsync(session));
    }

    private void DisconnectAll()
    {
        foreach (var session in sessions.Where(item => item.Connection is not null).ToArray())
            _ = SessionOperationAsync(session, () => DisconnectAsync(session));
    }

    private void ForceDisconnectAll()
    {
        foreach (var session in sessions.ToArray())
        {
            session.MasterCancellation?.Cancel();
            session.Connection?.Dispose();
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
            if (connected && !session.Model.Transferring)
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

    private async Task SessionOperationAsync(GameSession session, Func<Task> operation, bool allowClosing = false)
    {
        if (closingApplication && !allowClosing) return;
        pendingSessionOperations++;
        try { await operation(); }
        catch (Exception ex) when (IsExpected(ex))
        {
            session.Append($"[Error] {ex.Message}");
        }
        finally { pendingSessionOperations--; }
        if (!disposed)
            refresh();
    }

    private async Task PerformAsync(Func<Task> operation)
    {
        if (busy || closingApplication)
            return;
        busy = true;
        refresh();
        try { await operation(); }
        catch (Exception ex) when (IsExpected(ex))
        {
            notice = $"操作を完了できませんでした: {ex.Message}";
        }
        finally
        {
            busy = false;
            if (!disposed)
                refresh();
        }
    }

    private static bool IsExpected(Exception ex) =>
        ex is IOException or InvalidDataException or SocketException or ArgumentException or InvalidOperationException
            or UnauthorizedAccessException or JsonException or COMException or OperationCanceledException or System.Xml.XmlException;

}
