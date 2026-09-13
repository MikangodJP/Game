using Phase1A;
using Phase1A.Encounter;
using Phase1A.Magic;
using Phase1A.Preparation;
using Phase1A.Rules;
using Phase1A.Styles;
using Phase1A.Visual.Presentation;

var tests = new (string Name, Action Run)[]
{
    ("All actor stats are immutable projections and inspection does not alter combat", () =>
    {
        var session = new BattleSession();
        var log = session.MachineText;
        var initial = session.View;
        Equal(new CharacterStats(80, 12, 14, 6, 6, 6, 10), initial.Hero.EffectiveStats);
        Equal(new CharacterStats(38, 2, 8, 5, 2, 3, 6), initial.Enemies[0].EffectiveStats);
        Equal(new CharacterStats(46, 0, 10, 4, 1, 3, 12), initial.Enemies[1].EffectiveStats);
        foreach (var actor in initial.Enemies.Prepend(initial.Hero))
        {
            Equal(actor.EffectiveStats.MaxHp, actor.MaxHp);
            Equal(actor.MaxHp, actor.Hp);
            Equal(actor.EffectiveStats.MaxMp, actor.MaxMp);
            Equal(actor.MaxMp, actor.Mp);
        }
        var editedCopy = initial.Hero with
        {
            Hp = 1,
            EffectiveStats = initial.Hero.EffectiveStats.With(StatId.Strength, 999)
        };
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

        var expanded = new CharacterStats(80, 12, 12, 8, 6, 6, 10)
            .ToBuilder()
            .Set(StatId.Dexterity, 21)
            .Set(StatId.Speed, 22)
            .Set(StatId.Endurance, 23)
            .Set(StatId.Constitution, 24)
            .Set(StatId.Intelligence, 25)
            .Set(StatId.Reflex, 26)
            .Set(StatId.Balance, 27)
            .Set(StatId.MagicDexterity, 28)
            .Build();
        var expandedBattle = new BattleState(new EncounterSetup(
        [
            new("fixture:hero", "hero", Side.Adventurers, expanded),
            new("fixture:enemy-a", null, Side.Monsters,
                new CharacterStats(10, 0, 0, 0, 0, 0, 0)),
            new("fixture:enemy-b", null, Side.Monsters,
                new CharacterStats(10, 0, 0, 0, 0, 0, 0))
        ], 11));
        var expandedView = new BattleSession(expandedBattle).View.Hero;
        Equal(expanded, expandedView.EffectiveStats);
        Equal(26, expandedView.EffectiveStats[StatId.Reflex]);
        var changedProjection = expandedView with
        {
            EffectiveStats = expandedView.EffectiveStats.With(StatId.Reflex, 999)
        };
        Equal(999, changedProjection.EffectiveStats[StatId.Reflex]);
        Equal(26, expandedBattle.Read(0).EffectiveStats[StatId.Reflex]);
    }),
    ("Battle read model exposes immutable active Style and current Techniques", () =>
    {
        var session = new BattleSession();
        var style = session.View.PhysicalStyle ?? throw new Exception("player Style missing");
        Check(style.KnownStyles.Select(option => option.BattleLabel).SequenceEqual(
                new[] { "SWORD GOD", "WATER GOD", "NORTH GOD" }),
            "known Style labels");
        Equal(0, style.ActiveIndex);
        Equal(PrototypeCombatStyles.SwordGod.Id, style.ActiveStyleId);
        Equal("SWORD GOD", style.ActiveLabel);
        Equal(PrototypeCombatStyles.SwordGod.Id, style.TurnStartStyleId);
        Check(!style.Shifted, "Sword starts established");
        Check(style.Techniques.Select(technique => technique.DisplayName)
            .SequenceEqual(new[] { "STRAIGHT SLASH", "HEAVY SLASH" }),
            "Sword Techniques");

        var edited = style with { ActiveStyleId = "fixture:copy" };
        Equal("fixture:copy", edited.ActiveStyleId);
        Equal(PrototypeCombatStyles.SwordGod.Id,
            session.View.PhysicalStyle!.ActiveStyleId);
        Check(session.TryChangeStyle(PrototypeCombatStyles.WaterGod.Id),
            "session forwards an immediate Style change");
        var water = session.View.PhysicalStyle!;
        Equal("WATER GOD", water.ActiveLabel);
        Check(water.Shifted, "Water is shifted on the same turn");
        Check(water.Techniques.Select(technique => technique.DisplayName)
            .SequenceEqual(new[] { "STEADY CUT", "PRECISE CUT" }),
            "Water Techniques replace Sword Techniques");
    }),
    ("ATTACK is one three-row Physical Style screen with real clamped switching and Back persistence", () =>
    {
        var ui = Started();
        OpenPhysical(ui);
        Check(ui.PhysicalActionLabels.SequenceEqual(
                new[] { "STRAIGHT SLASH", "HEAVY SLASH", "BASIC ATTACK" }),
            "Sword physical actions");
        Equal("ATTACK > SWORD GOD", ui.Breadcrumb);
        var actions = ui.Session.Events.Count(e => e.Kind == "ActionStarted");
        ui.Handle(UiInput.Right);
        Equal(PrototypeCombatStyles.WaterGod.Id,
            ui.Session.View.PhysicalStyle!.ActiveStyleId);
        Check(ui.PhysicalActionLabels.SequenceEqual(
                new[] { "STEADY CUT", "PRECISE CUT", "BASIC ATTACK" }),
            "Water physical actions");
        Equal("ATTACK > WATER GOD > SHIFT", ui.Breadcrumb);
        Check(ui.Session.IsPlayerTurn, "Style switching keeps the player turn");
        Equal(actions, ui.Session.Events.Count(e => e.Kind == "ActionStarted"));

        ui.Handle(UiInput.Down);
        ui.Handle(UiInput.Down);
        ui.Handle(UiInput.Left);
        Equal(2, ui.PhysicalActionIndex);
        Check(!ui.Session.View.PhysicalStyle!.Shifted,
            "return to turn-start Style removes Shift");
        ui.Handle(UiInput.Confirm);
        Equal(ScreenMode.Targets, ui.Mode);
        Equal("BASIC ATTACK > CHOOSE TARGET", ui.Breadcrumb);
        ui.Handle(UiInput.Back);
        Equal(ScreenMode.PhysicalActions, ui.Mode);
        Equal(2, ui.PhysicalActionIndex);
        ui.Handle(UiInput.Back);
        Equal(ScreenMode.Menu, ui.Mode);
        Equal(PrototypeCombatStyles.SwordGod.Id,
            ui.Session.View.PhysicalStyle!.ActiveStyleId);

        OpenPhysical(ui);
        ui.Handle(UiInput.Down);
        ui.Handle(UiInput.Right);
        ui.Handle(UiInput.Right);
        Equal(PrototypeCombatStyles.NorthGod.Id,
            ui.Session.View.PhysicalStyle!.ActiveStyleId);
        var atNorth = ui.Session.Events.Count(e => e.Kind == "StyleChanged");
        ui.Handle(UiInput.Right);
        Equal(atNorth, ui.Session.Events.Count(e => e.Kind == "StyleChanged"));
        Equal(1, ui.PhysicalActionIndex);
        ui.Handle(UiInput.Left);
        ui.Handle(UiInput.Left);
        var atSword = ui.Session.Events.Count(e => e.Kind == "StyleChanged");
        ui.Handle(UiInput.Left);
        Equal(atSword, ui.Session.Events.Count(e => e.Kind == "StyleChanged"));
        Equal(1, ui.PhysicalActionIndex);
    }),
    ("Technique and BASIC routes present stable readable action and miss messages", () =>
    {
        var straight = PrototypeCombatStyles.SwordGod.Techniques[0];
        var hit = new BattleSession(FindTechniqueSeed(straight, hit: true));
        Check(hit.SubmitTechnique(straight.Id, 1), "Straight Slash submitted");
        Check(hit.LastMessages.Contains("Adventurer uses Straight Slash on Goblin!"),
            "Technique action message");
        Check(hit.Events.Any(e => e.Kind == "ActionStarted" && e.Source == 0 &&
            e.Detail == straight.Id), "Technique stable event identity");

        var heavy = PrototypeCombatStyles.SwordGod.Techniques[1];
        var miss = new BattleSession(FindTechniqueSeed(heavy, hit: false));
        Check(miss.SubmitTechnique(heavy.Id, 1), "Heavy Slash submitted");
        Check(miss.LastMessages.Contains("Adventurer misses Goblin with Heavy Slash."),
            "Technique miss message");

        var basic = new BattleSession();
        Check(basic.SubmitBasicAttack(1), "BASIC submitted");
        Check(basic.LastMessages.Contains("Adventurer attacks Goblin!"),
            "existing BASIC message");
        Equal(Scenario.Strike.Id, basic.Events.First(e =>
            e.Kind == "ActionStarted" && e.Source == 0).Detail);
    }),
    ("Attack selects Wolf independently, uses Strike, then enemies act", () =>
    {
        var ui = Started();
        OpenBasicTarget(ui);
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
    ("Battle Magic separates Chantless from Chant and preserves the complete taxonomy", () =>
    {
        var menu = new BattleMenu();
        var magic = menu.Root.Single(entry => entry.Label == "MAGIC");
        var magicChildren = magic.Children ?? throw new Exception("MAGIC children missing");
        Check(magicChildren.Select(entry => entry.Label).SequenceEqual(["CHANTLESS", "CHANT"]),
            "Battle Magic first separates chantless and chanted methods");
        var chant = magicChildren.Single(entry => entry.Label == "CHANT");
        Equal("Chanted magic is not implemented yet.", chant.WipMessage);
        var chantless = magicChildren.Single(entry => entry.Label == "CHANTLESS");
        var chantlessChildren = chantless.Children ?? throw new Exception("CHANTLESS children missing");
        var elemental = chantlessChildren.Single(entry => entry.Label == "ELEMENTAL MAGIC");
        var elementalChildren = elemental.Children ?? throw new Exception("ELEMENTAL MAGIC children missing");
        var fire = elementalChildren.Single(entry => entry.Label == "Fire");
        Equal(MenuAction.Fireball, fire.Action);
        Equal("Fire", fire.Label);

        var transformation = chantlessChildren.Single(entry => entry.Label == "TRANSFORMATION MAGIC");
        var transformationChildren = transformation.Children ?? throw new Exception("TRANSFORMATION MAGIC children missing");
        var expected = new[]
        {
            "Self Transformation", "Beast Transformation", "Material Transformation",
            "Size Manipulation", "Polymorph"
        };
        Check(transformationChildren.Select(entry => entry.Label).SequenceEqual(expected),
            "Transformation Magic taxonomy is unchanged");
        Check(transformationChildren.All(entry => entry.Action == MenuAction.None && entry.Children is null),
            "Transformation Magic leaves remain WIP");

        var ui = Started();
        Choose(ui, "MAGIC"); Choose(ui, "CHANT");
        Equal(ScreenMode.Wip, ui.Mode);
        Equal("CHANT", ui.WipLabel);
        Equal("Chanted magic is not implemented yet.", ui.WipMessage);
        ui.Handle(UiInput.Confirm);
        Equal(ScreenMode.Menu, ui.Mode);
        Equal("CHANT", ui.Menu.CurrentEntries[ui.Menu.SelectedIndex].Label);
    }),
    ("Affordable Fire casts the configured Fireball once before normal enemy responses", () =>
    {
        var ui = Started();
        var firstEvent = ui.Session.Events.Length;
        Choose(ui, "MAGIC"); Choose(ui, "CHANTLESS"); Choose(ui, "ELEMENTAL MAGIC"); Choose(ui, "Fire");
        Equal(ScreenMode.MagicAdjustment, ui.Mode);
        var adjustment = ui.MagicAdjustment!;
        Equal("CHANTLESS", adjustment.Method);
        Equal("FIREBALL", adjustment.BaseMagic);
        Equal("1.00", adjustment.SizeMultiplier); Equal("1.00", adjustment.OutputMultiplier);
        Equal(4, adjustment.MpCost); Equal(12, adjustment.CurrentMp);
        ui.Handle(UiInput.Down); ui.Handle(UiInput.Down); ui.Handle(UiInput.Confirm);
        Equal(ScreenMode.Targets, ui.Mode);
        Equal("FIREBALL > CHOOSE TARGET", ui.Breadcrumb);
        ui.Handle(UiInput.Confirm);
        Equal(ScreenMode.Messages, ui.Mode);
        Equal(8, ui.Session.View.Hero.Mp);
        Equal(25, ui.Session.View.Enemies[0].Hp);
        Check(ui.Session.View.Hero.Hp < 80, "living enemies answer the cast");

        var events = ui.Session.Events.Skip(firstEvent).ToArray();
        Equal(1, events.Count(e => e.Kind == "ManaChanged" && e.Source == 0 && e.Target == 0 && e.Amount == -4));
        var playerAction = Array.FindIndex(events, e => e.Kind == "ActionStarted" && e.Source == 0 && e.Detail == PrototypeMagic.Fireball.Id);
        var playerDamage = Array.FindIndex(events, e => e.Kind == "Damaged" && e.Source == 0 && e.Target == 1);
        var enemyAction = Array.FindIndex(events, e => e.Kind == "ActionStarted" && e.Source != 0);
        Check(playerAction >= 0 && playerAction < playerDamage && playerDamage < enemyAction,
            "Fireball resolves before the ordinary enemy response loop");
        Check(ui.Session.LastMessages.Contains("Adventurer casts Fireball on Goblin!"), "domain spell name is visible");
        Check(ui.Session.LastMessages.Contains("Size 1.00 | Output 1.00 | MP 4"), "configuration and exact cost are visible");
    }),
    ("Unaffordable Cast returns to the same intact adjustment without spending a turn", () =>
    {
        var player = new CharacterPreparation(Scenario.Setup(Scenario.GoldenSeed).Actors[0].InitialStats, mp: 3);
        var session = new BattleSession(player.BeginBattle(Scenario.Setup(Scenario.GoldenSeed)));
        var ui = new HarnessController(player, session);
        var beforeLog = session.MachineText;
        var before = session.View;
        Choose(ui, "MAGIC"); Choose(ui, "CHANTLESS"); Choose(ui, "ELEMENTAL MAGIC"); Choose(ui, "Fire");
        ui.Handle(UiInput.Right);
        Equal(5, ui.MagicAdjustment!.SizeSteps);
        ui.Handle(UiInput.Down); ui.Handle(UiInput.Down); ui.Handle(UiInput.Confirm);
        Equal(ScreenMode.Messages, ui.Mode);
        Check(ui.BattleLines.Contains("Not enough MP."), "visible MP failure");
        Equal(beforeLog, session.MachineText);
        Equal(before.Hero, session.View.Hero);
        Check(before.Enemies.SequenceEqual(session.View.Enemies), "enemy state is unchanged");
        Equal(before.Finished, session.View.Finished);
        Equal(new ChantlessMagicConfiguration(PrototypeMagic.Fireball, 4, 4),
            player.LastUsedChantlessMagic(PrototypeMagic.Fireball));
        DismissMessages(ui);
        Equal(ScreenMode.MagicAdjustment, ui.Mode);
        Equal(5, ui.MagicAdjustment!.SizeSteps);
        Equal(2, ui.MagicAdjustment.SelectedIndex);
        Equal(5, ui.MagicAdjustment.MpCost);
        ui.Handle(UiInput.Up); ui.Handle(UiInput.Left);
        Equal(3, ui.MagicAdjustment.OutputSteps);
        Equal(4, ui.MagicAdjustment.MpCost);
        ui.Handle(UiInput.Up); ui.Handle(UiInput.Left); ui.Handle(UiInput.Left);
        Equal(3, ui.MagicAdjustment.SizeSteps); Equal(3, ui.MagicAdjustment.OutputSteps);
        Equal(3, ui.MagicAdjustment.MpCost);
        ui.Handle(UiInput.Down); ui.Handle(UiInput.Down); ui.Handle(UiInput.Confirm);
        Equal(ScreenMode.Targets, ui.Mode);
    }),
    ("Battle adjustment uses exact quarter steps and Escape discards only its draft", () =>
    {
        var ui = Started();
        Choose(ui, "MAGIC"); Choose(ui, "CHANTLESS"); Choose(ui, "ELEMENTAL MAGIC"); Choose(ui, "Fire");
        Equal(ScreenMode.MagicAdjustment, ui.Mode);
        ui.Handle(UiInput.Right); Equal(5, ui.MagicAdjustment!.SizeSteps); Equal("1.25", ui.MagicAdjustment.SizeMultiplier);
        ui.Handle(UiInput.Right); Equal(6, ui.MagicAdjustment.SizeSteps); Equal("1.50", ui.MagicAdjustment.SizeMultiplier);
        ui.Handle(UiInput.Left); Equal(5, ui.MagicAdjustment.SizeSteps); Equal("1.25", ui.MagicAdjustment.SizeMultiplier);
        for (var i = 0; i < 30; i++) ui.Handle(UiInput.Left);
        Equal(1, ui.MagicAdjustment.SizeSteps); Equal("0.25", ui.MagicAdjustment.SizeMultiplier);
        for (var i = 0; i < 30; i++) ui.Handle(UiInput.Right);
        Equal(16, ui.MagicAdjustment.SizeSteps); Equal("4.00", ui.MagicAdjustment.SizeMultiplier);
        ui.Handle(UiInput.Down); ui.Handle(UiInput.Right);
        Equal(5, ui.MagicAdjustment.OutputSteps); Equal("1.25", ui.MagicAdjustment.OutputMultiplier);
        ui.Handle(UiInput.Back);
        Equal(ScreenMode.Menu, ui.Mode);
        Equal("Fire", ui.Menu.CurrentEntries[ui.Menu.SelectedIndex].Label);
        Choose(ui, "Fire");
        Equal(4, ui.MagicAdjustment!.SizeSteps); Equal(4, ui.MagicAdjustment.OutputSteps);
    }),
    ("Fireball target cancellation returns to the intact adjustment without MP or a turn", () =>
    {
        var ui = Started();
        var beforeLog = ui.Session.MachineText;
        Choose(ui, "MAGIC"); Choose(ui, "CHANTLESS"); Choose(ui, "ELEMENTAL MAGIC"); Choose(ui, "Fire");
        ui.Handle(UiInput.Right); ui.Handle(UiInput.Down); ui.Handle(UiInput.Right);
        Equal(5, ui.MagicAdjustment!.SizeSteps); Equal(5, ui.MagicAdjustment.OutputSteps);
        ui.Handle(UiInput.Down); ui.Handle(UiInput.Confirm);
        Equal(ScreenMode.Targets, ui.Mode);
        ui.Handle(UiInput.Back);
        Equal(ScreenMode.MagicAdjustment, ui.Mode);
        Equal(5, ui.MagicAdjustment!.SizeSteps); Equal(5, ui.MagicAdjustment.OutputSteps);
        Equal(12, ui.Session.View.Hero.Mp);
        Equal(beforeLog, ui.Session.MachineText);
        ui.Handle(UiInput.Back);
        Equal(ScreenMode.Menu, ui.Mode);
        Equal("Fire", ui.Menu.CurrentEntries[ui.Menu.SelectedIndex].Label);
        Equal(new ChantlessMagicConfiguration(PrototypeMagic.Fireball, 4, 4),
            ui.Preparation.LastUsedChantlessMagic(PrototypeMagic.Fireball));
    }),
    ("Only a successful Fireball updates same-battle and later-battle adjustment defaults", () =>
    {
        var player = new CharacterPreparation(new CharacterStats(80, 24, 12, 8, 6, 6, 10));
        var ui = new HarnessController(player,
            new BattleSession(player.BeginBattle(Scenario.Setup(Scenario.GoldenSeed))));
        var changed = new ChantlessMagicConfiguration(PrototypeMagic.Fireball, 7, 10);

        Check(!ui.Session.SubmitFireball(changed, 0), "friendly target rejects the cast");
        Equal(new ChantlessMagicConfiguration(PrototypeMagic.Fireball, 4, 4),
            player.LastUsedChantlessMagic(PrototypeMagic.Fireball));

        OpenFireAdjustment(ui);
        for (var i = 0; i < 3; i++) ui.Handle(UiInput.Right);
        ui.Handle(UiInput.Down);
        for (var i = 0; i < 6; i++) ui.Handle(UiInput.Right);
        ui.Handle(UiInput.Back);
        Equal(new ChantlessMagicConfiguration(PrototypeMagic.Fireball, 4, 4),
            player.LastUsedChantlessMagic(PrototypeMagic.Fireball));

        Choose(ui, "Fire");
        for (var i = 0; i < 3; i++) ui.Handle(UiInput.Right);
        ui.Handle(UiInput.Down);
        for (var i = 0; i < 6; i++) ui.Handle(UiInput.Right);
        ui.Handle(UiInput.Down); ui.Handle(UiInput.Confirm);
        ui.Handle(UiInput.Back);
        Equal(new ChantlessMagicConfiguration(PrototypeMagic.Fireball, 4, 4),
            player.LastUsedChantlessMagic(PrototypeMagic.Fireball));

        ui.Handle(UiInput.Down); ui.Handle(UiInput.Confirm);
        Equal(ScreenMode.Targets, ui.Mode);
        ui.Handle(UiInput.Confirm);
        Equal(changed, player.LastUsedChantlessMagic(PrototypeMagic.Fireball));
        DismissMessages(ui);

        OpenFireAdjustment(ui);
        Equal(7, ui.MagicAdjustment!.SizeSteps); Equal(10, ui.MagicAdjustment.OutputSteps);
        ui.Handle(UiInput.Back);
        ui.Handle(UiInput.Back); ui.Handle(UiInput.Back); ui.Handle(UiInput.Back);
        Choose(ui, "RUN"); DismissMessages(ui);
        Equal(ScreenMode.Ended, ui.Mode);
        player.CompleteBattle();

        var later = new HarnessController(player,
            new BattleSession(player.BeginBattle(Scenario.Setup(Scenario.GoldenSeed))));
        OpenFireAdjustment(later);
        Equal(7, later.MagicAdjustment!.SizeSteps); Equal(10, later.MagicAdjustment.OutputSteps);
    }),
    ("Battle adapter uses Output for Fireball damage and Size only for cost", () =>
    {
        var standard = MagicSession(new(PrototypeMagic.Fireball, 4, 4));
        var large = MagicSession(new(PrototypeMagic.Fireball, 16, 4));
        var strong = MagicSession(new(PrototypeMagic.Fireball, 4, 8));
        Check(standard.SubmitFireball(new(PrototypeMagic.Fireball, 4, 4), 1), "standard Fireball accepted");
        Check(large.SubmitFireball(new(PrototypeMagic.Fireball, 16, 4), 1), "large Fireball accepted");
        Check(strong.SubmitFireball(new(PrototypeMagic.Fireball, 4, 8), 1), "strong Fireball accepted");
        var standardDamage = standard.Events.Single(e => e.Kind == "Damaged" && e.Source == 0).Amount;
        var largeDamage = large.Events.Single(e => e.Kind == "Damaged" && e.Source == 0).Amount;
        var strongDamage = strong.Events.Single(e => e.Kind == "Damaged" && e.Source == 0).Amount;
        Equal(standardDamage, largeDamage);
        Check(strongDamage > standardDamage, "Output raises real battle damage");
        Check(large.View.Hero.Mp < standard.View.Hero.Mp, "Size still raises real MP cost");
    }),
    ("All unfinished roots and their remaining leaves stay outside simulation", () =>
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
        Choose(browsed, "MAGIC"); Choose(browsed, "CHANTLESS"); Choose(browsed, "ELEMENTAL MAGIC"); Choose(browsed, "Water");
        browsed.Handle(UiInput.Confirm); browsed.Handle(UiInput.Back); browsed.Handle(UiInput.Back); browsed.Handle(UiInput.Back);
        OpenBasicTarget(browsed); browsed.Handle(UiInput.Confirm);
        var untouched = Started();
        OpenBasicTarget(untouched); untouched.Handle(UiInput.Confirm);
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
        Choose(ui, "MAGIC"); Choose(ui, "CHANTLESS"); Choose(ui, "ELEMENTAL MAGIC");
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
        Equal("CHANTLESS", ui.Menu.CurrentEntries[ui.Menu.SelectedIndex].Label);
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
        Equal(18, ui.Session.View.Hero.EffectiveStats.Strength);
        Equal(10, ui.Session.View.Hero.EffectiveStats.Defense);
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
    ("Preparation change projection keeps the existing seven visible labels", () =>
    {
        var current = new CharacterStats(80, 12, 12, 8, 6, 6, 10);
        var candidate = current.ToBuilder()
            .Set(StatId.MaxHp, 85)
            .Set(StatId.Strength, 15)
            .Set(StatId.PhysicalDefense, 12)
            .Set(StatId.Dexterity, 99)
            .Set(StatId.Reflex, 88)
            .Build();
        Check(PreparationStatProjection.ChangedStats(current, candidate)
            .SequenceEqual(new[]
            {
                "MAXHP 80 > 85",
                "STR 12 > 15",
                "DEF 8 > 12"
            }), "expanded values stay out of the current preparation comparison");
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
        Equal(4, gearedHit - bareHit);
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
        OpenBasicTarget(ui);
        ui.Handle(UiInput.Left); ui.Handle(UiInput.Down); ui.Handle(UiInput.Up);
        Equal(0, ui.TargetIndex);
        ui.Handle(UiInput.Right); Equal(1, ui.TargetIndex);
        ui.Handle(UiInput.Right); Equal(2, ui.TargetIndex);
        ui.Handle(UiInput.Right); Equal(2, ui.TargetIndex);
        ui.Handle(UiInput.Confirm);
        Equal(ScreenMode.PhysicalActions, ui.Mode);
        Equal(2, ui.PhysicalActionIndex);
        Equal(log, ui.Session.MachineText);
        ui.Handle(UiInput.Back);
        Equal(ScreenMode.Menu, ui.Mode);
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
static void OpenPhysical(HarnessController ui)
{
    Choose(ui, "ATTACK");
    Equal(ScreenMode.PhysicalActions, ui.Mode);
}
static void OpenBasicTarget(HarnessController ui)
{
    OpenPhysical(ui);
    ui.Handle(UiInput.Down);
    ui.Handle(UiInput.Down);
    ui.Handle(UiInput.Confirm);
    Equal(ScreenMode.Targets, ui.Mode);
}
static ulong FindTechniqueSeed(PhysicalTechniqueDefinition technique, bool hit)
{
    var chance = CombatStyleRules.HitChanceMillionths(technique, shifted: false);
    for (ulong seed = 0; seed < 100_000; seed++)
    {
        var roll = new DeterministicRng(
            seed, "battle.technique-hit").NextInclusive(999_999);
        if ((roll < chance) == hit) return seed;
    }
    throw new Exception("No deterministic Technique seed found.");
}
static BattleSession MagicSession(ChantlessMagicConfiguration configuration)
{
    return new BattleSession();
}
static void OpenFireAdjustment(HarnessController ui)
{
    Choose(ui, "MAGIC"); Choose(ui, "CHANTLESS"); Choose(ui, "ELEMENTAL MAGIC"); Choose(ui, "Fire");
    Equal(ScreenMode.MagicAdjustment, ui.Mode);
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
