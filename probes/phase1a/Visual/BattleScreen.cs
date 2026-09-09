using Godot;
using Phase1A.Rules;
using Phase1A.Visual.Presentation;

namespace Phase1A.Visual;

// Input routes through the current game mode; battle drawing stays on this surface.
public partial class BattleScreen : Node2D
{
    private readonly GameController game = new();
    private HarnessController? standalone;
    private HarnessController ui => standalone ?? game.Harness;
    private FieldScreen fieldScreen = null!;
    private readonly List<(string Source, UiInput Direction)> heldMovement = [];
    private double movementDelay;
    private bool InField => standalone is null && game.Mode == GameMode.Field;
    private static readonly Color Ink = new("070a12"), Panel = new("101923"), Border = new("536b71"),
        Paper = new("d6d2ae"), Muted = new("84958d"), Gold = new("dfb963"),
        Red = new("c86959"), Green = new("9eaf74"), Blue = new("819eb5");
    private string debugNotice = "F3: SAVE ORIGINAL EVENTS";

    public override void _Ready()
    {
        TextureFilter = TextureFilterEnum.Nearest;
        GetWindow().MinSize = new Vector2I(640, 480);
        var args = OS.GetCmdlineUserArgs();
        // The old isolated probe remains opt-in for its independent regression suite.
        if (args.Contains("--battle-probe") || args.Contains("--qa")) standalone = new();
        fieldScreen = new FieldScreen { Game = game };
        AddChild(fieldScreen);
        GetWindow().FocusExited += ClearMovement;
        RefreshScreens();
        if (args.Length >= 2 && args[0] == "--qa") CallDeferred(MethodName.StartQa, args[1]);
        if (args.Length >= 2 && args[0] == "--field-qa") CallDeferred(MethodName.StartFieldQa, args[1]);
    }

    public override void _ExitTree() => GetWindow().FocusExited -= ClearMovement;

    public override void _Process(double delta)
    {
        if (!InField || heldMovement.Count == 0) return;
        movementDelay -= delta;
        if (movementDelay > 0) return;
        // Tile steps, with no physics or catch-up movement after a slow frame.
        movementDelay = 0.14;
        Route(heldMovement[^1].Direction);
    }

