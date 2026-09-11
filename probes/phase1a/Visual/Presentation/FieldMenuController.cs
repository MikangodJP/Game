using Phase1A.Magic;
using Phase1A.Preparation;

namespace Phase1A.Visual.Presentation;

public sealed record FieldMenuEntryView(string Label, bool Enabled);
public sealed record FieldMenuWindowView(
    string Title,
    IReadOnlyList<FieldMenuEntryView> Entries,
    int SelectedIndex,
    int Columns);
public sealed record FieldMenuStatusRow(string Label, string Value);
public sealed record FieldMenuAdjustmentView(
    string BaseMagic,
    int SizeSteps,
    string SizeMultiplier,
    int OutputSteps,
    string OutputMultiplier,
    int MpCost,
    int SelectedIndex);
public sealed record FieldMenuPanelView(
    FieldMenuPanelKind Kind,
    string Title,
    IReadOnlyList<string> Lines,
    IReadOnlyList<FieldMenuStatusRow> Rows,
    FieldMenuAdjustmentView? Adjustment);
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
    private ChantlessMagicConfiguration? adjustmentDraft;
    private int adjustmentSelected;

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
        adjustmentDraft = null;
        adjustmentSelected = 0;
    }

    public void Close() => Open();

    public void Move(int dx, int dy)
    {
        if (activePanel?.PanelKind == FieldMenuPanelKind.Adjustment)
        {
            adjustmentSelected = Math.Clamp(adjustmentSelected + dy, 0, 2);
            if (adjustmentDraft is null || dx == 0) return;
            if (adjustmentSelected == 0)
                adjustmentDraft = adjustmentDraft.WithSizeSteps(
                    QuarterStepMultiplier.Clamp(adjustmentDraft.SizeSteps + dx));
            else if (adjustmentSelected == 1)
                adjustmentDraft = adjustmentDraft.WithOutputSteps(
                    QuarterStepMultiplier.Clamp(adjustmentDraft.OutputSteps + dx));
            return;
        }
        if (activePanel is not null) return;
        var column = SelectedColumn + dx;
        var row = SelectedRow + dy;
        if (column < 0 || column >= Columns || row < 0) return;
        var candidate = row * Columns + column;
        if (candidate < CurrentEntries.Count) frames[^1].SelectedIndex = candidate;
    }

    public void Confirm(CharacterPreparation? player = null)
    {
        if (activePanel?.PanelKind == FieldMenuPanelKind.Adjustment)
        {
            if (adjustmentSelected != 2) return;
            if (player is null || adjustmentDraft is null || !player.TryConfigureChantlessMagic(adjustmentDraft))
                throw new InvalidOperationException("A valid out-of-battle player is required to apply Magic Adjustment.");
            activePanel = null;
            adjustmentDraft = null;
            return;
        }
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
        if (selected.PanelKind != FieldMenuPanelKind.None)
        {
            if (selected.PanelKind == FieldMenuPanelKind.Adjustment)
            {
                if (player is null)
                    throw new InvalidOperationException("A player is required to open Magic Adjustment.");
                adjustmentDraft = player.ChantlessMagic;
                adjustmentSelected = 0;
            }
            activePanel = selected;
        }
    }

    public bool Back()
    {
        if (activePanel is not null)
        {
            activePanel = null;
            adjustmentDraft = null;
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
        var adjustment = activePanel.PanelKind == FieldMenuPanelKind.Adjustment
            ? AdjustmentView()
            : null;
        return new(
            activePanel.PanelKind,
            activePanel.Label,
            ReadOnly(activePanel.Lines ?? Array.Empty<string>()),
            ReadOnly(rows),
            adjustment);
    }

    private FieldMenuAdjustmentView AdjustmentView()
    {
        var draft = adjustmentDraft ?? throw new InvalidOperationException("Adjustment needs a draft configuration.");
        return new(
            draft.BaseMagic.DisplayName.ToUpperInvariant(),
            draft.SizeSteps,
            QuarterStepMultiplier.Format(draft.SizeSteps),
            draft.OutputSteps,
            QuarterStepMultiplier.Format(draft.OutputSteps),
            ChantlessMagicCost.Calculate(draft),
            adjustmentSelected);
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
