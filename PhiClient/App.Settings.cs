using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using PhiClient.Core;
using static Microsoft.UI.Reactor.Factories;

partial class App
{
    private void OpenSettings() => Dispatch(new ClientMessage.SettingsEdited(new(model.Options,
        model.Options.MapZoom.ToString(System.Globalization.CultureInfo.InvariantCulture),
        model.Options.LogFontSize.ToString(System.Globalization.CultureInfo.InvariantCulture))));

    private void EditSettings(Func<SettingsEditorModel, SettingsEditorModel> change)
    {
        if (model.SettingsEditor is { } editor)
            Dispatch(new ClientMessage.SettingsEdited(change(editor) with { Message = "" }));
    }

    private Element SettingsView()
    {
        var editor = model.SettingsEditor!;
        var value = editor.Value;
        Element Flag(string label, bool selected, Func<ClientOptions, bool, ClientOptions> change, string key) =>
            CheckBox(selected, next => EditSettings(state => state with { Value = change(state.Value, next) }), label)
                .AutomationId(key);
        var shortcuts = Enumerable.Range(0, 5).Select(index => (Element)VStack(6,
            TextBox(value.Shortcuts[index], text => EditSettings(state => state with
            {
                Value = state.Value with { Shortcuts = state.Value.Shortcuts.SetItem(index, text) }
            }), header: $"F{index + 8}（通常）").AutomationId($"shortcut-{index + 8}"),
            TextBox(value.ListShortcuts[index], text => EditSettings(state => state with
            {
                Value = state.Value with { ListShortcuts = state.Value.ListShortcuts.SetItem(index, text) }
            }), header: $"F{index + 8}（リスト表示中）").AutomationId($"list-shortcut-{index + 8}")
        )).ToArray();
        var body = VStack(20,
            VStack(8, SubHeading("移動・魔法"),
                Flag("フルキー移動（789 UIO JKL M）", value.FullKeys, (o, b) => o with { FullKeys = b }, "settings-full-keys"),
                Flag("系統魔法の前に武器を外す", value.AutoUnequipSystemMagic,
                    (o, b) => o with { AutoUnequipSystemMagic = b }, "settings-unequip"),
                Flag("対象選択のない系統魔法の後に武器を装備し直す", value.AutoEquipSystemMagic,
                    (o, b) => o with { AutoEquipSystemMagic = b }, "settings-reequip"),
                Flag("マスター判定に関係なく超魔法メニューを表示する", value.MasterMagicMenu,
                    (o, b) => o with { MasterMagicMenu = b }, "settings-master")),
            VStack(8, SubHeading("マップ"),
                TextBox(editor.Zoom, text => EditSettings(state => state with { Zoom = text }),
                    header: "拡大率（50–400%）").AutomationId("settings-map-zoom"),
                Flag("通った地形を記録する拡張マップ・位置予測", value.Automap,
                    (o, b) => o with { Automap = b }, "settings-automap"),
                Flag("移動を滑らかに表示する（原典の実験的機能）", value.SmoothMove,
                    (o, b) => o with { SmoothMove = b }, "settings-smooth"),
                Flag("マップをログと同じ窓に表示する", value.SingleWindow,
                    (o, b) => o with { SingleWindow = b }, "settings-single-window")),
            VStack(8, SubHeading("ショートカット"),
                TextBlock(@"文字列中の \n は送信する行の区切りです。F8–F12は、リスト表示中の設定を別に使います。")
                    .TextWrapping(TextWrapping.Wrap),
                VStack(12, shortcuts),
                TextBox(value.AltGWord, text => EditSettings(state => state with
                    { Value = state.Value with { AltGWord = text } }), header: "Alt+Gで送る言葉").AutomationId("settings-alt-g")),
            VStack(8, SubHeading("ログ"),
                TextBox(value.LogFontName, text => EditSettings(state => state with
                    { Value = state.Value with { LogFontName = text } }), header: "ログのフォント（空欄で既定）")
                    .AutomationId("settings-log-font"),
                TextBox(editor.FontSize, text => EditSettings(state => state with { FontSize = text }),
                    header: "ログの文字サイズ（0–64px・0で既定）").AutomationId("settings-log-font-size"),
                Flag("ログに時刻を表示する", value.LogDispTime, (o, b) => o with { LogDispTime = b }, "settings-time"),
                Flag("自動保存するログをHTML形式にする", value.LogSaveHtml,
                    (o, b) => o with { LogSaveHtml = b }, "settings-html"),
                Flag("コピー時にPHIタグを除く", value.LogTagDeleteCopy,
                    (o, b) => o with { LogTagDeleteCopy = b }, "settings-copy-tags"),
                Flag("入力欄へコピーする場合もPHIタグを除く", value.LogTagDeleteAlways,
                    (o, b) => o with { LogTagDeleteAlways = b }, "settings-copy-input-tags"),
                Flag("コピー時に時刻を除く", value.LogTimeDelete,
                    (o, b) => o with { LogTimeDelete = b }, "settings-copy-time"),
                Flag("コピー時に発言者名を除く", value.LogNameDelete,
                    (o, b) => o with { LogNameDelete = b }, "settings-copy-name"),
                Flag("コピーしたタグを無効にする", value.LogTagCancel,
                    (o, b) => o with { LogTagCancel = b }, "settings-cancel-tags")),
            VStack(8, SubHeading("通知"),
                TextBox(value.SoundFolder, text => EditSettings(state => state with
                    { Value = state.Value with { SoundFolder = text } }),
                    header: "効果音を使うPhillyフォルダー（空欄で規則を解除）").AutomationId("settings-sound-folder"),
                TextBlock("Config内のSoundRegexp.iniとSoundFile.iniを読み、指定したローカルWAVを使います。")
                    .TextWrapping(TextWrapping.Wrap),
                Flag("設定した効果音を鳴らす", value.SoundPlay, (o, b) => o with { SoundPlay = b }, "settings-sound"),
                Flag("背面時に発言・privをタスクバーで知らせる", value.TaskBarFlash,
                    (o, b) => o with { TaskBarFlash = b }, "settings-taskbar")),
            VStack(8, SubHeading("Phillyの設定を取り込む"),
                TextBlock("Philly.iniの移動・ショートカット・ログ・自動装備の項目を編集画面へ取り込みます。" +
                    "キャラクターIDは読み込まず、接続もしません。「保存」で適用します。").TextWrapping(TextWrapping.Wrap),
                TextBox(editor.ImportPath, text => EditSettings(state => state with { ImportPath = text }),
                    header: "このPCのPhilly.iniの絶対パス").AutomationId("settings-import-path"),
                Button("設定を取り込む", () => _ = PerformAsync(ImportSettingsAsync))
                    .IsEnabled(!busy).AutomationId("settings-import")),
            TextBlock(editor.Message).TextWrapping(TextWrapping.Wrap).AutomationId("settings-message")
        ).MaxWidth(720);
        return FlexColumn(
            HStack(12, Heading("設定"), Button("閉じる（未保存の変更を破棄）",
                () => Dispatch(new ClientMessage.SettingsEdited(null))).IsEnabled(!busy)).Flex(shrink: 0),
            TextBlock("設定はこのPCに保存します。変更だけでは送信・接続しません。")
                .TextWrapping(TextWrapping.Wrap).Flex(shrink: 0),
            ScrollViewer(body).Flex(grow: 1, basis: 0),
            HStack(12, Button(busy ? "保存しています…" : "設定を保存", () => _ = PerformAsync(SaveSettingsAsync))
                    .IsEnabled(!busy).AutomationId("settings-save"),
                Button("既定値に戻して編集", () => Dispatch(new ClientMessage.SettingsEdited(
                    new(new(), "100", "0")))).IsEnabled(!busy).AutomationId("settings-defaults")).Flex(shrink: 0)
        ).AutomationId("settings-view").FlexPadding(12);
    }
}
