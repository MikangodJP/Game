using Phase1A.Preparation;

namespace Phase1A.Visual.Presentation;

public sealed record FieldMenuEntryView(string Label, bool Enabled);
public sealed record FieldMenuWindowView(
    string Title,
    IReadOnlyList<FieldMenuEntryView> Entries,
    int SelectedIndex,
    int Columns);
public sealed record FieldMenuStatusRow(string Label, string Value);
public sealed record FieldMenuPanelView(
    FieldMenuPanelKind Kind,
    string Title,
    IReadOnlyList<string> Lines,
    IReadOnlyList<FieldMenuStatusRow> Rows);
public sealed record FieldMenuView(
    IReadOnlyList<FieldMenuWindowView> Windows,
    FieldMenuPanelView? ActivePanel,
    string Breadcrumb);

public sealed class FieldMenuController
{
    private sealed class Frame(FieldMenuNode node)
    {
        public FieldMenuNode Node { get; } = node;
        public int SelectedIndex { get; set; }
    }

    private readonly List<Frame> frames = [];
    private FieldMenuNode? activePanel;

    public IReadOnlyList<FieldMenuNode> CurrentEntries =>
        frames[^1].Node.Children ?? Array.Empty<FieldMenuNode>();
    public int SelectedIndex => frames[^1].SelectedIndex;
    public int Columns => frames.Count == 1 ? 3 : 1;
    public int SelectedRow => SelectedIndex / Columns;
    public int SelectedColumn => SelectedIndex % Columns;
    public int Depth => frames.Count;

    public FieldMenuController() => Open();

    public void Open()
    {
        frames.Clear();
        frames.Add(new(FieldMenuCatalog.Root));
        activePanel = null;
    }

    public void Close() => Open();

    public void Move(int dx, int dy)
    {
        if (activePanel is not null) return;
        var column = SelectedColumn + dx;
        var row = SelectedRow + dy;
        if (column < 0 || column >= Columns || row < 0) return;
        var candidate = row * Columns + column;
        if (candidate < CurrentEntries.Count) frames[^1].SelectedIndex = candidate;
    }

    public void Confirm()
    {
        if (activePanel is { PanelKind: FieldMenuPanelKind.Info or FieldMenuPanelKind.Placeholder })
        {
            activePanel = null;
            return;
        }
        if (activePanel is not null || CurrentEntries.Count == 0) return;
        var selected = CurrentEntries[SelectedIndex];
        if (!selected.Enabled) return;
        if (selected.IsBack)
        {
            Back();
            return;
        }
        if (selected.Children is { Count: > 0 })
        {
            frames.Add(new(selected));
            return;
        }
        if (selected.PanelKind != FieldMenuPanelKind.None) activePanel = selected;
    }

    public bool Back()
    {
        if (activePanel is not null)
        {
            activePanel = null;
            return false;
        }
        if (frames.Count > 1)
        {
            frames.RemoveAt(frames.Count - 1);
            return false;
        }
        return true;
    }

    public FieldMenuView BuildView(CharacterPreparation player)
    {
        ArgumentNullException.ThrowIfNull(player);
        var windows = frames.Select(frame => new FieldMenuWindowView(
            frame.Node.Label,
            ReadOnly(frame.Node.Children!.Select(entry => new FieldMenuEntryView(entry.Label, entry.Enabled))),
            frame.SelectedIndex,
            ReferenceEquals(frame.Node, FieldMenuCatalog.Root) ? 3 : 1)).ToArray();
        var breadcrumb = frames.Count == 1
            ? FieldMenuCatalog.Root.Label
            : string.Join(" > ", frames.Skip(1).Select(frame => frame.Node.Label));
        return new(ReadOnly(windows), BuildPanel(player), breadcrumb);
    }

    private FieldMenuPanelView? BuildPanel(CharacterPreparation player)
    {
        if (activePanel is null) return null;
        var rows = activePanel.PanelKind == FieldMenuPanelKind.Status
            ? StatusRows(player)
            : Array.Empty<FieldMenuStatusRow>();
        return new(
            activePanel.PanelKind,
            activePanel.Label,
            ReadOnly(activePanel.Lines ?? Array.Empty<string>()),
            ReadOnly(rows));
    }

    private static IReadOnlyList<FieldMenuStatusRow> StatusRows(CharacterPreparation player)
    {
        var stats = player.EffectiveStats;
        return ReadOnly<FieldMenuStatusRow>(
        [
            new("NAME", "ADVENTURER"),
            new("HP", $"{player.Hp}/{stats.MaxHp}"),
            new("MP", $"{player.Mp}/{stats.MaxMp}"),
            new("STR", stats.Strength.ToString()),
            new("DEF", stats.Defense.ToString()),
            new("MAG", stats.Magic.ToString()),
            new("RES", stats.Resistance.ToString()),
            new("AGI", stats.Agility.ToString())
        ]);
    }

    private static IReadOnlyList<T> ReadOnly<T>(IEnumerable<T> values) =>
        Array.AsReadOnly(values.ToArray());
}
