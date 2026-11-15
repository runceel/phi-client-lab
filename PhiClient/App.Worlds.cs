using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using PhiClient.Core;
using static Microsoft.UI.Reactor.Factories;

partial class App
{
    private WorldDirectoryClient? worldClient;
    private readonly CancellationTokenSource worldLifetime = new();
    private WorldDirectoryClient WorldsClient => worldClient ??= new(Path.Combine(store.Folder, "world-directory"));

    private Element WorldDirectoryView()
    {
        var worlds = model.Worlds;
        var rows = worlds.Items.Select((world, index) => (world, index)).Where(row =>
            row.world.Name.Contains(worlds.Filter, StringComparison.OrdinalIgnoreCase) ||
            row.world.Host.Contains(worlds.Filter, StringComparison.OrdinalIgnoreCase)).ToArray();
        return VStack(10,
            Heading("接続先の世界"),
            TextBlock("選んだ世界のサーバーとポートを、登録欄に入力します。この操作は接続先の入力だけを行います。")
                .TextWrapping(TextWrapping.Wrap),
            InputBox(worlds.Filter, value => Dispatch(new ClientMessage.WorldFilterChanged(value)), header: "世界名・アドレスで絞り込み")
                .AutomationId("world-filter"),
            Caption($"取得元: {worlds.Source}").TextWrapping(TextWrapping.Wrap).AutomationId("world-source"),
            TextBlock(worlds.Message).TextWrapping(TextWrapping.Wrap).AutomationId("world-status"),
            Caption($"表示: {rows.Length} / {worlds.Items.Length} 件").AutomationId("world-count"),
            ScrollViewer(VStack(8, rows.Select(row => VStack(4,
                TextBlock(row.world.Name).TextWrapping(TextWrapping.Wrap),
                Caption($"{row.world.Host}:{row.world.Port}"),
                Button("登録欄に入力", () => Dispatch(new ClientMessage.WorldSelected(row.world)))
                    .IsEnabled(loaded && !busy && !worlds.Loading).AutomationId($"world-choose-{row.index}")
            ).WithKey($"{row.index}:{row.world.Host}:{row.world.Port}")).ToArray())).Height(280).AutomationId("world-list"),
            InputBox(worlds.Url, value => Dispatch(new ClientMessage.WorldUrlChanged(value)), header: "一覧の配布 URL（LZH / CP932 INI）")
                .IsEnabled(!busy).AutomationId("world-source-url"),
            HStack(8,
                Button("HTTP 一覧を更新", () => _ = LoadWorldsAsync(import: false))
                    .IsEnabled(loaded && !busy).AutomationId("world-refresh"),
                Button("INI ファイルから取り込む", () => _ = LoadWorldsAsync(import: true))
                    .IsEnabled(loaded && !busy).AutomationId("world-import")),
            TextBlock((worlds.Url.Trim() == WorldList.DownloadUrl
                ? "wphiの世界一覧は、HTTPで配布されるresist.lzhから取得します。"
                : "指定した一覧の配布URLから、HTTPまたはHTTPSで世界一覧を取得します。") +
                "一覧の取得とゲームサーバーへの接続は別の操作です。" +
                "一覧の内容や、掲載サーバーの安全性・稼働状況は保証されません。")
                .TextWrapping(TextWrapping.Wrap),
            active is not null ? Button("ゲーム画面に戻る", () => Dispatch(new ClientMessage.WorldVisibilityChanged(false))) : Empty()
        ).Padding(24);
    }

    private Task LoadWorldsAsync(bool import) => PerformAsync(async () =>
    {
        var path = "";
        if (import)
        {
            var answer = await RequestDialogAsync(new("世界一覧を取り込む",
                "ローカルの resist.ini または worlds.ini の絶対パスを指定してください。ゲームサーバーへの接続は行いません。",
                "取り込む", "INI ファイル", "", "world-file"));
            if (!answer.Accepted) return;
            path = answer.Value.Trim();
        }
        Dispatch(new ClientMessage.WorldsLoading());
        try
        {
            var result = import
                ? await WorldsClient.ImportAsync(path, worldLifetime.Token)
                : await WorldsClient.RefreshAsync(model.Worlds.Url, worldLifetime.Token);
            if (!disposed) Dispatch(new ClientMessage.WorldsLoaded(result));
        }
        catch (Exception ex) when (IsExpected(ex) || ex is InvalidDataException or HttpRequestException or System.ComponentModel.Win32Exception)
        {
            if (!disposed) Dispatch(new ClientMessage.WorldsFailed(ex.Message));
        }
    });
}
