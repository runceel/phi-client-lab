using PhiClient;
using PhiClient.Core;

partial class App
{
    private AutomapStore? automapStore;
    private readonly HashSet<string> loadingMaps = new(StringComparer.Ordinal);
    private bool savingMaps;
    private static readonly AutomapWorld EmptyMapping = new();

    private AutomapStore AutomapFiles => automapStore ??= new(Path.Combine(store.Folder, "Maps"));

    private async Task LoadAutomapAsync(GameSession session, string world)
    {
        if (model.Automaps.GetValueOrDefault(world)?.Loaded == true || !loadingMaps.Add(world)) return;
        try
        {
            Dispatch(new ClientMessage.AutomapLoaded(world, await AutomapFiles.LoadAsync(world, worldLifetime.Token)));
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            Dispatch(new ClientMessage.AutomapFailed(world, ex.Message));
            session.Append("[記録地図] 読み込みに失敗しました。ファイルは上書きしません: " + ex.Message);
        }
        finally { loadingMaps.Remove(world); }
    }

    private async Task FlushAutomapsAsync()
    {
        if (savingMaps) return;
        savingMaps = true;
        try
        {
            foreach (var (world, memory) in model.Automaps)
            {
                if (!memory.Loaded || memory.Error.Length > 0) continue;
                foreach (var block in memory.Dirty)
                {
                    var cells = memory.Blocks[block];
                    await AutomapFiles.SaveBlockAsync(world, block, cells);
                    Dispatch(new ClientMessage.AutomapSaved(world, block, cells));
                }
            }
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            notice = "記録地図を保存できませんでした。メモリ上の記録を保持し、次回の保存で再試行します: " + ex.Message;
        }
        finally { savingMaps = false; }
    }

    private async Task ClearAutomapAsync()
    {
        if (active is not { World: not "" and not "???" } session) return;
        var world = session.World;
        var token = worldLifetime.Token;
        if (!await SessionDialogAsync("記録地図を消す", $"{world} の記録地図を消します。よろしいですか？", "消す"))
            return;
        // A flush may already own an older snapshot; finish it before removing this world's files.
        while (savingMaps || loadingMaps.Contains(world)) await Task.Delay(20, token);
        savingMaps = true;
        try
        {
            await AutomapFiles.ClearAsync(world, token);
            Dispatch(new ClientMessage.AutomapCleared(world));
        }
        finally { savingMaps = false; }
    }

    private MapDisplay? DisplayMap(GameSession session)
    {
        if (session.Map is not { } map) return null;
        var enabled = model.Options.Automap && session.NorthFixed;
        var memory = model.Automaps.GetValueOrDefault(session.World) ?? EmptyMapping;
        var zoom = model.Options.MapZoom / 100.0;
        var ring = Automap.RingFor(session.MapViewportWidth, session.MapViewportHeight - 16 * zoom, 32 * zoom);
        var key = (map, memory, session.Model.Prediction.Offset, ring, enabled);
        if (session.DisplayKey == key && session.Display is not null) return session.Display;
        session.DisplayKey = key;
        return session.Display = enabled ? Automap.Compose(map, memory, ring, session.Model.Prediction.Offset) :
            new(map, []);
    }
}
