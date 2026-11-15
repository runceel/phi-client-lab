using PhiClient;
using PhiClient.Core;

partial class App
{
    private void ApplyChatEdit(GameSession session, (string Text, int Caret) edit)
    {
        if (session.Input != edit.Text) session.Dispatch(new GameMessage.InputChanged(edit.Text));
        if (active == session && inputControl?.XamlRoot is not null)
        {
            var input = inputControl;
            var revision = session.Model.InputRevision;
            if (input.Text != edit.Text) input.Text = edit.Text;
            input.DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
            {
                if (active == session && inputControl == input && input.IsLoaded &&
                    session.Model.InputRevision == revision && input.Text == edit.Text &&
                    !composing && !committingComposition)
                    input.Select(edit.Caret, 0);
            });
        }
    }

    private void Submit(GameSession session, bool party = false, bool shout = false)
    {
        if (composing || committingComposition) return;
        var now = Environment.TickCount64;
        var state = session.Model;
        if (!GameInput.ChatReady(now, state.LastChatAttempt)) return;
        session.Dispatch(new GameMessage.ChatAttempted(now));
        var original = state.Input;
        var caret = active == session ? inputControl?.SelectionStart ?? original.Length : original.Length;
        var loud = (shout || state.Loud) && !state.AllTabSpeech;
        var text = GameInput.ChatLine(original, caret, party, loud);
        if (text is null) return;
        _ = SessionOperationAsync(session, async () =>
        {
            if (!session.Connected || session.Model.Transferring || session.PreparingMagic || session.Model.Master is not null)
                throw new InvalidOperationException("接続と実行中の操作が終わってから発言してください。");
            var targets = new List<(GameSession Session, PhiConnection Connection, int Generation)>();
            foreach (var target in state.AllTabSpeech ? sessions.ToArray() : new[] { session })
                if (target.Connected && target.Connection is { } connection)
                    targets.Add((target, connection, connection.TransportVersion));
            if (targets.Count == 0)
                throw new InvalidOperationException("接続中のタブを選んでから発言してください。");
            if (state.AllTabSpeech)
            {
                if (!await SessionDialogAsync("全タブへの発言",
                    $"接続中の{targets.Count}タブへ同じ内容を送ります。", "送信"))
                {
                    session.Dispatch(new GameMessage.AllTabSpeechChanged(false));
                    return;
                }
            }
            else if (loud && !state.RepeatLoud)
            {
                var answer = await RequestDialogAsync(new("大声で発言",
                    "この世界にいる全員へ発言が届きます。", "今回だけ送信", Alternative: "すべて大声"));
                session.Dispatch(new GameMessage.LoudChanged(answer.Accepted && answer.Alternative,
                    answer.Accepted && answer.Alternative));
                if (!answer.Accepted) return;
            }
            if (!sessions.Contains(session) || targets.Any(target => target.Session.Connection != target.Connection ||
                !target.Session.Connected || target.Session.Model.Transferring || target.Session.PreparingMagic ||
                target.Session.Model.Master is not null || target.Connection.TransportVersion != target.Generation))
                throw new InvalidOperationException("確認中に接続状態が変わりました。送信先を確認してから再度発言してください。");
            foreach (var target in targets)
            {
                target.Session.Dispatch(new GameMessage.SelectionCleared());
                await target.Connection.SendPinnedAsync([text], target.Generation);
            }
            if (active == session && (composing || committingComposition)) return;
            var draft = session.Model;
            session.Dispatch(new GameMessage.ChatSent(state.InputRevision, caret));
            if (session.Model.InputRevision != draft.InputRevision)
                ApplyChatEdit(session, GameInput.AfterChatSend(original, caret, draft.TalkPrefix));
        });
    }

    private void ChooseTalkPrefix(GameSession session) => _ = SessionOperationAsync(session, async () =>
    {
        var answer = await RequestDialogAsync(new("発言の前に付ける言葉",
            "送信後の新しい行を、この言葉で始めます。空欄で解除できます。",
            "設定", "追加する言葉", session.Model.TalkPrefix, "talk-prefix"));
        if (!answer.Accepted || !sessions.Contains(session)) return;
        PhiWire.ValidateLine(answer.Value);
        session.Dispatch(new GameMessage.TalkPrefixChanged(answer.Value));
        var caret = active == session ? inputControl?.SelectionStart ?? session.Input.Length : session.Input.Length;
        ApplyChatEdit(session, GameInput.WithChatPrefix(session.Input, caret, answer.Value));
    });
}
