using PhiClient.Core;

namespace PhiClient.Core.Tests;

public sealed class TextInputBindingTests
{
    [Fact]
    public void NativeEditsReachTheModelBeforeAnUnrelatedRenderCanRestoreOldText()
    {
        var model = ClientUpdate.Apply(new(), new ClientMessage.GameOpened("chat"));
        var native = "";
        void Changed(string text) => model = ClientUpdate.Apply(model,
            new ClientMessage.GameChanged("chat", new GameMessage.InputChanged(text)));
        var binding = new TextInputBinding(Changed);
        void Write(string text)
        {
            if (native == text) return;
            native = text;
            binding.Edited(native);
        }
        binding.Apply(model.Games["chat"].Input, Changed, Write);

        native = "first\nunsent";
        binding.Edited(native);
        var captured = model.Games["chat"];
        Assert.Equal(native, captured.Input);
        model = ClientUpdate.Apply(model,
            new ClientMessage.GameChanged("chat", new GameMessage.Logged("received during editing")));
        binding.Apply(model.Games["chat"].Input, Changed, Write);
        Assert.Equal("first\nunsent", native);
        Assert.Equal(captured.InputRevision, model.Games["chat"].InputRevision);

        model = ClientUpdate.Apply(model,
            new ClientMessage.GameChanged("chat", new GameMessage.ChatSent(captured.InputRevision, 2)));
        binding.Apply(model.Games["chat"].Input, Changed, Write);
        Assert.Equal("unsent", native);
        Assert.Equal(captured.InputRevision + 1, model.Games["chat"].InputRevision);
    }

    [Fact]
    public void NativeEditsDuringAnAsyncSendProtectEvenAReturnToTheOriginalDraft()
    {
        var model = GameUpdate.Apply(new(), new GameMessage.InputChanged("sending"));
        var sentRevision = model.InputRevision;
        var native = model.Input;
        void Changed(string text) => model = GameUpdate.Apply(model, new GameMessage.InputChanged(text));
        var binding = new TextInputBinding(Changed);
        binding.Apply(model.Input, Changed, _ => { });

        foreach (var text in new[] { "new draft", "sending" })
        {
            native = text;
            binding.Edited(native);
        }
        model = GameUpdate.Apply(model, new GameMessage.ChatSent(sentRevision, 3));
        binding.Apply(model.Input, Changed, text => native = text);

        Assert.Equal("sending", native);
        Assert.Equal(sentRevision + 2, model.InputRevision);
    }

    [Fact]
    public void ReusedControlsReplaceTheCallbackBeforeApplyingModelTextWithoutEditingEitherField()
    {
        var host = "";
        var filter = "";
        var binding = new TextInputBinding(text => host = text);
        binding.Edited("127.0.0.1");

        binding.Apply("selected world", text => filter = text, binding.Edited);
        Assert.Equal("127.0.0.1", host);
        Assert.Empty(filter);

        binding.Edited("search");
        Assert.Equal("search", filter);
        Assert.Equal("127.0.0.1", host);
    }

    [Fact]
    public void AnInFlightRenderCannotFeedItsStaleValueBackIntoANewerNativeDraft()
    {
        var model = "";
        var native = "";
        void Changed(string text) => model = text;
        var binding = new TextInputBinding(Changed);
        void Write(string text)
        {
            native = text;
            binding.Edited(text);
        }
        var inFlight = model;
        Write("new native draft");
        binding.Apply(inFlight, Changed, Write);
        Assert.Equal("new native draft", model);

        binding.Apply(model, Changed, Write);
        Assert.Equal("new native draft", native);
    }

    [Fact]
    public void AFailedProgrammaticWriteDoesNotSuppressTheNextNativeEdit()
    {
        var edits = new List<string>();
        var binding = new TextInputBinding(edits.Add);
        Assert.Throws<InvalidOperationException>(() =>
            binding.Apply("model text", edits.Add, _ => throw new InvalidOperationException("write failed")));

        binding.Edited("next edit");
        Assert.Equal(["next edit"], edits);
    }
}
