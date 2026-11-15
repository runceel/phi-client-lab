using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using PhiClient;
using PhiClient.Core;
using static Microsoft.UI.Reactor.Factories;

partial class App
{
    private MenuBarItemData? MasterMenu(GameSession? session)
    {
        if (session is null || model.MasterMenu is not { } menu || !MasterMagic.Visible(session.Stats, model.Options))
            return null;
        MenuFlyoutItemBase Item(MasterItem item) => item.Separator ? MenuSeparator() :
            MenuItem(item.Caption, () => RunMaster(session, item)) with
            {
                IsEnabled = session.Connected && !session.Model.Transferring && !session.PreparingMagic &&
                    session.Model.Master is null
            };
        return Menu(menu.Caption, menu.Groups.Select(group =>
            (MenuFlyoutItemBase)MenuSubItem(group.Caption, group.Items.Select(Item).ToArray())).ToArray());
    }
}
