using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PhiClient.Core;
using static Microsoft.UI.Reactor.Factories;

partial class App
{
    private TaskCompletionSource<DialogAnswer>? dialogCompletion;
    private readonly SemaphoreSlim dialogRequests = new(1, 1);

    private async Task<bool> SessionDialogAsync(string title, string text, string? accept = null) =>
        (await RequestDialogAsync(new(title, text, accept))).Accepted;

    private async Task<DialogAnswer> RequestDialogAsync(DialogModel dialog, CancellationToken token = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(token, worldLifetime.Token);
        await dialogRequests.WaitAsync(linked.Token);
        try
        {
            var completion = new TaskCompletionSource<DialogAnswer>(TaskCreationOptions.RunContinuationsAsynchronously);
            dialogCompletion = completion;
            using var registration = linked.Token.Register(() => dispatcher?.TryEnqueue(() =>
            {
                if (dialogCompletion == completion) Dispatch(new ClientMessage.DialogChanged(null));
            }));
            Dispatch(new ClientMessage.DialogChanged(dialog));
            return await completion.Task;
        }
        finally { dialogRequests.Release(); }
    }

    private void CompleteDialog(ContentDialogResult result)
    {
        var completion = dialogCompletion;
        var alternative = result == ContentDialogResult.Secondary && model.Dialog?.Alternative is not null;
        var answer = new DialogAnswer(result == ContentDialogResult.Primary || alternative,
            model.Dialog?.Value ?? "", alternative);
        dialogCompletion = null;
        Dispatch(new ClientMessage.DialogChanged(null));
        completion?.TrySetResult(answer);
    }

    private Element DialogView()
    {
        var dialog = model.Dialog;
        Element body = dialog is null ? Empty() : VStack(12,
            TextBlock(dialog.Text).TextWrapping(TextWrapping.Wrap).IsTextSelectionEnabled(true),
            dialog.InputLabel is null ? Empty() :
                dialog.Secret ? VStack(8,
                    TextBox(dialog.Reveal ? dialog.Value : new string('●', dialog.Value.Length), _ => { },
                            header: dialog.InputLabel)
                        .IsReadOnly(true).AutomationId(dialog.InputId),
                    CheckBox(dialog.Reveal, value => Dispatch(new ClientMessage.DialogRevealChanged(value)),
                        dialog.InputId == "pending-registration-id" ? "IDを表示する" : "パスワードを表示する")
                        .AutomationId("reveal-created-password")) :
                TextBox(dialog.Value, value => Dispatch(new ClientMessage.DialogEdited(value)), header: dialog.InputLabel)
                    .AutomationId(dialog.InputId));
        return ContentDialog(dialog?.Title ?? "", body, dialog?.Accept ?? "") with
        {
            IsOpen = dialog is not null,
            CloseButtonText = dialog?.Accept is null ? "閉じる" : "キャンセル",
            SecondaryButtonText = dialog?.Alternative ?? "",
            DefaultButton = dialog?.Accept is null ? ContentDialogButton.Close : ContentDialogButton.Primary,
            OnClosed = CompleteDialog
        };
    }
}
