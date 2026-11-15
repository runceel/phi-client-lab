using Microsoft.UI.Windowing;
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
        if (closingApplication) return;
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
            window.Closing -= OnWindowClosing;
            window.Destroy();
            Application.Current.Exit();
        }
        catch (Exception error) when (IsExpected(error) || error is TimeoutException)
        {
            closingApplication = false;
            if (mainRoot is not null) mainRoot.IsHitTestVisible = true;
            animation?.Start();
            predictionTimer?.Start();
            mapSaveTimer?.Start();
            notice = "終了前の保存を確認してください。画面とメモリ上の記録を保持しています: " + error.Message;
            refresh();
        }
    }
}
