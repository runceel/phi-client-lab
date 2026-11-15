using PhiClient;
using PhiClient.Core;
using Windows.ApplicationModel.DataTransfer;

partial class App
{
    private LogStore? logStore;
    private LogStore Logs => logStore ??= new(Path.Combine(store.Folder, "Logs"));

    private void ObserveLogSaving(ClientModel previous, ClientMessage message)
    {
        if (disposed || message is not ClientMessage.GameChanged change ||
            !previous.Games.TryGetValue(change.Key, out var before) ||
            !model.Games.TryGetValue(change.Key, out var after) || !before.LogSaving)
            return;
        foreach (var row in after.LogEntries.Where(row => row.Id >= before.NextLogId && row.Time is not null))
        {
            var session = sessions.FirstOrDefault(game => game.Character.Key == change.Key);
            if (session is not null) _ = AppendSavedLogAsync(session, row, model.Options);
        }
    }

    private async Task AppendSavedLogAsync(GameSession session, LogEntry row, ClientOptions options)
    {
        try { await Logs.AppendAsync(session.Character.Name, row, options); }
        catch (Exception error) when (IsExpected(error))
        {
            session.Dispatch(new GameMessage.LogSavingChanged(false));
            session.Append($"ログの保存に失敗しました。自動保存を停止しました: {error.Message}");
        }
    }

    private void ToggleLogSaving(GameSession session, bool enabled)
    {
        session.Dispatch(new GameMessage.LogSavingChanged(enabled));
        if (enabled) session.Append("ログの自動保存を始めました。");
    }

    private void OpenLogFolder(GameSession session) => _ = PerformAsync(async () =>
    {
        var path = Logs.Folder(session.Character.Name);
        Directory.CreateDirectory(path);
        var folder = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(path);
        if (!await Windows.System.Launcher.LaunchFolderAsync(folder))
            throw new IOException("ログの保存先を開けませんでした。");
    });

    private void CopyLog(GameSession session, IReadOnlyList<LogEntry> rows, bool toInput, bool privateInput = false)
    {
        _ = SessionOperationAsync(session, () =>
        {
            var text = string.Join('\n', rows.Select(row => row.Copy(model.Options, toInput)));
            if (toInput)
            {
                session.Dispatch(privateInput ? new GameMessage.PrivDraftChanged(text) : new GameMessage.InputChanged(text));
                var input = privateInput ? session.PrivInput : inputControl;
                input?.Focus(Microsoft.UI.Xaml.FocusState.Programmatic);
            }
            else
            {
                var data = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
                data.SetText(text);
                Clipboard.SetContent(data);
                Clipboard.Flush();
            }
            return Task.CompletedTask;
        });
    }

    private void OpenLogLink(Uri uri) => _ = PerformAsync(async () =>
    {
        if (!LogMarkup.PublicHttpUrl(uri.AbsoluteUri, out _))
            throw new ArgumentException("このリンクは公開HTTP/HTTPSのアドレスではありません。");
        if (!await SessionDialogAsync("外部リンクを開く", "ブラウザーで外部サイトを開きます。続行しますか？", "開く"))
            return;
        var addresses = await System.Net.Dns.GetHostAddressesAsync(uri.DnsSafeHost, worldLifetime.Token);
        if (addresses.Length == 0 || addresses.Any(address =>
            !LogMarkup.PublicHttpUrl($"http://{(address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ?
                $"[{address}]" : address.ToString())}/", out _)))
            throw new IOException("ローカル・非公開のアドレスに解決されるリンクは開けません。");
        if (!await Windows.System.Launcher.LaunchUriAsync(uri))
            throw new IOException("ブラウザーを起動できませんでした。");
    });
}
