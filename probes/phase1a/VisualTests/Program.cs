using Phase1A.Encounter;
using Phase1A.Rules;
using Phase1A.Visual.Presentation;

var tests = new (string Name, Action Run)[]
{
    ("All actor stats are immutable projections and inspection does not alter combat", () =>
    {
        var session = new BattleSession();
        var log = session.MachineText;
        var initial = session.View;
        Equal(new CharacterStats(80, 12, 12, 8, 6, 6, 10), initial.Hero.EffectiveStats);
        Equal(new CharacterStats(38, 2, 8, 5, 2, 3, 6), initial.Enemies[0].EffectiveStats);
        Equal(new CharacterStats(46, 0, 10, 4, 1, 3, 12), initial.Enemies[1].EffectiveStats);
        foreach (var actor in initial.Enemies.Prepend(initial.Hero))
        {
            Equal(actor.EffectiveStats.MaxHp, actor.MaxHp);
            Equal(actor.MaxHp, actor.Hp);
            Equal(actor.EffectiveStats.MaxMp, actor.MaxMp);
            Equal(actor.MaxMp, actor.Mp);
        }
        var editedCopy = initial.Hero with { Hp = 1, EffectiveStats = initial.Hero.EffectiveStats with { Strength = 999 } };
        Equal(999, editedCopy.EffectiveStats.Strength);
        Equal(initial.Hero, session.View.Hero);
        Equal(log, session.MachineText);
        Check(session.Submit(MenuAction.Attack, 2), "attack accepted after inspection");
        Check(session.View.Hero.Hp < initial.Hero.Hp, "live view updates after enemy response");
        Equal(80, initial.Hero.Hp);
        Equal(initial.Hero.EffectiveStats, session.View.Hero.EffectiveStats);
        var untouched = new BattleSession();
        Check(untouched.Submit(MenuAction.Attack, 2), "control attack accepted");
        Equal(untouched.MachineText, session.MachineText);
    }),
    ("Attack selects Wolf independently, uses Strike, then enemies act", () =>
    {
        var ui = Started();
        ui.Handle(UiInput.Confirm);
        Equal(ScreenMode.Targets, ui.Mode);
        ui.Handle(UiInput.Right);
        ui.Handle(UiInput.Confirm);
        Equal(ScreenMode.Messages, ui.Mode);
        Equal(38, ui.Session.View.Enemies[0].Hp);
        Check(ui.Session.View.Enemies[1].Hp < 46, "Wolf must take damage");
        Check(ui.Session.View.Hero.Hp < 80, "enemies must answer");
        Equal(12, ui.Session.View.Hero.Mp);
        Check(ui.Session.LastMessages.Any(m => m == "Adventurer attacks Wolf!"), "readable attack event");
        Check(ui.Session.Events.Any(e => e.Kind == "ActionStarted" && e.Detail == "probe:ability.strike" && e.Target == 2), "real Strike event");
    }),
    ("Defend reduces incoming damage for both enemy turns without MP cost", () =>
    {
        var ui = Started();
        Choose(ui, "DEFEND");
        Check(ui.Session.View.Hero.Guarding, "guard must still be active after enemy responses");
        Check(ui.Session.View.Hero.Hp is > 70 and < 80, "mitigated enemy damage");
        Equal(12, ui.Session.View.Hero.Mp);
        Equal(2, ui.Session.Events.Count(e => e.Kind == "GuardBlocked"));
        Check(ui.Session.LastMessages.Contains("Adventurer defends!"), "defend text");
    }),
    ("Run produces a distinct terminal result without enemy actions", () =>
    {
        var ui = Started();
        Choose(ui, "RUN");
        Equal(Outcome.Fled, ui.Session.Result!.Outcome);
        Equal(1, ui.Session.Events.Count(e => e.Kind == "ActionStarted"));
        Equal(80, ui.Session.View.Hero.Hp);
        DismissMessages(ui);
        Equal(ScreenMode.Ended, ui.Mode);
        ui.Handle(UiInput.Restart);
        Equal(ScreenMode.Preparation, ui.Mode);
        Equal(80, ui.Preparation.Hp);
        Equal(12, ui.Preparation.Mp);
        Check(!ui.Preparation.InBattle, "new preparation has no active battle");
    }),
    ("All six unfinished roots and every leaf remain outside simulation", () =>
    {
        var ui = Started();
        var original = ui.Session.MachineText;
        var rootLabels = ui.Menu.Root.Select(e => e.Label).ToArray();
        Check(rootLabels.SequenceEqual(new[] { "ATTACK", "DEFEND", "MAGIC", "SUMMONING", "SKILLS", "SPECIAL", "ITEMS", "TACTICS", "RUN" }), "root hierarchy");
        var paths = LeafPaths(ui.Menu.Root).ToArray();
        Equal(149, paths.Length);
        foreach (var path in paths)
        {
            while (!ui.Menu.IsRoot) ui.Handle(UiInput.Back);
            foreach (var label in path) Choose(ui, label);
            Equal(ScreenMode.Wip, ui.Mode);
            Check(!string.IsNullOrEmpty(ui.WipLabel), "WIP must name its leaf");
            var breadcrumb = ui.Menu.Breadcrumb;
            var selected = ui.Menu.SelectedIndex;
            ui.Handle(UiInput.Confirm);
            Equal(ScreenMode.Menu, ui.Mode);
            Equal(breadcrumb, ui.Menu.Breadcrumb);
            Equal(selected, ui.Menu.SelectedIndex);
            Equal(original, ui.Session.MachineText);
        }
    }),
    ("Deep WIP, Back, and target cancellation restore the correct selection", () =>
    {
        var ui = Started();
        Choose(ui, "SUMMONING"); Choose(ui, "CREATURE SUMMONING"); Choose(ui, "Dragon");
        Equal(ScreenMode.Wip, ui.Mode);
        Check(ui.Breadcrumb.Contains("CREATURE SUMMONING"), "deep breadcrumb");
        ui.Handle(UiInput.Back);
        Equal("Dragon", ui.Menu.CurrentEntries[ui.Menu.SelectedIndex].Label);
        ui.Handle(UiInput.Back);
        Equal("CREATURE SUMMONING", ui.Menu.CurrentEntries[ui.Menu.SelectedIndex].Label);
        Choose(ui, "Back");
        Equal("SUMMONING", ui.Menu.CurrentEntries[ui.Menu.SelectedIndex].Label);
        Choose(ui, "ATTACK");
        ui.Handle(UiInput.Back);
        Equal(ScreenMode.Menu, ui.Mode);
        Equal("ATTACK", ui.Menu.CurrentEntries[ui.Menu.SelectedIndex].Label);
        Equal(4, ui.Session.Events.Length);
    }),
    ("WIP browsing cannot consume RNG or affect the next attack", () =>
    {
        var browsed = Started();
        Choose(browsed, "MAGIC"); Choose(browsed, "ELEMENTAL MAGIC"); Choose(browsed, "Fire");
        browsed.Handle(UiInput.Confirm); browsed.Handle(UiInput.Back); browsed.Handle(UiInput.Back);
        Choose(browsed, "ATTACK"); browsed.Handle(UiInput.Confirm);
        var untouched = Started();
        Choose(untouched, "ATTACK"); untouched.Handle(UiInput.Confirm);
        Equal(untouched.Session.MachineText, browsed.Session.MachineText);
    }),
    ("Debug log navigation is read-only and returns to its prior menu", () =>
    {
        var ui = Started();
        Choose(ui, "MAGIC");
        var log = ui.Session.MachineText;
        ui.Handle(UiInput.Debug);
        Equal(ScreenMode.MachineLog, ui.Mode);
        ui.Handle(UiInput.Down); ui.Handle(UiInput.Right); ui.Handle(UiInput.Back);
        Equal(ScreenMode.Menu, ui.Mode);
        Equal("MAGIC", ui.Menu.Breadcrumb);
        Equal(log, ui.Session.MachineText);
    }),
    ("Rejected friendly target and placeholder choices never reach combat", () =>
    {
        var session = new BattleSession();
        var log = session.MachineText;
        Check(!session.Submit(MenuAction.Attack, 0), "friendly target rejected by adapter");
        Check(!session.Submit(MenuAction.None), "placeholder rejected by adapter");
        Equal(log, session.MachineText);
    }),
    ("All nine root cells have spatial neighbors and stop at grid edges", () =>
    {
        var neighbors = new[]
        {
            new[] { 0, 1, 0, 3 }, new[] { 0, 2, 1, 4 }, new[] { 1, 2, 2, 5 },
            new[] { 3, 4, 0, 6 }, new[] { 3, 5, 1, 7 }, new[] { 4, 5, 2, 8 },
            new[] { 6, 7, 3, 6 }, new[] { 6, 8, 4, 7 }, new[] { 7, 8, 5, 8 }
        };
        var directions = new[] { (-1, 0), (1, 0), (0, -1), (0, 1) };
        for (var index = 0; index < 9; index++)
            for (var direction = 0; direction < directions.Length; direction++)
            {
                var menu = new BattleMenu();
                Equal(3, menu.Columns);
                menu.Move(0, index / 3); menu.Move(index % 3, 0);
                Equal(index, menu.SelectedIndex);
                Equal(index / 3, menu.SelectedRow); Equal(index % 3, menu.SelectedColumn);
                menu.Move(directions[direction].Item1, directions[direction].Item2);
                Equal(neighbors[index][direction], menu.SelectedIndex);
            }
        var ui = Started();
        ui.Handle(UiInput.Down); Equal("SUMMONING", ui.Menu.CurrentEntries[ui.Menu.SelectedIndex].Label);
        ui.Handle(UiInput.Right); Equal("SKILLS", ui.Menu.CurrentEntries[ui.Menu.SelectedIndex].Label);
        ui.Handle(UiInput.Up); Equal("DEFEND", ui.Menu.CurrentEntries[ui.Menu.SelectedIndex].Label);
        ui.Handle(UiInput.Left); Equal("ATTACK", ui.Menu.CurrentEntries[ui.Menu.SelectedIndex].Label);
    }),
    ("Submenu grids stop at missing cells and preserve parent selection on Back", () =>
    {
        var ui = Started();
        Choose(ui, "MAGIC"); Choose(ui, "ELEMENTAL MAGIC");
        Equal(2, ui.Menu.Columns);
        ui.Handle(UiInput.Right);
        for (var i = 0; i < 3; i++) ui.Handle(UiInput.Down);
        Equal("Composite Elements", ui.Menu.CurrentEntries[ui.Menu.SelectedIndex].Label);
        ui.Handle(UiInput.Down);
        Equal(7, ui.Menu.SelectedIndex);
        ui.Handle(UiInput.Left); ui.Handle(UiInput.Down);
        Equal("Back", ui.Menu.CurrentEntries[ui.Menu.SelectedIndex].Label);
        ui.Handle(UiInput.Right); Equal(8, ui.Menu.SelectedIndex);
        ui.Handle(UiInput.Confirm);
        Equal("ELEMENTAL MAGIC", ui.Menu.CurrentEntries[ui.Menu.SelectedIndex].Label);
        ui.Handle(UiInput.Back);
        Equal("MAGIC", ui.Menu.CurrentEntries[ui.Menu.SelectedIndex].Label);
        Equal(3, ui.Menu.Columns);
    }),
    ("Preparation previews and equips Sword and Armor before snapshotting and locking battle", () =>
    {
        var ui = new HarnessController();
        Check(ui.IsPreparing && !ui.Preparation.InBattle, "starts outside battle");
        ui.Handle(UiInput.Debug);
        Equal(ScreenMode.Preparation, ui.Mode);
        SelectSlot(ui, EquipmentSlot.Weapon);
        HighlightEquipment(ui, PrototypeEquipment.WoodenSword);
        Equal(15, ui.PreviewStats.Strength);
        Equal(12, ui.Preparation.EffectiveStats.Strength);
        Check(ui.Preparation.Loadout.Get(EquipmentSlot.Weapon) is null, "preview cannot equip");
        ui.Handle(UiInput.Confirm);
        Equal(15, ui.Preparation.EffectiveStats.Strength);
        Equal(12, ui.Preparation.BaseStats.Strength);
        Equip(ui, EquipmentSlot.Body, PrototypeEquipment.LeatherArmor);
        Equal(12, ui.Preparation.EffectiveStats.Defense);
        StartBattle(ui);
        Equal(15, ui.Session.View.Hero.EffectiveStats.Strength);
        Equal(12, ui.Session.View.Hero.EffectiveStats.Defense);
        Check(ui.Preparation.InBattle, "preparation locked at encounter start");
        var snapshot = ui.Session.View.Hero;
        var log = ui.Session.MachineText;
        Check(!ui.Preparation.TryEquip(EquipmentSlot.Weapon, null), "active unequip rejected");
        Check(!ui.Preparation.TryEquip(EquipmentSlot.Head, PrototypeEquipment.ClothCap), "active equip rejected");
        ui.Handle(UiInput.Back); ui.Handle(UiInput.Restart);
        Equal(ScreenMode.Menu, ui.Mode);
        Equal(snapshot, ui.Session.View.Hero);
        Equal(log, ui.Session.MachineText);
        Choose(ui, "ITEMS"); Choose(ui, "Equipment Quick Use");
        Equal(ScreenMode.Wip, ui.Mode);
        Equal(log, ui.Session.MachineText);
        ui.Handle(UiInput.Back); ui.Handle(UiInput.Back);
        Choose(ui, "RUN"); DismissMessages(ui); ui.Handle(UiInput.Restart);
        Check(ui.IsPreparing && !ui.Preparation.InBattle, "ended restart returns to fresh preparation");
        Check(ui.Slots.All(slot => ui.Preparation.Loadout.Get(slot) is null), "new preparation has empty gear");
        Equal(new CharacterStats(80, 12, 12, 8, 6, 6, 10), ui.Preparation.EffectiveStats);
    }),
    ("Prepared Sword and Armor affect real Strike damage through effective snapshots", () =>
    {
        var bare = Started();
        var geared = new HarnessController();
        Equip(geared, EquipmentSlot.Weapon, PrototypeEquipment.WoodenSword);
        Equip(geared, EquipmentSlot.Body, PrototypeEquipment.LeatherArmor);
        StartBattle(geared);
        Check(bare.Session.Submit(MenuAction.Attack, 2), "bare attack accepted");
        Check(geared.Session.Submit(MenuAction.Attack, 2), "geared attack accepted");
        var bareHit = bare.Session.Events.Single(e => e.Kind == "Damaged" && e.Source == 0).Amount;
        var gearedHit = geared.Session.Events.Single(e => e.Kind == "Damaged" && e.Source == 0).Amount;
        Equal(3, gearedHit - bareHit);
        Check(geared.Session.View.Hero.Hp > bare.Session.View.Hero.Hp, "armor reduces enemy response damage");
        Equal(bare.Preparation.BaseStats, geared.Preparation.BaseStats);
    }),
    ("Four preparation slots offer compatible items None and Back without inventory mechanics", () =>
    {
        var ui = new HarnessController();
        Equal(4, ui.Slots.Count);
        foreach (var slot in ui.Slots)
        {
            SelectSlot(ui, slot);
            Check(ui.EquipmentChoices[0] is null, "None choice available");
            Check(ui.EquipmentChoices.Skip(1).All(item => item is not null && item.Slot == slot), "only compatible items offered");
            HighlightEquipment(ui, ui.EquipmentChoices[1]);
            ui.Handle(UiInput.Confirm);
        }
        Equal(85, ui.Preparation.EffectiveStats.MaxHp);
        Equal(80, ui.Preparation.Hp);
        Equal(13, ui.Preparation.EffectiveStats.Defense);
        foreach (var slot in ui.Slots) Equip(ui, slot, null);
        Equal(ui.Preparation.BaseStats, ui.Preparation.EffectiveStats);
        SelectSlot(ui, EquipmentSlot.Weapon);
        for (var i = 0; i < ui.EquipmentChoices.Count; i++) ui.Handle(UiInput.Down);
        Equal(ui.EquipmentChoices.Count, ui.EquipmentIndex);
        ui.Handle(UiInput.Confirm);
        Equal(ScreenMode.EquipmentSlots, ui.Mode);
        for (var i = 0; i < ui.Slots.Count; i++) ui.Handle(UiInput.Down);
        ui.Handle(UiInput.Confirm);
        Equal(ScreenMode.Preparation, ui.Mode);
    }),
    ("Horizontal target grid stops at edges and cancels Back without a command", () =>
    {
        var ui = Started();
        var log = ui.Session.MachineText;
        Choose(ui, "ATTACK");
        ui.Handle(UiInput.Left); ui.Handle(UiInput.Down); ui.Handle(UiInput.Up);
        Equal(0, ui.TargetIndex);
        ui.Handle(UiInput.Right); Equal(1, ui.TargetIndex);
        ui.Handle(UiInput.Right); Equal(2, ui.TargetIndex);
        ui.Handle(UiInput.Right); Equal(2, ui.TargetIndex);
        ui.Handle(UiInput.Confirm);
        Equal(ScreenMode.Menu, ui.Mode);
        Equal(log, ui.Session.MachineText);
    })
};
tests = tests.Concat(FieldLoopTests.All).Concat(FieldMenuTests.All).ToArray();
var failed = 0;
foreach (var (name, run) in tests)
{
    try { run(); Console.WriteLine($"PASS {name}"); }
    catch (Exception e) { failed++; Console.Error.WriteLine($"FAIL {name}: {e.Message}"); }
}
Console.WriteLine($"{tests.Length - failed}/{tests.Length} visual model tests passed");
return failed == 0 ? 0 : 1;

