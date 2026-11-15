using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using PhiClient;
using PhiClient.Core;
using static Microsoft.UI.Reactor.Factories;

ReactorApp.Run<App>("PHI Client", width: 1200, height: 850);

partial class App : Component
{
    public override Element Render()
    {
        var (snapshot, update) = UseReducer(model);
        model = snapshot;
        publish = next => update(_ => next);
        refresh = Refresh;
        UseEffect(StartServices, Array.Empty<object>());

        Element content = model.SettingsEditor is not null ? SettingsView().Flex(grow: 1, basis: 0) :
            active is null || model.Worlds.Visible
            ? ScrollViewer(WorldDirectoryView()).AutomationId("world-directory").Flex(grow: 1, basis: 0)
            : TabView(sessions.IndexOf(active), index =>
                {
                    if (index >= 0 && index < sessions.Count) SelectSession(sessions[index]);
                }, sessions.Select(s => Tab(
                    s.Character.Name + (s.Connected ? "" : s.Connecting ? " (...)" : " (offline)"),
                    s == active ? GameView(s).WithKey(s.Character.Key) : Empty())).ToArray())
                .TabCloseRequested(index =>
                {
                    if (index >= 0 && index < sessions.Count) CloseTab(sessions[index]);
                }).IsAddTabButtonVisible(false).CanDragTabs(false).CanReorderTabs(false)
                .AutomationId("character-tabs").Flex(grow: 1, basis: 0);
        return FlexColumn(
            TitleBar("PHI Client").Flex(shrink: 0),
            AppMenu().Flex(shrink: 0),
            TextBlock(notice).TextWrapping(TextWrapping.Wrap).Padding(16, 8).AutomationId("notice").Flex(shrink: 0),
            FlexRow(model.SettingsEditor is null && (showCharacters || active is null) ? CharacterSidebar() : Empty(), content)
                .Flex(grow: 1, basis: 0).FlexPadding(16),
            DialogView()
        ).Backdrop(BackdropKind.Mica).OnPreviewKeyDown(OnGameKeyDown);
    }

    private Element CharacterSidebar() => ScrollViewer(VStack(10,
        Heading("Characters"), Caption("Saved profiles"),
        VStack(6, characters.Select(CharacterRow).ToArray()),
        Button("Phillyの登録を取り込む…", ImportPhillyProfiles)
            .IsEnabled(loaded && !busy).AutomationId("import-philly-profiles"),
        SubHeading(creating ? "Create a new character" : "Register an existing ID"),
        Button("世界一覧から接続先を選ぶ", () => Dispatch(new ClientMessage.WorldVisibilityChanged(true)))
            .AutomationId("show-worlds"),
        TextBox(name, value => name = value, header: "Display name").AutomationId("character-name"),
        TextBox(host, value => host = value, header: "Server hostname / IP").AutomationId("server-host"),
        TextBox(port, value => port = value, header: "TCP port").AutomationId("server-port"),
        creating
            ? VStack(8,
                RegistrationGraphicsView(),
                PasswordBox(password, value => password = value, "パスワード（6文字・空欄で自動生成）")
                    .AutomationName("パスワード。空欄の場合は作成時に6文字を自動生成").AutomationId("new-password"),
                TextBox(image, value =>
                {
                    image = value;
                    _ = PrepareRegistrationPreviewAsync();
                }, header: "初期画像の番号（一覧の0番から）").AutomationId("initial-image"),
                TextBox(email, value => email = value, header: "メールアドレス（任意）").AutomationId("registration-email"),
                Caption("Creates a character on the specified server."))
            : PasswordBox(id, value => id = value, "Character ID")
                .AutomationName("Character ID").AutomationId("character-id"),
        Button(creating ? "Create on server and save" : "Save character",
            () => _ = PerformAsync(SaveCharacterAsync)).IsEnabled(loaded && !busy).AutomationId("save-character"),
        Button(creating ? "Use an existing ID instead" : "Create a new character instead", () =>
        {
            creating = !creating;
            password = "";
        }).IsEnabled(!busy),
        TextBlock("PHI sends credentials and game data over unencrypted TCP. Use only a trusted server.")
            .TextWrapping(TextWrapping.Wrap).Width(300),
        Button("Open settings folder", () => _ = PerformAsync(OpenSettingsFolderAsync))
    ).Width(300)).Width(300).Flex(shrink: 0);

    private Element CharacterRow(SavedCharacter character) => VStack(4,
        TextBlock(character.Name), Caption($"{character.Host}:{character.Port}"),
        HStack(6,
            Button(sessions.Any(s => s.Character.Key == character.Key) ? "Show" : "Connect",
                () => _ = PerformAsync(() => OpenAsync(character)))
                .IsEnabled(!busy).AutomationId($"connect-{character.Key}"),
            Button(pendingDelete == character.Key ? "Confirm delete" : "Delete", () => DeleteCharacter(character))
                .IsEnabled(!busy).AutomationId($"delete-{character.Key}")));
}
