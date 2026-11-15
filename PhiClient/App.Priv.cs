using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using Microsoft.UI.Xaml;
using PhiClient;
using PhiClient.Core;
using static Microsoft.UI.Reactor.Factories;

partial class App
{
    private Element PrivView(GameSession session, bool fill = false)
    {
        var priv = session.Model.Priv;
        if (!priv.Visible) return Empty();
        var current = priv.Current;
        var users = session.Model.Users.Where(user => user.Name != session.Model.PlayerName).ToArray();
        var view = FlexColumn(
            HStack(8, SubHeading("priv（内緒話）"),
                Button("閉じる", () => session.Dispatch(new GameMessage.PrivVisibilityChanged(false)))
                    .AutomationId("priv-close")).Flex(shrink: 0),
            ListView(current is null ? -1 : priv.Conversations.IndexOf(current), index =>
                {
                    if (index >= 0 && index < priv.Conversations.Length)
                        session.Dispatch(new GameMessage.PrivSelected(priv.Conversations[index].Key));
                }, priv.Conversations.Select((conversation, index) => (Element)TextBlock(
                    string.Join("・", conversation.To) + (conversation.Unread ? "（未読）" : ""))
                    .TextWrapping(TextWrapping.Wrap).AutomationId($"priv-conversation-{index}")).ToArray())
                .MaxHeight(110).IsEnabled(!priv.Sending).AutomationId("priv-conversations").Flex(shrink: 0),
            ScrollViewer(Empty()).WithKey($"priv-{current?.Key ?? "none"}")
                .OnMount(element =>
                {
                    var viewer = (Microsoft.UI.Xaml.Controls.ScrollViewer)element;
                    viewer.HorizontalScrollMode = Microsoft.UI.Xaml.Controls.ScrollMode.Disabled;
                    viewer.HorizontalScrollBarVisibility = Microsoft.UI.Xaml.Controls.ScrollBarVisibility.Disabled;
                    viewer.HorizontalContentAlignment = HorizontalAlignment.Stretch;
                    var presenter = new LogPresenter((rows, toInput) => CopyLog(session, rows, toInput, privateInput: true),
                        OpenLogLink, ReportLogImageFailure);
                    presenter.Attach("priv-log", "選択した相手との内緒話");
                    presenter.Update(current?.Entries ?? [], model.Options, model.LogImages);
                    viewer.Content = presenter.Surface;
                    viewer.Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.White);
                    session.PrivPresenter = presenter;
                }).Flex(grow: 1, basis: 0),
            TextBox(priv.Draft, text => session.Dispatch(new GameMessage.PrivDraftChanged(text)),
                    header: current is null ? "一覧で相手を選んでください" : $"{string.Join("・", current.To)} へ")
                .IsEnabled(current is not null).AutomationId("priv-input")
                .OnMount(element => AttachPrivInput(session, (Microsoft.UI.Xaml.Controls.TextBox)element))
                .OnPreviewKeyDown((_, args) =>
                {
                    if (args.Key != Windows.System.VirtualKey.Enter || session.PrivComposing || session.PrivCommitting) return;
                    if (!args.KeyStatus.WasKeyDown) SendPriv(session);
                    args.Handled = true;
                }).OnKeyUp((_, args) =>
                    { if (args.Key == Windows.System.VirtualKey.Enter) session.PrivCommitting = false; })
                .Flex(shrink: 0),
            Button(priv.SendStage switch
                {
                    PrivSendStage.Confirming => "返信先を確認しています…",
                    PrivSendStage.Preparing => "相手の番号を確認しています…",
                    PrivSendStage.Transmitting => "内緒話を送信しています…",
                    _ => "内緒話を送信"
                }, () => SendPriv(session))
                .IsEnabled(session.Connected && !session.Model.Transferring && !priv.Sending &&
                    session.Model.Master is null && current is not null && priv.Draft.Length > 0).AutomationId("priv-send").Flex(shrink: 0),
            Expander("この世界の相手を選ぶ", VStack(4,
            HStack(6, TextBlock(session.Model.UserRefresh.Started is null ? "この世界の相手" : "相手の一覧を読み込んでいます…"),
                Button("一覧を更新", () => _ = SessionOperationAsync(session, () => RefreshUsersAsync(session)))
                    .IsEnabled(session.Connected && !session.Model.Transferring && session.Model.UserRefresh.Started is null)
                    .AutomationId("priv-refresh")),
            ListView(-1, _ => { }, users.Select(user => (Element)CheckBox(priv.Picked.Contains(user.Name),
                    value => session.Dispatch(new GameMessage.PrivPicked(user.Name, value)), user.Name)
                    .AutomationId($"priv-user-{user.Number}"))
                .ToArray()).MaxHeight(160).AutomationId("priv-users"),
            Button("選んだ相手との会話を開く", () =>
                    session.Dispatch(new GameMessage.PrivOpened(priv.Picked.ToArray())))
                .IsEnabled(priv.Picked.Count > 0 && !priv.Sending).AutomationId("priv-open-conversation")))
                .Flex(shrink: 0)
        ).FlexPadding(4).AutomationId("priv-panel");
        return fill ? view : view.Width(300);
    }
}
