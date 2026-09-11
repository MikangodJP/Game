using Phase1A;
using Phase1A.Encounter;
using Phase1A.Preparation;
using Phase1A.Rules;
using Phase1A.Visual.Presentation;

internal static class FieldMenuTests
{
    public static readonly (string Name, Action Run)[] All =
    [
        ("Field menu root is the exact clamped three-column command grid", () =>
        {
            var menu = new FieldMenuController();
            Equal(0, menu.SelectedIndex);
            Equal(3, menu.Columns);
            SequenceEqual(
                ["ITEMS", "MAGIC", "EQUIP", "STATUS", "ACTIONS", "SYSTEM"],
                menu.CurrentEntries.Select(entry => entry.Label));

            menu.Move(-1, 0); Equal(0, menu.SelectedIndex);
            menu.Move(0, -1); Equal(0, menu.SelectedIndex);
            menu.Move(1, 0); Equal(1, menu.SelectedIndex);
            menu.Move(1, 0); Equal(2, menu.SelectedIndex);
            menu.Move(1, 0); Equal(2, menu.SelectedIndex);
            menu.Move(0, 1); Equal(5, menu.SelectedIndex);
            menu.Move(0, 1); Equal(5, menu.SelectedIndex);
            menu.Move(-1, 0); Equal(4, menu.SelectedIndex);
            menu.Move(0, -1); Equal(1, menu.SelectedIndex);
        }),
        ("Magic child and Escape-style Back preserve the parent cursor", () =>
        {
            var menu = new FieldMenuController();
            Select(menu, "MAGIC");
            menu.Confirm();
            Equal(2, menu.Depth);
            Equal(1, menu.Columns);
            SequenceEqual(["SPELLS", "ADJUSTMENT", "INFORMATION", "BACK"],
                menu.CurrentEntries.Select(entry => entry.Label));
            Select(menu, "INFORMATION");
            Check(!menu.Back(), "child Back stays inside the menu");
            Equal(1, menu.Depth);
            Equal("MAGIC", menu.CurrentEntries[menu.SelectedIndex].Label);
            Check(menu.Back(), "root Back requests closing the menu");
        }),
        ("Explicit System Back pops one frame without opening a panel", () =>
        {
            var menu = new FieldMenuController();
            Select(menu, "SYSTEM");
            menu.Confirm();
            SequenceEqual(["SETTINGS", "FOR TESTING", "BACK"],
                menu.CurrentEntries.Select(entry => entry.Label));
            Select(menu, "BACK");
            menu.Confirm();
            Equal(1, menu.Depth);
            Equal("SYSTEM", menu.CurrentEntries[menu.SelectedIndex].Label);
            Check(menu.BuildView(Player()).ActivePanel is null, "Back is navigation, not a leaf panel");
        }),
        ("The interactive Adjustment panel dismisses before its child frame", () =>
        {
            var player = Player();
            var menu = new FieldMenuController();
            Select(menu, "MAGIC"); menu.Confirm(player);
            Select(menu, "ADJUSTMENT"); menu.Confirm(player);
            var panel = menu.BuildView(player).ActivePanel;
            Equal(FieldMenuPanelKind.Adjustment, panel!.Kind);
            Equal("ADJUSTMENT", panel.Title);
            Check(!menu.Back(), "panel Back stays inside the menu");
            Check(menu.BuildView(player).ActivePanel is null, "first Back dismisses the panel");
            Equal(2, menu.Depth);
            Equal("ADJUSTMENT", menu.CurrentEntries[menu.SelectedIndex].Label);
            Check(!menu.Back(), "second Back pops the child frame");
            Equal(1, menu.Depth);
        }),
        ("Enter dismisses one passive Field panel without closing its parent", () =>
        {
            var game = new GameController();
            game.Handle(UiInput.Menu);
            game.Handle(UiInput.Right); // Magic.
            game.Handle(UiInput.Confirm);
            game.Handle(UiInput.Down); game.Handle(UiInput.Down); // Information.
            game.Handle(UiInput.Confirm);
            Check(game.FieldMenu.BuildView(game.State.Player).ActivePanel is not null,
                "Information opens a passive panel");

            game.Handle(UiInput.Confirm);

            Equal(GameMode.Menu, game.Mode);
            Equal(2, game.FieldMenu.Depth);
            Equal("INFORMATION", game.FieldMenu.CurrentEntries[game.FieldMenu.SelectedIndex].Label);
            Check(game.FieldMenu.BuildView(game.State.Player).ActivePanel is null,
                "Enter dismisses only the passive panel");

            game.Handle(UiInput.Back); // Root, still on Magic.
            game.Handle(UiInput.Down); game.Handle(UiInput.Left); // Status.
            game.Handle(UiInput.Confirm);
            game.Handle(UiInput.Confirm);
            Equal(FieldMenuPanelKind.Status,
                game.FieldMenu.BuildView(game.State.Player).ActivePanel!.Kind);
        }),
        ("Magic Adjustment edits exact quarter-step draft values and clamps", () =>
        {
            var player = Player();
            var menu = new FieldMenuController();
            Select(menu, "MAGIC"); menu.Confirm(player);
            Select(menu, "ADJUSTMENT"); menu.Confirm(player);

            var initial = menu.BuildView(player).ActivePanel!.Adjustment!;
            Equal("FIREBALL", initial.BaseMagic);
            Equal(4, initial.SizeSteps); Equal("1.00", initial.SizeMultiplier);
            Equal(4, initial.OutputSteps); Equal("1.00", initial.OutputMultiplier);
            Equal(4, initial.MpCost); Equal(0, initial.SelectedIndex);

            menu.Move(1, 0); menu.Move(1, 0); menu.Move(-1, 0);
            var size = menu.BuildView(player).ActivePanel!.Adjustment!;
            Equal(5, size.SizeSteps); Equal("1.25", size.SizeMultiplier);
            Equal(new Phase1A.Magic.ChantlessMagicConfiguration(
                Phase1A.Magic.PrototypeMagic.Fireball, 4, 4), player.ChantlessMagic);

            for (var i = 0; i < 30; i++) menu.Move(-1, 0);
            Equal(1, menu.BuildView(player).ActivePanel!.Adjustment!.SizeSteps);
            for (var i = 0; i < 30; i++) menu.Move(1, 0);
            Equal(16, menu.BuildView(player).ActivePanel!.Adjustment!.SizeSteps);

            menu.Move(0, 1);
            for (var i = 0; i < 30; i++) menu.Move(1, 0);
            var maximum = menu.BuildView(player).ActivePanel!.Adjustment!;
            Equal(16, maximum.OutputSteps); Equal("4.00", maximum.OutputMultiplier);
            menu.Move(0, 1); menu.Move(0, 1);
            Equal(2, menu.BuildView(player).ActivePanel!.Adjustment!.SelectedIndex);
        }),
        ("Magic Adjustment applies only on Apply and Escape cancels its draft", () =>
        {
            var player = Player();
            var menu = new FieldMenuController();
            Select(menu, "MAGIC"); menu.Confirm(player);
            Select(menu, "ADJUSTMENT"); menu.Confirm(player);
            menu.Move(1, 0);
            menu.Confirm(player);
            Check(menu.BuildView(player).ActivePanel?.Adjustment is not null,
                "Enter on Size keeps the interactive panel open");
            Equal(4, player.ChantlessMagic.SizeSteps);
            Check(!menu.Back(), "Escape cancels Adjustment inside the menu");
            Equal(4, player.ChantlessMagic.SizeSteps);

            menu.Confirm(player);
            menu.Move(1, 0); // Size 1.25.
            menu.Move(0, 1); menu.Move(1, 0); menu.Move(1, 0); // Output 1.50.
            var draft = menu.BuildView(player).ActivePanel!.Adjustment!;
            Equal(5, draft.SizeSteps); Equal(6, draft.OutputSteps); Equal(7, draft.MpCost);
            menu.Move(0, 1); menu.Confirm(player);

            Check(menu.BuildView(player).ActivePanel is null, "Apply returns to Magic submenu");
            Equal(5, player.ChantlessMagic.SizeSteps); Equal(6, player.ChantlessMagic.OutputSteps);
            Equal(2, menu.Depth);
            Equal("ADJUSTMENT", menu.CurrentEntries[menu.SelectedIndex].Label);

            menu.Confirm(player);
            var reopened = menu.BuildView(player).ActivePanel!.Adjustment!;
            Equal(5, reopened.SizeSteps); Equal(6, reopened.OutputSteps);
        }),
        ("Every reachable non-Back leaf produces a truthful panel", () =>
        {
            var cases = new (string[] Path, FieldMenuPanelKind Kind, string? FirstLine)[]
            {
                (["ITEMS"], FieldMenuPanelKind.Placeholder, "NO INVENTORY AVAILABLE."),
                (["MAGIC", "SPELLS"], FieldMenuPanelKind.Placeholder, "SPELLS ARE NOT IMPLEMENTED YET."),
                (["MAGIC", "ADJUSTMENT"], FieldMenuPanelKind.Adjustment, null),
                (["MAGIC", "INFORMATION"], FieldMenuPanelKind.Info,
                    "CHANTLESS MAGIC CAN MODIFY THE SIZE AND OUTPUT OF A LEARNED BASE MAGIC."),
                (["EQUIP"], FieldMenuPanelKind.Info, "EQUIPMENT OPENS FROM THE FIELD WITH E."),
                (["STATUS"], FieldMenuPanelKind.Status, "MAG/RES/AGI ARE WIP."),
                (["ACTIONS"], FieldMenuPanelKind.Info, "NO CONTEXTUAL ACTIONS AVAILABLE."),
                (["SYSTEM", "SETTINGS"], FieldMenuPanelKind.Placeholder, "SETTINGS ARE NOT IMPLEMENTED YET."),
                (["SYSTEM", "FOR TESTING"], FieldMenuPanelKind.Placeholder, "FOR TESTING IS NOT IMPLEMENTED YET.")
            };

            foreach (var item in cases)
            {
                var player = Player();
                var menu = new FieldMenuController();
                foreach (var label in item.Path) { Select(menu, label); menu.Confirm(player); }
                var panel = menu.BuildView(player).ActivePanel;
                Check(panel is not null, "leaf must produce a panel: " + string.Join(" > ", item.Path));
                Equal(item.Kind, panel!.Kind);
                Equal(item.Path[^1], panel.Title);
                if (item.FirstLine is not null) Equal(item.FirstLine, panel.Lines[0]);
            }
        }),
        ("Status projects current player vitals and all seven effective stats", () =>
        {
            var player = Player(hp: 47, mp: 3);
            Check(player.TryEquip(EquipmentSlot.Weapon, PrototypeEquipment.WoodenSword), "test sword equips");
            Check(player.TryEquip(EquipmentSlot.Body, PrototypeEquipment.LeatherArmor), "test armor equips");
            var menu = new FieldMenuController();
            Select(menu, "STATUS"); menu.Confirm();
            var panel = menu.BuildView(player).ActivePanel!;

            Equal(FieldMenuPanelKind.Status, panel.Kind);
            SequenceEqual(
                ["NAME", "HP", "MP", "STR", "DEF", "MAG", "RES", "AGI"],
                panel.Rows.Select(row => row.Label));
            SequenceEqual(
                ["ADVENTURER", "47/80", "3/12", "15", "12", "6", "6", "10"],
                panel.Rows.Select(row => row.Value));

            var frozen = panel;
            menu.Back();
            menu.Move(1, 0);
            Equal("47/80", frozen.Rows[1].Value);
        }),
        ("Open resets a deep menu and its panel to root ITEMS", () =>
        {
            var menu = new FieldMenuController();
            Select(menu, "SYSTEM"); menu.Confirm();
            Select(menu, "SETTINGS"); menu.Confirm();
            menu.Open();
            Equal(1, menu.Depth);
            Equal(0, menu.SelectedIndex);
            Equal("ITEMS", menu.CurrentEntries[0].Label);
            Check(menu.BuildView(Player()).ActivePanel is null, "reopen clears stale panel");
        }),
        ("Menu mode owns input and closes from any hierarchy depth", () =>
        {
            var game = new GameController();
            var player = game.State.Player;
            var position = game.State.Field.PlayerPosition;

            game.Handle(UiInput.Menu);
            Equal(GameMode.Menu, game.Mode);
            Equal(0, game.FieldMenu.SelectedIndex);
            Check(!game.StepField(1, 0), "menu blocks field movement");
            Equal(position, game.State.Field.PlayerPosition);
            game.Handle(UiInput.Right);
            game.Handle(UiInput.Confirm);
            Equal(2, game.FieldMenu.Depth);
            Equal("MAGIC", game.FieldMenu.BuildView(player).Breadcrumb);

            game.Handle(UiInput.Menu);
            Equal(GameMode.Field, game.Mode);
            Equal(1, game.FieldMenu.Depth);
            Equal(0, game.FieldMenu.SelectedIndex);
            Check(ReferenceEquals(player, game.State.Player), "menu preserves the persistent player owner");
            Check(game.StepField(-1, 0), "fresh field movement resumes after close");
        }),
        ("Escape at root closes while menu input is inert outside Field and Menu", () =>
        {
            var preparation = new GameController();
            preparation.Handle(UiInput.Menu);
            preparation.Handle(UiInput.Back);
            Equal(GameMode.Field, preparation.Mode);
            preparation.Handle(UiInput.Confirm);
            var preparationMode = preparation.Harness.Mode;
            preparation.Handle(UiInput.Menu);
            Equal(GameMode.Preparation, preparation.Mode);
            Equal(preparationMode, preparation.Harness.Mode);

            var battle = new GameController();
            Contact(battle);
            var battleMode = battle.Harness.Mode;
            var battleLog = battle.Harness.Session.MachineText;
            var battleSelection = battle.Harness.Menu.SelectedIndex;
            battle.Handle(UiInput.Menu);
            Equal(GameMode.Battle, battle.Mode);
            Equal(battleMode, battle.Harness.Mode);
            Equal(battleSelection, battle.Harness.Menu.SelectedIndex);
            Equal(battleLog, battle.Harness.Session.MachineText);

            var defeated = new GameController(player: Player(hp: 1, mp: 2));
            Contact(defeated);
            defeated.Handle(UiInput.Right);
            defeated.Handle(UiInput.Confirm);
            FinishMessages(defeated);
            Equal(ScreenMode.Ended, defeated.Harness.Mode);
            defeated.Handle(UiInput.Confirm);
            Equal(GameMode.GameOver, defeated.Mode);
            defeated.Handle(UiInput.Menu);
            Equal(GameMode.GameOver, defeated.Mode);
        })
    ];

