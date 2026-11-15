using PhiClient.Core;

namespace PhiClient.Core.Tests;

public sealed class RegistrationFormTests
{
    [Theory]
    [InlineData(RegistrationStage.SavePending, true)]
    [InlineData(RegistrationStage.Unknown, true)]
    [InlineData(RegistrationStage.Review, false)]
    public void OnlyUnresolvedRegistrationSecretsMakeDiscardConfirmationCancelByDefault(
        RegistrationStage stage, bool cancelDefault)
    {
        var model = new ClientModel { Registration = new(stage), Draft = new() { Id = "private", Password = "secret" } };
        var dialog = model.Registration.DiscardConfirmation();
        Assert.Equal(cancelDefault, dialog.CancelDefault);
        var closed = ClientUpdate.Apply(ClientUpdate.Apply(model, new ClientMessage.DialogChanged(dialog)),
            new ClientMessage.DialogChanged(null));
        Assert.Same(model.Draft, closed.Draft);
        Assert.Same(model.Registration, closed.Registration);
        Assert.False(new DialogModel("Send", "Confirm", "Send").CancelDefault);
    }

    [Fact]
    public void WorldStepOnlyValidatesEndpointWithoutGeneratingCredentials()
    {
        var draft = new CharacterDraft { Host = "127.0.0.1", Port = "2000" };
        Assert.Empty(RegistrationForm.Errors(draft, true, worldOnly: true));
        Assert.NotEmpty(RegistrationForm.Errors(draft, true));
        Assert.Empty(draft.Password);
        Assert.Contains("server-host", RegistrationForm.Errors(draft with { Port = "no" }, true, true).Keys);
    }

    [Fact]
    public void FormDistinguishesLocalIdFromServerCreationAndPreservesWireWarnings()
    {
        var draft = new CharacterDraft { Host = "localhost", Port = "2000", Name = "名前", Id = "synthetic" };
        Assert.Empty(RegistrationForm.Errors(draft, false));
        Assert.Empty(RegistrationForm.Errors(draft, true));
        Assert.Contains("new-password", RegistrationForm.Errors(draft, true, customPassword: true).Keys);
        var invalid = RegistrationForm.Errors(draft with { Name = "a", Password = "bad", Image = "-1", Email = "a b" }, true);
        Assert.Equal(4, invalid.Count);
        Assert.Equal("名前", RegistrationForm.WireText("名前"));
        Assert.NotEqual("名前😀", RegistrationForm.WireText("名前😀"));
    }

    [Theory]
    [InlineData(RegistrationStage.Creating)]
    [InlineData(RegistrationStage.SavePending)]
    [InlineData(RegistrationStage.Unknown)]
    public void FrozenResultsCannotBeChangedByEditors(RegistrationStage stage)
    {
        var original = new ClientModel { Draft = new() { Name = "保持", Id = "private", Password = "secret" },
            Registration = new(stage) };
        var changed = ClientUpdate.Apply(original, new ClientMessage.DraftChanged(new()));
        Assert.Same(original, changed);
        if (stage != RegistrationStage.Creating) Assert.True(changed.Registration.NeedsDiscardWarning);
        Assert.False(changed.Registration.CanEdit);
        var restarted = ClientUpdate.Apply(changed, new ClientMessage.RegistrationStarted(true));
        Assert.Same(changed, restarted);
    }

    [Fact]
    public void ReceivedIdRetainsOneStableCharacterAndPasswordUntilExplicitCompletionOrDiscard()
    {
        var original = new ClientModel { Registration = new(RegistrationStage.Creating),
            Draft = new() { Name = "名前", Password = "secret" } };
        var character = new SavedCharacter("stable-key", "名前", "localhost", 2000);
        var pending = ClientUpdate.Apply(original, new ClientMessage.RegistrationCreated(character, "private"));
        Assert.Equal(RegistrationStage.SavePending, pending.Registration.Stage);
        Assert.Same(character, pending.Registration.Character);
        Assert.Equal("private", pending.Draft.Id);
        Assert.Equal("secret", pending.Draft.Password);
        var ended = ClientUpdate.Apply(pending, new ClientMessage.RegistrationEnded());
        Assert.Null(ended.Registration.Character);
        Assert.Empty(ended.Draft.Id);
        Assert.Empty(ended.Draft.Password);
    }
}
