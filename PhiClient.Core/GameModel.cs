using System.Collections.Immutable;

namespace PhiClient.Core;

public enum ConnectionPhase { Offline, Connecting, Connected, Closing }

public sealed record GameModel
{
    public ConnectionPhase Phase { get; init; }
    public bool NorthUp { get; init; } = true;
    public bool NorthFixed => NorthUp && (Map?.Size ?? 7) == 7;
    public string Status { get; init; } = "No status received.";
    public PlayerStatus? Stats { get; init; }
    public Conditions Conditions { get; init; } = new("");
    public bool AttackMode { get; init; }
    public bool MagicMode { get; init; }
    public bool PreparingMagic { get; init; }
    public string World { get; init; } = "";
    public string Area { get; init; } = "";
    public string Input { get; init; } = "";
    public string BoardText { get; init; } = "";
    public bool Editing { get; init; }
    public MapFrame? Map { get; init; }
    public EagleFrame? Eagle { get; init; }
    public bool ShowEagle { get; init; }
    public ImmutableArray<string> MapSets { get; init; } = ["default", "default", "default", "default"];
    public ChoiceSelection Selection { get; init; } = new();
    public ImmutableArray<string> Log { get; init; } = [];
    public int LogCharacters { get; init; }
}

public abstract record GameMessage
{
    public sealed record Received(ServerEvent Event) : GameMessage;
    public sealed record PhaseChanged(ConnectionPhase Phase) : GameMessage;
    public sealed record InputChanged(string Text) : GameMessage;
    public sealed record BoardChanged(string Text) : GameMessage;
    public sealed record EditingChanged(bool Value) : GameMessage;
    public sealed record PreparingChanged(bool Value) : GameMessage;
    public sealed record EagleToggled : GameMessage;
    public sealed record Logged(string Text) : GameMessage;
    public sealed record SelectionMoved(int Delta) : GameMessage;
    public sealed record SelectionToggled(int Index) : GameMessage;
    public sealed record SelectionAdvanced : GameMessage;
    public sealed record SelectionBegan(SelectionCommand Command) : GameMessage;
    public sealed record SelectionCleared : GameMessage;
}

public static class GameUpdate
{
    public static GameModel Apply(GameModel model, GameMessage message) => message switch
    {
        GameMessage.Received received => Receive(model, received.Event),
        GameMessage.PhaseChanged { Phase: ConnectionPhase.Connecting } =>
            Append(new GameModel { Input = model.Input, BoardText = model.BoardText, Log = model.Log,
                LogCharacters = model.LogCharacters, Phase = ConnectionPhase.Connecting }, "[Connecting...]"),
        GameMessage.PhaseChanged { Phase: ConnectionPhase.Offline } =>
            model with { Phase = ConnectionPhase.Offline, Selection = new(), Editing = false, PreparingMagic = false },
        GameMessage.PhaseChanged phase => model with { Phase = phase.Phase },
        GameMessage.InputChanged input => model with { Input = input.Text },
        GameMessage.BoardChanged board => model with { BoardText = board.Text },
        GameMessage.EditingChanged edit => model with { Editing = edit.Value },
        GameMessage.PreparingChanged prepare => model with { PreparingMagic = prepare.Value },
        GameMessage.EagleToggled => model with { ShowEagle = !model.ShowEagle },
        GameMessage.Logged log => Append(model, log.Text),
        GameMessage.SelectionMoved move => Select(model, selection => selection.Move(move.Delta)),
        GameMessage.SelectionToggled toggle => Select(model, selection => selection.Toggle(toggle.Index)),
        GameMessage.SelectionAdvanced => Select(model, selection => selection.ToggleCursor()),
        GameMessage.SelectionBegan begin => Select(model, selection => selection.Begin(begin.Command)),
        GameMessage.SelectionCleared => model with { Selection = new() },
        _ => model
    };

    private static GameModel Receive(GameModel model, ServerEvent item) => item switch
    {
        ConnectionReady when model.Phase != ConnectionPhase.Closing => model with { Phase = ConnectionPhase.Connected },
        LogLine line => Append(model, line.Text),
        StatusLine status => model with { Status = status.Text, Stats = PlayerStatus.Parse(status.Text) },
        MapFrame map => model with { Map = map },
        MapSetNotice mapset => model with { MapSets = model.MapSets.SetItem(mapset.Index, mapset.Name) },
        Choices choices => Select(model, selection => selection.Receive(choices.Items)),
        MapStyle style => model with { NorthUp = style.NorthUp },
        EditMode edit => model with { Editing = edit.Active },
        Conditions conditions => model with { Conditions = conditions },
        EagleFrame eagle => model with { Eagle = eagle, ShowEagle = true },
        LocationNotice { Kind: "world" } location => model with { World = location.Value },
        LocationNotice location => model with { Area = location.Value },
        ActionMode { Magic: true } mode => model with { MagicMode = mode.Active },
        ActionMode mode => model with { AttackMode = mode.Active },
        Closed => Append(model, "[Server closed the session]"),
        _ => model
    };

    private static GameModel Select(GameModel model, Action<ChoiceSelection> change)
    {
        var selection = model.Selection.Copy();
        change(selection);
        return model with { Selection = selection };
    }

    private static GameModel Append(GameModel model, string text)
    {
        if (text.Length > 8192) text = text[..8192] + " [display truncated]";
        var log = model.Log.Add(text);
        var count = model.LogCharacters + text.Length;
        var remove = 0;
        while (log.Length - remove > 500 || count > 100000) count -= log[remove++].Length;
        return model with { Log = log.RemoveRange(0, remove), LogCharacters = count };
    }
}
