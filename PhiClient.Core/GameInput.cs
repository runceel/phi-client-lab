namespace PhiClient.Core;

public enum InputIntent
{
    Send, FocusInput, NextTab, PreviousTab, ToggleMap, WriteSign,
    ForceDisconnect, ForceDisconnectAll, ListNumber, ListUp, ListDown, ListToggle, ListAccept
}

public sealed record InputAction(string[] Commands)
{
    public InputIntent Intent { get; init; }
    public string? Prompt { get; init; }
    public bool Numeric { get; init; }
    public string? Confirmation { get; init; }
    public bool AutoUnequip { get; init; }
    public bool AutoReequip { get; init; }
    public int HiddenLists { get; init; }
    public bool Repeatable { get; init; }
    public int Number { get; init; }
}

public static class GameInput
{
    public static InputAction Command(params string[] commands) => new(commands);
    private static InputAction Intent(InputIntent intent) => new([]) { Intent = intent };
    public static InputAction Spell(string name, bool unequip = true) =>
        new(["cast", name]) { HiddenLists = 1, AutoUnequip = unequip };

    public static InputAction ApplyOptions(InputAction action, ClientOptions options) => action with
    {
        AutoUnequip = action.AutoUnequip && options.AutoUnequipSystemMagic,
        AutoReequip = action.AutoUnequip && options.AutoUnequipSystemMagic && options.AutoEquipSystemMagic &&
            action.Commands.Length >= 2 && action.Commands[0] == "cast" &&
            action.Commands[1] is "wizard eye" or "wizard light" or "list" or "eagle eye" or "identify" or
                "party eye" or "wizard lock" or "unlock" or "call" or "return"
    };

    public static string? FirstWeaponName(Choices list)
    {
        var text = list.Items.FirstOrDefault(choice => choice.Number == "1")?.Text;
        if (text is null) return null;
        var wp = text.LastIndexOf("Wp  :", StringComparison.Ordinal);
        if (wp < 0) wp = text.IndexOf("Wp", StringComparison.Ordinal);
        if (wp < 0 || !text[wp..].Contains("[Wpn]", StringComparison.Ordinal)) return null;
        return text[..wp].Trim();
    }

