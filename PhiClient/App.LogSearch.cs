using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using PhiClient;
using PhiClient.Core;
using static Microsoft.UI.Reactor.Factories;

partial class App
{
    private void OpenLogSearch(GameSession session)
    {
        if (showCharacters || model.Worlds.Visible || model.SettingsEditor is not null ||
            model.Registration.Stage != RegistrationStage.None || dialogOpen)
        {
            notice = "ログ検索はゲーム画面で開いてください。";
            return;
        }
        if (session.Model.Priv.Visible && windowWidth < 1100)
            session.Dispatch(new GameMessage.PrivVisibilityChanged(false));
        if (session.Model.LogSearchVisible)
        {
            session.LogSearchPanel?.Focus();
            return;
        }
        session.Dispatch(new GameMessage.LogSearchVisibilityChanged(true));
    }

    private Element LogSearchView(GameSession session) => !session.Model.LogSearchVisible ? Empty() :
        Border(Empty()).AutomationId("log-search").OnMount(element =>
        {
            var host = (Microsoft.UI.Xaml.Controls.Border)element;
            void Attach(object sender, RoutedEventArgs args)
            {
                host.Loaded -= Attach;
                if (session.LogPresenter is not { Control.IsLoaded: true } presenter)
                {
                    notice = "ログ表示の準備ができていないため検索を開けませんでした。ゲーム画面で再度お試しください。";
                    session.Dispatch(new GameMessage.LogSearchVisibilityChanged(false));
                    return;
                }
                var panel = new LogSearchPanel(presenter, () =>
                {
                    session.Dispatch(new GameMessage.LogSearchVisibilityChanged(false));
                    dispatcher?.TryEnqueue(() =>
                    {
                        if (active == session && gameControl is { IsLoaded: true } &&
                            model.SettingsEditor is null && !dialogOpen)
                            gameControl.Focus(FocusState.Keyboard);
                    });
                });
                session.LogSearchPanel = panel;
                host.Child = panel.Surface;
                host.Unloaded += (_, _) =>
                {
                    if (ReferenceEquals(session.LogSearchPanel, panel)) session.LogSearchPanel = null;
                };
            }
            host.Loaded += Attach;
            if (host.IsLoaded) Attach(host, new RoutedEventArgs());
        });
}