    public override void _UnhandledInput(InputEvent input)
    {
        UiInput? command = null;
        string source;
        bool pressed;
        if (input is InputEventKey key)
        {
            var code = key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode;
            source = "key:" + code;
            pressed = key.Pressed;
            if (key.Echo) return;
            command = code switch
            {
                Key.Up or Key.W => UiInput.Up, Key.Down or Key.S => UiInput.Down,
                Key.Left or Key.A => UiInput.Left, Key.Right or Key.D => UiInput.Right,
                Key.Enter or Key.KpEnter or Key.Space => UiInput.Confirm,
                Key.E when InField => UiInput.Confirm,
                Key.Escape or Key.Backspace => UiInput.Back,
                Key.F2 => UiInput.Debug, Key.R => UiInput.Restart, _ => null
            };
            if (pressed && code == Key.F3 && (standalone is not null || game.Mode == GameMode.Battle) && ui.Mode == ScreenMode.MachineLog)
            {
                var path = System.IO.Path.GetFullPath(System.IO.Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "artifacts", "visual", "machine-events.log"));
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
                System.IO.File.WriteAllText(path, ui.Session.MachineText, new System.Text.UTF8Encoding(false));
                debugNotice = "SAVED: MACHINE-EVENTS.LOG";
                GD.Print($"Original event log: {path}");
                QueueRedraw();
            }
        }
        else if (input is InputEventJoypadButton pad)
        {
            source = $"pad:{pad.Device}:{pad.ButtonIndex}";
            pressed = pad.Pressed;
            command = pad.ButtonIndex switch
            {
                JoyButton.DpadUp => UiInput.Up, JoyButton.DpadDown => UiInput.Down,
                JoyButton.DpadLeft => UiInput.Left, JoyButton.DpadRight => UiInput.Right,
                JoyButton.A => UiInput.Confirm, JoyButton.B => UiInput.Back,
                JoyButton.Start => UiInput.Restart, _ => null
            };
        }
        else return;
        if (!pressed)
        {
            heldMovement.RemoveAll(held => held.Source == source);
            if (command is not null) GetViewport().SetInputAsHandled();
            return;
        }
        if (command is { } selected)
        {
            if (InField && selected is UiInput.Up or UiInput.Down or UiInput.Left or UiInput.Right)
            {
                heldMovement.RemoveAll(held => held.Source == source);
                heldMovement.Add((source, selected));
                movementDelay = 0.18;
            }
            Route(selected);
            GetViewport().SetInputAsHandled();
        }
    }

    private void Route(UiInput input)
    {
        if (standalone is not null) standalone.Handle(input);
        else
        {
            var previousMode = game.Mode;
            if (InField && input is UiInput.Up or UiInput.Down or UiInput.Left or UiInput.Right)
            {
                var (dx, dy) = input switch
                {
                    UiInput.Up => (0, -1), UiInput.Down => (0, 1),
                    UiInput.Left => (-1, 0), _ => (1, 0)
                };
                game.StepField(dx, dy);
            }
            else game.Handle(input);
            if (game.Mode != previousMode) ClearMovement();
        }
        RefreshScreens();
    }

    private void ClearMovement() { heldMovement.Clear(); movementDelay = 0; }

    private void RefreshScreens()
    {
        fieldScreen.Visible = standalone is null && game.Mode is GameMode.Field or GameMode.GameOver;
        fieldScreen.QueueRedraw();
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (standalone is null && game.Mode is GameMode.Field or GameMode.GameOver) return;
        Fill(0, 0, 320, 240, Ink);
        if (ui.IsPreparing)
        {
            DrawPreparation();
            return;
        }
        Text("FIELD ENCOUNTER", 8, 6, Muted);
        Text(ui.Session.View.Finished ? "BATTLE OVER" : "YOUR COMMAND", 230, 6, Gold);
        // Sparse, hard-edged ground marks. The empty dark field is intentional.
        Fill(21, 68, 113, 1, Panel); Fill(190, 68, 108, 1, Panel);
        Fill(49, 70, 3, 1, Border); Fill(108, 66, 2, 2, Panel); Fill(251, 70, 4, 1, Border);
        DrawEnemy(ui.Session.View.Enemies[0], 82, true);
        DrawEnemy(ui.Session.View.Enemies[1], 237, false);

        DrawPath();
        Window(8, 94, 304, 30);
        Window(8, 139, 304, 64);
        DrawCommands();
        DrawParty();
        Window(8, 204, 304, 32);
        for (var i = 0; i < ui.BattleLines.Count && i < 3; i++)
            Text(ui.BattleLines[i], 14, 209 + i * 8, Paper);
        if (ui.Mode == ScreenMode.Wip) DrawWip();
        if (ui.Mode == ScreenMode.MachineLog) DrawMachineLog();
    }

    private void DrawEnemy(ActorView enemy, int center, bool goblin)
    {
        var selected = ui.Mode == ScreenMode.Targets && ui.TargetIndex < ui.Targets.Count && ui.Targets[ui.TargetIndex].Id == enemy.Id;
        if (selected)
        {
            Text("v", center - 2, 21, Gold);
            Fill(center - 37, 68, 74, 1, Gold);
        }
        if (goblin) PixelArt.Goblin(this, center, 65, enemy.Hp == 0);
        else PixelArt.Wolf(this, center, 65, enemy.Hp == 0);
        Center(enemy.Name, center, 73, selected ? Gold : enemy.Hp == 0 ? Muted : Paper);
        Center($"HP {enemy.Hp}/{enemy.MaxHp}", center, 83, enemy.Hp == 0 ? Muted : Green);
        if (enemy.Status == "WEAKENED") Center("[WEAKENED]", center, 23, Red);
    }

    private void DrawPath()
    {
        var path = ui.Breadcrumb.ToUpperInvariant();
        // Keep the root category and the current end visible on long nested paths.
        if (path.Length > 49)
        {
            var root = path.Split(" > ")[0];
            path = root + " > ... > " + path[^Math.Min(49 - root.Length - 9, path.Length)..];
        }
        Text(path, 10, 128, Gold);
    }

    private void DrawCommands()
    {
        if (ui.Mode == ScreenMode.Messages)
        {
            Text("BATTLE EVENTS", 18, 147, Gold);
            Text("ENTER / A  NEXT", 18, 165, Paper);
            Text("ESC / B    CLOSE", 18, 181, Muted);
            return;
        }
        if (ui.Mode == ScreenMode.Ended)
        {
            Text(ui.Session.View.Outcome?.ToString() ?? "BATTLE OVER", 18, 147, Gold, 2);
            Text(standalone is not null ? "R / START  NEW PREPARATION" :
                ui.Session.View.Outcome == Phase1A.Encounter.Outcome.Defeat ? "ENTER / A  CONTINUE" : "ENTER / A  RETURN TO FIELD", 18, 174, Paper);
            Text("F2         EVENT LOG", 18, 189, Muted);
            return;
        }
        IReadOnlyList<string> entries;
        int selected;
        int columns;
        if (ui.Mode == ScreenMode.Targets)
        {
            entries = ui.Targets.Select(t => t.Name).Append("Back").ToArray();
            selected = ui.TargetIndex;
            columns = 3;
        }
        else
        {
            entries = ui.Menu.CurrentEntries.Select(e => e.Label).ToArray();
            selected = ui.Menu.SelectedIndex;
            columns = ui.Menu.Columns;
        }
        DrawCommandGrid(entries, selected, columns);
    }

    private void DrawCommandGrid(IReadOnlyList<string> entries, int selected, int columns)
    {
        var visibleRows = columns == 3 ? 3 : 4;
        var rowHeight = columns == 3 ? 18 : 14;
        var selectedRow = selected / columns;
        var firstRow = Math.Max(0, selectedRow - visibleRows + 1);
        var start = firstRow * columns;
        var cellWidth = 296 / columns;
        var end = Math.Min(entries.Count, start + visibleRows * columns);
        for (var i = start; i < end; i++)
        {
            var x = 12 + i % columns * cellWidth;
            var y = 146 + (i / columns - firstRow) * rowHeight;
            if (i == selected)
            {
                Fill(x, y - 2, cellWidth - 2, 12, new Color("263338"));
                Text(">", x + 1, y, Gold);
            }
            Text(entries[i], x + 10, y, i == selected ? Paper : Muted);
        }
        if (start > 0) Text("^", 304, 139, Gold);
        if (entries.Count > end) Text("+", 304, 195, Gold);
    }

    private void DrawParty()
    {
        var hero = ui.Session.View.Hero;
        Text(hero.Name, 16, 100, Paper);
        Text($"HP {hero.Hp,2}/{hero.MaxHp}", 120, 100, hero.Hp < 20 ? Red : Green);
        Text($"MP {hero.Mp,2}/{hero.MaxMp}", 228, 100, Blue);
        Text(hero.Hp == 0 ? "DEFEATED" : hero.Status, 16, 113, hero.Guarding ? Gold : Muted);
        Text("F2: LOG", 258, 113, Muted);
    }

    private void DrawPreparation()
    {
        var preparation = ui.Preparation;
        Text("PREPARATION", 8, 7, Gold);
        Text("ADVENTURER", 248, 7, Paper);
        Window(204, 26, 108, 174);
        Text("EFFECTIVE", 214, 37, Gold);
        var stats = preparation.EffectiveStats;
        Text($"HP {preparation.Hp}/{stats.MaxHp}", 214, 58, Green);
        Text($"MP {preparation.Mp}/{stats.MaxMp}", 214, 74, Blue);
        Text($"STR {stats.Strength}", 214, 96, Paper);
        Text($"DEF {stats.Defense}", 214, 111, Paper);
        Text($"MAG {stats.Magic}", 214, 132, Muted);
        Text($"RES {stats.Resistance}", 214, 147, Muted);
        Text($"AGI {stats.Agility}", 214, 162, Muted);
        Text("MAG/RES/AGI", 214, 180, Muted);
        Text("ARE WIP", 214, 189, Muted);

        if (ui.Mode == ScreenMode.EquipmentItems)
        {
            Window(8, 26, 190, 116);
            Text(ui.SelectedSlot + " OPTIONS", 16, 37, Gold);
            var labels = ui.EquipmentChoices.Select(item => item?.DisplayName ?? "None").Append("Back").ToArray();
            for (var i = 0; i < labels.Length; i++)
                PrepChoice(labels[i], 16, 59 + i * 20, ui.EquipmentIndex == i, 174);
            Window(8, 148, 190, 52);
            Text("PREVIEW ONLY", 16, 157, Gold);
            var changes = ChangedStats(stats, ui.PreviewStats);
            if (changes.Count == 0) Text("NO STAT CHANGE", 16, 175, Muted);
            else for (var i = 0; i < changes.Count; i++) Text(changes[i], 16, 173 + i * 9, Green);
        }
        else
        {
            var choosingSlots = ui.Mode == ScreenMode.EquipmentSlots;
            Window(8, 26, 190, choosingSlots ? 174 : 124);
            Text("EQUIPMENT", 16, 37, Gold);
            for (var i = 0; i < ui.Slots.Count; i++)
            {
                var slot = ui.Slots[i];
                var y = 53 + i * (choosingSlots ? 25 : 23);
                if (choosingSlots && ui.SlotIndex == i) Fill(12, y - 2, 182, 21, new Color("263338"));
                if (choosingSlots && ui.SlotIndex == i) Text(">", 14, y, Gold);
                Text(slot.ToString(), 24, y, Paper);
                Text(preparation.Loadout.Get(slot)?.DisplayName ?? "None", 30, y + 9, Muted);
            }
            if (choosingSlots) PrepChoice("Back", 16, 177, ui.SlotIndex == ui.Slots.Count, 174);
            else
            {
                Window(8, 156, 190, 44);
                PrepChoice("Equipment", 16, 166, ui.PreparationIndex == 0, 174);
                PrepChoice(ui.PreparationActionLabel, 16, 184, ui.PreparationIndex == 1, 174);
            }
        }
        Window(8, 206, 304, 30);
        Text(ui.Mode == ScreenMode.EquipmentItems ? "ENTER: EQUIP   ESC: BACK" : "ARROWS: MOVE   ENTER: SELECT   ESC: BACK", 14, 213, Paper);
        Text(ui.Mode == ScreenMode.EquipmentItems ? "MORE MAX HP DOES NOT HEAL." : "EQUIPMENT LOCKS WHEN BATTLE STARTS.", 14, 225, Muted);
    }

    private void PrepChoice(string label, int x, int y, bool selected, int width)
    {
        if (selected)
        {
            Fill(x - 4, y - 2, width, 12, new Color("263338"));
            Text(">", x - 2, y, Gold);
        }
        Text(label, x + 8, y, selected ? Paper : Muted);
    }

    private static List<string> ChangedStats(CharacterStats current, CharacterStats candidate)
    {
        var changes = new List<string>();
        foreach (var (name, before, after) in new[]
        {
            ("MAXHP", current.MaxHp, candidate.MaxHp), ("MAXMP", current.MaxMp, candidate.MaxMp),
            ("STR", current.Strength, candidate.Strength), ("DEF", current.Defense, candidate.Defense),
            ("MAG", current.Magic, candidate.Magic), ("RES", current.Resistance, candidate.Resistance),
            ("AGI", current.Agility, candidate.Agility)
        })
            if (before != after) changes.Add($"{name} {before} > {after}");
        return changes;
    }

    private void DrawWip()
    {
        Fill(29, 59, 264, 118, Ink);
        Window(25, 55, 268, 118);
        Center("WIP", 159, 67, Gold, 2);
        var lines = Wrap(ui.WipLabel.ToUpperInvariant(), 39);
        for (var i = 0; i < lines.Count; i++) Center(lines[i], 159, 92 + i * 9, Paper);
        Center("IS NOT IMPLEMENTED.", 159, 122, Muted);
        Center("[ CONTINUE ]", 159, 150, Gold);
    }

    private void DrawMachineLog()
    {
        Fill(0, 0, 320, 240, Ink);
        Text("ORIGINAL EVENT LOG", 8, 7, Gold);
        Text("UP/DOWN: SCROLL   ESC/F2: BACK", 8, 20, Muted);
        var events = ui.Session.Events;
        for (var i = ui.DebugOffset; i < events.Length && i < ui.DebugOffset + 7; i++)
        {
            var e = events[i];
            var y = 36 + (i - ui.DebugOffset) * 27;
            Text($"{e.Sequence:D4} {e.Kind}  {e.Source}>{e.Target}", 8, y, Paper);
            Text(e.Detail, 20, y + 8, Muted);
            Text($"AMOUNT {e.Amount}  VALUE {e.Value}", 20, y + 16, Blue);
        }
        Text(debugNotice, 8, 230, Gold);
    }

    private void Window(int x, int y, int width, int height)
    {
        Fill(x, y, width, height, Panel);
        Fill(x + 2, y, width - 4, 1, Border); Fill(x + 2, y + height - 1, width - 4, 1, Border);
        Fill(x, y + 2, 1, height - 4, Border); Fill(x + width - 1, y + 2, 1, height - 4, Border);
        Fill(x + 1, y + 1, 2, 1, Paper); Fill(x + width - 3, y + 1, 2, 1, Paper);
        Fill(x + 1, y + height - 2, 2, 1, Border); Fill(x + width - 3, y + height - 2, 2, 1, Border);
    }
    private void Fill(int x, int y, int width, int height, Color color)
    {
        if (width > 0 && height > 0) DrawRect(new Rect2(x, y, width, height), color);
    }
    private void Text(string text, int x, int y, Color color, int scale = 1) => PixelArt.Text(this, text, x, y, color, scale);
    private void Center(string text, int center, int y, Color color, int scale = 1) => Text(text, center - (text.Length * 6 - 1) * scale / 2, y, color, scale);
    private static List<string> Wrap(string text, int columns)
    {
        var lines = new List<string>();
        var line = "";
        foreach (var word in text.Split(' '))
        {
            if (line.Length > 0 && line.Length + word.Length + 1 > columns) { lines.Add(line); line = ""; }
            line += (line.Length == 0 ? "" : " ") + word;
        }
        if (line.Length > 0) lines.Add(line);
        return lines;
    }
}
