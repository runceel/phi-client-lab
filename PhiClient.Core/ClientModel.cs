using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace PhiClient.Core;

public sealed record SavedCharacter(string Key, string Name, string Host, int Port);
public sealed record CharacterDraft(string Name = "", string Host = "", string Port = "2000",
    string Id = "", string Password = "", string Image = "0", string Email = "");
public sealed record ClientOptions(bool ShowCharacters = true, bool ShowMap = true,
    [property: JsonPropertyName("fullKeyMove")] bool FullKeys = true, bool FollowLog = true)
{
    public ImmutableArray<string> Shortcuts { get; init; } = ["", "", "", "", ""];
    public ImmutableArray<string> ListShortcuts { get; init; } = ["", "", "", "", ""];
    public string AltGWord { get; init; } = "";
    public bool SoundPlay { get; init; } = true;
    public bool TaskBarFlash { get; init; } = true;
    public bool LogSaveHtml { get; init; } = true;
    public bool LogDispTime { get; init; } = true;
    public string LogFontName { get; init; } = "";
    public uint LogFontSize { get; init; }
    public bool LogTagDeleteCopy { get; init; }
    public bool LogTagDeleteAlways { get; init; }
    public bool LogTimeDelete { get; init; } = true;
    public bool LogNameDelete { get; init; }
    public bool LogTagCancel { get; init; }
    public bool AutoUnequipSystemMagic { get; init; } = true;
    public bool AutoEquipSystemMagic { get; init; }
    public bool MasterMagicMenu { get; init; }
    public int MapZoom { get; init; } = 100;
    public bool Automap { get; init; } = true;
    public bool SmoothMove { get; init; }
    public bool SingleWindow { get; init; }
}
public sealed record DialogModel(string Title, string Text, string? Accept = null,
    string? InputLabel = null, string Value = "", string InputId = "action-answer",
    bool Secret = false, bool Reveal = false);
public sealed record DialogAnswer(bool Accepted, string Value);
public sealed record SettingsEditorModel(ClientOptions Value, string Zoom, string FontSize,
    string ImportPath = "", string Message = "");
public sealed record RegistrationGraphicsModel(long Request = 0, bool Loading = false, string Message = "")
{
    public ImmutableArray<string> Names { get; init; } = [];
}

public sealed record ClientModel
{
    public CharacterDraft Draft { get; init; } = new();
    public ClientOptions Options { get; init; } = new();
    public ImmutableArray<SavedCharacter> Characters { get; init; } = [];
    public ImmutableDictionary<string, GameModel> Games { get; init; } = ImmutableDictionary<string, GameModel>.Empty;
    public string? ActiveKey { get; init; }
    public string Notice { get; init; } = "Load or register a character to begin. IDs are stored in Windows Credential Manager.";
    public string? PendingDelete { get; init; }
    public bool Creating { get; init; }
    public bool Loaded { get; init; }
    public bool Busy { get; init; }
    public DialogModel? Dialog { get; init; }
    public SettingsEditorModel? SettingsEditor { get; init; }
    public int GraphicsRevision { get; init; }
    public string GraphicsNotice { get; init; } = "";
    public WorldDirectoryModel Worlds { get; init; } = new();
    public RegistrationGraphicsModel RegistrationGraphics { get; init; } = new();
    public ImmutableDictionary<string, AutomapWorld> Automaps { get; init; } =
        ImmutableDictionary<string, AutomapWorld>.Empty;
}

