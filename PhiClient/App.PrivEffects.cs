using PhiClient;
using PhiClient.Core;

partial class App
{
    private void TogglePriv(GameSession session)
    {
        var open = !session.Model.Priv.Visible;
        session.Dispatch(new GameMessage.PrivVisibilityChanged(open));
        if (open) _ = SessionOperationAsync(session, () => RefreshUsersAsync(session));
    }

    private Task RefreshUsersAsync(GameSession session)
    {
        if (session.UsersRefreshTask is { IsCompleted: false } waiting) return waiting;
        if (!session.Model.UserRefresh.CanRequest(Environment.TickCount64)) return Task.CompletedTask;
        return session.UsersRefreshTask = RefreshUsersCoreAsync(session);
    }

    private async Task RefreshUsersCoreAsync(GameSession session)
    {
        var connection = session.Connection;
        if (!session.Connected || connection is null || session.Model.Transferring)
            throw new InvalidOperationException("世界に接続してから相手の一覧を更新してください。");
        var generation = connection.TransportVersion;
        session.Dispatch(new GameMessage.UsersRefreshStarted(Environment.TickCount64));
        try
        {
            await connection.SendAsync(["#ulist"], worldLifetime.Token);
            while (true)
            {
                if (session.Connection != connection || !session.Connected || session.Model.Transferring ||
                    connection.TransportVersion != generation)
                    throw new InvalidOperationException("接続先が変わったため、相手の一覧の更新を中止しました。");
                var game = session.Model;
                if (game.UserRefresh.Complete(Environment.TickCount64, game.UsersRevision, game.UsersUpdatedAt))
                {
                    if (game.UsersRevision == game.UserRefresh.Revision)
                        throw new IOException("60秒以内に相手の一覧が届きませんでした。接続状態を確認して再度更新してください。");
                    return;
                }
                await Task.Delay(20, worldLifetime.Token);
            }
        }
        finally
        {
            if (session.Connection == connection && connection.TransportVersion == generation)
                session.Dispatch(new GameMessage.UsersRefreshFinished());
        }
    }

    private void SendPriv(GameSession session) => _ = SessionOperationAsync(session, async () =>
    {
        var priv = session.Model.Priv;
        if (session.PrivComposing || session.PrivCommitting || priv.Sending ||
            priv.Current is not { } conversation || priv.Draft.Length == 0) return;
        var connection = session.Connection;
        if (connection is null || !session.Connected || session.Model.Transferring)
            throw new InvalidOperationException("接続が完了してから内緒話を送信してください。");
        if (session.Model.Master is not null)
            throw new InvalidOperationException("超魔法の手順が終わってから、内緒話を送信してください。");
        var generation = connection.TransportVersion;
        session.Dispatch(new GameMessage.PrivSendingChanged(true));
        try
        {
            if (conversation.Confirm)
            {
                session.Dispatch(new GameMessage.PrivSendStageChanged(PrivSendStage.Confirming));
                if (!await SessionDialogAsync("内緒話の返信先を確認",
                    $"{string.Join("・", conversation.To)}の全員へ返信します。\n" +
                    "この会話は、受信メッセージのタグから作られました。全員へ返信してよろしいですか？", "全員へ返信"))
                    return;
                if (session.Connection != connection || !session.Connected || connection.TransportVersion != generation)
                    throw new InvalidOperationException("確認中に接続先が変わったため、内緒話は送信しませんでした。");
                session.Dispatch(new GameMessage.PrivConfirmed(conversation.Key));
            }
            session.Dispatch(new GameMessage.PrivSendStageChanged(PrivSendStage.Preparing));
            await RefreshUsersAsync(session);
            if (session.Connection != connection || !session.Connected || connection.TransportVersion != generation)
                throw new InvalidOperationException("確認中に接続先が変わったため、内緒話は送信しませんでした。");
            var send = Priv.Send(conversation.To, priv.Draft, session.Model.Users, session.Model.PlayerName);
            foreach (var missing in send.Failed)
                session.Append($"「{missing}」さんの番号が見つからず、内緒話を送信できませんでした。");
            if (send.Lines.Count == 0) return;
            session.Dispatch(new GameMessage.PrivSendStageChanged(PrivSendStage.Transmitting));
            await ExecuteAsync(session, GameInput.Command(send.Lines.ToArray()));
            session.Dispatch(new GameMessage.PrivSent(priv.Draft));
        }
        finally { session.Dispatch(new GameMessage.PrivSendingChanged(false)); }
    });

    private void AttachPrivInput(GameSession session, Microsoft.UI.Xaml.Controls.TextBox box)
    {
        session.PrivInput = box;
        session.PrivComposing = session.PrivCommitting = false;
        box.TextCompositionStarted += (_, _) => session.PrivComposing = true;
        box.TextCompositionEnded += (_, _) =>
        {
            session.PrivComposing = false;
            session.PrivCommitting = Down(Windows.System.VirtualKey.Enter);
        };
    }
}
