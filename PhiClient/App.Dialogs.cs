using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using PhiClient.Core;
using static Microsoft.UI.Reactor.Factories;

partial class App
{
    private TaskCompletionSource<DialogAnswer>? dialogCompletion;

    private async Task<bool> SessionDialogAsync(string title, string text, string? accept = null) =>
        (await RequestDialogAsync(new(title, text, accept))).Accepted;

    private Task<DialogAnswer> RequestDialogAsync(DialogModel dialog)
    {
        if (dialogOpen) throw new InvalidOperationException("Close the current dialog first.");
        dialogCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatch(new ClientMessage.DialogChanged(dialog));
        return dialogCompletion.Task;
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
                TextBox(dialog.Value, value => Dispatch(new ClientMessage.DialogEdited(value)), header: dialog.InputLabel)
                    .AutomationId(dialog.InputId));
        return ContentDialog(dialog?.Title ?? "", body, dialog?.Accept ?? "") with
        {
            IsOpen = dialog is not null,
            CloseButtonText = dialog?.Accept is null ? "Close" : "Cancel",
            DefaultButton = dialog?.Accept is null ? ContentDialogButton.Close : ContentDialogButton.Primary,
            OnClosed = CompleteDialog
        };
    }
}
