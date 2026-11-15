using System.Xml;
using PhiClient.Core;

namespace PhiClient.Core.Tests;

public sealed class MasterMagicTests
{
    [Fact]
    public void XmlMenuPreservesOrderedStepsCaptionsSeparatorsAndOriginalEncodedLineBreaks()
    {
        var menu = MasterMagic.Parse("""
            <mastermagic caption="超魔法(&amp;S)">
            <menu caption="移動"><item caption="jump"><send list="no">cast</send><send>list</send>
            <int_dialog>対象&amp;#xa;番号</int_dialog><send list="no">cast</send><send>jump %int</send></item>
            <item caption="-"/><item caption="文字"><string_dialog>名前</string_dialog><ignored>ignored</ignored>
            <send>%string</send></item></menu></mastermagic>
            """);
        Assert.Equal("超魔法", menu.Caption);
        var group = Assert.Single(menu.Groups);
        Assert.Equal("移動", group.Caption);
        Assert.True(group.Items[1].Separator);
        var first = MasterMagic.Advance(new(group.Items[0].Steps));
        Assert.Equal(new[] {"cast", "list"}, first.Lines);
        Assert.Equal(1, first.HiddenLists);
        Assert.Equal("対象\n番号", first.Next.Prompt!.Text);
        Assert.Null(MasterMagic.Answer(first.Next, null));
        var next = MasterMagic.Advance(MasterMagic.Answer(first.Next, " -3 ")!);
        Assert.Equal(new[] {"cast", "jump -3"}, next.Lines);
        Assert.Equal(1, next.HiddenLists);
        Assert.Null(next.Next.Prompt);
        Assert.Throws<ArgumentException>(() => MasterMagic.Answer(first.Next, "３"));
        Assert.Throws<ArgumentException>(() => MasterMagic.Answer(first.Next, "3\n#x"));
    }

    [Fact]
    public void ConfirmationPrecedesSendingAndStringThenIntegerSubstitutionRemovesOnlyActualLineBreaks()
    {
        var run = new MasterRun([
            new(MasterStepKind.Confirm, "確認"), new(MasterStepKind.Integer, "数"),
            new(MasterStepKind.String, "文字"), new(MasterStepKind.Send, "%string:%int\n\\n")
        ]);
        var confirm = MasterMagic.Advance(run);
        Assert.Empty(confirm.Lines);
        var integer = MasterMagic.Advance(MasterMagic.Answer(confirm.Next, "")!);
        var text = MasterMagic.Advance(MasterMagic.Answer(integer.Next, "2")!);
        var send = MasterMagic.Advance(MasterMagic.Answer(text.Next, "a\r\n%int")!);
        Assert.Equal("a2:2\\n", Assert.Single(send.Lines));
        Assert.Throws<InvalidOperationException>(() => MasterMagic.Advance(confirm.Next));
    }

    [Fact]
    public void XmlEntitiesAreProhibitedAndNonMasterVisibilityRequiresTheExplicitSetting()
    {
        Assert.Throws<XmlException>(() => MasterMagic.Parse("""
            <!DOCTYPE mastermagic [<!ENTITY leak SYSTEM "file:///private">]>
            <mastermagic caption="&leak;" />
            """));
        Assert.Throws<XmlException>(() => MasterMagic.Parse("<not-a-menu/>"));
        Assert.False(MasterMagic.Visible(null, new()));
        Assert.True(MasterMagic.Visible(null, new() { MasterMagicMenu = true }));
        Assert.True(MasterMagic.Visible(new("Master", 1, 1, 1, 1, 99999999, 0, 0, 0, 0, 0), new()));
    }

    [Fact]
    public void ChangingOrClosingTheTransportClearsTheWizardButRetainsTheCommandDraft()
    {
        var original = GameUpdate.Apply(new() { Input = "draft" },
            new GameMessage.MasterChanged(new([new(MasterStepKind.Integer, "target")])));
        foreach (var message in new GameMessage[]
        {
            new GameMessage.PhaseChanged(ConnectionPhase.Offline),
            new GameMessage.PhaseChanged(ConnectionPhase.Connecting),
            new GameMessage.Received(new WorldMoved("127.0.0.1", 12345), 100)
        })
        {
            var changed = GameUpdate.Apply(original, message);
            Assert.Null(changed.Master);
            Assert.Equal("draft", changed.Input);
            Assert.NotNull(original.Master);
        }
    }
}
