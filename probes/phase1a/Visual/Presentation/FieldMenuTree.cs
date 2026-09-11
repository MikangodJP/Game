namespace Phase1A.Visual.Presentation;

public enum FieldMenuPanelKind { None, Info, Placeholder, Status }

public sealed record FieldMenuNode(
    string Id,
    string Label,
    IReadOnlyList<FieldMenuNode>? Children = null,
    FieldMenuPanelKind PanelKind = FieldMenuPanelKind.None,
    IReadOnlyList<string>? Lines = null,
    bool Enabled = true,
    bool IsBack = false);

public static class FieldMenuCatalog
{
    private static readonly FieldMenuNode Back = new("back", "BACK", IsBack: true);

    public static FieldMenuNode Root { get; } = new(
        "command",
        "COMMAND",
        Entries(
            Panel("items", "ITEMS", FieldMenuPanelKind.Placeholder, "NO INVENTORY AVAILABLE."),
            Branch("magic", "MAGIC",
                Panel("spells", "SPELLS", FieldMenuPanelKind.Placeholder, "SPELLS ARE NOT IMPLEMENTED YET."),
                Panel("adjustment", "ADJUSTMENT", FieldMenuPanelKind.Placeholder, "MAGIC ADJUSTMENT IS NOT IMPLEMENTED YET."),
                Panel("information", "INFORMATION", FieldMenuPanelKind.Placeholder, "MAGIC INFORMATION IS NOT IMPLEMENTED YET."),
                Back),
            Panel("equip", "EQUIP", FieldMenuPanelKind.Info, "EQUIPMENT OPENS FROM THE FIELD WITH E."),
            Panel("status", "STATUS", FieldMenuPanelKind.Status, "MAG/RES/AGI ARE WIP."),
            Panel("actions", "ACTIONS", FieldMenuPanelKind.Info, "NO CONTEXTUAL ACTIONS AVAILABLE."),
            Branch("system", "SYSTEM",
                Panel("settings", "SETTINGS", FieldMenuPanelKind.Placeholder, "SETTINGS ARE NOT IMPLEMENTED YET."),
                Panel("for-testing", "FOR TESTING", FieldMenuPanelKind.Placeholder, "FOR TESTING IS NOT IMPLEMENTED YET."),
                Back)));

    private static FieldMenuNode Branch(string id, string label, params FieldMenuNode[] children) =>
        new(id, label, Entries(children));

    private static FieldMenuNode Panel(string id, string label, FieldMenuPanelKind kind, params string[] lines) =>
        new(id, label, PanelKind: kind, Lines: Entries(lines));

    private static IReadOnlyList<T> Entries<T>(params T[] values) => Array.AsReadOnly(values);
}
