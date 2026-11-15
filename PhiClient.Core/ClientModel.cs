using System.Collections.Immutable;

namespace PhiClient.Core;

public sealed record SavedCharacter(string Key, string Name, string Host, int Port);
public sealed record CharacterDraft(string Name = "", string Host = "", string Port = "2000",
    string Id = "", string Password = "", string Image = "0", string Email = "");
public sealed record ClientOptions(bool ShowCharacters = true, bool ShowMap = true,
    bool FullKeys = true, bool FollowLog = true);
public sealed record DialogModel(string Title, string Text, string? Accept = null,
    string? InputLabel = null, string Value = "", string InputId = "action-answer");
public sealed record DialogAnswer(bool Accepted, string Value);

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
    public int GraphicsRevision { get; init; }
    public string GraphicsNotice { get; init; } = "";
    public WorldDirectoryModel Worlds { get; init; } = new();
}

public abstract record ClientMessage
{
    public sealed record DraftChanged(CharacterDraft Value) : ClientMessage;
    public sealed record OptionsChanged(ClientOptions Value) : ClientMessage;
    public sealed record CharactersChanged(IReadOnlyList<SavedCharacter> Value) : ClientMessage;
    public sealed record NoticeChanged(string Value) : ClientMessage;
    public sealed record DeleteMarked(string? Key) : ClientMessage;
    public sealed record CreatingChanged(bool Value) : ClientMessage;
    public sealed record LoadedChanged(bool Value) : ClientMessage;
    public sealed record BusyChanged(bool Value) : ClientMessage;
    public sealed record GameOpened(string Key) : ClientMessage;
    public sealed record GameSelected(string? Key) : ClientMessage;
    public sealed record GameClosed(string Key, string? NextKey) : ClientMessage;
    public sealed record GameChanged(string Key, GameMessage Message) : ClientMessage;
    public sealed record DialogChanged(DialogModel? Value) : ClientMessage;
    public sealed record DialogEdited(string Value) : ClientMessage;
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
        ClientMessage.DraftChanged edit => model with { Draft = edit.Value },
        ClientMessage.OptionsChanged edit => model with { Options = edit.Value },
        ClientMessage.CharactersChanged edit => model with { Characters = edit.Value.ToImmutableArray() },
        ClientMessage.NoticeChanged edit => model with { Notice = edit.Value },
        ClientMessage.DeleteMarked edit => model with { PendingDelete = edit.Key },
        ClientMessage.CreatingChanged edit => model with { Creating = edit.Value },
        ClientMessage.LoadedChanged edit => model with { Loaded = edit.Value },
        ClientMessage.BusyChanged edit => model with { Busy = edit.Value },
        ClientMessage.DialogChanged edit => model with { Dialog = edit.Value },
        ClientMessage.DialogEdited edit when model.Dialog is { } dialog => model with { Dialog = dialog with { Value = edit.Value } },
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
            model with { ActiveKey = select.Key },
        ClientMessage.GameClosed close => model with
        {
            Games = model.Games.Remove(close.Key),
            ActiveKey = model.ActiveKey == close.Key ? close.NextKey : model.ActiveKey
        },
        ClientMessage.GameChanged update when model.Games.TryGetValue(update.Key, out var game) =>
            model with { Games = model.Games.SetItem(update.Key, GameUpdate.Apply(game, update.Message)) },
        _ => model
    };
}
