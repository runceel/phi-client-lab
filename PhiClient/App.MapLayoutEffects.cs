using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Reactor.Layout;
using PhiClient;
using PhiClient.Core;
using static Microsoft.UI.Reactor.Factories;

partial class App
{
    private MapPaneSplitter? mapSplitter;

    private double MapAvailableWidth => Math.Max(0, windowWidth - (active?.Model.Priv.Visible == true ? 352 : 0));

    private void SyncMapSplitter() => mapSplitter?.Configure(
        MapPaneLayout.Width(model.Options.MapPaneWidth, MapAvailableWidth), MapPaneLayout.MaximumFor(MapAvailableWidth));

    private Element MapSplitter(double height) => Border(Empty()).Width(12).Height(height).Flex(shrink: 0)
        .IsEnabled(!busy && !dialogOpen && !closingApplication)
        .OnMount(element =>
        {
            var thumb = new MapPaneSplitter((width, save) =>
            {
                // Automation providers can run without the XAML synchronization context.
                if (dispatcher?.TryEnqueue(() =>
                {
                    Dispatch(new ClientMessage.MapPaneWidthChanged(width));
                    if (save) _ = PerformAsync(SaveMapOptionsAsync);
                }) != true)
                    throw new InvalidOperationException("地図の幅を変更する画面が終了しました。");
            });
            mapSplitter = thumb;
            SyncMapSplitter();
            thumb.Unloaded += (_, _) => { if (mapSplitter == thumb) mapSplitter = null; };
            ((Microsoft.UI.Xaml.Controls.Border)element).Child = thumb;
        });

    private void SetMapOptions(ClientOptions options)
    {
        Dispatch(new ClientMessage.OptionsChanged(options));
        _ = PerformAsync(SaveMapOptionsAsync);
    }

    private async Task SaveMapOptionsAsync()
    {
        settingsStore ??= new(Path.Combine(store.Folder, "settings.json"));
        ClientOptions saved;
        do
        {
            saved = model.Options;
            await settingsStore.SaveAsync(saved, worldLifetime.Token);
        } while (model.Options != saved);
        notice = "地図の表示設定を保存しました。";
    }
}
