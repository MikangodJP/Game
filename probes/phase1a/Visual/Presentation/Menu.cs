namespace Phase1A.Visual.Presentation;

public enum MenuAction { None, Attack, Defend, Run }
public enum ChoiceKind { None, Attack, Defend, Run, Wip }
public sealed record MenuEntry(
    string Label, MenuEntry[]? Children = null,
    MenuAction Action = MenuAction.None, string? WipLabel = null);
public readonly record struct MenuChoice(ChoiceKind Kind, string Label = "");

// A disposable, presentation-only tree. Only the three functional choices can
// become combat commands; selecting a WIP leaf leaves this menu exactly as it was.
public sealed class BattleMenu
{
    private static readonly MenuEntry BackEntry = new("Back");
    private readonly List<Frame> frames = [];

    public IReadOnlyList<MenuEntry> Root { get; } = Array.AsReadOnly(CreateRoot());
    public IReadOnlyList<MenuEntry> CurrentEntries => frames[^1].Entries;
    public int SelectedIndex => frames[^1].Selected;
    public bool IsRoot => frames.Count == 1;
    public int Columns => IsRoot ? 3 : 2;
    public int SelectedRow => SelectedIndex / Columns;
    public int SelectedColumn => SelectedIndex % Columns;
    public string Breadcrumb => IsRoot ? "COMMAND" : string.Join(" > ", frames.Skip(1).Select(frame => frame.Label));

    public BattleMenu() => Reset();

    public void Reset()
    {
        frames.Clear();
        frames.Add(new("COMMAND", Root));
    }

    public void Move(int dx, int dy)
    {
        var row = SelectedRow + dy;
        var column = SelectedColumn + dx;
        if (row < 0 || column < 0 || column >= Columns) return;
        var index = row * Columns + column;
        if (index >= CurrentEntries.Count) return;
        frames[^1].Selected = index;
    }

    public MenuChoice Confirm()
    {
        var entry = CurrentEntries[SelectedIndex];
        if (ReferenceEquals(entry, BackEntry))
        {
            Back();
            return new(ChoiceKind.None);
        }
        if (entry.Children is { Length: > 0 } children)
        {
            frames.Add(new(entry.Label, Array.AsReadOnly(children.Append(BackEntry).ToArray())));
            return new(ChoiceKind.None);
        }
        var kind = entry.Action switch
        {
            MenuAction.Attack => ChoiceKind.Attack,
            MenuAction.Defend => ChoiceKind.Defend,
            MenuAction.Run => ChoiceKind.Run,
            _ => ChoiceKind.Wip
        };
        return new(kind, entry.WipLabel ?? entry.Label);
    }

    public bool Back()
    {
        if (IsRoot) return false;
        frames.RemoveAt(frames.Count - 1);
        return true;
    }

    private sealed class Frame(string label, IReadOnlyList<MenuEntry> entries)
    {
        public string Label { get; } = label;
        public IReadOnlyList<MenuEntry> Entries { get; } = entries;
        public int Selected { get; set; }
    }

    private static MenuEntry Category(string label, params string[] leaves) =>
        new(label, leaves.Select(leaf => new MenuEntry(leaf)).ToArray());

    private static MenuEntry[] CreateRoot() =>
    [
        new("ATTACK", Action: MenuAction.Attack),
        new("DEFEND", Action: MenuAction.Defend),
        new("MAGIC", [
            Category("ELEMENTAL MAGIC", "Fire", "Water", "Ice", "Wind", "Earth", "Lightning", "Nature", "Composite Elements"),
            Category("RESTORATION MAGIC", "Healing", "Regeneration", "Purification", "Revival", "Restoration"),
            Category("ENHANCEMENT MAGIC", "Physical Enhancement", "Magical Enhancement", "Speed", "Defense", "Resistance", "Weapon Enhancement"),
            Category("WEAKENING MAGIC", "Stat Reduction", "Vulnerability", "Silence", "Binding", "Sleep", "Curse"),
            Category("SPATIAL MAGIC", "Teleportation", "Displacement", "Barrier", "Pocket Space", "Spatial Distortion"),
            Category("TEMPORAL MAGIC", "Acceleration", "Deceleration", "Delay", "Time Manipulation"),
            Category("CONJURATION MAGIC", "Object Conjuration", "Weapon Conjuration", "Armor Conjuration", "Material Conjuration", "Construct Creation", "Temporary Creation"),
            Category("TRANSFORMATION MAGIC", "Self Transformation", "Beast Transformation", "Material Transformation", "Size Manipulation", "Polymorph"),
            Category("MIND / SOUL MAGIC", "Mental Influence", "Illusion", "Memory", "Soul Manipulation", "Spirit Interaction"),
            Category("PRIMORDIAL / ROOT MAGIC", "Light", "Darkness", "Creation", "Destruction", "Order", "Chaos", "Life", "Death", "Space", "Time", "Unknown / Forbidden")
        ]),
        new("SUMMONING", [
            Category("CREATURE SUMMONING", "Beast", "Monster", "Magical Beast", "Dragon", "Demon", "Other Creature"),
            Category("SPIRIT SUMMONING", "Elemental Spirit", "Nature Spirit", "Ancestral Spirit", "Greater Spirit"),
            Category("OBJECT SUMMONING", "Weapon", "Armor", "Tool", "Material", "Special Object"),
            Category("CONSTRUCT SUMMONING", "Golem", "Automaton", "Magical Construct", "Artificial Life"),
            Category("UNDEAD SUMMONING", "Skeleton", "Corpse", "Ghost", "Greater Undead"),
            Category("DIVINE SUMMONING", "Divine Servant", "Avatar", "Divine Entity"),
            Category("CONTRACT SUMMONING", "Contracted Creature", "Contracted Spirit", "Contracted Demon", "Unique Contract"),
            Category("SUMMON MANAGEMENT", "Active Summons", "Summon Capacity", "Contracts", "Formation", "Dismiss")
        ]),
        new("SKILLS", [
            Category("WEAPON SKILLS", "Sword", "Greatsword", "Spear", "Axe", "Bow", "Dagger", "Staff", "Unarmed", "Other"),
            Category("MARTIAL SKILLS", "Strikes", "Grappling", "Movement", "Counter", "Defensive Technique"),
            new("CLASS SKILLS"),
            new("RACIAL / SPECIES SKILLS"),
            new("MONSTER SKILLS"),
            new("PASSIVE SKILLS"),
            new("UNIQUE SKILLS")
        ]),
        new("SPECIAL", [
            Category("INTERACT", "Talk", "Threaten", "Persuade", "Examine", "Use Environment"),
            Category("MOVEMENT", "Reposition", "Advance", "Retreat", "Evade", "Change Formation"),
            new("CREATURE ACTION"),
            new("TRANSFORMATION"),
            new("LIMIT / AWAKENING"),
            new("CONTRACT ACTION"),
            Category("COMMAND", "Command Ally", "Command Summon", "Command Group"),
            new("UNIQUE ACTION")
        ]),
        Category("ITEMS", "Consumables", "Medicine", "Food", "Bombs / Throwables", "Scrolls", "Magical Items", "Tools", "Quest / Special", "Equipment Quick Use"),
        Category("TACTICS", "Party Formation", "Target Priority", "Ally Behavior", "Summon Behavior", "Auto Battle", "Reserve / Swap", "Battle Information"),
        new("RUN", Action: MenuAction.Run)
    ];
}
