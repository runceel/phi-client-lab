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

    private async Task<DialogAnswer> RequestDialogAsync(DialogModel dialog)
    {
        await dialogRequests.WaitAsync(worldLifetime.Token);
        try
        {
            dialogCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Dispatch(new ClientMessage.DialogChanged(dialog));
            return await dialogCompletion.Task;
        }
        finally { dialogRequests.Release(); }
    }

    private void CompleteDialog(ContentDialogResult result)
    {
        var completion = dialogCompletion;
        var answer = new DialogAnswer(result == ContentDialogResult.Primary, model.Dialog?.Value ?? "");
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
                        "パスワードを表示する").AutomationId("reveal-created-password")) :
                TextBox(dialog.Value, value => Dispatch(new ClientMessage.DialogEdited(value)), header: dialog.InputLabel)
                    .AutomationId(dialog.InputId));
        return ContentDialog(dialog?.Title ?? "", body, dialog?.Accept ?? "") with
        {
            IsOpen = dialog is not null,
            CloseButtonText = dialog?.Accept is null ? "閉じる" : "キャンセル",
            DefaultButton = dialog?.Accept is null ? ContentDialogButton.Close : ContentDialogButton.Primary,
            OnClosed = CompleteDialog
        };
    }
}
