using Microsoft.UI;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.System;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using PhiClient.Core;
using Windows.UI.ViewManagement;
using static Microsoft.UI.Reactor.Factories;

namespace PhiClient;

internal static class StatusBarView
{
    public static Element Bar(string label, long? current, long? maximum, double width)
    {
        var value = new StatusBarValue(current, maximum);
        return Border(Empty()).Width(Math.Max(0, width)).Height(28).IsHitTestVisible(false)
            .WithKey($"{label}-{current}-{maximum}-{width}")
            .OnMount(element =>
            {
                var host = (Microsoft.UI.Xaml.Controls.Border)element;
                // The built-in template sizes its fill from MinHeight, not Height.
                var progress = new ProgressBar
                {
                    Minimum = 0, Maximum = 100, Value = value.Percentage,
                    IsIndeterminate = false, IsTabStop = false, IsHitTestVisible = false,
                    Height = 28, MinHeight = 28, CornerRadius = new CornerRadius(4)
                };
                progress.Resources["ProgressBarTrackHeight"] = 28d;
                progress.Resources["ProgressBarTrackCornerRadius"] = new CornerRadius(4);
                var fill = label == "HP" ? Windows.UI.Color.FromArgb(255, 185, 28, 28)
                    : Windows.UI.Color.FromArgb(255, 29, 78, 216);
                var track = label == "HP" ? Windows.UI.Color.FromArgb(255, 62, 18, 18)
                    : Windows.UI.Color.FromArgb(255, 16, 33, 65);
                foreach (var theme in new[] { "Light", "Dark" })
                    progress.Resources.ThemeDictionaries[theme] = new ResourceDictionary
                    {
                        ["ProgressBarForeground"] = new SolidColorBrush(fill),
                        ["ProgressBarBackground"] = new SolidColorBrush(track)
                    };
                progress.Resources.ThemeDictionaries["HighContrast"] = new ResourceDictionary();
                AutomationProperties.SetAutomationId(progress, label.ToLowerInvariant() + "-bar");
                AutomationProperties.SetName(progress, $"{label} {value.Text}");
                var text = new Microsoft.UI.Xaml.Controls.TextBlock
                {
                    Text = $"{label}   {value.Text}", FontSize = 13, FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                };
                AutomationProperties.SetAutomationId(text, label.ToLowerInvariant() + "-value");
                AutomationProperties.SetAccessibilityView(text, AccessibilityView.Raw);
                var backing = new Microsoft.UI.Xaml.Controls.Border
                {
                    Child = new Viewbox { Child = text, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly },
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(4, 1, 4, 1),
                    MaxWidth = Math.Max(0, width - 8), MaxHeight = 28
                };
                var surface = new Microsoft.UI.Xaml.Controls.Grid();
                surface.Children.Add(progress);
                surface.Children.Add(backing);
                ThemeSettings? themeSettings = null;
                var colors = new UISettings();
                void ApplyColors(bool highContrast)
                {
                    text.Foreground = new SolidColorBrush(highContrast
                        ? colors.UIElementColor(UIElementType.WindowText) : Colors.White);
                    backing.Background = highContrast
                        ? new SolidColorBrush(colors.UIElementColor(UIElementType.Window)) : null;
                }
                void ColorsChanged(ThemeSettings sender, object args) =>
                    host.DispatcherQueue.TryEnqueue(() =>
                    {
                        if (host.IsLoaded && ReferenceEquals(themeSettings, sender))
                            ApplyColors(sender.HighContrast);
                    });
                surface.Loaded += (_, _) =>
                {
                    themeSettings = ThemeSettings.CreateForWindowId(surface.XamlRoot.ContentIslandEnvironment.AppWindowId);
                    themeSettings.Changed += ColorsChanged;
                    ApplyColors(themeSettings.HighContrast);
                };
                surface.Unloaded += (_, _) =>
                {
                    if (themeSettings is not null)
                    {
                        themeSettings.Changed -= ColorsChanged;
                        themeSettings = null;
                    }
                };
                host.Child = surface;
            });
    }
}
