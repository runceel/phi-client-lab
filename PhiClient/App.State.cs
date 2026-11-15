using PhiClient;
using PhiClient.Core;

partial class App
{
    private ClientModel model = new();
    private Action<ClientModel>? publish;

    private void Dispatch(ClientMessage message)
    {
        model = ClientUpdate.Apply(model, message);
        refresh();
    }

    private IReadOnlyList<SavedCharacter> characters
    {
        get => model.Characters;
        set => Dispatch(new ClientMessage.CharactersChanged(value));
    }
    private GameSession? active
    {
        get => sessions.FirstOrDefault(session => session.Character.Key == model.ActiveKey);
        set => Dispatch(new ClientMessage.GameSelected(value?.Character.Key));
    }
    private string name { get => model.Draft.Name; set => Edit(model.Draft with { Name = value }); }
    private string host { get => model.Draft.Host; set => Edit(model.Draft with { Host = value }); }
    private string port { get => model.Draft.Port; set => Edit(model.Draft with { Port = value }); }
    private string id { get => model.Draft.Id; set => Edit(model.Draft with { Id = value }); }
    private string password { get => model.Draft.Password; set => Edit(model.Draft with { Password = value }); }
    private string image { get => model.Draft.Image; set => Edit(model.Draft with { Image = value }); }
    private string email { get => model.Draft.Email; set => Edit(model.Draft with { Email = value }); }
    private void Edit(CharacterDraft draft) => Dispatch(new ClientMessage.DraftChanged(draft));
    private string notice { get => model.Notice; set => Dispatch(new ClientMessage.NoticeChanged(value)); }
    private string? pendingDelete { get => model.PendingDelete; set => Dispatch(new ClientMessage.DeleteMarked(value)); }
    private bool creating { get => model.Creating; set => Dispatch(new ClientMessage.CreatingChanged(value)); }
    private bool loaded { get => model.Loaded; set => Dispatch(new ClientMessage.LoadedChanged(value)); }
    private bool busy { get => model.Busy; set => Dispatch(new ClientMessage.BusyChanged(value)); }
    private bool dialogOpen => model.Dialog is not null;
    private bool showCharacters
    {
        get => model.Options.ShowCharacters;
        set => Dispatch(new ClientMessage.OptionsChanged(model.Options with { ShowCharacters = value }));
    }
    private bool showMap
    {
        get => model.Options.ShowMap;
        set => Dispatch(new ClientMessage.OptionsChanged(model.Options with { ShowMap = value }));
    }
    private bool fullKeys
    {
        get => model.Options.FullKeys;
        set => Dispatch(new ClientMessage.OptionsChanged(model.Options with { FullKeys = value }));
    }
    private bool followLog
    {
        get => model.Options.FollowLog;
        set => Dispatch(new ClientMessage.OptionsChanged(model.Options with { FollowLog = value }));
    }

    private void Refresh()
    {
        if (disposed) return;
        if (active is { } session) session.GraphicsImage = graphics.Draw(session,
            gameControl?.XamlRoot?.RasterizationScale ?? 1, DisplayMap(session), model.Options.MapZoom / 100.0,
            model.Options.SmoothMove && visualPreferences.AnimationsEnabled);
        model = ClientUpdate.Apply(model, new ClientMessage.GraphicsChanged(graphics.Revision, graphics.Notice));
        publish?.Invoke(model);
    }
}
