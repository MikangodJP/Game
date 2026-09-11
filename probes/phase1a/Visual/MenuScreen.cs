using Godot;
using Phase1A.Magic;
using Phase1A.Visual.Presentation;

namespace Phase1A.Visual;

// Drawing-only projection of FieldMenuController. Input and game state stay above this node.
public partial class MenuScreen : Node2D
{
    private static readonly Color MenuInk = new("000000");
    private static readonly Color MenuPaper = new("ffffff");
    private static readonly Color MenuDisabled = new("7f7f7f");
    private static readonly Rect2I RootRect = new(8, 8, 160, 54);
    private static readonly Rect2I AdjustmentRect = new(40, 28, 240, 180);
    private IReadOnlyList<Rect2I> lastWindowBounds = Array.Empty<Rect2I>();

    public GameController Game { get; set; } = null!;
    internal IReadOnlyList<Rect2I> LastWindowBounds => lastWindowBounds;

    public override void _Ready() => TextureFilter = TextureFilterEnum.Nearest;

    public override void _Draw()
    {
        if (Game.Mode != GameMode.Menu)
        {
            lastWindowBounds = Array.Empty<Rect2I>();
            return;
        }

        var view = Game.FieldMenu.BuildView(Game.State.Player);
        var bounds = new List<Rect2I> { RootRect };
        DrawCommandWindow(view.Windows[0], RootRect, root: true);

        for (var depth = 1; depth < view.Windows.Count; depth++)
        {
            var window = view.Windows[depth];
            var size = CommandSize(window);
            var rect = ChildRect(bounds[^1], view.Windows[depth - 1], size.X, size.Y, depth);
            bounds.Add(rect);
            DrawCommandWindow(window, rect, root: false);
        }

        if (view.ActivePanel is { } panel)
        {
            Rect2I rect;
            if (panel.Kind == FieldMenuPanelKind.Adjustment) rect = AdjustmentRect;
            else
            {
                var size = PanelSize(panel);
                var parent = view.Windows[^1];
                rect = ChildRect(bounds[^1], parent, size.X, size.Y, view.Windows.Count);
            }
            bounds.Add(rect);
            DrawPanel(panel, rect);
        }

        lastWindowBounds = Array.AsReadOnly(bounds.ToArray());
    }

    private void DrawCommandWindow(FieldMenuWindowView window, Rect2I rect, bool root)
    {
        Window(rect);
        Text(window.Title, rect.Position.X + 6, rect.Position.Y + 6, MenuPaper);
        Fill(rect.Position.X + 1, rect.Position.Y + 18, rect.Size.X - 2, 1, MenuPaper);
        var columns = window.Columns;
        var cellWidth = root ? 48 : rect.Size.X - 12;
        for (var i = 0; i < window.Entries.Count; i++)
        {
            var entry = window.Entries[i];
            var x = rect.Position.X + 6 + i % columns * cellWidth;
            var y = rect.Position.Y + 24 + i / columns * 12;
            if (i == window.SelectedIndex) Text(">", x, y, entry.Enabled ? MenuPaper : MenuDisabled);
            Text(entry.Label, x + (root ? 6 : 8), y, entry.Enabled ? MenuPaper : MenuDisabled);
        }
    }

    private void DrawPanel(FieldMenuPanelView panel, Rect2I rect)
    {
        Window(rect);
        Text(panel.Title, rect.Position.X + 6, rect.Position.Y + 6, MenuPaper);
        Fill(rect.Position.X + 1, rect.Position.Y + 18, rect.Size.X - 2, 1, MenuPaper);
        if (panel.Kind == FieldMenuPanelKind.Adjustment)
        {
            DrawAdjustment(panel.Adjustment ?? throw new InvalidOperationException("Adjustment panel needs a view."), rect);
            return;
        }
        if (panel.Kind == FieldMenuPanelKind.Status)
        {
            var y = rect.Position.Y + 25;
            foreach (var row in panel.Rows)
            {
                Text(row.Label, rect.Position.X + 6, y, MenuPaper);
                Text(row.Value, rect.End.X - 6 - TextWidth(row.Value), y, MenuPaper);
                y += 10;
            }
            foreach (var line in panel.Lines)
            {
                Text(line, rect.Position.X + 6, y + 1, MenuPaper);
                y += 9;
            }
            return;
        }

        var lines = Wrap(panel.Lines, Math.Max(1, (rect.Size.X - 12) / 6));
        for (var i = 0; i < lines.Count; i++)
            Text(lines[i], rect.Position.X + 6, rect.Position.Y + 26 + i * 10, MenuPaper);
    }

