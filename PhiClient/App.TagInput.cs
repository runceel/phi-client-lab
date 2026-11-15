using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using PhiClient;
using PhiClient.Core;

partial class App
{
    private void AttachTagInput(TextBox input, GameSession session, bool sign = false)
    {
        if (sign) session.SignInput = input;
        var composingTag = false;
        input.TextCompositionStarted += (_, _) => composingTag = true;
        input.TextCompositionEnded += (_, _) => composingTag = false;
        var menu = new MenuFlyout();
        var editable = new List<MenuFlyoutItem>();
        MenuFlyoutItem Item(string text, string id, Action action)
        {
            var item = new MenuFlyoutItem { Text = text };
            AutomationProperties.SetAutomationId(item, (sign ? "sign-" : "chat-") + id);
            item.Click += (_, _) => action();
            editable.Add(item);
            return item;
        }
        menu.Items.Add(Item("元に戻す", "undo", input.Undo));
        menu.Items.Add(Item("切り取り", "cut", input.CutSelectionToClipboard));
        menu.Items.Add(Item("コピー", "copy", input.CopySelectionToClipboard));
        menu.Items.Add(Item("貼り付け", "paste", input.PasteFromClipboard));
        menu.Items.Add(Item("すべて選択", "select-all", input.SelectAll));
        menu.Items.Add(new MenuFlyoutSeparator());
        foreach (var group in TagEditing.Groups)
        {
            var submenu = new MenuFlyoutSubItem { Text = group.Caption };
            foreach (var tag in group.Tags)
                submenu.Items.Add(Item(tag.Caption, "tag-" + tag.Key, () =>
                {
                    if (composingTag || !sign && (composing || committingComposition))
                    {
                        notice = "変換を確定してからタグを選んでください。";
                        return;
                    }
                    if (!input.IsLoaded || !sessions.Contains(session)) return;
                    var edit = TagEditing.Wrap(input.Text, input.SelectionStart, input.SelectionLength, tag);
                    if (sign)
                    {
                        session.Dispatch(new GameMessage.BoardChanged(edit.Text));
                        input.Text = edit.Text;
                        input.Select(edit.Caret, 0);
                    }
                    else ApplyChatEdit(session, edit);
                    input.Focus(FocusState.Programmatic);
                }));
            menu.Items.Add(submenu);
        }
        menu.Opening += (_, _) =>
        {
            foreach (var item in editable)
            {
                var id = AutomationProperties.GetAutomationId(item);
                item.IsEnabled = !composingTag && (sign || !composing && !committingComposition) &&
                    (id.EndsWith("-undo", StringComparison.Ordinal) ? input.GetValue(TextBox.CanUndoProperty) is true :
                        id.EndsWith("-copy", StringComparison.Ordinal) || id.EndsWith("-cut", StringComparison.Ordinal)
                            ? input.SelectionLength > 0 :
                        !id.EndsWith("-select-all", StringComparison.Ordinal) || input.Text.Length > 0);
            }
        };
        input.ContextFlyout = menu;
        input.Unloaded += (_, _) =>
        {
            if (sign && session.SignInput == input) session.SignInput = null;
        };
    }

    private void ShowInputTags(GameSession session, bool sign = false)
    {
        var input = sign ? session.SignInput : active == session ? inputControl : null;
        if (input?.ContextFlyout is { } flyout && input.IsLoaded)
            flyout.ShowAt(input);
    }
}
