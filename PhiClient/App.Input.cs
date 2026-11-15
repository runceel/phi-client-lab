using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using PhiClient;
using PhiClient.Core;
using Windows.System;
using Windows.UI.Core;
using static Microsoft.UI.Reactor.Factories;

partial class App
{
    private static bool Down(VirtualKey key) =>
        InputKeyboardSource.GetKeyStateForCurrentThread(key).HasFlag(CoreVirtualKeyStates.Down);

    private void OnGameKeyDown(object sender, KeyRoutedEventArgs args)
    {
        if (active is not { } session || model.Worlds.Visible || model.SettingsEditor is not null ||
            dialogOpen || Down(VirtualKey.LeftWindows) || Down(VirtualKey.RightWindows))
            return;
        var control = Down(VirtualKey.Control);
        var shift = Down(VirtualKey.Shift);
        var alt = Down(VirtualKey.Menu);
        if (args.Key == VirtualKey.Tab && control && !alt && !composing && !session.PrivComposing && !session.PrivCommitting)
        {
            if (!args.KeyStatus.WasKeyDown) CycleTab(shift ? -1 : 1);
            args.Handled = true;
            return;
        }
        if (gameControl?.XamlRoot is not { } root) return;
        var focused = FocusManager.GetFocusedElement(root);
        if (focused is Microsoft.UI.Xaml.Controls.TextBox or Microsoft.UI.Xaml.Controls.PasswordBox or Microsoft.UI.Xaml.Controls.RichEditBox)
            return;
        if (focused is Microsoft.UI.Xaml.Controls.RichTextBlock)
            return;
        for (var parent = focused as DependencyObject; parent is not null; parent = VisualTreeHelper.GetParent(parent))
            if (parent is Microsoft.UI.Xaml.Controls.MenuBar or MenuFlyoutPresenter)
                return;
        // OEM virtual keys depend on the keyboard layout; scan codes identify the physical keys used by wphi.
        var key = args.KeyStatus.ScanCode switch
        {
            0x0C => ConsoleKey.OemMinus,
            0x27 => ConsoleKey.Oem1,
            0x35 when !args.KeyStatus.IsExtendedKey => ConsoleKey.Oem2,
            0x28 => ConsoleKey.Oem7,
            0x47 when !args.KeyStatus.IsExtendedKey => ConsoleKey.NumPad7,
            0x48 when !args.KeyStatus.IsExtendedKey => ConsoleKey.NumPad8,
            0x49 when !args.KeyStatus.IsExtendedKey => ConsoleKey.NumPad9,
            0x4B when !args.KeyStatus.IsExtendedKey => ConsoleKey.NumPad4,
            0x4C when !args.KeyStatus.IsExtendedKey => ConsoleKey.NumPad5,
            0x4D when !args.KeyStatus.IsExtendedKey => ConsoleKey.NumPad6,
            0x4F when !args.KeyStatus.IsExtendedKey => ConsoleKey.NumPad1,
            0x50 when !args.KeyStatus.IsExtendedKey => ConsoleKey.NumPad2,
            0x51 when !args.KeyStatus.IsExtendedKey => ConsoleKey.NumPad3,
            0x52 when !args.KeyStatus.IsExtendedKey => ConsoleKey.NumPad0,
            0x53 when !args.KeyStatus.IsExtendedKey => ConsoleKey.Decimal,
            _ => (ConsoleKey)args.Key
        };
        var action = GameInput.Resolve(key, session.NorthFixed, fullKeys, shift, control, alt,
            session.Selection.Active, args.KeyStatus.IsExtendedKey, model.Options);
        if (action is null)
            return;
        args.Handled = true;
        if (args.KeyStatus.WasKeyDown && !action.Repeatable &&
            !(action.Intent == InputIntent.ListNumber && action.Number == 9))
            return;
        if (args.KeyStatus.WasKeyDown && model.Options.Automap && session.NorthFixed &&
            !session.Model.Prediction.Queue.IsEmpty && action.Commands.Any(line =>
                line.Length == 4 && line.StartsWith("go ", StringComparison.Ordinal)))
            return;
        Execute(session, action);
    }

    private void AttachInput(Microsoft.UI.Xaml.Controls.TextBox box, GameSession session)
    {
        inputControl = box;
        composing = committingComposition = false;
        box.TextCompositionStarted += (_, _) => composing = true;
        box.TextCompositionEnded += (_, _) =>
        {
            composing = false;
            committingComposition = Down(VirtualKey.Enter);
        };
    }

