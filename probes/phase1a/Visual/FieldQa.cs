using Godot;
using Phase1A.Encounter;
using Phase1A.Magic;
using Phase1A.Rules;
using Phase1A.Styles;
using Phase1A.Visual.Presentation;

namespace Phase1A.Visual;

// Opt-in acceptance run through real key press/release events and the normal field entry point.
public partial class BattleScreen
{
    private async void StartFieldQa(string outputDirectory)
    {
        System.IO.Directory.CreateDirectory(outputDirectory);
        var report = System.IO.Path.Combine(outputDirectory, "field-qa.txt");
        try
        {
            await Frames();
            Check(standalone is null && game.Mode == GameMode.Field, "normal startup enters Field, not the battle probe");
            FieldPosition(2, 5, "new game spawns at the starting map location");
            var initialState = game.State;
            var initialPlayer = game.State.Player;
            var initialMap = game.State.Field.Map;
            Check(initialMap.Width == 19 && initialMap.Height == 11, "starting field uses the map definition dimensions");
            Check(game.State.Field.Encounters.Count(e => !e.Defeated) == 1, "one active visible field encounter starts on the map");
            await Capture(outputDirectory, "field-00-start");

            await Press(Key.W); FieldPosition(2, 4, "W immediately moves up one tile");
            await Press(Key.S); FieldPosition(2, 5, "S immediately moves down one tile");
            await Press(Key.A); FieldPosition(1, 5, "A immediately moves left one tile");
            await Press(Key.A); FieldPosition(1, 5, "the solid left boundary blocks movement");
            await Press(Key.D); FieldPosition(2, 5, "D immediately moves right one tile");

            FieldKey(Key.W, true);
            await Frames();
            Check(game.State.Field.PlayerPosition.Y < 5, "held movement starts on key press");
            await FieldDelay(0.34);
            FieldKey(Key.W, false);
            await Frames();
            var released = game.State.Field.PlayerPosition;
            Check(released.X == 2 && released.Y <= 3, "holding W repeats movement across multiple tiles");
            await FieldDelay(0.28);
            Check(game.State.Field.PlayerPosition == released, "movement stops after key release");
            for (var i = 0; i < 10 && game.State.Field.PlayerPosition.Y > 1; i++) await Press(Key.W);
            await Press(Key.W);
            FieldPosition(2, 1, "the solid top boundary blocks movement");
            Check(!initialMap.IsWalkable(2, 0) && !initialMap.IsWalkable(-1, 5), "map data owns boundary collision");
            for (var i = 0; i < 10 && game.State.Field.PlayerPosition.Y < 5; i++) await Press(Key.S);
            FieldPosition(2, 5, "released movement can resume in the opposite direction");
            await Press(Key.D); await Press(Key.D); await Press(Key.W);
            FieldPosition(4, 5, "the obstacle at 4,4 blocks upward movement");
            Check(!initialMap.IsWalkable(4, 4), "obstacle collision comes from map data");
            await Capture(outputDirectory, "field-01-collision");
            await Press(Key.R);
            Check(ReferenceEquals(initialState, game.State), "R cannot reset a live field");

            await EnterFieldEncounter();
            Check(ReferenceEquals(initialPlayer, ui.Preparation), "field encounter uses the authoritative player preparation");
            var initialBattleStats = CombatStyleRules.ApplyStance(
                initialPlayer.EffectiveStats, PrototypeCombatStyles.SwordGod.Stance, shifted: false);
            Check(ui.Session.View.Hero.Hp == initialPlayer.Hp &&
                    ui.Session.View.Hero.EffectiveStats == initialBattleStats &&
                    initialBattleStats.Strength == 14 && initialBattleStats.Defense == 6,
                "first battle starts from current HP and the bare STR14 DEF6 Sword stance");
            await Choose("RUN"); await FinishMessages();
            Check(ui.Mode == ScreenMode.Ended && ui.Session.View.Outcome == Outcome.Fled, "existing Run resolves the field encounter as Fled");
            await Press(Key.R);
            Check(game.Mode == GameMode.Battle && ui.Mode == ScreenMode.Ended, "R cannot replace a finished field battle before its result is applied");
            await Press(Key.Enter);
            FieldPosition(7, 5, "flee returns to the safe tile before contact");
            Check(ReferenceEquals(initialState, game.State) && ReferenceEquals(initialMap, game.State.Field.Map), "flee preserves the game and map instances");
            Check(game.State.Field.Encounters.All(e => !e.Defeated), "flee does not defeat the field enemy");
            await FieldDelay(0.30);
            FieldPosition(7, 5, "flee does not immediately retrigger or resume stale movement");
            await Capture(outputDirectory, "field-02-fled");

            // Deliberately lose through ordinary controls so GameOver and its only reset path are exercised.
            await EnterFieldEncounter();
            for (var turn = 0; turn < 30 && !ui.Session.View.Finished; turn++)
            {
                await Choose("DEFEND"); await FinishMessages();
            }
            Check(ui.Mode == ScreenMode.Ended && ui.Session.View.Outcome == Outcome.Defeat, "ordinary Defend turns can reach Defeat");
            await Press(Key.Enter);
            Check(game.Mode == GameMode.GameOver && initialPlayer.Hp == 0, "defeat applies zero HP to the same player and enters GameOver");
            var defeatedPosition = game.State.Field.PlayerPosition;
            await Press(Key.Enter); await Press(Key.W);
            Check(game.Mode == GameMode.GameOver && game.State.Field.PlayerPosition == defeatedPosition, "GameOver ignores movement and ordinary confirmation");
            await Capture(outputDirectory, "field-03-game-over");
            await Press(Key.R);
            FieldPosition(2, 5, "R from GameOver starts a fresh field game");
            Check(!ReferenceEquals(initialState, game.State) && game.State.Player.Hp == 80, "only explicit GameOver restart replaces the player and restores HP");

            var player = game.State.Player;
            var field = game.State.Field;
            await Press(Key.E);
            Check(game.Mode == GameMode.Preparation && ui.IsWorldBound && ReferenceEquals(player, ui.Preparation), "field E opens preparation bound to the same player");
            await Press(Key.Enter); await Press(Key.Enter); await Press(Key.Down); await Press(Key.Enter);
            Check(player.EffectiveStats.Strength == 15 && player.BaseStats.Strength == 12, "Wooden Sword equips through preparation without changing base strength");
            await Press(Key.Down); await Press(Key.Down); await Press(Key.Enter); await Press(Key.Down); await Press(Key.Enter);
            Check(player.EffectiveStats.Defense == 12, "Leather Armor equips through preparation");
            await Capture(outputDirectory, "field-04-equipment");
            await Press(Key.Escape); await Press(Key.Escape);
            FieldPosition(2, 5, "preparation Back returns to the unchanged field position");
            await Press(Key.Enter);
            Check(game.Mode == GameMode.Preparation && ui.PreparationActionLabel == "Return to Field", "field Enter also opens preparation with a field-return action");
            await Press(Key.Down); await Press(Key.Enter);
            FieldPosition(2, 5, "preparation Return to Field does not start a standalone battle");

            await EnterFieldEncounter();
            var encounterPosition = field.PlayerPosition;
            var equippedStats = player.EffectiveStats;
            var equippedBattleStats = CombatStyleRules.ApplyStance(
                equippedStats, PrototypeCombatStyles.SwordGod.Stance, shifted: false);
            Check(ui.Session.View.Hero.Hp == player.Hp &&
                    ui.Session.View.Hero.EffectiveStats == equippedBattleStats &&
                    equippedBattleStats.Strength == 18 && equippedBattleStats.Defense == 10,
                "contact battle preserves current HP and projects equipped stats to STR18 DEF10");
            Check(!player.TryEquip(EquipmentSlot.Weapon, null) && !player.TryEquip(EquipmentSlot.Body, null), "equipment changes are rejected while battle is active");
            Check(ui.Menu.Columns == 3, "field battle retains the existing three-column command grid");
            await Press(Key.D); Check(ui.Menu.SelectedIndex == 1, "battle D navigates horizontally to Defend");
            await Press(Key.S); Check(ui.Menu.SelectedIndex == 4, "battle S navigates vertically to Skills");
            await Press(Key.A); await Press(Key.W);
            Check(ui.Menu.SelectedIndex == 0 && field.PlayerPosition == encounterPosition, "battle WASD changes menus without moving the field player");
            await Press(Key.E); await Press(Key.R);
            Check(game.Mode == GameMode.Battle && ui.Mode == ScreenMode.Menu, "E and R cannot open equipment or reset during battle");
            var beforeWip = ui.Session.MachineText;
            await Choose("MAGIC"); await Choose("CHANTLESS"); await Choose("ELEMENTAL MAGIC"); await Choose("Water");
            Check(ui.Mode == ScreenMode.Wip, "existing nested battle commands retain their WIP flow");
            await Press(Key.Enter); await Press(Key.Escape); await Press(Key.Escape); await Press(Key.Escape);
            Check(ui.Session.MachineText == beforeWip, "WIP navigation consumes no battle action");
            await Capture(outputDirectory, "field-05-battle");

            await Choose("ATTACK");
            Check(ui.Mode == ScreenMode.PhysicalActions && ui.Breadcrumb == "ATTACK > SWORD GOD",
                "field ATTACK opens the current Sword Style");
            await Press(Key.D);
            Check(ui.Breadcrumb == "ATTACK > WATER GOD > SHIFT" &&
                    ui.PhysicalActionLabels.SequenceEqual(
                        new[] { "STEADY CUT", "PRECISE CUT", "BASIC ATTACK" }),
                "field battle switches immediately to the shifted Water action list");
            await Press(Key.Escape);
            Check(ui.Mode == ScreenMode.Menu, "Back leaves the field battle Physical Style screen");
            await Choose("ATTACK");
            Check(ui.Breadcrumb == "ATTACK > WATER GOD > SHIFT",
                "backing out preserves the active Water Style and Shift state");
            await Press(Key.A);
            Check(ui.Breadcrumb == "ATTACK > SWORD GOD" && !ui.PhysicalStyle.Shifted,
                "returning to turn-start Sword removes Shift before acting");
            await Press(Key.Escape);
            Check(ui.Mode == ScreenMode.Menu, "field battle returns to root after Style cancellation checks");

            var fireballFirstEvent = ui.Session.Events.Length;
            await Choose("MAGIC"); await Choose("CHANTLESS"); await Choose("ELEMENTAL MAGIC"); await Choose("Fire");
            Check(ui.Mode == ScreenMode.MagicAdjustment && ui.MagicAdjustment is { SizeSteps: 4, OutputSteps: 4 },
                "Field encounter opens Battle-owned Fireball adjustment at the default values");
            await Press(Key.Right); await Press(Key.Down); await Press(Key.Right);
            Check(ui.MagicAdjustment is { SizeSteps: 5, OutputSteps: 5, MpCost: 6 },
                "Battle adjustment edits the field player's cast draft");
            await Press(Key.Down); await Press(Key.Enter);
            Check(ui.Mode == ScreenMode.Targets && ui.Breadcrumb == "FIREBALL > CHOOSE TARGET",
                "Battle Cast uses the shared target screen");
            await Press(Key.Enter);
            Check(ui.Session.View.Hero.Mp == 6, "Size 1.25 Output 1.25 Fireball costs exactly six MP");
            Check(ui.Session.View.Enemies[0].Hp == 23, "Output 1.25 produces fifteen magical damage on Goblin");
            var fireballEvents = ui.Session.Events.Skip(fireballFirstEvent).ToArray();
            Check(fireballEvents.Count(e => e.Kind == "ManaChanged" && e.Source == 0 && e.Amount == -6) == 1,
                "configured Fireball deducts MP exactly once");
            Check(ui.Session.LastMessages.Contains("Size 1.25 | Output 1.25 | MP 6"),
                "configured Fireball details are readable in battle");
            Check(fireballEvents.Any(e => e.Kind == "ActionStarted" && e.Source != 0),
                "enemies respond through the normal loop after configured Fireball");
            await Capture(outputDirectory, "field-05-fireball");
            await FinishMessages();
            await Choose("DEFEND");
            Check(ui.Session.View.Hero.Guarding && ui.Session.Events.Any(e => e.Kind == "GuardBlocked"), "existing Defend and enemy responses work in a field encounter");
            await FinishMessages();
            for (var turn = 0; turn < 20 && !ui.Session.View.Finished; turn++)
            {
                await Choose("ATTACK"); await Press(Key.Down); await Press(Key.Down);
                await Press(Key.Enter); await Press(Key.Enter); await FinishMessages();
            }
            Check(ui.Mode == ScreenMode.Ended && ui.Session.View.Outcome == Outcome.Victory, "equipped ordinary attacks win the existing battle");
            var finalHp = ui.Session.View.Hero.Hp;
            var finalMp = ui.Session.View.Hero.Mp;
            Check(finalHp > 0 && finalHp < 80, "victory has surviving HP loss to persist");
            await Capture(outputDirectory, "field-06-victory");
            await Press(Key.Enter);
            Check(game.Mode == GameMode.Field && ReferenceEquals(field, game.State.Field), "victory returns to the same field instance");
            Check(field.PlayerPosition == encounterPosition, "victory preserves the encounter contact position");
            Check(ReferenceEquals(player, game.State.Player) && player.Hp == finalHp && player.Mp == finalMp, "victory applies HP and MP to the same player without healing");
            Check(player.LastUsedChantlessMagic(PrototypeMagic.Fireball) ==
                new ChantlessMagicConfiguration(PrototypeMagic.Fireball, 5, 5),
                "successful Battle adjustment persists as Fireball's last-used configuration");
            Check(player.EffectiveStats == equippedStats && !player.InBattle, "equipment persists and the battle preparation lock is released");
            Check(field.Encounters.All(e => e.Defeated), "victory records the encounter as defeated in field state");
            await FieldDelay(0.28);
            Check(game.Mode == GameMode.Field && field.PlayerPosition == encounterPosition, "returned field remains idle until a new movement press");
            await Capture(outputDirectory, "field-07-returned");
            await Press(Key.D); FieldPosition(9, 5, "exploration continues beyond the defeated encounter");
            await Press(Key.A); FieldPosition(8, 5, "walking back over the defeated enemy tile does not retrigger battle");
            Check(player.Hp == finalHp && field.Encounters.All(e => e.Defeated), "continued exploration keeps the result and defeated encounter state");
            await Capture(outputDirectory, "field-08-continue");
            GD.Print($"FIELD QA PASS: {qaChecks.Count} checks");
            System.IO.File.WriteAllLines(report, qaChecks.Append("PASS ALL"));
            GetTree().Quit(0);
        }
        catch (Exception error)
        {
            GD.PrintErr(error.ToString());
            System.IO.File.WriteAllLines(report, qaChecks.Append("FAIL " + error));
            GetTree().Quit(1);
        }
    }

