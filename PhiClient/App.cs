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
    Title = "PHI Client", Width = 1200, Height = 850,
    PersistenceId = "phi-main", PersistPlacement = true
}, configure: host => host.Reconciler.Pool.Enabled = false);

partial class App : Component
{
    public override Element Render()
    {
        var (snapshot, update) = UseReducer(model);
        model = snapshot;
        publish = next => update(_ => next);
        refresh = Refresh;
        UseEffect(StartServices, Array.Empty<object>());
        UseEffect(SyncMapSplitter, model.Options.MapPaneWidth, windowWidth, active?.Model.Priv.Visible ?? false);

        Element content = model.SettingsEditor is not null ? SettingsView().Flex(grow: 1, basis: 0) :
            active is null || model.Worlds.Visible
            ? ScrollViewer(WorldDirectoryView())
                .OnMount(element => AttachReadableScroll((Microsoft.UI.Xaml.Controls.ScrollViewer)element))
                .AutomationId("world-directory").Flex(grow: 1, basis: 0)
            : TabView(sessions.IndexOf(active), index =>
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
        var sidebarVisible = model.SettingsEditor is null && (showCharacters || active is null);
        var body = windowWidth < 900 && model.SettingsEditor is null && sidebarVisible
            ? (Element)ScrollViewer(VStack(16, sidebarVisible ? CharacterSidebar() : Empty(), WorldDirectoryView()))
                .OnMount(element => AttachReadableScroll((Microsoft.UI.Xaml.Controls.ScrollViewer)element))
                .AutomationId("registration-scroll").Flex(grow: 1, basis: 0).Padding(16)
            : FlexRow(sidebarVisible ? CharacterSidebar() : Empty(), content)
                .Flex(grow: 1, basis: 0).FlexPadding(16);
        return FlexColumn(
            TitleBar("PHI Client").Flex(shrink: 0),
            AppMenu().Flex(shrink: 0),
            TextBlock(notice).TextWrapping(TextWrapping.Wrap).Padding(16, 8).AutomationId("notice").Flex(shrink: 0),
            body,
            DialogView()
        ).Backdrop(BackdropKind.Mica).OnPreviewKeyDown(OnGameKeyDown)
            .OnMount(element =>
            {
                var root = (FrameworkElement)element;
                AttachNotificationWindow(root);
                AttachWindowLayout(root);
            });
    }

    private Element CharacterSidebar() => ScrollViewer(VStack(10,
        active is not null ? Button("ゲーム画面に戻る", () =>
        {
            showCharacters = false;
            Dispatch(new ClientMessage.WorldVisibilityChanged(false));
        }).AutomationId("return-to-game") : Empty(),
        Heading("キャラクター"), Caption("保存済みキャラクター"),
        VStack(6, characters.Select(CharacterRow).ToArray()),
        Button("Phillyの登録を取り込む…", ImportPhillyProfiles)
            .IsEnabled(loaded && !busy).AutomationId("import-philly-profiles"),
        SubHeading(creating ? "キャラクターを新規作成" : "既存のキャラクターIDを登録"),
        Button("世界一覧から接続先を選ぶ", () => Dispatch(new ClientMessage.WorldVisibilityChanged(true)))
            .AutomationId("show-worlds"),
        TextBox(name, value => name = value, header: "表示名").AutomationId("character-name"),
        TextBox(host, value => host = value, header: "ゲームサーバー（ホスト名 / IPアドレス）").AutomationId("server-host"),
        TextBox(port, value => port = value, header: "TCPポート").AutomationId("server-port"),
        creating
            ? VStack(8,
                RegistrationGraphicsView(),
                PasswordBox(password, value => password = value, "パスワード（6文字・空欄で自動生成）")
                    .AutomationName("パスワード。空欄の場合は作成時に6文字を自動生成").AutomationId("new-password"),
                TextBox(image, value =>
                {
                    image = value;
                    _ = PrepareRegistrationPreviewAsync();
                }, header: "作成時のキャラクター画像の番号（0から数えます）").AutomationId("initial-image"),
                TextBox(email, value => email = value, header: "メールアドレス（任意）").AutomationId("registration-email"),
                Caption("指定した世界にキャラクターを作成します。"))
            : PasswordBox(id, value => id = value, "キャラクターID")
                .AutomationName("キャラクターID").AutomationId("character-id"),
        Button(creating ? "世界で作成して保存" : "キャラクターを保存",
            () => _ = PerformAsync(SaveCharacterAsync)).IsEnabled(loaded && !busy).AutomationId("save-character"),
        Button(creating ? "既存のキャラクターIDを登録する" : "キャラクターを新規作成する", () =>
        {
            creating = !creating;
            password = "";
        }).IsEnabled(!busy).AutomationId("registration-mode"),
        TextBlock("PHIでは、認証情報とゲームデータを暗号化せずにTCPで送信します。信頼できるゲームサーバーだけを利用してください。")
            .TextWrapping(TextWrapping.Wrap).Width(300),
        Button("設定フォルダーを開く", () => _ = PerformAsync(OpenSettingsFolderAsync))
    ).Width(300)).Width(300).Flex(shrink: 0);

    private Element CharacterRow(SavedCharacter character) => VStack(4,
        TextBlock(character.Name), Caption($"{character.Host}:{character.Port}"),
        HStack(6,
            Button(sessions.Any(s => s.Character.Key == character.Key) ? "表示" : "接続",
                () => _ = PerformAsync(() => OpenAsync(character)))
                .IsEnabled(!busy).AutomationId($"connect-{character.Key}"),
            Button(pendingDelete == character.Key ? "削除を確認" : "削除", () => DeleteCharacter(character))
                .IsEnabled(!busy).AutomationId($"delete-{character.Key}")));
}