    // ConsoleKey provides portable key identifiers with the same codes as Windows virtual keys.
    public static InputAction? Resolve(ConsoleKey key, bool northUp, bool fullKeys = true,
        bool shift = false, bool control = false, bool alt = false, bool listing = false, bool extended = false,
        ClientOptions? options = null)
    {
        if (options is not null && !control && !shift)
        {
            var macro = alt && key == ConsoleKey.G ? options.AltGWord :
                !alt && key is >= ConsoleKey.F8 and <= ConsoleKey.F12
                    ? (listing ? options.ListShortcuts : options.Shortcuts)[key - ConsoleKey.F8] : "";
            if (macro.Length > 0)
            {
                var lines = macro.Replace("\\n", "\n", StringComparison.Ordinal)
                    .Split('\n').Select(line => line.TrimEnd('\r')).ToArray();
                foreach (var line in lines) PhiWire.ValidateLine(line);
                return Command(lines);
            }
        }
        if (key == ConsoleKey.Tab && control && !alt)
            return Intent(shift ? InputIntent.PreviousTab : InputIntent.NextTab);
        if (control && (shift || alt))
            return null;
        if (alt)
            return AltAction(key, shift);
        if (shift)
        {
            var shifted = key switch
            {
                ConsoleKey.F1 => Intent(InputIntent.ForceDisconnect),
                ConsoleKey.F5 => Intent(InputIntent.ToggleMap),
                ConsoleKey.T => Command("#ex-map style=solid"),
                ConsoleKey.D5 or ConsoleKey.NumPad5 => Command("#ex-map size=40"),
                ConsoleKey.D7 or ConsoleKey.NumPad7 => Command("#ex-map size=57"),
                _ => null
            };
            if (shifted is not null)
                return shifted;
        }
        if (listing && !control && !shift && key == ConsoleKey.OemMinus)
            return Command("-");
        if (fullKeys)
            key = key switch
            {
                ConsoleKey.M => ConsoleKey.NumPad0,
                ConsoleKey.J => ConsoleKey.NumPad1,
                ConsoleKey.K => ConsoleKey.NumPad2,
                ConsoleKey.L => ConsoleKey.NumPad3,
                ConsoleKey.U => ConsoleKey.NumPad4,
                ConsoleKey.I => ConsoleKey.NumPad5,
                ConsoleKey.O => ConsoleKey.NumPad6,
                ConsoleKey.D7 => ConsoleKey.NumPad7,
                ConsoleKey.D8 => ConsoleKey.NumPad8,
                ConsoleKey.D9 => ConsoleKey.NumPad9,
                ConsoleKey.OemMinus => ConsoleKey.Subtract,
                ConsoleKey.Oem1 => ConsoleKey.Add,
                ConsoleKey.Oem2 => ConsoleKey.Divide,
                ConsoleKey.Oem7 => ConsoleKey.Multiply,
                _ => key
            };
        if (listing && !control && !shift)
        {
            var digit = key is >= ConsoleKey.D1 and <= ConsoleKey.D9 ? key - ConsoleKey.D0 :
                key is >= ConsoleKey.NumPad1 and <= ConsoleKey.NumPad9 ? key - ConsoleKey.NumPad0 : 0;
            if (digit > 0)
                return new([]) { Intent = InputIntent.ListNumber, Number = digit };
            var listAction = key switch
            {
                ConsoleKey.D0 or ConsoleKey.NumPad0 or ConsoleKey.Escape or ConsoleKey.OemPeriod or
                    ConsoleKey.Decimal or ConsoleKey.LeftArrow or ConsoleKey.RightArrow => Command("."),
                ConsoleKey.Add => Command("-"),
                ConsoleKey.UpArrow => Intent(InputIntent.ListUp),
                ConsoleKey.DownArrow => Intent(InputIntent.ListDown),
                ConsoleKey.Spacebar => Intent(InputIntent.ListToggle),
                ConsoleKey.Enter => Intent(InputIntent.ListAccept),
                _ => null
            };
            if (listAction is not null)
                return listAction;
        }
        var direction = key switch
        {
            ConsoleKey.UpArrow or ConsoleKey.NumPad8 => "N",
            ConsoleKey.DownArrow or ConsoleKey.NumPad2 => "S",
            ConsoleKey.LeftArrow or ConsoleKey.NumPad4 => "W",
            ConsoleKey.RightArrow or ConsoleKey.NumPad6 => "E",
            _ => null
        };
        if (direction is not null)
        {
            var command = shift && key is ConsoleKey.LeftArrow or ConsoleKey.RightArrow
                ? key == ConsoleKey.LeftArrow ? "turn l" : "turn r"
                : shift && key is ConsoleKey.UpArrow or ConsoleKey.DownArrow
                    ? key == ConsoleKey.UpArrow ? "go" : "go b"
                    : ProtocolParser.Movement(direction, northUp, control);
            return Command(command) with { Repeatable = true };
        }
        if (key is ConsoleKey.NumPad5 or ConsoleKey.NumPad7 or ConsoleKey.NumPad9)
            return Command(key switch
            {
                ConsoleKey.NumPad5 => control && !northUp ? "turn n" : "turn b",
                ConsoleKey.NumPad7 => "turn l",
                _ => "turn r"
            }) with { Repeatable = true };
        if (control)
            return key switch
            {
                ConsoleKey.Spacebar or ConsoleKey.NumPad1 => Command("hit"),
                ConsoleKey.NumPad3 => Command("cast"),
                _ => null
            };
        if (shift && key != ConsoleKey.Spacebar)
            return null;
        if (key is >= ConsoleKey.F1 and <= ConsoleKey.F7)
            return Spell((key - ConsoleKey.F1 + 1).ToString(), false);
        if (key is >= ConsoleKey.D1 and <= ConsoleKey.D9)
            return Command((key - ConsoleKey.D0).ToString());
        return key switch
        {
            ConsoleKey.Pause => Intent(InputIntent.ForceDisconnect),
            ConsoleKey.Tab => Intent(InputIntent.FocusInput),
            ConsoleKey.A => Command("read"),
            ConsoleKey.B => Command("board"),
            ConsoleKey.C or ConsoleKey.Multiply => Command("use"),
            ConsoleKey.D => Command("erase"),
            ConsoleKey.F => Command("floor item"),
            ConsoleKey.H => Command("hi"),
            ConsoleKey.M or ConsoleKey.NumPad0 => Command("check", "look"),
            ConsoleKey.N => Command("n"),
            ConsoleKey.P => new(["pay {}"]) { Prompt = "Amount to pay", Numeric = true },
            ConsoleKey.Q or ConsoleKey.Divide => Command("equip"),
            ConsoleKey.R => Command("spells"),
            ConsoleKey.S => Intent(InputIntent.WriteSign),
            ConsoleKey.T => Command("#ex-map style=turn"),
            ConsoleKey.V => Command("sort"),
            ConsoleKey.W => Command("unequip"),
            ConsoleKey.X or ConsoleKey.Subtract => Command("put"),
            ConsoleKey.Y => Command("y"),
            ConsoleKey.Z or ConsoleKey.Add => Command("get"),
            ConsoleKey.D0 or ConsoleKey.OemPeriod or ConsoleKey.Decimal => Command("."),
            ConsoleKey.Spacebar or ConsoleKey.NumPad1 => Command("hit"),
            ConsoleKey.NumPad3 => Command("cast"),
            ConsoleKey.Enter when extended => Command("cast"),
            ConsoleKey.Home => Command("equip", "sort"),
            ConsoleKey.End => Command("unequip", "1") with { HiddenLists = 1 },
            _ => null
        };
    }