public abstract record ClientMessage
{
    public sealed record DraftChanged(CharacterDraft Value) : ClientMessage;
    public sealed record OptionsChanged(ClientOptions Value) : ClientMessage;
    public sealed record SettingsEdited(SettingsEditorModel? Value) : ClientMessage;
    public sealed record AutomapLoaded(string World, AutomapWorld Value) : ClientMessage;
    public sealed record AutomapFailed(string World, string Error) : ClientMessage;
    public sealed record AutomapSaved(string World, MapPosition Block,
        ImmutableDictionary<MapPosition, MapCell> Cells) : ClientMessage;
    public sealed record AutomapCleared(string World) : ClientMessage;
    public sealed record CharactersChanged(IReadOnlyList<SavedCharacter> Value) : ClientMessage;
    public sealed record NoticeChanged(string Value) : ClientMessage;
    public sealed record DeleteMarked(string? Key) : ClientMessage;
    public sealed record CreatingChanged(bool Value) : ClientMessage;
    public sealed record RegistrationGraphicsLoading(long Request) : ClientMessage;
    public sealed record RegistrationGraphicsLoaded(long Request, IReadOnlyList<string> Names) : ClientMessage;
    public sealed record RegistrationGraphicsFailed(long Request, string Error) : ClientMessage;
    public sealed record RegistrationGraphicSelected(int Index) : ClientMessage;
    public sealed record LoadedChanged(bool Value) : ClientMessage;
    public sealed record BusyChanged(bool Value) : ClientMessage;
    public sealed record GameOpened(string Key) : ClientMessage;
    public sealed record GameSelected(string? Key) : ClientMessage;
    public sealed record GameClosed(string Key, string? NextKey) : ClientMessage;
    public sealed record GameChanged(string Key, GameMessage Message) : ClientMessage;
    public sealed record DialogChanged(DialogModel? Value) : ClientMessage;
    public sealed record DialogEdited(string Value) : ClientMessage;
    public sealed record DialogRevealChanged(bool Value) : ClientMessage;
    public sealed record GraphicsChanged(int Revision, string Notice) : ClientMessage;
    public sealed record WorldUrlChanged(string Value) : ClientMessage;
    public sealed record WorldFilterChanged(string Value) : ClientMessage;
    public sealed record WorldVisibilityChanged(bool Value) : ClientMessage;
    public sealed record WorldsLoading : ClientMessage;
    public sealed record WorldsLoaded(WorldListResult Value) : ClientMessage;
    public sealed record WorldsFailed(string Error) : ClientMessage;
    public sealed record WorldSelected(WorldEndpoint Value) : ClientMessage;
}

