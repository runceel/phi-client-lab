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
    public bool Transferring { get; init; }
    public string PlayerName { get; init; } = "";
    public ImmutableArray<User> Users { get; init; } = [];
    public string World { get; init; } = "";
    public string Area { get; init; } = "";
    public string Input { get; init; } = "";
    public string BoardText { get; init; } = "";
    public bool Editing { get; init; }
    public MapFrame? Map { get; init; }
    public MovePrediction Prediction { get; init; } = new();
    public MapMotion Motion { get; init; } = new();
    public EagleFrame? Eagle { get; init; }
    public bool ShowEagle { get; init; }
    public ImmutableArray<string> MapSets { get; init; } = ["default", "default", "default", "default"];
    public ChoiceSelection Selection { get; init; } = new();
    public ImmutableArray<string> Log { get; init; } = [];
    public int LogCharacters { get; init; }
}

public abstract record GameMessage
{
    public sealed record Received(ServerEvent Event, long At = 0) : GameMessage;
    public sealed record CommandSent(string Text, long At) : GameMessage;
    public sealed record PredictionTick(long At) : GameMessage;
    public sealed record PredictionReset : GameMessage;
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
    public static GameModel Apply(GameModel model, GameMessage message)
    {
        var next = ApplyMessage(model, message);
        var now = message switch
        {
            GameMessage.Received received => received.At,
            GameMessage.PredictionTick tick => tick.At,
            GameMessage.CommandSent sent => sent.At,
            _ => model.Motion.Now
        };
        var offset = next.Prediction.Offset;
        var position = next.Map?.Position;
        if (position is not null)
            position = new(position.X + offset.X, position.Y + offset.Y);
        return next with { Motion = model.Motion.Update(position, next.World, now, next.NorthFixed) };
    }

    private static GameModel ApplyMessage(GameModel model, GameMessage message) => message switch
    {
        GameMessage.Received received => Receive(model, received.Event, received.At),
        GameMessage.CommandSent sent => model with { Prediction = model.Prediction.Sent(sent.Text, sent.At) },
        GameMessage.PredictionTick tick => model with { Prediction = model.Prediction.Tick(tick.At) },
        GameMessage.PredictionReset => model with { Prediction = model.Prediction.Reset() },
        GameMessage.PhaseChanged { Phase: ConnectionPhase.Connecting } =>
            Append(new GameModel { Input = model.Input, BoardText = model.BoardText, Log = model.Log,
                LogCharacters = model.LogCharacters, Phase = ConnectionPhase.Connecting }, "[Connecting...]"),
        GameMessage.PhaseChanged { Phase: ConnectionPhase.Offline } =>
            model with { Phase = ConnectionPhase.Offline, Selection = new(), Editing = false,
                PreparingMagic = false, Transferring = false, Prediction = model.Prediction.Reset() },
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

    private static GameModel Receive(GameModel model, ServerEvent item, long at) => item switch
    {
        ConnectionReady when model.Phase != ConnectionPhase.Closing => model with { Phase = ConnectionPhase.Connected },
        PlayerName name => model with { PlayerName = name.Name },
        UserList list => model with { Users = list.Users.ToImmutableArray() },
        TransferProgress progress => model with { Transferring = progress.Active, Prediction = model.Prediction.Reset() },
        WorldMoved => model with { Map = null, Eagle = null, ShowEagle = false, World = "", Area = "",
            Users = [], Selection = new(), Editing = false, PreparingMagic = false, Prediction = model.Prediction.Reset() },
        LogLine { Literal: false } line when line.Text.StartsWith("DM > ", StringComparison.Ordinal) &&
            line.Text[5..] is "進めません." or "Can not go." =>
            Append(model with { Prediction = model.Prediction.CannotGo(at) }, line.Text),
        LogLine line => Append(model, line.Text),
        StatusLine status => model with { Status = status.Text, Stats = PlayerStatus.Parse(status.Text) },
        MapFrame map => model with { Map = map, Prediction = map.Size == 7 && model.NorthUp && map.Position is { } pos
            ? model.Prediction.OnMap(pos, at) : model.Prediction.Reset() },
        MapSetNotice mapset => model with { MapSets = model.MapSets.SetItem(mapset.Index, mapset.Name) },
        Choices choices => Select(model, selection => selection.Receive(choices.Items)),
        MapStyle style => model with { NorthUp = style.NorthUp, Prediction = model.Prediction.Reset() },
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