    private void DrawAdjustment(FieldMenuAdjustmentView adjustment, Rect2I rect)
    {
        var left = rect.Position.X + 14;
        var right = rect.End.X - 14;
        Text("BASE", left, rect.Position.Y + 27, MenuPaper);
        Text(adjustment.BaseMagic, right - TextWidth(adjustment.BaseMagic), rect.Position.Y + 27, MenuPaper);

        AdjustmentRow("SIZE", adjustment.SizeMultiplier, 0, rect.Position.Y + 48);
        DrawSlider(left, rect.Position.Y + 61, adjustment.SizeSteps);
        AdjustmentRow("OUTPUT", adjustment.OutputMultiplier, 1, rect.Position.Y + 81);
        DrawSlider(left, rect.Position.Y + 94, adjustment.OutputSteps);

        Text("MP COST", left, rect.Position.Y + 119, MenuPaper);
        var cost = adjustment.MpCost.ToString();
        Text(cost, right - TextWidth(cost), rect.Position.Y + 119, MenuPaper);
        if (adjustment.SelectedIndex == 2) Text(">", left, rect.Position.Y + 145, MenuPaper);
        Text("APPLY", left + 8, rect.Position.Y + 145, MenuPaper);
        return;

        void AdjustmentRow(string label, string value, int index, int y)
        {
            if (adjustment.SelectedIndex == index) Text(">", left, y, MenuPaper);
            Text(label, left + 8, y, MenuPaper);
            Text(value, right - TextWidth(value), y, MenuPaper);
        }
    }

    private void DrawSlider(int x, int y, int steps)
    {
        Fill(x, y, QuarterStepMultiplier.MaxSteps * 5 + 2, 9, MenuPaper);
        Fill(x + 1, y + 1, QuarterStepMultiplier.MaxSteps * 5, 7, MenuInk);
        for (var step = 0; step < QuarterStepMultiplier.MaxSteps; step++)
            if (step < steps) Fill(x + 1 + step * 5, y + 1, 4, 7, MenuPaper);
    }

    private static Vector2I CommandSize(FieldMenuWindowView window)
    {
        var longest = window.Entries.Select(entry => entry.Label.Length).Append(window.Title.Length).Max();
        var width = Even(Math.Clamp(longest * 6 + 26, 96, 296));
        var rows = (window.Entries.Count + window.Columns - 1) / window.Columns;
        return new(width, Even(30 + rows * 12));
    }

    private static Vector2I PanelSize(FieldMenuPanelView panel)
    {
        if (panel.Kind == FieldMenuPanelKind.Status)
        {
            var widestRow = panel.Rows.Select(row => row.Label.Length + row.Value.Length + 5).DefaultIfEmpty(0).Max();
            var statusLongest = panel.Lines.Select(line => line.Length).Append(panel.Title.Length).Append(widestRow).Max();
            return new(Even(Math.Clamp(statusLongest * 6 + 12, 112, 296)), Even(30 + panel.Rows.Count * 10 + panel.Lines.Count * 9));
        }

        var longest = panel.Lines.Select(line => line.Length).Append(panel.Title.Length).DefaultIfEmpty(0).Max();
        var width = Even(Math.Clamp(longest * 6 + 12, 112, 208));
        var wrapped = Wrap(panel.Lines, Math.Max(1, (width - 12) / 6));
        return new(width, Even(32 + wrapped.Count * 10));
    }

    // Every non-root command and panel window uses this single anchor/clamp/flip rule.
    private static Rect2I ChildRect(Rect2I parentRect, FieldMenuWindowView parent, int width, int height, int depth)
    {
        var columnWidth = parent.Columns == 3 ? 48 : Math.Max(1, parentRect.Size.X - 12);
        var columnOrigin = parentRect.Position.X + 6 + parent.SelectedIndex % parent.Columns * columnWidth;
        var cascade = depth >= 2 ? 8 : 0;
        var x = columnOrigin - 4 + cascade;
        var y = parentRect.End.Y + 4 + cascade;
        if (y + height > 232) y = parentRect.Position.Y - height - 4 - cascade;
        x = Math.Clamp(x, 8, 312 - width);
        y = Math.Clamp(y, 8, 232 - height);
        return new(x, y, width, height);
    }

    private static IReadOnlyList<string> Wrap(IReadOnlyList<string> paragraphs, int columns)
    {
        var lines = new List<string>();
        foreach (var paragraph in paragraphs)
        {
            var line = "";
            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.Length > 0 && line.Length + word.Length + 1 > columns)
                {
                    lines.Add(line);
                    line = "";
                }
                line += (line.Length == 0 ? "" : " ") + word;
            }
            if (line.Length > 0) lines.Add(line);
        }
        return Array.AsReadOnly(lines.ToArray());
    }

    private void Window(Rect2I rect)
    {
        Fill(rect.Position.X, rect.Position.Y, rect.Size.X, rect.Size.Y, MenuInk);
        Fill(rect.Position.X, rect.Position.Y, rect.Size.X, 1, MenuPaper);
        Fill(rect.Position.X, rect.End.Y - 1, rect.Size.X, 1, MenuPaper);
        Fill(rect.Position.X, rect.Position.Y, 1, rect.Size.Y, MenuPaper);
        Fill(rect.End.X - 1, rect.Position.Y, 1, rect.Size.Y, MenuPaper);
    }

    private void Fill(int x, int y, int width, int height, Color color)
    {
        if (width > 0 && height > 0) DrawRect(new Rect2(x, y, width, height), color);
    }

    private void Text(string text, int x, int y, Color color) => PixelArt.Text(this, text, x, y, color);
    private static int TextWidth(string text) => Math.Max(0, text.Length * 6 - 1);
    private static int Even(int value) => value + value % 2;
}