    private void OnInputKeyDown(GameSession session, KeyRoutedEventArgs args)
    {
        if (composing || committingComposition || Down(VirtualKey.Menu))
            return;
        if (args.Key == VirtualKey.Escape)
        {
            gameControl?.Focus(FocusState.Programmatic);
            args.Handled = true;
        }
        else if (args.Key == VirtualKey.Enter && !Down(VirtualKey.Shift))
        {
            if (!args.KeyStatus.WasKeyDown) Submit(session, Down(VirtualKey.Control));
            args.Handled = true;
        }
    }

    private void Execute(GameSession session, InputAction action)
    {
        if (session.Model.Master is not null && action.Commands.Any())
            return;
        if (session.Model.Transferring && action.Intent is not
            (InputIntent.ForceDisconnect or InputIntent.ForceDisconnectAll or InputIntent.NextTab or
                InputIntent.PreviousTab or InputIntent.FocusInput))
            return;
        var selection = session.Selection;
        switch (action.Intent)
        {
            case InputIntent.FocusInput: inputControl?.Focus(FocusState.Programmatic); return;
            case InputIntent.NextTab: CycleTab(1); return;
            case InputIntent.PreviousTab: CycleTab(-1); return;
            case InputIntent.ToggleMap: showMap = !showMap; refresh(); return;
            case InputIntent.ForceDisconnect: session.Connection?.Dispose(); return;
            case InputIntent.ForceDisconnectAll:
                foreach (var s in sessions) s.Connection?.Dispose();
                return;
            case InputIntent.ListUp: session.Dispatch(new GameMessage.SelectionMoved(-1)); break;
            case InputIntent.ListDown: session.Dispatch(new GameMessage.SelectionMoved(1)); break;
            case InputIntent.ListToggle: session.Dispatch(new GameMessage.SelectionAdvanced()); break;
            case InputIntent.ListNumber:
                if (selection.Number(action.Number) is { } number) Choose(session, number);
                return;
            case InputIntent.ListAccept:
                if (!selection.Waiting) Choose(session, selection.Confirm());
                return;
            default:
                _ = SessionOperationAsync(session, () => ExecuteAsync(session, action));
                return;
        }
        if (listControl is not null && session.Selection.Cursor < listControl.Items.Count)
            listControl.ScrollIntoView(listControl.Items[session.Selection.Cursor]);
        refresh();
    }

    private async Task ExecuteAsync(GameSession session, InputAction action, bool keepSelection = false)
    {
        if (!session.Connected || session.Connection is null)
            throw new InvalidOperationException("Connect before sending a command.");
        if (session.Model.Transferring)
            throw new InvalidOperationException("世界移動中です。移動が終わってから再度操作してください。");
        if (session.PreparingMagic)
            throw new InvalidOperationException("Wait for the equipment response before sending another command.");
        if (session.Model.Master is not null)
            throw new InvalidOperationException("超魔法の手順を実行中です。完了するか中止してから操作してください。");
        var connection = session.Connection;
        var transportVersion = connection.TransportVersion;
        if (action.Confirmation is not null &&
            !await SessionDialogAsync("Confirm magic", action.Confirmation, "Cast"))
            return;
        if (action.Prompt is not null)
        {
            var response = await RequestDialogAsync(new(action.Prompt, "", "Send", action.Prompt));
            if (!response.Accepted) return;
            var answer = response.Value;
            if (answer.Length == 0) throw new ArgumentException("Enter a value before sending.");
            PhiWire.ValidateLine(answer);
            if (action.Numeric && (answer.Any(c => !char.IsAsciiDigit(c)) || !long.TryParse(answer, out var amount) || amount <= 0))
                throw new ArgumentException("Enter a positive whole-number amount.");
            action = action with { Commands = action.Commands.Select(line => line.Replace("{}", answer)).ToArray() };
        }
        if (session.Connection != connection || !session.Connected)
            throw new InvalidOperationException("The session disconnected while the dialog was open.");
        if (transportVersion != connection.TransportVersion)
            throw new InvalidOperationException("確認中に世界移動が始まったため、操作を取り消しました。");
        if (!keepSelection)
            session.Dispatch(new GameMessage.SelectionCleared());
        if (action.Intent == InputIntent.WriteSign)
        {
            session.Dispatch(new GameMessage.EditingChanged(true));
            action = GameInput.Command("write");
        }
        action = GameInput.ApplyOptions(action, model.Options);
        session.Dispatch(new GameMessage.PreparingChanged(action.AutoUnequip));
        refresh();
        try { await connection.SendActionAsync(action); }
        finally { session.Dispatch(new GameMessage.PreparingChanged(false)); }
    }