    private static CharacterPreparation Player(int? hp = null, int? mp = null) =>
        new(Scenario.Setup(Scenario.GoldenSeed).Actors[0].InitialStats, hp, mp);

    private static void Select(FieldMenuController menu, string label)
    {
        var index = menu.CurrentEntries.ToList().FindIndex(entry => entry.Label == label);
        Check(index >= 0, "field menu entry exists: " + label);
        var row = index / menu.Columns;
        var column = index % menu.Columns;
        while (menu.SelectedRow > row) menu.Move(0, -1);
        while (menu.SelectedRow < row) menu.Move(0, 1);
        while (menu.SelectedColumn > column) menu.Move(-1, 0);
        while (menu.SelectedColumn < column) menu.Move(1, 0);
        Equal(index, menu.SelectedIndex);
    }

    private static void Contact(GameController game)
    {
        for (var i = 0; i < 6; i++) Check(game.StepField(1, 0), "contact path remains walkable");
        Equal(GameMode.Battle, game.Mode);
    }

    private static void FinishMessages(GameController game)
    {
        for (var i = 0; i < 20 && game.Harness.Mode == ScreenMode.Messages; i++) game.Handle(UiInput.Confirm);
        Check(game.Harness.Mode != ScreenMode.Messages, "battle message pages finish");
    }

    private static void SequenceEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual) =>
        Check(expected.SequenceEqual(actual), $"expected [{string.Join(", ", expected)}], got [{string.Join(", ", actual)}]");
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Equal<T>(T expected, T actual) =>
        Check(EqualityComparer<T>.Default.Equals(expected, actual), $"expected {expected}, got {actual}");
}