    private static InputAction? AltAction(ConsoleKey key, bool shift)
    {
        if (shift)
            return key switch
            {
                ConsoleKey.W => Spell("wizard light"),
                ConsoleKey.E => new(["#ex-switch eagleeye=text", "cast", "eagle eye", "#ex-switch eagleeye=form"])
                    { HiddenLists = 1, AutoUnequip = true },
                ConsoleKey.L => Spell("list"),
                ConsoleKey.D => Spell("destroy"),
                ConsoleKey.C => Spell("charge spell"),
                ConsoleKey.A => new(["cast", "appear {}"]) { Prompt = "NPC to summon", HiddenLists = 1 },
                ConsoleKey.U => new(["cast", "disappear {}"]) { Prompt = "NPC to dismiss", HiddenLists = 1 },
                _ => null
            };
        if (key is >= ConsoleKey.D1 and <= ConsoleKey.D7)
            return Spell((key - ConsoleKey.D0).ToString(), false);
        return key switch
        {
            ConsoleKey.Pause => Intent(InputIntent.ForceDisconnectAll),
            ConsoleKey.W => Spell("wizard eye"),
            ConsoleKey.E => Spell("eagle eye"),
            ConsoleKey.I => Spell("identify"),
            ConsoleKey.P => Spell("party eye"),
            ConsoleKey.L => Spell("wizard lock") with { Commands = ["cast", "wizard lock", "see"] },
            ConsoleKey.U => Spell("unlock") with { Commands = ["cast", "unlock", "see"] },
            ConsoleKey.D => Spell("detect"),
            ConsoleKey.C => Spell("create"),
            ConsoleKey.A => Spell("analyze"),
            ConsoleKey.F => Spell("forget"),
            ConsoleKey.B => Spell("call") with { Confirmation = "Set the current location as your Call point?" },
            ConsoleKey.R => Spell("return") with { Confirmation = "Return to the Call point? You may lose carried items." },
            ConsoleKey.S => new(["cast", "search", "{}"]) { Prompt = "Text to search for on signs", HiddenLists = 1 },
            _ => null
        };
    }

    public static string? ChatLine(string text, int caret, bool party, bool loud = false)
    {
        var (start, end) = ChatBounds(text, caret);
        var line = text[start..end];
        if (line.Length == 0)
            return end == text.Length ? null : "/**/";
        return loud ? "*" + line : line.StartsWith('*') ? "/**/" + line : party ? "+" + line : line;
    }

    private static (int Start, int End) ChatBounds(string text, int caret)
    {
        caret = Math.Clamp(caret, 0, text.Length);
        if (caret > 0 && caret < text.Length && text[caret - 1] == '\r' && text[caret] == '\n')
            caret--;
        var start = caret;
        var end = caret;
        while (start > 0 && text[start - 1] is not '\r' and not '\n') start--;
        while (end < text.Length && text[end] is not '\r' and not '\n') end++;
        return (start, end);
    }

    public static (string Text, int Caret) AfterChatSend(string text, int caret, string prefix = "")
    {
        var (_, end) = ChatBounds(text, caret);
        if (end < text.Length)
            return (text, end + (text[end] == '\r' && end + 1 < text.Length && text[end + 1] == '\n' ? 2 : 1));
        var separator = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : text.Contains('\r') ? "\r" : "\n";
        var next = text + separator + prefix;
        var lines = next.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        if (lines.Length > 100) next = string.Join(separator, lines[^100..]);
        return (next, next.Length);
    }

    public static string[] MapMovement(int x, int y, bool northUp)
    {
        if (x == 0 && y == 0) return ["check", "look"];
        var commands = new List<string>();
        var boundary = Math.Tan(Math.PI / 8);
        if (Math.Abs((double)y) > Math.Abs((double)x) * boundary)
            commands.Add(ProtocolParser.Movement(y < 0 ? "N" : "S", northUp));
        if (Math.Abs((double)x) > Math.Abs((double)y) * boundary)
            commands.Add(ProtocolParser.Movement(x < 0 ? "W" : "E", northUp));
        return commands.ToArray();
    }
}
