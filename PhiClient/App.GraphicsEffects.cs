using PhiClient.Core;

partial class App
{
    private void ShowGraphicsSettings() => _ = PerformAsync(async () =>
    {
        var answer = await RequestDialogAsync(new("Graphics folder",
            "Use a local Philly-compatible folder you have permission to use. No images are downloaded or bundled. " +
            "Expected: chara, chip and Skins\\Mine. Leave blank for the schematic map.",
            "Load", "Graphics folder", graphicsFolder, "graphics-folder"));
        if (!answer.Accepted) return;
        var folder = answer.Value.Trim();
        await graphics.SetFolderAsync(folder);
        graphicsFolder = folder;
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