    private void FieldPosition(int x, int y, string message) => Check(
        game.Mode == GameMode.Field && game.State.Field.PlayerPosition.X == x && game.State.Field.PlayerPosition.Y == y,
        message + $" (actual {game.Mode} {game.State.Field.PlayerPosition.X},{game.State.Field.PlayerPosition.Y})");

    private static void FieldKey(Key key, bool pressed) => Input.ParseInputEvent(
        new InputEventKey { Keycode = key, PhysicalKeycode = key, Pressed = pressed });

    private async Task FieldDelay(double seconds) => await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);

    private async Task EnterFieldEncounter()
    {
        Check(game.Mode == GameMode.Field && game.State.Field.PlayerPosition.Y == 5, "encounter approach starts on the open field path");
        for (var step = 0; step < 10 && game.State.Field.PlayerPosition.X < 7; step++) await Press(Key.D);
        FieldPosition(7, 5, "player approaches the encounter through real field input");
        FieldKey(Key.D, true);
        await Frames();
        Check(game.Mode == GameMode.Battle && ui.Mode == ScreenMode.Menu, "contact enters the existing battle command screen");
        var session = ui.Session;
        var position = game.State.Field.PlayerPosition;
        await FieldDelay(0.32);
        FieldKey(Key.D, false);
        await Frames();
        Check(position.X == 8 && position.Y == 5 && game.State.Field.PlayerPosition == position, "field movement stops immediately when the encounter starts");
        Check(ReferenceEquals(session, ui.Session) && ui.Menu.SelectedIndex == 0, "held contact input neither restarts battle nor drives its menu");
    }
}
