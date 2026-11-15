using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PhiClient.Core;

public sealed class SettingsStore(string path)
{
    private readonly SemaphoreSlim writes = new(1, 1);

    public async Task<ClientOptions> LoadAsync(CancellationToken token = default)
    {
        if (!File.Exists(path)) return new();
        if (new FileInfo(path).Length > 1024 * 1024)
            throw new InvalidDataException("設定ファイルが1 MiBを超えています。");
        await using var input = File.OpenRead(path);
        var value = await JsonSerializer.DeserializeAsync(input, SettingsJsonContext.Default.ClientOptions, token)
            ?? throw new InvalidDataException("設定ファイルに設定がありません。");
        return Normalize(value);
    }

    public async Task SaveAsync(ClientOptions options, CancellationToken token = default)
    {
        options = Normalize(options);
        await writes.WaitAsync(token);
        var temporary = path + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            await using (var output = File.Create(temporary))
                await JsonSerializer.SerializeAsync(output, options, SettingsJsonContext.Default.ClientOptions, token);
            File.Move(temporary, path, true);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            finally { writes.Release(); }
        }
    }

    public static ClientOptions Normalize(ClientOptions options)
    {
        if (options.SoundFolder is null || options.SoundFolder.Length > 2048)
            throw new InvalidDataException("効果音のPhillyフォルダーを2048文字以内のパスで入力してください。");
        if (options.Shortcuts.IsDefault || options.Shortcuts.Length != 5 ||
            options.ListShortcuts.IsDefault || options.ListShortcuts.Length != 5 ||
            options.Shortcuts.Any(value => value is null) || options.ListShortcuts.Any(value => value is null) ||
            options.AltGWord is null || options.LogFontName is null)
            throw new InvalidDataException("F8–F12の設定は通常用・リスト用それぞれ5項目が必要です。");
        foreach (var macro in options.Shortcuts.Concat(options.ListShortcuts).Append(options.AltGWord))
            foreach (var line in macro.Replace("\\n", "\n", StringComparison.Ordinal).Split('\n'))
                PhiWire.ValidateLine(line.TrimEnd('\r'));
        return options with { MapZoom = Math.Clamp(options.MapZoom, 50, 400),
            LogFontSize = Math.Min(options.LogFontSize, 64) };
    }

    public static ClientOptions ImportPhilly(ClientOptions current, byte[] bytes)
    {
        current = Normalize(current);
        var fields = new Dictionary<(string Section, string Key), string>();
        var section = "";
        foreach (var row in PhiWire.Encoding.GetString(bytes).Split('\n'))
        {
            var line = row.Trim();
            if (line.StartsWith('[') && line.IndexOf(']') is var end && end > 0)
            {
                section = line[1..end].Trim();
                continue;
            }
            var equal = line.IndexOf('=');
            if (equal < 0) continue;
            var value = line[(equal + 1)..].TrimStart();
            var quotedEnd = value.StartsWith('"') ? value.IndexOf('"', 1) : -1;
            value = quotedEnd > 0 ? value[1..quotedEnd] : value.Split(';')[0].Trim();
            fields.TryAdd((section, line[..equal].Trim()), value);
        }
        string Value(string group, string key, string fallback) => fields.GetValueOrDefault((group, key), fallback);
        bool Flag(string group, string key, bool fallback) => fields.TryGetValue((group, key), out var value)
            ? value.Equals("on", StringComparison.OrdinalIgnoreCase) || value == "1" ||
                value.Equals("true", StringComparison.OrdinalIgnoreCase) : fallback;
        var shortcuts = current.Shortcuts;
        var lists = current.ListShortcuts;
        for (var i = 0; i < 5; i++)
        {
            shortcuts = shortcuts.SetItem(i, Value("Shortcut", $"ShortcutF{i + 8:00}", shortcuts[i]));
            lists = lists.SetItem(i, Value("Shortcut", $"ListModeShortcutF{i + 8:00}", lists[i]));
        }
        var font = current.LogFontSize;
        if (uint.TryParse(Value("Font", "LogFontSize", ""), NumberStyles.None, CultureInfo.InvariantCulture, out var size))
            font = size;
        return Normalize(current with
        {
            FullKeys = Flag("Move", "FullKeyMove", current.FullKeys), Shortcuts = shortcuts, ListShortcuts = lists,
            AltGWord = Value("Shortcut", "AltGWord", current.AltGWord),
            SoundPlay = Flag("Sound", "SoundPlay", current.SoundPlay),
            TaskBarFlash = Flag("TaskBarFlash", "Enabled", current.TaskBarFlash),
            LogSaveHtml = Flag("Log", "LogSaveHtml", current.LogSaveHtml),
            LogDispTime = Flag("Log", "LogDispTime", current.LogDispTime), LogFontSize = font,
            LogTagDeleteCopy = Flag("LogEdit", "LogTagDeleteCutCopy", current.LogTagDeleteCopy),
            LogTagDeleteAlways = Flag("LogEdit", "LogTagDeleteAlways", current.LogTagDeleteAlways),
            LogTimeDelete = Flag("LogEdit", "LogTimeDelete", current.LogTimeDelete),
            LogNameDelete = Flag("LogEdit", "LogNameDelete", current.LogNameDelete),
            LogTagCancel = Flag("LogEdit", "LogTagCancel", current.LogTagCancel),
            AutoUnequipSystemMagic = Flag("Automation", "AutoUnequipSystemMagic", current.AutoUnequipSystemMagic),
            AutoEquipSystemMagic = Flag("Automation", "AutoEquipSystemMagic", current.AutoEquipSystemMagic)
        });
    }
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonSerializable(typeof(ClientOptions))]
internal partial class SettingsJsonContext : JsonSerializerContext;
