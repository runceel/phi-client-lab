using PhiClient.Core;
using System.Collections.Immutable;

namespace PhiClient;

internal sealed class GameSession(SavedCharacter character, Func<GameModel> read, Action<GameMessage> dispatch)
{
    public SavedCharacter Character { get; } = character;
    private GameModel? detachedModel;
    public GameModel Model => detachedModel ?? read();
    public void Dispatch(GameMessage message)
    {
        if (detachedModel is null) dispatch(message);
    }
    public void Detach() => detachedModel = read();
    public void Append(string text) => Dispatch(new GameMessage.Logged(text));
    public PhiConnection? Connection { get; set; }
    public Task? ConnectionTask { get; set; }
    public bool Connecting => Model.Phase == ConnectionPhase.Connecting;
    public bool Connected => Model.Phase is ConnectionPhase.Connected or ConnectionPhase.Closing;
    public bool Closing => Model.Phase == ConnectionPhase.Closing;
    public bool NorthFixed => Model.NorthFixed;
    public PlayerStatus? Stats => Model.Stats;
    public Conditions Conditions => Model.Conditions;
    public bool AttackMode => Model.AttackMode;
    public bool MagicMode => Model.MagicMode;
    public bool PreparingMagic => Model.PreparingMagic;
    public string World => Model.World;
    public string Area => Model.Area;
    public string Input => Model.Input;
    public string BoardText => Model.BoardText;
    public bool Editing => Model.Editing;
    public MapFrame? Map => Model.Map;
    public EagleFrame? Eagle => Model.Eagle;
    public bool ShowEagle => Model.ShowEagle;
    public IReadOnlyList<string> MapSets => Model.MapSets;
    public ChoiceSelection Selection => Model.Selection;
    public IReadOnlyList<string> Log => Model.Log;
    public Microsoft.UI.Xaml.Media.Imaging.WriteableBitmap? GraphicsImage { get; set; }
    public (MapFrame?, EagleFrame?, ImmutableArray<string>, int, int, double) GraphicsKey { get; set; }
    public int GraphicsWidth { get; set; }
    public int GraphicsHeight { get; set; }
    public IReadOnlyList<MapLabel> GraphicsLabels { get; set; } = [];
}
