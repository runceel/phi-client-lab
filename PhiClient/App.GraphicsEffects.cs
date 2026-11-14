using PhiClient.Core;

partial class App
{
    private void ShowGraphicsSettings() => _ = PerformAsync(async () =>
    {
        var answer = await RequestDialogAsync(new("Graphics folder",
            "Use the original wphi graphics from tools\\Get-WphiGraphics.ps1, a wphi checkout, " +
            "or a permitted Philly folder. The app itself does not download images. " +
            "Expected: chara and Skins\\Mine. Leave blank for the schematic map.",
            "Load", "Graphics folder", graphicsFolder, "graphics-folder"));
        if (!answer.Accepted) return;
        var folder = answer.Value.Trim();
        await graphics.SetFolderAsync(folder);
        graphicsFolder = graphics.Folder;
        foreach (var session in sessions) await graphics.PrepareAsync(session);
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
