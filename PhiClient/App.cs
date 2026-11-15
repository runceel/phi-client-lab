using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using PhiClient;
using PhiClient.Core;
using static Microsoft.UI.Reactor.Factories;

// Preview.17 pooling made replaced settings controls inaccessible to native UIA.
ReactorApp.Run<App>(new WindowSpec
{
    Title = App.ProductName, Width = 1200, Height = 850,
    PersistenceId = "phi-main", PersistPlacement = true
}, configure: host => host.Reconciler.Pool.Enabled = false);

partial class App : Component
{
    internal const string ProductName = "PHI for Windows 11";

    public override Element Render()
    {
        var (snapshot, update) = UseReducer(model);
        model = snapshot;
        publish = next => update(_ => next);
        var (_, updateMapEffects) = UseReducer(0L);
        redrawMapEffects = () => updateMapEffects(frame => frame + 1);
        refresh = Refresh;
        UseEffect(StartServices, Array.Empty<object>());
        UseEffect(SyncMapSplitter, model.Options.MapPaneWidth, windowWidth, active?.Model.Priv.Visible ?? false);

        Element content = model.SettingsEditor is not null ? SettingsView().Flex(grow: 1, basis: 0) :
            model.Registration.Stage != RegistrationStage.None ? RegistrationView().Flex(grow: 1, basis: 0) :
            active is null || showCharacters || model.Worlds.Visible
            ? ScrollViewer(CharacterSidebar())
                .OnMount(element => AttachReadableScroll((Microsoft.UI.Xaml.Controls.ScrollViewer)element))
                .AutomationId("world-directory").Flex(grow: 1, basis: 0)
            : sessions.Count == 1 ? GameView(active).Flex(grow: 1, basis: 0) :
            TabView(sessions.IndexOf(active), index =>
                {
                    if (index >= 0 && index < sessions.Count) SelectSession(sessions[index]);
                }, sessions.Select(s => Tab(
                    s.Character.Name + (s.Connected ? "" : s.Connecting ? "（接続中）" : "（未接続）"),
                    s == active ? GameView(s).WithKey(s.Character.Key) : Empty())).ToArray())
                .TabCloseRequested(index =>
                {
                    if (index >= 0 && index < sessions.Count) CloseTab(sessions[index]);
                }).IsAddTabButtonVisible(false).CanDragTabs(false).CanReorderTabs(false)
                .AutomationId("character-tabs").Flex(grow: 1, basis: 0);
        var body = FlexRow(content).Flex(grow: 1, basis: 0).FlexPadding(8);
        return FlexColumn(
            MainTitleBar().Flex(shrink: 0),
            notice is "" or "設定を保存しました。" or "地図の表示設定を保存しました。" ? Empty() :
                HStack(6, TextBlock(notice).TextWrapping(TextWrapping.Wrap).MaxWidth(Math.Max(200, windowWidth - 100))
                        .AutomationId("notice"), Button("閉じる", () => notice = "").AutomationName("通知を閉じる"))
                    .Padding(8, 4).Flex(shrink: 0),
            body,
            DialogView()
        ).Backdrop(BackdropKind.Mica).OnPreviewKeyDown(OnGameKeyDown).OnPreviewKeyUp(OnGameKeyUp)
            .OnGotFocus(ObserveInputFocus)
            .OnMount(element =>
            {
                var root = (FrameworkElement)element;
                AttachNotificationWindow(root);
                AttachWindowLayout(root);
            });
    }

    private Element CharacterSidebar() => VStack(12,
        active is not null ? Button("ゲーム画面に戻る", () =>
        {
            showCharacters = false;
            pendingGameFocus = true;
            Dispatch(new ClientMessage.WorldVisibilityChanged(false));
        }).AutomationId("return-to-game") : Empty(),
        TextBlock("キャラクター").FontSize(20),
        VStack(6, characters.Select(CharacterRow).ToArray()),
        characters.Count == 0 ? TextBlock("保存済みのキャラクターはありません。新しく作るか、持っているIDを登録してください。")
            .TextWrapping(TextWrapping.Wrap) : Empty(),
        Button("キャラクターを新規作成する", () => BeginRegistration(true))
            .IsEnabled(loaded && !busy).AutomationId("registration-mode"),
        Button("既存のキャラクターIDを登録する", () => BeginRegistration(false))
            .IsEnabled(loaded && !busy).AutomationId("register-existing"),
        Button("Phillyの登録を取り込む…", ImportPhillyProfiles)
            .IsEnabled(loaded && !busy).AutomationId("import-philly-profiles"),
        TextBlock("PHIでは、認証情報とゲームデータを暗号化せずにTCPで送信します。信頼できるゲームサーバーだけを利用してください。")
            .TextWrapping(TextWrapping.Wrap),
        Button("設定フォルダーを開く", () => _ = PerformAsync(OpenSettingsFolderAsync))
    ).MaxWidth(720).HorizontalAlignment(HorizontalAlignment.Center);

    private Element CharacterRow(SavedCharacter character) => VStack(4,
        TextBlock(character.Name), Caption($"{character.Host}:{character.Port}"),
        HStack(6,
            Button(sessions.Any(s => s.Character.Key == character.Key) ? "表示" : "接続",
                () => _ = PerformAsync(() => OpenAsync(character)))
                .IsEnabled(!busy).AutomationId($"connect-{character.Key}"),
            Button(pendingDelete == character.Key ? "削除を確認" : "削除", () => DeleteCharacter(character))
                .IsEnabled(!busy).AutomationId($"delete-{character.Key}")));
}
