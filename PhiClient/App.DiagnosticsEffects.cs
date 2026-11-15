using System.Text;
using Microsoft.UI.Reactor;
using Microsoft.UI.Windowing;
using PhiClient.Core;

partial class App
{
    private DiagnosticTrace? diagnosticTrace;

    private static string ProductVersion
    {
        get
        {
            var version = Windows.ApplicationModel.Package.Current.Id.Version;
            return $"{version.Major}.{version.Minor}.{version.Build}.{version.Revision}";
        }
    }

    private Task ShowVersionAsync() => SessionDialogAsync(ProductName + " " + ProductVersion,
        "C#・WinUI 3と公式Microsoft.UI.Reactor previewを使用しています。\n" +
        "wphiをもとにした移植版です。wphi本体とは別の製品として、このリポジトリの公開リリースを確認します。\n" +
        "画像素材は設定した外部フォルダーから読み込みます。素材の作者・利用条件は素材設定から確認できます。\n" +
        "PHIの認証情報とゲームデータは、暗号化されていないTCPで送信されます。");

    private async Task CheckReleaseAsync()
    {
        Dispatch(new ClientMessage.CheckingReleaseChanged(true));
        notice = "このアプリの公開リリースをGitHubで確認しています…";
        try
        {
            using var client = new ProductReleaseClient();
            var release = await client.LatestAsync(worldLifetime.Token);
            if (release is null)
            {
                await SessionDialogAsync("更新の確認", "現在の版は " + ProductVersion +
                    " です。このアプリの公開リリースはまだありません。");
                return;
            }
            if (ProductReleaseClient.ParseVersion(release.Version)! <= ProductReleaseClient.ParseVersion(ProductVersion)!)
                await SessionDialogAsync("更新の確認", "現在の版は " + ProductVersion +
                    " です。公開されている最新の版は " + release.Version + " です。");
            else if (await SessionDialogAsync("更新の確認", "新しい版 " + release.Version +
                " が公開されています。GitHubのリリースページから内容と導入方法を確認できます。",
                "リリースページを開く"))
            {
                if (!await Windows.System.Launcher.LaunchUriAsync(release.Url))
                    throw new IOException("リリースページを開けませんでした。既定のブラウザーを確認してください。");
            }
        }
        catch (HttpRequestException error)
        {
            throw new IOException("公開リリースの取得に失敗しました。接続状況を確認して再試行してください: " + error.Message, error);
        }
        finally
        {
            Dispatch(new ClientMessage.CheckingReleaseChanged(false));
            if (notice == "このアプリの公開リリースをGitHubで確認しています…") notice = "更新の確認を終了しました。";
        }
    }

    private void ToggleDiagnosticTrace()
    {
        if (diagnosticTrace is not null)
        {
            StopDiagnosticTrace();
            notice = "時刻の記録を停止し、ファイルへ保存しました。";
            return;
        }
        var folder = Path.Combine(store.Folder, "Logs");
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"trace-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}.txt");
        var trace = new DiagnosticTrace(new StreamWriter(
            new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read), new UTF8Encoding(false)));
        try { trace.Write(DiagnosticEvent.Started); }
        catch { trace.Dispose(); throw; }
        diagnosticTrace = trace;
        Dispatch(new ClientMessage.TraceRecordingChanged(true));
        notice = "不具合調査用に操作・受信・地図更新の時刻を記録しています。発言本文・ID・パスワードは記録しません。保存先: " + path;
    }

    private void StopDiagnosticTrace()
    {
        var trace = diagnosticTrace;
        diagnosticTrace = null;
        try
        {
            if (trace is not null)
            {
                try { trace.Write(DiagnosticEvent.Stopped); }
                finally { trace.Dispose(); }
            }
        }
        finally { Dispatch(new ClientMessage.TraceRecordingChanged(false)); }
    }

    private void TraceDiagnostic(DiagnosticEvent value)
    {
        if (diagnosticTrace is not { } trace) return;
        try { trace.Write(value); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            diagnosticTrace = null;
            var detail = error.Message;
            try { trace.Dispose(); }
            catch (Exception closeError) when (closeError is IOException or UnauthorizedAccessException)
            { detail += " " + closeError.Message; }
            Dispatch(new ClientMessage.TraceRecordingChanged(false));
            notice = "時刻の記録を停止しました。書き込み済みのファイルは保持しています。保存先を確認して記録を再開してください: " + detail;
        }
    }

    private async Task ResetWindowLayoutAsync()
    {
        if (!await SessionDialogAsync("窓の位置を元に戻す",
            "メイン窓と地図窓を現在の画面へ配置し直します。接続・登録・設定項目はそのまま保持します。",
            "配置し直す")) return;
        void Reset(ReactorWindow window, int width, int height)
        {
            if (window.AppWindow.Presenter is OverlappedPresenter presenter) presenter.Restore();
            var area = DisplayArea.GetFromWindowId(window.AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
            var scale = window.DipScale;
            var w = Math.Min(area.Width, (int)(width * scale));
            var h = Math.Min(area.Height, (int)(height * scale));
            window.AppWindow.MoveAndResize(new(area.X + (area.Width - w) / 2,
                area.Y + (area.Height - h) / 2, w, h));
            window.SavePlacement();
        }
        if (ReactorApp.PrimaryWindow is { } primary) Reset(primary, 1200, 850);
        if (mapWindow is { } map) Reset(map, 400, 780);
        notice = "窓の位置と大きさを元に戻しました。";
    }
}
