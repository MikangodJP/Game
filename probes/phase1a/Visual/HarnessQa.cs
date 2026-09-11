using Godot;
using Phase1A.Magic;
using Phase1A.Rules;
using Phase1A.Visual.Presentation;

namespace Phase1A.Visual;

// Opt-in harness self-check. Injects real Godot input events into the same path as a player.
public partial class BattleScreen
{
    private readonly List<string> qaChecks = [];

    private async void StartQa(string outputDirectory)
    {
        System.IO.Directory.CreateDirectory(outputDirectory);
        try
        {
            await Frames();
            Check(ui.Mode == ScreenMode.Preparation && !ui.Preparation.InBattle, "starts outside battle in preparation");
            Check(ui.Preparation.EffectiveStats.Strength == 12 && ui.Preparation.EffectiveStats.Defense == 8, "preparation shows base STR12 DEF8");
            await Capture(outputDirectory, "00-preparation");
            await Press(Key.F2);
            Check(ui.Mode == ScreenMode.Preparation, "battle debug cannot create an encounter during preparation");
            await Press(Key.Enter); // Equipment.
            Check(ui.Mode == ScreenMode.EquipmentSlots && ui.Slots.Count == 4, "all four equipment slots available");
            await Press(Key.Enter); await Press(Key.Down); // Weapon -> Wooden Sword preview.
            Check(ui.Mode == ScreenMode.EquipmentItems && ui.PreviewStats.Strength == 15, "Wooden Sword preview STR12 to15");
            Check(ui.Preparation.EffectiveStats.Strength == 12, "preview does not equip or mutate effective stats");
            await Capture(outputDirectory, "00-sword-preview");
            await Press(Key.Enter);
            Check(ui.Preparation.EffectiveStats.Strength == 15 && ui.Preparation.BaseStats.Strength == 12, "equipped sword adds3 without altering base STR");
            await Press(Key.Down); await Press(Key.Down); // Body.
            await Press(Key.Enter); await Press(Key.Down);
            Check(ui.PreviewStats.Defense == 12 && ui.Preparation.EffectiveStats.Defense == 8, "Leather Armor preview DEF8 to12 without mutation");
            await Capture(outputDirectory, "00-armor-preview");
            await Press(Key.Enter);
            Check(ui.Preparation.EffectiveStats.Defense == 12, "equipped Leather Armor adds4 DEF");
            await Press(Key.Down); await Press(Key.Enter); await Press(Key.Down); // Accessory.
            Check(ui.PreviewStats.MaxHp == 85, "Copper Charm previews MaxHP85");
            await Press(Key.Enter);
            Check(ui.Preparation.EffectiveStats.MaxHp == 85 && ui.Preparation.Hp == 80, "MaxHP increase does not heal");
            await Press(Key.Enter); // Accessory again; move to None regardless of prior selection.
            while (ui.EquipmentIndex > 0) await Press(Key.Up);
            Check(ui.PreviewStats.MaxHp == 80, "unequip preview removes MaxHP bonus");
            await Press(Key.Enter);
            Check(ui.Preparation.EffectiveStats.MaxHp == 80 && ui.Preparation.Hp == 80, "unequip restores maximum and valid HP");
            await Capture(outputDirectory, "00-equipped-slots");
            await Press(Key.Escape);
            Check(ui.Mode == ScreenMode.Preparation, "equipment Back returns to preparation");
            await Capture(outputDirectory, "00-preparation-equipped");
            await Press(Key.Down); await Press(Key.Enter); // Start Battle.
            Check(ui.Mode == ScreenMode.Menu && ui.Preparation.InBattle, "Start Battle freezes preparation and enters grid");
            var initialStats = ui.Session.View.Hero.EffectiveStats;
            Check(initialStats.MaxHp == 80 && ui.Session.View.Hero.MaxHp == initialStats.MaxHp, "displayed maximum HP comes from effective stats");
            Check(initialStats.MaxMp == 12 && ui.Session.View.Hero.MaxMp == initialStats.MaxMp, "displayed maximum MP comes from effective stats");
            Check(initialStats.Strength == 15, "battle snapshot includes equipped STR15");
            Check(initialStats.Defense == 12, "battle snapshot includes equipped DEF12");
            Check(initialStats.Magic == 6, "hero MAG read model is 6");
            Check(initialStats.Resistance == 6, "hero RES read model is 6");
            Check(initialStats.Agility == 10, "hero AGI read model is 10");
            Check(ui.Session.View.Enemies[0].EffectiveStats == new Phase1A.Rules.CharacterStats(38, 2, 8, 5, 2, 3, 6), "Goblin effective stat profile inspectable");
            Check(ui.Session.View.Enemies[1].EffectiveStats == new Phase1A.Rules.CharacterStats(46, 0, 10, 4, 1, 3, 12), "Wolf effective stat profile inspectable");
            var untouched = ui.Session.MachineText;
            Check(!ui.Preparation.TryEquip(EquipmentSlot.Weapon, null), "higher layer rejects unequipping in active encounter");
            Check(!ui.Preparation.TryEquip(EquipmentSlot.Body, null), "higher layer rejects armor changes in active encounter");
            await Press(Key.Escape); await Press(Key.R);
            Check(ui.Mode == ScreenMode.Menu && ui.Preparation.InBattle, "battle Back and R cannot reopen preparation");
            Check(ui.Menu.Columns == 3, "root menu is a three-column grid");
            await Press(Key.D); Check(ui.Menu.SelectedIndex == 1, "Right moves ATTACK to DEFEND");
            await Press(Key.S); Check(ui.Menu.SelectedIndex == 4, "Down moves DEFEND to SKILLS");
            await Press(Key.A); Check(ui.Menu.SelectedIndex == 3, "Left moves SKILLS to SUMMONING");
            await Press(Key.W); Check(ui.Menu.SelectedIndex == 0, "Up moves SUMMONING to ATTACK");
            await Press(Key.Up); await Press(Key.Left);
            Check(ui.Menu.SelectedIndex == 0, "grid stops at top-left edges");
            foreach (var label in new[] { "ATTACK", "DEFEND", "MAGIC", "SUMMONING", "SKILLS", "SPECIAL", "ITEMS", "TACTICS", "RUN" })
                await MoveMenuTo(label);
            await Press(Key.Down); await Press(Key.Right);
            Check(ui.Menu.SelectedIndex == 8, "grid stops at bottom-right edges");
            await MoveMenuTo("ATTACK"); await Capture(outputDirectory, "01-battle");
            foreach (var category in new[] { "MAGIC", "SUMMONING", "SKILLS", "SPECIAL", "ITEMS", "TACTICS" })
            {
                await Choose(category);
                Check(ui.Menu.Breadcrumb == category, $"root {category} opens");
                Check(ui.Menu.Columns == 2, $"{category} uses two-column submenu");
                await Press(Key.Escape);
            }
            await Choose("ITEMS"); await Choose("Equipment Quick Use");
            Check(ui.Mode == ScreenMode.Wip && ui.Preparation.InBattle, "ITEMS equipment-named leaf remains WIP and cannot equip");
            await Press(Key.Enter); await Press(Key.Escape);
            await Choose("MAGIC");
            Check(ui.Menu.CurrentEntries.Select(entry => entry.Label).SequenceEqual(["CHANTLESS", "CHANT", "Back"]),
                "Battle Magic first displays Chantless and Chant");
            await Capture(outputDirectory, "02-magic-methods");
            await Choose("CHANT");
            Check(ui.Mode == ScreenMode.Wip && ui.WipLabel == "CHANT" &&
                ui.WipMessage == "Chanted magic is not implemented yet.",
                "Chant opens the exact passive WIP message");
            await Capture(outputDirectory, "02-chant-wip");
            await Press(Key.Enter);
            await Choose("CHANTLESS");
            Check(ui.Menu.CurrentEntries.Any(entry => entry.Label == "TRANSFORMATION MAGIC"),
                "Chantless contains the complete existing Magic taxonomy");
            await Capture(outputDirectory, "02-chantless-grid");
            await Choose("ELEMENTAL MAGIC"); await Choose("Fire");
            Check(ui.Mode == ScreenMode.MagicAdjustment && ui.Breadcrumb == "CHANTLESS > FIREBALL",
                "Elemental Fire opens the Battle cast adjustment");
            var initialMagic = ui.MagicAdjustment!;
            Check(initialMagic.Method == "CHANTLESS" && initialMagic.BaseMagic == "FIREBALL" &&
                initialMagic.SizeMultiplier == "1.00" && initialMagic.OutputMultiplier == "1.00" &&
                initialMagic.MpCost == 4 && initialMagic.CurrentMp == 12,
                "Battle adjustment starts from the default Fireball configuration and central cost");
            var emptyFifthSizeStep = await PixelAt(89, 100);
            await Press(Key.Right); await Press(Key.Down); await Press(Key.Right);
            var editedMagic = ui.MagicAdjustment!;
            Check(editedMagic.SizeMultiplier == "1.25" && editedMagic.OutputMultiplier == "1.25" &&
                editedMagic.MpCost == 6, "Battle arrows edit exact quarter steps and recalculate MP cost");
            Check(await PixelAt(89, 100) != emptyFifthSizeStep,
                "the Battle Size slider visibly fills its fifth step");
            await CaptureMagicAdjustment(outputDirectory, "02-magic-adjustment");
            await Press(Key.Down); await Press(Key.Enter);
            Check(ui.Mode == ScreenMode.Targets && ui.Breadcrumb == "FIREBALL > CHOOSE TARGET",
                "Cast opens the shared Fireball target screen");
            await Capture(outputDirectory, "02-fire-target");
            await Press(Key.Backspace);
            Check(ui.Mode == ScreenMode.MagicAdjustment && ui.MagicAdjustment is { SizeSteps: 5, OutputSteps: 5 } &&
                ui.Session.MachineText == untouched && ui.Session.View.Hero.Mp == 12,
                "Fireball target Back preserves the draft without a turn or MP cost");
            Check(ui.Preparation.LastUsedChantlessMagic(PrototypeMagic.Fireball) ==
                new ChantlessMagicConfiguration(PrototypeMagic.Fireball, 4, 4),
                "target cancellation does not update Fireball's last-used configuration");
            await Press(Key.Escape); await Press(Key.Escape);
            await Choose("TRANSFORMATION MAGIC"); await Choose("Self Transformation");
            Check(ui.Mode == ScreenMode.Wip && ui.WipLabel == "Self Transformation",
                "Transformation Magic remains a separate WIP taxonomy");
            await Capture(outputDirectory, "02-transformation-wip");
            await Press(Key.Backspace); await Press(Key.Escape); await Press(Key.Escape); await Press(Key.Escape);
            await Choose("SUMMONING"); await Choose("CREATURE SUMMONING"); await Choose("Dragon");
            Check(ui.Mode == ScreenMode.Wip && ui.WipLabel == "Dragon", "Summoning / Creature / Dragon WIP");
            await Capture(outputDirectory, "03-dragon-wip");
            await Press(Key.Enter); await Press(Key.Escape); await Choose("Back");
            await Choose("MAGIC"); await Choose("CHANTLESS"); await Choose("PRIMORDIAL / ROOT MAGIC");
            await Capture(outputDirectory, "04-root-magic");
            await Choose("Unknown / Forbidden");
            Check(ui.Mode == ScreenMode.Wip, "long submenu scrolls to final deep leaf");
            await Press(Key.Enter); await Press(Key.Escape); await Press(Key.Escape); await Press(Key.Escape);
            Check(ui.Session.MachineText == untouched, "all WIP navigation leaves simulation untouched");
            await Choose("ATTACK");
            await Press(Key.D);
            Check(ui.TargetIndex == 1, "WASD selects Wolf independently");
            await Capture(outputDirectory, "04-target-wolf");
            await Press(Key.Escape);
            Check(ui.Mode == ScreenMode.Menu && ui.Session.MachineText == untouched, "target Back cancels without a turn");
            await Choose("ATTACK"); await Press(Key.Right); await Press(Key.Space);
            Check(ui.Session.View.Enemies[0].Hp == 38 && ui.Session.View.Enemies[1].Hp < 46, "Attack damages selected Wolf via core");
            Check(ui.Session.View.Hero.Hp < 80 && ui.Session.View.Hero.Mp == 12, "enemy responses update visible HP without spell costs");
            Check(ui.Session.View.Hero.EffectiveStats == initialStats, "combat keeps equipped stat snapshot intact");
            var bareBattle = new BattleSession();
            bareBattle.Submit(MenuAction.Attack, 2);
            Check(ui.Session.View.Enemies[1].Hp == bareBattle.View.Enemies[1].Hp - 3, "sword increases real first attack damage by3 with identical RNG");
            Check(ui.Session.View.Hero.Hp == bareBattle.View.Hero.Hp + 4, "armor prevents2 damage from each of two enemy responses");
            Check(ui.BattleLines.Any(s => s.Contains("attacks Wolf")), "readable events displayed from machine stream");
            await Capture(outputDirectory, "05-attack");
            await FinishMessages(); await Choose("DEFEND");
            Check(ui.Session.View.Hero.Guarding && ui.Session.Events.Count(e => e.Kind == "GuardBlocked") == 2, "Defend covers both enemy responses");
            await Capture(outputDirectory, "06-defend");
            await FinishMessages();
            var fireballFirstEvent = ui.Session.Events.Length;
            var beforeFireballMp = ui.Session.View.Hero.Mp;
            var beforeFireballHp = ui.Session.View.Enemies[0].Hp;
            await Choose("MAGIC"); await Choose("CHANTLESS"); await Choose("ELEMENTAL MAGIC"); await Choose("Fire");
            Check(ui.Mode == ScreenMode.MagicAdjustment, "affordable Fireball first reaches Battle adjustment");
            await Press(Key.Down); await Press(Key.Down); await Press(Key.Enter);
            Check(ui.Mode == ScreenMode.Targets, "Battle Cast reaches the shared target screen");
            await Press(Key.Enter);
            Check(ui.Mode == ScreenMode.Messages && ui.Session.View.Hero.Mp == beforeFireballMp - 4,
                "default Fireball deducts exactly four MP once");
            Check(ui.Session.View.Enemies[0].Hp == beforeFireballHp - 13,
                "default Fireball deals real configured magical damage to Goblin");
            var fireballEvents = ui.Session.Events.Skip(fireballFirstEvent).ToArray();
            Check(fireballEvents.Count(e => e.Kind == "ManaChanged" && e.Source == 0 && e.Amount == -4) == 1,
                "Fireball emits one player MP deduction");
            var castIndex = Array.FindIndex(fireballEvents, e => e.Kind == "ActionStarted" && e.Detail == PrototypeMagic.Fireball.Id);
            var damageIndex = Array.FindIndex(fireballEvents, e => e.Kind == "Damaged" && e.Source == 0);
            var responseIndex = Array.FindIndex(fireballEvents, e => e.Kind == "ActionStarted" && e.Source != 0);
            Check(castIndex >= 0 && castIndex < damageIndex && damageIndex < responseIndex,
                "Fireball damage precedes the normal enemy response sequence");
            Check(ui.Session.LastMessages.Contains("Adventurer casts Fireball on Goblin!") &&
                ui.Session.LastMessages.Contains("Size 1.00 | Output 1.00 | MP 4"),
                "Fireball messages expose domain identity, configuration, and cost");
            Check(ui.Preparation.LastUsedChantlessMagic(PrototypeMagic.Fireball) ==
                new ChantlessMagicConfiguration(PrototypeMagic.Fireball, 4, 4),
                "successful Fireball records its exact last-used configuration");
            await Capture(outputDirectory, "06-fireball");
            await FinishMessages(); await Choose("RUN");
            Check(ui.Session.View.Outcome == Phase1A.Encounter.Outcome.Fled, "Run ends with Fled");
            await FinishMessages(); await Capture(outputDirectory, "07-fled");
            var ended = ui.Session.MachineText;
            await Press(Key.Enter); Check(ui.Session.MachineText == ended, "ended battle cannot accept another combat action");
            await Press(Key.R);
            Check(ui.Mode == ScreenMode.Preparation && ui.Preparation.Hp == 80 && ui.Preparation.EffectiveStats.Strength == 12, "R returns to fresh unequipped preparation");
            await Pad(JoyButton.DpadDown); await Pad(JoyButton.A); // Start Battle.
            Check(ui.Mode == ScreenMode.Menu, "controller can start battle from preparation");
            await Pad(JoyButton.DpadRight); await Pad(JoyButton.A);
            Check(ui.Session.View.Hero.Guarding, "controller D-pad and A submit Defend");
            await Pad(JoyButton.B);
            Check(ui.Mode == ScreenMode.Menu, "controller B closes message pages");
            await Press(Key.F2);
            Check(ui.Mode == ScreenMode.MachineLog, "original machine events inspectable");
            await Press(Key.F3); await Capture(outputDirectory, "08-machine-log"); await Press(Key.F2);
            Check(ui.Mode == ScreenMode.Menu, "debug Back restores command menu");
            var sawDefeatedGoblin = false;
            for (var turn = 0; turn < 20 && !ui.Session.View.Finished; turn++)
            {
                await Choose("ATTACK"); await Press(Key.Enter); await FinishMessages();
                if (!sawDefeatedGoblin && ui.Session.View.Enemies[0].Hp == 0)
                {
                    sawDefeatedGoblin = true;
                    Check(ui.Targets.All(t => t.Id != 1), "defeated Goblin removed from legal targets");
                    await Capture(outputDirectory, "09-goblin-defeated");
                }
            }
            Check(sawDefeatedGoblin, "repeated Attack defeats independently targeted Goblin");
            Check(ui.Mode == ScreenMode.Ended && ui.Session.View.Outcome is Phase1A.Encounter.Outcome.Victory or Phase1A.Encounter.Outcome.Defeat,
                "ordinary attacks also reach a terminal battle result");
            await Capture(outputDirectory, "10-battle-end");
            GD.Print($"VISUAL QA PASS: {qaChecks.Count} checks");
            System.IO.File.WriteAllLines(System.IO.Path.Combine(outputDirectory, "qa.txt"), qaChecks.Append("PASS ALL"));
            GetTree().Quit(0);
        }
        catch (Exception error)
        {
            GD.PrintErr(error.ToString());
            System.IO.File.WriteAllLines(System.IO.Path.Combine(outputDirectory, "qa.txt"), qaChecks.Append("FAIL " + error));
            GetTree().Quit(1);
        }
    }
    private void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException("Visual QA: " + name);
        qaChecks.Add("PASS " + name);
    }
    private async Task Frames()
    {
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
    }
    private async Task Press(Key key)
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = true });
        await Frames();
        Input.ParseInputEvent(new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = false });
        await Frames();
    }
    private async Task Pad(JoyButton button)
    {
        Input.ParseInputEvent(new InputEventJoypadButton { ButtonIndex = button, Pressed = true });
        await Frames();
        Input.ParseInputEvent(new InputEventJoypadButton { ButtonIndex = button, Pressed = false });
        await Frames();
    }
    private async Task Choose(string label)
    {
        await MoveMenuTo(label);
        await Press(Key.Enter);
    }
    private async Task MoveMenuTo(string label)
    {
        var index = ui.Menu.CurrentEntries.ToList().FindIndex(e => e.Label == label);
        Check(index >= 0, "menu entry present: " + label);
        for (var i = 0; i < 4 && ui.Menu.SelectedColumn > 0; i++) await Press(Key.Left);
        var row = index / ui.Menu.Columns;
        for (var i = 0; i < 30 && ui.Menu.SelectedRow != row; i++)
            await Press(ui.Menu.SelectedRow < row ? Key.Down : Key.Up);
        for (var i = 0; i < 4 && ui.Menu.SelectedColumn != index % ui.Menu.Columns; i++) await Press(Key.Right);
        Check(ui.Menu.SelectedIndex == index, "cursor reaches: " + label);
    }
    private async Task FinishMessages()
    {
        for (var i = 0; i < 30 && ui.Mode == ScreenMode.Messages; i++) await Press(Key.Enter);
        Check(ui.Mode != ScreenMode.Messages, "message pages finish cleanly");
    }
    private async Task Capture(string directory, string name)
    {
        QueueRedraw();
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var frame = GetViewport().GetTexture().GetImage();
        Check(frame.GetWidth() == 320 && frame.GetHeight() == 240, name + " native 320x240 viewport");
        var colors = new HashSet<Color>();
        for (var y = 0; y < frame.GetHeight(); y++)
            for (var x = 0; x < frame.GetWidth(); x++) colors.Add(frame.GetPixel(x, y));
        Check(colors.Count <= 32, name + $" limited palette ({colors.Count} colors)");
        var error = frame.SavePng(System.IO.Path.Combine(directory, name + ".png"));
        Check(error == Error.Ok, name + " captured");
    }

    private async Task CaptureMagicAdjustment(string directory, string name)
    {
        await Capture(directory, name);
        Check(LastMagicAdjustmentBounds == new Rect2I(40, 28, 240, 180),
            "Magic adjustment stays inside the fixed native viewport bounds");
        using var frame = GetViewport().GetTexture().GetImage();
        var black = new Color("000000");
        var white = new Color("ffffff");
        var sawBlack = false;
        var sawWhite = false;
        var paletteOnly = true;
        var rect = LastMagicAdjustmentBounds!.Value;
        for (var y = rect.Position.Y; y < rect.End.Y; y++)
            for (var x = rect.Position.X; x < rect.End.X; x++)
            {
                var color = frame.GetPixel(x, y);
                sawBlack |= color == black;
                sawWhite |= color == white;
                if (color != black && color != white) paletteOnly = false;
            }
        Check(paletteOnly && sawBlack && sawWhite,
            "Magic adjustment uses only literal black and white inside its one-pixel border");
    }
}
