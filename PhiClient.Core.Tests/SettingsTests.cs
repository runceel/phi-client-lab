using PhiClient.Core;

namespace PhiClient.Core.Tests;

public sealed class SettingsTests
{
    [Fact]
    public async Task MovementPadVisibilityAndZoomPersistAndUnspecifiedPadStartsHidden()
    {
        var folder = Path.Combine(Path.GetTempPath(), "phi-pad-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "settings.json");
            var store = new SettingsStore(path);
            var imported = SettingsStore.ImportPhilly(new() { ShowMovementPad = false, MapZoom = 175 },
                PhiWire.Encode("[Move]\nFullKeyMove=off\n"));
            await store.SaveAsync(imported);
            var loaded = await store.LoadAsync();
            Assert.False(loaded.ShowMovementPad);
            Assert.Equal(175, loaded.MapZoom);
            Assert.False(loaded.FullKeys);
            await File.WriteAllTextAsync(path, "{\"mapZoom\":125}");
            var legacy = await store.LoadAsync();
            Assert.False(legacy.ShowMovementPad);
            Assert.Equal(125, legacy.MapZoom);
            await File.WriteAllTextAsync(path, "{\"showMovementPad\":true,\"mapZoom\":175}");
            Assert.True((await store.LoadAsync()).ShowMovementPad);
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public void IntegratedMapWidthReservesLogSpaceWithoutLosingTheSavedPreference()
    {
        var original = new ClientModel();
        var resized = ClientUpdate.Apply(original, new ClientMessage.MapPaneWidthChanged(900));
        Assert.Equal(360, original.Options.MapPaneWidth);
        Assert.Equal(900, resized.Options.MapPaneWidth);
        Assert.Equal(640, MapPaneLayout.Width(resized.Options.MapPaneWidth, 1000));
        Assert.Equal(900, MapPaneLayout.Width(resized.Options.MapPaneWidth, 1600));
        Assert.Equal(200, MapPaneLayout.Width(100, 1000));
        Assert.Equal(200, MapPaneLayout.Width(1600, 100));
        Assert.Equal(900, resized.Options.MapPaneWidth);
        Assert.Throws<ArgumentOutOfRangeException>(() => MapPaneLayout.Width(360, double.NaN));
        Assert.Equal(184, ClientUpdate.Apply(resized, new ClientMessage.DetachedMapWidthChanged(10)).DetachedMapWidth);
    }

    [Fact]
    public void PhillyLogOptionsAndFontNameImportWithoutChangingUnspecifiedOptions()
    {
        var options = SettingsStore.ImportPhilly(new(), PhiWire.Encode(
            "[Font]\nLogFontName=ＭＳ 明朝\n[Log]\nLogUrlLink=off\nLogImageDisp=off\n" +
            "LogImageDispURL=off\nLogImageLinkDisp=off\nLogImageDispLoud=off\n" +
            "LogLargeDispLoud=on\nNotInterpretedTagDisp=on\n"));
        Assert.Equal("ＭＳ 明朝", options.LogFontName);
        Assert.False(options.LogUrlLink);
        Assert.False(options.LogImageDisp);
        Assert.False(options.LogImageDispURL);
        Assert.False(options.LogImageLinkDisp);
        Assert.False(options.LogImageDispLoud);
        Assert.True(options.LogLargeDispLoud);
        Assert.True(options.NotInterpretedTagDisp);
        Assert.True(options.LogDispTime);
    }

    [Fact]
    public void EditingSettingsKeepsLiveOptionsUntilAppliedAndSpellOptionsRespectTargetLists()
    {
        var original = new ClientModel();
        var editor = new SettingsEditorModel(original.Options with { FullKeys = false }, "75", "16");
        var editing = ClientUpdate.Apply(original, new ClientMessage.SettingsEdited(editor));
        Assert.True(editing.Options.FullKeys);
        Assert.False(editing.SettingsEditor!.Value.FullKeys);
        var applied = ClientUpdate.Apply(editing, new ClientMessage.OptionsChanged(editor.Value));
        Assert.False(applied.Options.FullKeys);
        Assert.True(original.Options.FullKeys);
        Assert.Null(ClientUpdate.Apply(editing, new ClientMessage.SettingsEdited(null)).SettingsEditor);
        var options = new ClientOptions { AutoEquipSystemMagic = true };
        foreach (var spell in new[] { "wizard eye", "wizard light", "list", "eagle eye", "identify",
                     "party eye", "wizard lock", "unlock", "call", "return" })
            Assert.True(GameInput.ApplyOptions(GameInput.Spell(spell), options).AutoReequip);
        foreach (var spell in new[] { "detect", "create", "destroy", "analyze", "forget", "charge spell" })
            Assert.False(GameInput.ApplyOptions(GameInput.Spell(spell), options).AutoReequip);
        var disabled = GameInput.ApplyOptions(GameInput.Spell("wizard eye"),
            options with { AutoUnequipSystemMagic = false });
        Assert.False(disabled.AutoUnequip);
        Assert.False(disabled.AutoReequip);
        Assert.False(GameInput.ApplyOptions(GameInput.Spell("1", false), options).AutoUnequip);
    }

    [Fact]
    public async Task SettingsRoundTripImportsOriginalFieldsWithoutCredentialsOrNetwork()
    {
        var folder = Path.Combine(Path.GetTempPath(), "phi-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var store = new SettingsStore(Path.Combine(folder, "settings.json"));
            var settings = SettingsStore.ImportPhilly(new(), PhiWire.Encode(
                "[Move]\nFullKeyMove=off\nFullKeyMove=on\n[Shortcut]\nShortcutF08=\"go N\\ngo E;保持\"\n" +
                "ListModeShortcutF08=.\nAltGWord=こんにちは\n[Automation]\nAutoEquipSystemMagic=on\n" +
                "[LogEdit]\nLogTagDeleteCutCopy=on\n[Log]\nLogDispTime=off\n"));
            await store.SaveAsync(settings with { MapZoom = 999, LogFontSize = 999, MapPaneWidth = 9999 });
            var loaded = await store.LoadAsync();
            Assert.False(loaded.FullKeys);
            Assert.False(loaded.LogDispTime);
            Assert.True(loaded.AutoEquipSystemMagic);
            Assert.True(loaded.LogTagDeleteCopy);
            Assert.Equal(400, loaded.MapZoom);
            Assert.Equal(1600, loaded.MapPaneWidth);
            Assert.Equal(64u, loaded.LogFontSize);
            Assert.Equal("go N\\ngo E;保持", loaded.Shortcuts[0]);
            Assert.Equal(["go N", "go E;保持"],
                GameInput.Resolve(ConsoleKey.F8, true, options: loaded)!.Commands);
            Assert.Equal(["."], GameInput.Resolve(ConsoleKey.F8, true, listing: true, options: loaded)!.Commands);
            Assert.Equal(["こんにちは"], GameInput.Resolve(ConsoleKey.G, true, alt: true, options: loaded)!.Commands);
            Assert.Null(GameInput.Resolve(ConsoleKey.F8, true, control: true, options: loaded));
        }
        finally { Directory.Delete(folder, true); }
    }

    [Fact]
    public async Task InvalidSettingsAreReportedAndFailedSaveDoesNotReplaceGoodFile()
    {
        var folder = Path.Combine(Path.GetTempPath(), "phi-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var path = Path.Combine(folder, "settings.json");
            var store = new SettingsStore(path);
            await store.SaveAsync(new ClientOptions { MapZoom = 75 });
            var before = await File.ReadAllBytesAsync(path);
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                store.SaveAsync(new ClientOptions { Shortcuts = ["incomplete"] }));
            Assert.Equal(before, await File.ReadAllBytesAsync(path));
            await File.WriteAllTextAsync(path, "{invalid");
            await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => store.LoadAsync());
            Assert.Throws<ArgumentException>(() => GameInput.Resolve(ConsoleKey.F8, true,
                options: new ClientOptions { Shortcuts = ["bad\0command", "", "", "", ""] }));
        }
        finally { Directory.Delete(folder, true); }
    }
}
