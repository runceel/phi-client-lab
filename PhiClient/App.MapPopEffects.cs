using Microsoft.UI;
using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using PhiClient;
using PhiClient.Core;
using static Microsoft.UI.Reactor.Factories;

partial class App
{
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? mapEffectTimer;

    private void StartMapEffectAnimation(Microsoft.UI.Dispatching.DispatcherQueue queue)
    {
        mapEffectTimer = queue.CreateTimer();
        mapEffectTimer.Interval = TimeSpan.FromMilliseconds(100);
        var previouslyVisible = false;
        mapEffectTimer.Tick += (_, _) =>
        {
            var visible = showMap && model.Options.MapEffects &&
                active?.Model.MapEffects.Items.Any(item => item.Alive(Environment.TickCount64)) == true;
            if (visible || previouslyVisible) refresh();
            previouslyVisible = visible;
        };
        mapEffectTimer.Start();
    }

    private Element MapEffectLayer(GameSession session)
    {
        var now = Environment.TickCount64;
        return Canvas().Width(session.GraphicsWidth).Height(session.GraphicsHeight)
            .IsHitTestVisible(false).AccessibilityHidden().WithKey($"map-effects-{now / 100}")
            .AutomationId("map-effects")
            .OnMount(element =>
            {
                var canvas = (Microsoft.UI.Xaml.Controls.Canvas)element;
                canvas.Children.Clear();
                if (!model.Options.MapEffects || session.ShowEagle) return;
                var actors = session.Display?.Frame.Actors ?? session.Map?.Actors ?? [];
                var center = session.GraphicsDim / 2;
                var zoom = session.GraphicsZoom;
                var stack = new Dictionary<string, int>();
                foreach (var effect in session.Model.MapEffects.Items.Where(item => item.Alive(now)))
                {
                    var actor = MapEffectState.FindActor(actors, effect.Name, center);
                    var tile = effect.Anchor is { } anchor ? (anchor.X + center, anchor.Y + center) :
                        actor is not null ? (actor.X, actor.Y) : ((int, int)?)null;
                    if (tile is not { } position) continue;
                    var x = (position.Item1 * 32 + 16) * zoom;
                    var y = (position.Item2 * 32 + 32) * zoom;
                    var progress = Math.Clamp((now - effect.Started) / (double)effect.Duration, 0, 1);
                    var moving = visualPreferences.AnimationsEnabled;
                    if (effect.Kind == MapEffectKind.Dead)
                    {
                        Ring(x, y, 42 * zoom, Colors.OrangeRed, moving ? 1 - progress : 1);
                        continue;
                    }
                    var n = effect.Kind == MapEffectKind.Talk ? 0 : stack.GetValueOrDefault(effect.Name);
                    if (effect.Kind != MapEffectKind.Talk) stack[effect.Name] = n + 1;
                    if (n == 0 && effect.Kind is MapEffectKind.Buff or MapEffectKind.Debuff)
                    {
                        var aura = new Rectangle { Width = 24 * zoom, Height = 40 * zoom,
                            Fill = new SolidColorBrush(effect.Kind == MapEffectKind.Buff ? Colors.LightGoldenrodYellow : Colors.MediumPurple),
                            Opacity = moving ? (1 - progress) * .5 : .35 };
                        Add(aura, x - aura.Width / 2, y - aura.Height / 2);
                    }
                    if (n == 0 && effect.By is { } by &&
                        MapEffectState.FindActor(actors, by, center) is { } source &&
                        (source.X != position.Item1 || source.Y != position.Item2))
                    {
                        if (effect.Magic)
                        {
                            var t = moving ? Math.Min(1, progress * 3) : 1;
                            var sx = (source.X * 32 + 16) * zoom;
                            var sy = (source.Y * 32 + 32) * zoom;
                            Ring(sx + (x - sx) * t, sy + (y - sy) * t, 10 * zoom, Colors.Cyan, 1);
                            if (t >= 1) Ring(x, y, 26 * zoom, Colors.DeepSkyBlue, 1 - progress);
                        }
                        else
                        {
                            var slash = new Line { X1 = x - 12 * zoom, Y1 = y + 12 * zoom,
                                X2 = x + 12 * zoom, Y2 = y - 12 * zoom, StrokeThickness = 3,
                                Stroke = new SolidColorBrush(Colors.White), Opacity = moving ? 1 - progress : 1 };
                            canvas.Children.Add(slash);
                        }
                    }
                    var talk = effect.Kind == MapEffectKind.Talk;
                    var color = effect.Kind switch
                    {
                        MapEffectKind.HpLost => Colors.DarkRed,
                        MapEffectKind.HpGained => Colors.DarkGreen,
                        MapEffectKind.MpLost or MapEffectKind.MpGained => Colors.DarkBlue,
                        MapEffectKind.Poison or MapEffectKind.Palsy or MapEffectKind.Debuff => Colors.Purple,
                        _ => Colors.Black
                    };
                    var label = new Microsoft.UI.Xaml.Controls.TextBlock { Text = effect.Text,
                        FontSize = Math.Clamp(12 * zoom, 10, 20), TextWrapping = TextWrapping.Wrap,
                        MaxLines = talk ? 3 : 1, TextTrimming = TextTrimming.CharacterEllipsis,
                        MaxWidth = Math.Min(180, session.GraphicsWidth), Foreground = new SolidColorBrush(color),
                        FontWeight = Microsoft.UI.Text.FontWeights.Bold };
                    var bubble = new Border { Child = label, Padding = new Thickness(3),
                        Background = new SolidColorBrush(talk ? Colors.White : Colors.LightYellow),
                        CornerRadius = new CornerRadius(talk ? 4 : 2),
                        Opacity = talk || !moving ? 1 : Math.Max(.2, 1 - progress) };
                    bubble.Measure(new Windows.Foundation.Size(Math.Min(186, session.GraphicsWidth), double.PositiveInfinity));
                    var left = Math.Clamp(x - bubble.DesiredSize.Width / 2, 0,
                        Math.Max(0, session.GraphicsWidth - bubble.DesiredSize.Width));
                    var top = y - (talk ? 16 * zoom + bubble.DesiredSize.Height : 8 * zoom + n * 16);
                    if (!talk && moving) top -= progress * 24;
                    Add(bubble, left, Math.Max(0, top));
                }

                void Add(FrameworkElement child, double x, double y)
                {
                    Microsoft.UI.Xaml.Controls.Canvas.SetLeft(child, x);
                    Microsoft.UI.Xaml.Controls.Canvas.SetTop(child, y);
                    canvas.Children.Add(child);
                }
                void Ring(double x, double y, double size, Windows.UI.Color color, double opacity)
                {
                    var ring = new Ellipse { Width = size, Height = size, Stroke = new SolidColorBrush(color),
                        StrokeThickness = 3, Opacity = opacity };
                    Add(ring, x - size / 2, y - size / 2);
                }
            });
    }
}