static void Choose(HarnessController ui, string label)
{
    var index = ui.Menu.CurrentEntries.ToList().FindIndex(e => e.Label == label);
    Check(index >= 0, $"missing menu {label}");
    // Travel via column zero so a missing final-row cell cannot trap the helper.
    while (ui.Menu.SelectedColumn > 0) ui.Handle(UiInput.Left);
    while (ui.Menu.SelectedRow > index / ui.Menu.Columns) ui.Handle(UiInput.Up);
    while (ui.Menu.SelectedRow < index / ui.Menu.Columns) ui.Handle(UiInput.Down);
    while (ui.Menu.SelectedColumn < index % ui.Menu.Columns) ui.Handle(UiInput.Right);
    Equal(index, ui.Menu.SelectedIndex);
    ui.Handle(UiInput.Confirm);
}
static HarnessController Started()
{
    var ui = new HarnessController();
    StartBattle(ui);
    return ui;
}
static void StartBattle(HarnessController ui)
{
    while (ui.Mode is ScreenMode.EquipmentItems or ScreenMode.EquipmentSlots) ui.Handle(UiInput.Back);
    Equal(ScreenMode.Preparation, ui.Mode);
    ui.Handle(UiInput.Down); ui.Handle(UiInput.Confirm);
    Equal(ScreenMode.Menu, ui.Mode);
}
static void SelectSlot(HarnessController ui, EquipmentSlot slot)
{
    if (ui.Mode == ScreenMode.Preparation) { ui.Handle(UiInput.Up); ui.Handle(UiInput.Confirm); }
    Equal(ScreenMode.EquipmentSlots, ui.Mode);
    var index = ui.Slots.ToList().IndexOf(slot);
    Check(index >= 0, "slot available");
    while (ui.SlotIndex > index) ui.Handle(UiInput.Up);
    while (ui.SlotIndex < index) ui.Handle(UiInput.Down);
    ui.Handle(UiInput.Confirm);
    Equal(ScreenMode.EquipmentItems, ui.Mode);
}
static void HighlightEquipment(HarnessController ui, EquipmentDefinition? item)
{
    var index = ui.EquipmentChoices.ToList().FindIndex(choice => choice?.Id == item?.Id);
    Check(index >= 0, "compatible equipment choice available");
    while (ui.EquipmentIndex > index) ui.Handle(UiInput.Up);
    while (ui.EquipmentIndex < index) ui.Handle(UiInput.Down);
}
static void Equip(HarnessController ui, EquipmentSlot slot, EquipmentDefinition? item)
{
    SelectSlot(ui, slot); HighlightEquipment(ui, item); ui.Handle(UiInput.Confirm);
    Equal(ScreenMode.EquipmentSlots, ui.Mode);
}
static void DismissMessages(HarnessController ui)
{
    for (var i = 0; i < 100 && ui.Mode == ScreenMode.Messages; i++) ui.Handle(UiInput.Confirm);
}
static IEnumerable<string[]> LeafPaths(IReadOnlyList<MenuEntry> entries, string[]? prefix = null)
{
    foreach (var entry in entries)
    {
        var path = (prefix ?? []).Append(entry.Label).ToArray();
        if (entry.Children is { Length: > 0 })
            foreach (var child in LeafPaths(entry.Children, path)) yield return child;
        else if (entry.Action == MenuAction.None) yield return path;
    }
}
static void Check(bool condition, string text) { if (!condition) throw new Exception(text); }
static void Equal<T>(T want, T got) => Check(EqualityComparer<T>.Default.Equals(want, got), $"expected {want}, got {got}");
