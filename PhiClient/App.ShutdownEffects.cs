using Microsoft.UI.Windowing;
using Microsoft.UI.Reactor;
using Microsoft.UI.Xaml;
using PhiClient.Core;

partial class App
{
    private AppWindow? mainWindow;
    private FrameworkElement? mainRoot;
    private bool closingApplication;
    private int pendingSessionOperations;
    private int pendingLogWrites;
    private readonly Dictionary<PhiClient.GameSession, List<LogEntry>> unsavedLogEntries = [];
    private bool confirmingRegistrationClose;

    private void AttachClosingWindow(FrameworkElement root)
    {
        var window = AppWindow.GetFromWindowId(root.XamlRoot.ContentIslandEnvironment.AppWindowId);
        mainRoot = root;
        if (mainWindow == window) return;
        if (mainWindow is not null) mainWindow.Closing -= OnWindowClosing;
        mainWindow = window;
        mainWindow.Closing += OnWindowClosing;
    }

    private void OnWindowClosing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        args.Cancel = true;
        if (closingApplication || confirmingRegistrationClose) return;
        if (model.Registration.Stage == RegistrationStage.Creating)
        {
            notice = "キャラクターの作成結果を待っています。結果を受け取ってから終了してください。";
            return;
        }
        if (model.Registration.NeedsDiscardWarning)
        {
            _ = ConfirmRegistrationCloseAsync(sender);
            return;
        }
        BeginClosing(sender);
    }

    private async Task ConfirmRegistrationCloseAsync(AppWindow sender)
    {
        confirmingRegistrationClose = true;
        try { if (await EndRegistrationAsync()) BeginClosing(sender); }
        finally { confirmingRegistrationClose = false; }
    }

    private void BeginClosing(AppWindow sender)
    {
        closingApplication = true;
        if (mainRoot is not null) mainRoot.IsHitTestVisible = false;
        animation?.Stop();
        predictionTimer?.Stop();
        mapSaveTimer?.Stop();
        dialogCompletion?.TrySetResult(new(false, ""));
        notice = "接続を終了し、ログと記録地図を保存しています…";
        refresh();
        _ = FinishClosingAsync(sender);
    }

    private async Task FinishClosingAsync(AppWindow window)
    {
        var stoppingTrace = false;
        try
        {
            foreach (var session in sessions)
            {
                session.MasterCancellation?.Cancel();
                try { await DisconnectAsync(session).WaitAsync(TimeSpan.FromSeconds(10)); }
                catch (Exception error) when (IsExpected(error) || error is TimeoutException)
                {
                    session.Connection?.Dispose();
                    session.Append("接続の終了を確認してください: " + error.Message);
                }
            }
            await Task.WhenAll(sessions.Select(session => session.ConnectionTask ?? Task.CompletedTask))
                .WaitAsync(TimeSpan.FromSeconds(30));
            var deadline = Environment.TickCount64 + 30_000;
            while (busy || pendingSessionOperations > 0 || pendingLogWrites > 0 ||
                savingMaps || loadingMaps.Count > 0)
            {
                if (Environment.TickCount64 >= deadline)
                    throw new TimeoutException("保存処理の完了を待っています。少し待ってから終了を再試行してください。");
                await Task.Delay(20);
            }
            foreach (var (session, rows) in unsavedLogEntries.ToArray())
            {
                await Logs.SaveBufferAsync(session.Character.Name, rows.ToArray(), model.Options);
                unsavedLogEntries.Remove(session);
            }
            await Logs.FlushAsync();
            await FlushAutomapsAsync(failOnError: true);
            var primary = ReactorApp.PrimaryWindow ??
                throw new InvalidOperationException("メイン窓の配置を保存する画面を確認できません。");
            primary.SavePlacement();
            mapWindow?.SavePlacement();
            stoppingTrace = true;
            StopDiagnosticTrace();
            stoppingTrace = false;
            window.Closing -= OnWindowClosing;
            ReactorApp.Exit();
        }
        catch (Exception error) when (IsExpected(error) || error is TimeoutException)
        {
            closingApplication = false;
            if (mainRoot is not null) mainRoot.IsHitTestVisible = true;
            animation?.Start();
            predictionTimer?.Start();
            mapSaveTimer?.Start();
            notice = stoppingTrace
                ? "終了前の時刻記録の保存に失敗しました。書き込み済みのファイルは保持しています。" +
                    "保存先を確認してから、終了を再試行してください。詳細: " + error.Message
                : error is TimeoutException
                ? "終了前の処理が待機期限を超えました。記録は画面とメモリに残っています。状態を確認してから、終了を再試行してください。詳細: " + error.Message
                : "終了前のログ・記録地図の保存に失敗しました。記録は画面とメモリに残っています。" +
                    "保存先を確認してから、終了を再試行してください。詳細: " + error.Message;
            refresh();
        }
    }
}
