using System.Globalization;
using PhiClient.Core;

partial class App
{
    private SettingsStore? settingsStore;

    private async Task SaveSettingsAsync()
    {
        if (model.SettingsEditor is not { } editor) return;
        var auto = editor.Zoom.Trim().Equals("Auto", StringComparison.OrdinalIgnoreCase);
        var zoom = 0;
        if (!auto && !int.TryParse(editor.Zoom, NumberStyles.Integer, CultureInfo.InvariantCulture, out zoom))
            throw new ArgumentException("マップの拡大率はAutoまたは50–400の整数で入力してください。");
        if (!uint.TryParse(editor.FontSize, NumberStyles.None, CultureInfo.InvariantCulture, out var fontSize) ||
            fontSize > 64)
            throw new ArgumentException("ログの文字サイズは0–64の整数で入力してください。");
        var options = SettingsStore.Normalize(editor.Value with { MapZoom = zoom, LogFontSize = fontSize });
        var rules = await SoundFiles.LoadAsync(options.SoundFolder, worldLifetime.Token);
        settingsStore ??= new(Path.Combine(store.Folder, "settings.json"));
        await settingsStore.SaveAsync(options, worldLifetime.Token);
        Dispatch(new ClientMessage.OptionsChanged(options));
        SetSoundRules(rules);
        Dispatch(new ClientMessage.SettingsEdited(editor with { Value = options,
            Zoom = options.MapZoom == 0 ? "Auto" : options.MapZoom.ToString(CultureInfo.InvariantCulture),
            Message = rules.Issues.Count == 0 ? "設定を保存しました。" : "設定を保存しました。" + SoundIssues(rules) }));
        notice = "設定を保存しました。";
        if (!loaded) await LoadAsync();
    }

    private async Task ImportSettingsAsync()
    {
        if (model.SettingsEditor is not { } editor) return;
        if (!Path.IsPathFullyQualified(editor.ImportPath) || editor.ImportPath.StartsWith(@"\\", StringComparison.Ordinal))
            throw new ArgumentException("このPCのPhilly.iniの絶対パスを入力してください。ネットワーク共有は使えません。");
        if (new FileInfo(editor.ImportPath).Length > 1024 * 1024)
            throw new InvalidDataException("Philly.iniが1 MiBを超えています。");
        var imported = SettingsStore.ImportPhilly(editor.Value, await File.ReadAllBytesAsync(editor.ImportPath));
        Dispatch(new ClientMessage.SettingsEdited(editor with { Value = imported,
            FontSize = imported.LogFontSize.ToString(CultureInfo.InvariantCulture),
            Message = "設定を編集画面へ取り込みました。「設定を保存」で適用します。" }));
    }
}