    private void Choose(GameSession session, SelectionCommand command)
    {
        if (session.Selection.Waiting) return;
        session.Dispatch(new GameMessage.SelectionBegan(command));
        _ = SessionOperationAsync(session, async () =>
        {
            try { await ExecuteAsync(session, GameInput.Command(command.Text), command.NextPage); }
            catch
            {
                session.Dispatch(new GameMessage.SelectionCleared());
                throw;
            }
        });
    }

    private void Submit(GameSession session, bool party = false, bool shout = false)
    {
        if (composing || committingComposition)
            return;
        var text = GameInput.ChatLine(session.Input, inputControl?.SelectionStart ?? session.Input.Length, party);
        if (text is null) return;
        var action = shout ? GameInput.Command("*" + text)
            with { Confirmation = "Shout this line to everyone in the world?" } : GameInput.Command(text);
        Execute(session, action);
    }

    private void MapClick(GameSession session, int x, int y)
    {
        gameControl?.Focus(FocusState.Programmatic);
        Send(session, GameInput.MapMovement(x, y, session.NorthFixed));
    }

    private void SelectSession(GameSession session)
    {
        if (active == session) return;
        active = session;
        composing = committingComposition = false;
        refresh();
    }

    private void CycleTab(int delta)
    {
        if (active is not null && sessions.Count > 0)
            SelectSession(sessions[(sessions.IndexOf(active) + delta + sessions.Count) % sessions.Count]);
    }

    private void CloseTab(GameSession session) =>
        _ = SessionOperationAsync(session, async () =>
        {
            session.MasterCancellation?.Cancel();
            await DisconnectAsync(session);
            if (session.ConnectionTask is { } task) await task;
            if (logStore is not null) await logStore.FlushAsync();
            var wasActive = active == session;
            session.Detach();
            sessions.Remove(session);
            Dispatch(new ClientMessage.GameClosed(session.Character.Key,
                wasActive ? sessions.LastOrDefault()?.Character.Key : model.ActiveKey));
        });

    private void ToggleConnection()
    {
        if (active is not { } session || session.Connecting || session.Closing) return;
        if (session.Connected)
            _ = SessionOperationAsync(session, () => DisconnectAsync(session));
        else
            _ = PerformAsync(() => StartAsync(session));
    }


    private void SaveLog()
    {
        if (active is not { } session) return;
        _ = PerformAsync(async () =>
        {
            var file = await Logs.SaveBufferAsync(session.Character.Name, session.Model.LogEntries, model.Options);
            notice = $"ログ欄の内容を保存しました: {file}";
        });
    }

    private void ShowKeyboardHelp() => _ = PerformAsync(async () => await SessionDialogAsync("Keyboard controls",
        "Outside text fields:\n" +
        "Arrows / numpad 8246: move; Ctrl: strafe / face; Shift+arrows: relative move / turn.\n" +
        "Full keys: 789 UIO JKL M and - ; / ' act as numpad keys (Operation menu toggles).\n" +
        "Z/X/C/V: get/put/use/sort; F: floor items; A/S/D/B: read/write/erase/watch sign.\n" +
        "Q/W: equip/unequip; M: check+look; R: spells; P: pay; H/N/Y: hi/no/yes.\n" +
        "Space / J: attack; L / numpad Enter: cast; Home: equip+sort; End: unequip first.\n" +
        "T / Shift+T: rotate / north-up; Shift+7 / Shift+5: 7x7 / 5x5; Shift+F5: map pane.\n" +
        "F1-F7 / Alt+1-7: numbered spells. Alt magic is listed in the Magic menu.\n" +
        "Ctrl+Tab / Ctrl+Shift+Tab: next / previous character; Pause / Shift+F1: force disconnect.\n" +
        "Lists: 1-9 current page; arrows cursor; Space check; Enter send; 0/Esc/./left/right cancel; -/+ all.\n\n" +
        "Text field: Enter sends the current line and keeps it; Shift+Enter inserts a newline;\n" +
        "Ctrl+Enter sends to party. Esc enters movement mode; Tab returns to input.\n" +
        "IME composition Enter confirms text only. Shout requires confirmation via Operation."));
}
