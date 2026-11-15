using PhiClient.Core;

namespace PhiClient.Core.Tests;

public sealed class MapEffectTests
{
    [Theory]
    [InlineData(112, 120)]
    [InlineData(224, 240)]
    [InlineData(448, 480)]
    public void DenseMeasuredLabelsStayInsideMapWithoutOverlapping(int mapWidth, int mapHeight)
    {
        var occupied = new List<MapEffectBounds>();
        var sizes = new[] { (Math.Min(186, mapWidth), 48), (Math.Min(186, mapWidth), 48),
            (42, 22), (26, 22), (64, 22), (30, 22) };
        foreach (var (width, height) in sizes)
        {
            var result = MapEffectLayout.Place(mapWidth / 2.0 - width / 2.0, -8, width, height,
                mapWidth, mapHeight, occupied);
            if (result is not { } bounds) continue;
            Assert.InRange(bounds.X, 0, mapWidth - bounds.Width);
            Assert.InRange(bounds.Y, 0, mapHeight - bounds.Height);
            Assert.All(occupied, previous => Assert.False(bounds.Overlaps(previous)));
            occupied.Add(bounds);
        }
        Assert.Equal(mapWidth == 112 ? 2 : sizes.Length, occupied.Count);
    }

    [Fact]
    public void EffectPlacementKeepsNearestFreePositionAndOmitsLabelsWhenMapIsFull()
    {
        MapEffectBounds[] occupied = [new(0, 0, 100, 40)];
        Assert.Equal(new MapEffectBounds(20, 42, 40, 20),
            MapEffectLayout.Place(20, 10, 40, 20, 100, 100, occupied));
        Assert.Null(MapEffectLayout.Place(0, 0, 40, 20, 100, 40, occupied));
        Assert.Null(MapEffectLayout.Place(0, 0, 101, 20, 100, 100, []));
        Assert.Null(MapEffectLayout.Place(0, 0, 40, 101, 100, 100, []));
    }

    [Theory]
    [InlineData("DM > Alice は Slime を 剣 で攻撃した. [88 hp]", "Slime", MapEffectKind.HpLost, "88", "Alice")]
    [InlineData("DM > Alice attacked to Slime by Sword. [88 hp]", "Slime", MapEffectKind.HpLost, "88", "Alice")]
    [InlineData("DM > Alice が Cure によって回復した. [14 hp]", "Alice", MapEffectKind.HpGained, "+14", null)]
    [InlineData("DM > Alice は Bob を Cure で回復した. [9 mp]", "Bob", MapEffectKind.MpGained, "+9", null)]
    [InlineData("DM > Alice is cured by Cure. [14 hp]", "Alice", MapEffectKind.HpGained, "+14", null)]
    [InlineData("DM > Slime は Alice の Fire による攻撃を吸収した. [30 hp]", "Slime", MapEffectKind.HpGained, "+30", "Alice")]
    [InlineData("DM > Alice は食中毒でダメージを受けた. [3 mp]", "Alice", MapEffectKind.MpLost, "3", null)]
    [InlineData("DM > Alice はミスした.", "Alice", MapEffectKind.Miss, "MISS", null)]
    [InlineData("DM > Alice miss.", "Alice", MapEffectKind.Miss, "MISS", null)]
    [InlineData("DM > Alice は 3 lv の毒を受けた.", "Alice", MapEffectKind.Poison, "毒", null)]
    [InlineData("DM > Alice is 3 lv poisoned.", "Alice", MapEffectKind.Poison, "毒", null)]
    [InlineData("DM > Alice は 5 秒間 麻痺する.", "Alice", MapEffectKind.Palsy, "麻痺", null)]
    [InlineData("DM > Alice is paralyzed in 5 sec.", "Alice", MapEffectKind.Palsy, "麻痺", null)]
    [InlineData("DM > 防御率が 20% 60 秒間上がった.", "", MapEffectKind.Buff, "防御率↑", null)]
    [InlineData("DM > Attack speed is 10% up in 60 sec.", "", MapEffectKind.Buff, "攻撃速度↑", null)]
    [InlineData("DM > Force field が 30 秒間張られる.", "", MapEffectKind.Buff, "Force field", null)]
    [InlineData("DM > Slime の攻撃力が 5 point Degrade によって下がった.", "Slime", MapEffectKind.Debuff, "攻撃力↓", null)]
    public void ProtocolMessagesProduceTargetedEffects(string raw, string name, MapEffectKind kind, string text, string? by)
    {
        var result = new MapEffectState().Receive(new(raw), 100, null);
        var effect = Assert.Single(result.Items);
        Assert.Equal((name, kind, text, by), (effect.Name, effect.Kind, effect.Text, effect.By));
    }

