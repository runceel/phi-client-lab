using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media.Imaging;

partial class App
{
    private void MountMapImage(Microsoft.UI.Xaml.Controls.Image image, WriteableBitmap bitmap)
    {
        image.Source = bitmap;
        XamlRoot? root = null;
        void Changed(XamlRoot _, XamlRootChangedEventArgs __) => refresh();
        image.Loaded += (_, _) =>
        {
            root = image.XamlRoot;
            if (root is not null) root.Changed += Changed;
            refresh();
        };
        image.Unloaded += (_, _) =>
        {
            if (root is not null) root.Changed -= Changed;
        };
    }
}
