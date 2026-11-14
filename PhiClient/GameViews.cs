using Microsoft.UI.Reactor;
using Microsoft.UI.Reactor.Core;
using Microsoft.UI.Xaml;
using PhiClient.Core;
using Microsoft.UI.Text;
using static Microsoft.UI.Reactor.Factories;

namespace PhiClient;

internal static class GameViews
{
    private static readonly ThemeRef Stroke = new("ControlStrokeColorDefaultBrush");
    private static readonly ThemeRef Accent = new("AccentFillColorDefaultBrush");
    private static readonly ThemeRef AccentText = new("TextOnAccentFillColorPrimaryBrush");

    public static Element Map(GameSession session, Action<int, int> move, Action attack)
    {
        var eagle = session.ShowEagle ? session.Eagle : null;
        var width = eagle?.Width ?? session.Map?.Size ?? 7;
        var height = eagle?.Height ?? width;
        var tiles = eagle?.Tiles ?? session.Map?.Tiles;
        var attributes = eagle?.Attributes ?? session.Map?.Attributes;
        var selfX = eagle?.SelfX ?? width / 2;
        var selfY = eagle?.SelfY ?? height / 2;
        var cell = Math.Min(48, 336.0 / Math.Max(width, height));
        var rows = new List<Element>();
        for (var y = 0; y < height; y++)
        {
            var cells = new List<Element>();
            for (var x = 0; x < width; x++)
            {
                var index = y * width + x;
                var tile = tiles is null ? '?' : (char)tiles[index];
                var attr = attributes?[index] ?? 0;
                var actors = eagle is null ? session.Map?.Actors.Where(a => a.X == x && a.Y == y).ToArray() : null;
                var self = x == selfX && y == selfY;
                var actor = actors?.Length > 0 || eagle is not null && attr == 0x58;
                var caption = tile is ' ' or 's' ? "" : tile.ToString();
                var glyph = self ? !session.NorthFixed && eagle is null ? "^" : session.Map?.Direction switch
                    { 'E' => ">", 'S' => "v", 'W' => "<", _ => "^" } : "o";
                var description = $"Tile {x},{y}: {(tile == ' ' ? "floor" : tile)}" +
                    (self ? ", you" : actor ? ", character" : "") +
                    ((attr & 8) != 0 ? ", sign" : "") + ((attr & 0x70) != 0 ? ", item" : "") +
                    (actors is null ? "" : ", " + string.Join(", ", actors.Select(a => a.Name)));
                var surface = Grid(columns: [], rows: [], children: [
                    tile == '#' ? Border(Empty()).Margin(3).CornerRadius(3)
                        .Background(new ThemeRef("ControlStrongFillColorDefaultBrush")).BorderThickness(1).BorderBrush(Stroke) : Empty(),
                    TextBlock(caption).FontFamily("Consolas").FontSize(cell * 0.4)
                        .HorizontalAlignment(HorizontalAlignment.Center).VerticalAlignment(VerticalAlignment.Center)
                        .AutomationName(description),
                    (attr & 8) == 0 ? Empty() : TextBlock("!").FontSize(cell * 0.32)
                        .HorizontalAlignment(HorizontalAlignment.Right).VerticalAlignment(VerticalAlignment.Top).Margin(2),
                    (attr & 0x70) == 0 || eagle is not null && attr == 0x58 ? Empty() : TextBlock("*").FontSize(cell * 0.32)
                        .HorizontalAlignment(HorizontalAlignment.Right).VerticalAlignment(VerticalAlignment.Bottom).Margin(2),
                    !self && !actor ? Empty() : Border(TextBlock(glyph).FontFamily("Consolas").FontWeight(FontWeights.Bold)
                        .FontSize(cell * 0.4).Foreground(AccentText)
                        .HorizontalAlignment(HorizontalAlignment.Center).VerticalAlignment(VerticalAlignment.Center))
                        .Background(Accent).CornerRadius(cell).Width(cell * 0.62).Height(cell * 0.62)
                        .HorizontalAlignment(HorizontalAlignment.Center).VerticalAlignment(VerticalAlignment.Center)
                ]);
                var tileX = x;
                var tileY = y;
                cells.Add(Border(surface).Width(cell).Height(cell).BorderBrush(Stroke).BorderThickness(0.5)
                    .Background(new ThemeRef(tile == '?' ? "ControlFillColorDisabledBrush" :
                        (x + y) % 2 == 0 ? "LayerFillColorDefaultBrush" : "CardBackgroundFillColorDefaultBrush"))
                    .ToolTip(description).OnPointerPressed((_, args) =>
                    {
                        if (eagle is not null) return;
                        var pointer = args.GetCurrentPoint(null).Properties;
                        if (pointer.IsRightButtonPressed) attack();
                        else if (pointer.IsLeftButtonPressed) move(tileX - width / 2, tileY - height / 2);
                        else return;
                        args.Handled = true;
                    }));
            }
            rows.Add(HStack(0, cells.ToArray()));
        }
        return Border(VStack(0, rows.ToArray())).BorderBrush(Stroke).BorderThickness(1).AutomationId("map-surface");
    }

    public static Element Status(GameSession session)
    {
        var status = session.Stats;
        Element Bar(string label, long? now, long? max) => VStack(2,
            TextBlock($"{label}  {now?.ToString() ?? "--"} / {max?.ToString() ?? "--"}").AutomationId($"{label.ToLowerInvariant()}-value"),
            Progress(now is null || max is null or <= 0 ? 0 : Math.Clamp((double)now / max.Value * 100, 0, 100))
                .AutomationName($"{label}: {now?.ToString() ?? "unknown"} of {max?.ToString() ?? "unknown"}"));
        return VStack(6,
            TextBlock(status?.Name ?? session.Character.Name).FontWeight(FontWeights.SemiBold),
            Bar("HP", status?.Hp, status?.MaxHp),
            Bar("MP", status?.Mp, status?.MaxMp),
            TextBlock($"F {status?.Fighter}   W {status?.Warrior}   M {status?.Magician}   C {status?.Cleric}\n" +
                $"Gp {status?.Gold}   Exp {status?.Experience}").AutomationId("game-status"),
            TextBlock(string.Join(" | ", session.Conditions.Active
                .Concat(session.AttackMode ? new[] { "Attack" } : [])
                .Concat(session.MagicMode ? new[] { "Magic" } : [])))
                .TextWrapping(TextWrapping.Wrap).AutomationId("conditions")
        );
    }
}