    [Fact]
    public void SpeechReplacesOnlyItsSpeakerAndRemembersSpellsPerSession()
    {
        var state = new MapEffectState().Receive(new("Alice > Fire ball"), 0, null)
            .Receive(new("[Bob] > /*color=red*/内緒/*.*/"), 100, null)
            .Receive(new("Alice > Fire ball"), 200, null);
        Assert.Equal(2, state.Items.Length);
        Assert.Equal("内緒", state.Items.Single(item => item.Name == "Bob").Text);
        state = state.Receive(new("DM > Alice は Slime を Fire ball で攻撃した. [30 hp]"), 300, null);
        Assert.True(state.Items[^1].Magic);
        Assert.False(new MapEffectState().Receive(
            new("DM > Alice は Slime を Fire ball で攻撃した. [30 hp]"), 300, null).Items[^1].Magic);
        Assert.True(state.Items[^1].Alive(1499));
        Assert.False(state.Items[^1].Alive(1500));
        Assert.True(state.Items[1].Alive(4199));
        Assert.False(state.Items[1].Alive(4200));
    }

    [Fact]
    public void CastTargetIsConsumedByOneDmLineAndLiteralTextCannotCreateEffects()
    {
        var state = new MapEffectState().Receive(new("DM > Alice は Bob に Protect をかけた."), 0, null)
            .Receive(new("DM > 防御率が 20% 上がった."), 1, null)
            .Receive(new("DM > 魔法抵抗率が 20% 上がった."), 2, null);
        Assert.Equal(["Bob", ""], state.Items.Select(item => item.Name));
        Assert.Empty(new MapEffectState().Receive(new("Alice > ignored") { Literal = true }, 0, null).Items);
        Assert.Empty(new MapEffectState().Receive(new("DM > Fighter level が 1 上昇した."), 0, null).Items);
        Assert.Empty(new MapEffectState().Receive(new("DM > Alice は既に麻痺している."), 0, null).Items);
    }

    [Fact]
    public void DeathAnchorsNearestNamedActorAndWorldAndReconnectClearState()
    {
        Actor[] actors = [new(0, 0, "Slime", "", 'F', '*'), new(4, 3, "/*color=red*/Slime", "", 'F', '*')];
        var map = new MapFrame('N', [], [], actors);
        var model = new GameModel { Map = map };
        model = GameUpdate.Apply(model, new GameMessage.Received(new LogLine("DM > Slime is killed by Alice."), 10));
        Assert.Equal((1, 0), Assert.Single(model.MapEffects.Items).Anchor);
        model = GameUpdate.Apply(model, new GameMessage.Received(map with { Actors = [] }, 20));
        Assert.Single(model.MapEffects.Items);
        Assert.Empty(GameUpdate.Apply(model, new GameMessage.PhaseChanged(ConnectionPhase.Offline)).MapEffects.Items);
        Assert.Empty(GameUpdate.Apply(model, new GameMessage.PhaseChanged(ConnectionPhase.Connecting)).MapEffects.Items);
        Assert.Empty(GameUpdate.Apply(model, new GameMessage.Received(new WorldMoved("localhost", 2000))).MapEffects.Items);
    }

    [Fact]
    public void HiddenTabsAndDisabledEffectsKeepSpellContextWithoutReplayingVisuals()
    {
        var model = ClientUpdate.Apply(new(), new ClientMessage.GameOpened("one"));
        model = ClientUpdate.Apply(model, new ClientMessage.GameOpened("two"));
        model = ClientUpdate.Apply(model, new ClientMessage.GameChanged("one",
            new GameMessage.Received(new LogLine("Alice > Fire"), 0)));
        Assert.Empty(model.Games["one"].MapEffects.Items);
        Assert.Equal("Fire", model.Games["one"].MapEffects.Spoken["Alice"]);
        model = ClientUpdate.Apply(model, new ClientMessage.GameChanged("two",
            new GameMessage.Received(new LogLine("Bob > Hello"), 1)));
        Assert.Single(model.Games["two"].MapEffects.Items);
        model = ClientUpdate.Apply(model, new ClientMessage.OptionsChanged(model.Options with { MapEffects = false }));
        Assert.Empty(model.Games["two"].MapEffects.Items);
        model = ClientUpdate.Apply(model, new ClientMessage.GameChanged("two",
            new GameMessage.Received(new LogLine("Bob > New"), 2)));
        model = ClientUpdate.Apply(model, new ClientMessage.OptionsChanged(model.Options with { MapEffects = true }));
        Assert.Empty(model.Games["two"].MapEffects.Items);
    }
}
