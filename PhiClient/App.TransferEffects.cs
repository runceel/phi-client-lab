using Microsoft.UI.Dispatching;
using PhiClient;
using PhiClient.Core;

partial class App
{
    private Task<bool> ConfirmWorldTransferAsync(GameSession session, PhiConnection connection,
        string targetHost, int targetPort, CancellationToken token)
    {
        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (dispatcher?.TryEnqueue(DispatcherQueuePriority.Normal, () => _ = ConfirmAsync()) != true)
            throw new InvalidOperationException("世界移動の確認画面を表示できません。");
        return completion.Task.WaitAsync(token);

        async Task ConfirmAsync()
        {
            try
            {
                if (disposed || session.Connection != connection || token.IsCancellationRequested)
                {
                    completion.TrySetResult(false);
                    return;
                }
                var answer = await RequestDialogAsync(new("別ホストへの世界移動",
                    $"移動先: {targetHost}:{targetPort}\n\n" +
                    "キャラクターIDをこの移動先へ送信します。移動先を信頼できる場合だけ続行してください。",
                    "移動する"), token);
                completion.TrySetResult(answer.Accepted);
            }
            catch (Exception ex) when (IsExpected(ex)) { completion.TrySetException(ex); }
        }
    }
}
