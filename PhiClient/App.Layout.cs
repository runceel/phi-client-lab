using Microsoft.UI.Xaml;
using Microsoft.UI.Windowing;
using PhiClient.Core;

partial class App
{
    private double windowWidth => model.WindowWidth;
    private double windowHeight => model.WindowHeight;

    private static void AttachReadableScroll(Microsoft.UI.Xaml.Controls.ScrollViewer scroll)
    {
        scroll.HorizontalScrollBarVisibility = Microsoft.UI.Xaml.Controls.ScrollBarVisibility.Disabled;
        scroll.HorizontalScrollMode = Microsoft.UI.Xaml.Controls.ScrollMode.Disabled;
        scroll.HorizontalContentAlignment = HorizontalAlignment.Stretch;
    }

    private void AttachWindowLayout(FrameworkElement root)
    {
        XamlRoot? observed = null;
        AppWindow? observedWindow = null;
        void ReadSize()
        {
            if (root.XamlRoot is not { } xamlRoot) return;
            var width = observedWindow is { } window
                ? window.ClientSize.Width / xamlRoot.RasterizationScale : xamlRoot.Size.Width;
            var height = observedWindow is { } sized
                ? sized.ClientSize.Height / xamlRoot.RasterizationScale : xamlRoot.Size.Height;
            if (width <= 0 || height <= 0) return;
            var changed = Math.Abs(windowWidth - width) >= 1 || Math.Abs(windowHeight - height) >= 1;
            if (changed) Dispatch(new ClientMessage.WindowSizeChanged(width, height));
        }
        void Changed(XamlRoot sender, XamlRootChangedEventArgs args) => ReadSize();
        void WindowChanged(AppWindow sender, AppWindowChangedEventArgs args)
        {
            if (args.DidSizeChange) ReadSize();
        }
        void Attach()
        {
            if (observed is not null) observed.Changed -= Changed;
            if (observedWindow is not null) observedWindow.Changed -= WindowChanged;
            observed = root.XamlRoot;
            if (observed is not null) observed.Changed += Changed;
            observedWindow = observed is null ? null : AppWindow.GetFromWindowId(observed.ContentIslandEnvironment.AppWindowId);
            if (observedWindow is not null) observedWindow.Changed += WindowChanged;
            ReadSize();
        }
        root.Loaded += (_, _) => Attach();
        root.Unloaded += (_, _) =>
        {
            if (observed is not null) observed.Changed -= Changed;
            if (observedWindow is not null) observedWindow.Changed -= WindowChanged;
            observed = null;
            observedWindow = null;
        };
        if (root.XamlRoot is not null) Attach();
    }
}