public static class ClientUpdate
{
    public static ClientModel Apply(ClientModel model, ClientMessage message) => message switch
    {
        ClientMessage.DraftChanged edit => model with { Draft = edit.Value,
            RegistrationGraphics = edit.Value.Host != model.Draft.Host || edit.Value.Port != model.Draft.Port
                ? new() : model.RegistrationGraphics },
        ClientMessage.OptionsChanged edit => model with { Options = edit.Value,
            Games = edit.Value.Automap != model.Options.Automap ? ResetPredictions(model.Games) :
                edit.Value.SmoothMove != model.Options.SmoothMove ? model.Games.SetItems(model.Games.Select(pair =>
                    KeyValuePair.Create(pair.Key, pair.Value with { Motion = pair.Value.Motion.Stop() }))) : model.Games },
        ClientMessage.SettingsEdited edit => model with { SettingsEditor = edit.Value },
        ClientMessage.AutomapLoaded loaded => model with
        {
            Automaps = model.Automaps.SetItem(loaded.World,
                (model.Automaps.GetValueOrDefault(loaded.World) ?? new()).MergeLoaded(loaded.Value))
        },
        ClientMessage.AutomapFailed failed => model with
        {
            Automaps = model.Automaps.SetItem(failed.World,
                (model.Automaps.GetValueOrDefault(failed.World) ?? new()) with { Loaded = true, Error = failed.Error })
        },
        ClientMessage.AutomapCleared clear => model with
            { Automaps = model.Automaps.SetItem(clear.World, new() { Loaded = true }) },
        ClientMessage.AutomapSaved saved when model.Automaps.GetValueOrDefault(saved.World) is { } world &&
            ReferenceEquals(world.Blocks.GetValueOrDefault(saved.Block), saved.Cells) =>
            model with { Automaps = model.Automaps.SetItem(saved.World, world with { Dirty = world.Dirty.Remove(saved.Block) }) },
        ClientMessage.CharactersChanged edit => model with { Characters = edit.Value.ToImmutableArray() },
        ClientMessage.NoticeChanged edit => model with { Notice = edit.Value },
        ClientMessage.DeleteMarked edit => model with { PendingDelete = edit.Key },
        ClientMessage.CreatingChanged edit => model with { Creating = edit.Value },
        ClientMessage.RegistrationGraphicsLoading load => model with
            { RegistrationGraphics = new(load.Request, true, "指定したサーバーから初期画像の候補を取得しています…") },
        ClientMessage.RegistrationGraphicsLoaded load when load.Request == model.RegistrationGraphics.Request =>
            model with { RegistrationGraphics = new(load.Request, false,
                load.Names.Count == 0 ? "サーバーから初期画像の候補は返されませんでした。画像番号を手動で入力できます。" :
                    $"初期画像の候補を{load.Names.Count}件取得しました。") { Names = load.Names.ToImmutableArray() },
                Draft = model.Draft with { Image = "0" } },
        ClientMessage.RegistrationGraphicsFailed failed when failed.Request == model.RegistrationGraphics.Request =>
            model with { RegistrationGraphics = model.RegistrationGraphics with
                { Loading = false, Message = $"初期画像の候補を取得できませんでした: {failed.Error}" } },
        ClientMessage.RegistrationGraphicSelected select when select.Index >= 0 &&
            select.Index < model.RegistrationGraphics.Names.Length =>
            model with { Draft = model.Draft with
                { Image = select.Index.ToString(System.Globalization.CultureInfo.InvariantCulture) } },
        ClientMessage.LoadedChanged edit => model with { Loaded = edit.Value },
        ClientMessage.BusyChanged edit => model with { Busy = edit.Value },
        ClientMessage.DialogChanged edit => model with { Dialog = edit.Value },
        ClientMessage.DialogEdited edit when model.Dialog is { Secret: false } dialog =>
            model with { Dialog = dialog with { Value = edit.Value } },
        ClientMessage.DialogRevealChanged edit when model.Dialog is { Secret: true } dialog =>
            model with { Dialog = dialog with { Reveal = edit.Value } },
        ClientMessage.GraphicsChanged graphics => model with { GraphicsRevision = graphics.Revision, GraphicsNotice = graphics.Notice },
        ClientMessage.WorldUrlChanged edit => model with { Worlds = model.Worlds with { Url = edit.Value } },
        ClientMessage.WorldFilterChanged edit => model with { Worlds = model.Worlds with { Filter = edit.Value } },
        ClientMessage.WorldVisibilityChanged edit => model with { Worlds = model.Worlds with { Visible = edit.Value } },
        ClientMessage.WorldsLoading => model with { Worlds = model.Worlds with { Loading = true, Message = "世界一覧を読み込んでいます…" } },
        ClientMessage.WorldsLoaded loaded => model with
        {
            Worlds = model.Worlds with
            {
                Items = loaded.Value.Items, Source = loaded.Value.Source,
                Message = loaded.Value.Message, Loading = false
            }
        },
        ClientMessage.WorldsFailed failed => model with
        {
            Worlds = model.Worlds with { Loading = false, Message = $"世界一覧の取得に失敗しました: {failed.Error} 前の一覧を保持しています。" }
        },
        ClientMessage.WorldSelected select when !model.Busy && !model.Worlds.Loading && model.Worlds.Items.Contains(select.Value) => model with
        {
            Draft = model.Draft with { Host = select.Value.Host, Port = select.Value.Port.ToString(System.Globalization.CultureInfo.InvariantCulture) },
            RegistrationGraphics = new(),
            Worlds = model.Worlds with { Visible = false },
            Options = model.Options with { ShowCharacters = true },
            Notice = $"「{select.Value.Name}」の接続先を入力しました。選択だけでは接続・キャラ作成しません。"
        },
        ClientMessage.GameOpened open => model with
        {
            Games = model.Games.SetItem(open.Key, new GameModel()), ActiveKey = open.Key,
            Options = model.Options with { ShowCharacters = false }
        },
        ClientMessage.GameSelected select when select.Key is null || model.Games.ContainsKey(select.Key) =>
            model with { ActiveKey = select.Key, Games = select.Key != model.ActiveKey ? ResetPredictions(model.Games) : model.Games },
        ClientMessage.GameClosed close => model with
        {
            Games = model.Games.Remove(close.Key),
            ActiveKey = model.ActiveKey == close.Key ? close.NextKey : model.ActiveKey
        },
        ClientMessage.GameChanged update when model.Games.TryGetValue(update.Key, out var game) =>
            UpdateGame(model, update.Key, game, update.Message),
        _ => model
    };

    private static ImmutableDictionary<string, GameModel> ResetPredictions(ImmutableDictionary<string, GameModel> games) =>
        games.SetItems(games.Select(pair => KeyValuePair.Create(pair.Key,
            pair.Value with { Prediction = pair.Value.Prediction.Reset(), Motion = pair.Value.Motion.Stop() })));

    private static ClientModel UpdateGame(ClientModel model, string key, GameModel game, GameMessage message)
    {
        var next = GameUpdate.Apply(game, message);
        var result = model with { Games = model.Games.SetItem(key, next) };
        if (message is not GameMessage.Received { Event: MapFrame frame } || !next.NorthFixed ||
            next.Conditions.Flags.Length > 6 && next.Conditions.Flags[6] == '*' ||
            next.World is "" or "???") return result;
        var world = model.Automaps.GetValueOrDefault(next.World) ?? new();
        try
        {
            return result with { Automaps = model.Automaps.SetItem(next.World, world.Absorb(frame)) };
        }
        catch (InvalidOperationException ex)
        {
            return result with
            {
                Automaps = model.Automaps.SetItem(next.World, world with { Error = ex.Message }),
                Games = result.Games.SetItem(key, GameUpdate.Apply(next, new GameMessage.Logged("[記録地図] " + ex.Message)))
            };
        }
    }
}
