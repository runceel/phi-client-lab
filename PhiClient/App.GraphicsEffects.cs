using PhiClient.Core;

partial class App
{
    private void ShowGraphicsSettings() => _ = PerformAsync(async () =>
    {
        var answer = await RequestDialogAsync(new("画像素材フォルダー",
            "tools\\Get-WphiGraphics.ps1で取得したwphi素材、wphiのソースフォルダー、または利用が許可されたPhillyフォルダーを指定してください。" +
            "画像は指定したローカルフォルダーのcharaとSkins\\Mineから読み込みます。素材ごとの利用条件を確認してください。" +
            "外部画像を優先し、欠落分は同梱の基本画像で補います。空欄で同梱画像だけに戻します。",
            "読み込む", "画像素材のローカルフォルダー", graphicsFolder, "graphics-folder"));
        if (!answer.Accepted) return;
        var folder = answer.Value.Trim();
        await graphics.SetFolderAsync(folder);
        graphicsFolder = graphics.Folder;
        foreach (var session in sessions) await graphics.PrepareAsync(session);
        await PrepareRegistrationPreviewAsync();
        Directory.CreateDirectory(store.Folder);
        var path = Path.Combine(store.Folder, "graphics-folder.txt");
        await File.WriteAllTextAsync(path + ".tmp", graphicsFolder);
        File.Move(path + ".tmp", path, true);
        notice = graphics.Notice;
    });

    private void SendSign(PhiClient.GameSession session)
    {
        var lines = session.BoardText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        if (lines.Any(line => line is "." or ".!" || line.StartsWith('#')))
        {
            notice = "Sign lines must not be protocol commands, '.' or '.!'.";
            return;
        }
        Send(session, lines.Select(line => line.Length == 0 ? " " : line).Append(".").ToArray());
    }
}
