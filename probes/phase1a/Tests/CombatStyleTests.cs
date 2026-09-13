using System.Collections.Immutable;
using Phase1A;
using Phase1A.Encounter;
using Phase1A.Magic;
using Phase1A.Preparation;
using Phase1A.Rules;
using Phase1A.Styles;

internal static class CombatStyleTests
{
    public static readonly (string Name, Action Run)[] All =
    [
        ("Prototype Styles expose the approved identities names ownership and shared physical action", () =>
        {
            Check(PrototypeCombatStyles.All.Select(style => style.Id).SequenceEqual(new[]
            {
                "probe:style.sword-god",
                "probe:style.water-god",
                "probe:style.north-god"
            }), "stable Style IDs");
            Check(PrototypeCombatStyles.All.Select(style => style.JapaneseName)
                .SequenceEqual(new[] { "剣神流", "水神流", "北神流" }),
                "Japanese Style names");
            Check(PrototypeCombatStyles.All.Select(style => style.EnglishName)
                .SequenceEqual(new[] { "Sword God Style", "Water God Style", "North God Style" }),
                "English Style names");
            Check(PrototypeCombatStyles.All.Select(style => style.BattleLabel)
                .SequenceEqual(new[] { "SWORD GOD", "WATER GOD", "NORTH GOD" }),
                "ASCII Battle labels");
            Check(PrototypeCombatStyles.All.All(style => style.Techniques.Length == 2),
                "each Style owns exactly two Techniques");

            var techniques = PrototypeCombatStyles.All
                .SelectMany(style => style.Techniques).ToArray();
            Check(techniques.Select(technique => technique.Id).SequenceEqual(new[]
            {
                "probe:technique.sword-god.straight-slash",
                "probe:technique.sword-god.heavy-slash",
                "probe:technique.water-god.steady-cut",
                "probe:technique.water-god.precise-cut",
                "probe:technique.north-god.adaptive-cut",
                "probe:technique.north-god.risky-cut"
            }), "stable Technique IDs");
            Check(techniques.Select(technique => technique.DisplayName).SequenceEqual(new[]
            {
                "STRAIGHT SLASH", "HEAVY SLASH", "STEADY CUT",
                "PRECISE CUT", "ADAPTIVE CUT", "RISKY CUT"
            }), "Technique display names");
            Check(PrototypeCombatStyles.All.All(style => style.Techniques.All(
                    technique => technique.OwnerStyleId == style.Id)),
                "Technique owner IDs match their Style");
            Check(techniques.All(technique =>
                    ReferenceEquals(technique.BaseAction, Scenario.Strike)),
                "all Techniques reuse BASIC ATTACK");
            Check(ReferenceEquals(Scenario.Strike, PrototypePhysicalActions.BasicAttack),
                "BASIC ATTACK has one authoritative Ability instance");
        }),
        ("Prototype stances and Technique multipliers match the approved values", () =>
        {
            Equal(new StanceModifiers(StrengthPercent: 20, DefensePercent: -20),
                PrototypeCombatStyles.SwordGod.Stance);
            Equal(new StanceModifiers(StrengthPercent: -15, DefensePercent: 20,
                    ResistancePercent: 15),
                PrototypeCombatStyles.WaterGod.Stance);
            Equal(new StanceModifiers(StrengthPercent: 10, DefensePercent: -10,
                    ResistancePercent: 10),
                PrototypeCombatStyles.NorthGod.Stance);
            Check(PrototypeCombatStyles.All.SelectMany(style => style.Techniques)
                .Select(technique => (technique.DamagePercent, technique.AccuracyPercent))
                .SequenceEqual(new[]
                {
                    (110, 100), (125, 85), (90, 110),
                    (100, 105), (100, 110), (115, 90)
                }), "Technique multipliers");
        }),
        ("Style Shift attenuates positive stance magnitudes but keeps negatives whole", () =>
        {
            var stats = new CharacterStats(500, 200, 100, 100, 100, 100, 100);
            Equal(stats.With(StatId.Strength, 120).With(StatId.PhysicalDefense, 80),
                CombatStyleRules.ApplyStance(
                    stats, PrototypeCombatStyles.SwordGod.Stance, shifted: false));
            Equal(stats.With(StatId.Strength, 85)
                    .With(StatId.PhysicalDefense, 120)
                    .With(StatId.MagicalDefense, 115),
                CombatStyleRules.ApplyStance(
                    stats, PrototypeCombatStyles.WaterGod.Stance, shifted: false));
            Equal(stats.With(StatId.Strength, 110)
                    .With(StatId.PhysicalDefense, 90)
                    .With(StatId.MagicalDefense, 110),
                CombatStyleRules.ApplyStance(
                    stats, PrototypeCombatStyles.NorthGod.Stance, shifted: false));
            Equal(stats.With(StatId.Strength, 117).With(StatId.PhysicalDefense, 80),
                CombatStyleRules.ApplyStance(
                    stats, PrototypeCombatStyles.SwordGod.Stance, shifted: true));
            Equal(stats.With(StatId.Strength, 85)
                    .With(StatId.PhysicalDefense, 117)
                    .With(StatId.MagicalDefense, 113),
                CombatStyleRules.ApplyStance(
                    stats, PrototypeCombatStyles.WaterGod.Stance, shifted: true));
            Equal(stats.With(StatId.Strength, 109)
                    .With(StatId.PhysicalDefense, 90)
                    .With(StatId.MagicalDefense, 109),
                CombatStyleRules.ApplyStance(
                    stats, PrototypeCombatStyles.NorthGod.Stance, shifted: true));

            var shiftedWater = CombatStyleRules.ModifiersFor(
                PrototypeCombatStyles.WaterGod, shifted: true);
            Check(shiftedWater.Select(modifier =>
                    (modifier.Stat, modifier.Operation, modifier.Amount,
                        modifier.SourceId)).SequenceEqual(new[]
                {
                    (StatId.Strength, StatModifierOperation.PercentAdd, -1_500,
                        PrototypeCombatStyles.WaterGod.Id),
                    (StatId.PhysicalDefense, StatModifierOperation.PercentAdd, 1_700,
                        PrototypeCombatStyles.WaterGod.Id),
                    (StatId.MagicalDefense, StatModifierOperation.PercentAdd, 1_275,
                        PrototypeCombatStyles.WaterGod.Id)
                }), "Shifted Style modifiers remain typed and frozen");
        }),
        ("Typed stances can address an expanded stat without changing prototype Styles", () =>
        {
            var stance = StanceModifiers.Create(builder =>
                builder.SetBasisPoints(StatId.Reflex, 1_000));
            Equal(1_000, stance[StatId.Reflex]);
            Equal(0, stance[StatId.Strength]);
            var stats = CharacterStats.Create(builder => builder
                .Set(StatId.MaxHp, 100)
                .Set(StatId.Reflex, 10));
            Equal(stats.With(StatId.Reflex, 11),
                CombatStyleRules.ApplyStance(stats, stance, shifted: false));
            Equal(stats.With(StatId.Reflex, 11),
                CombatStyleRules.ApplyStance(stats, stance, shifted: true));
            Equal(3, PrototypeCombatStyles.All.Length);
            Check(PrototypeCombatStyles.All.All(style =>
                    style.Stance[StatId.Reflex] == 0),
                "test-only stance cannot alter production Styles");
        }),
        ("Battle resolves frozen Weakened flats before the active stance percentage", () =>
        {
            var battle = StyledBattle(hero:
                new CharacterStats(500, 20, 100, 100, 100, 100, 100));
            battle.ApplyStatus(0, Scenario.Weakened, 20, 1);
            Equal(96, battle.Read(0).EffectiveStats[StatId.Strength]);
            Equal(64, battle.Read(0).EffectiveStats[StatId.PhysicalDefense]);
            Equal(20, battle.ReadStatus(0)!.FrozenAmount);
        }),
        ("Technique hit chances use exact independent millionth thresholds", () =>
        {
            var techniques = PrototypeCombatStyles.All
                .SelectMany(style => style.Techniques).ToArray();
            Check(techniques.Select(technique =>
                    CombatStyleRules.HitChanceMillionths(technique, shifted: false))
                .SequenceEqual(new[] { 900000, 765000, 990000, 945000, 990000, 810000 }),
                "established Technique chances");
            Check(techniques.Select(technique =>
                    CombatStyleRules.HitChanceMillionths(technique, shifted: true))
                .SequenceEqual(new[] { 765000, 650250, 841500, 803250, 841500, 688500 }),
                "shifted Technique chances");
        }),
        ("Preparation knows all Styles and fixes Sword God as Primary", () =>
        {
            var player = new CharacterPreparation(Scenario.Setup(7).Actors[0].InitialStats);
            Check(player.KnownCombatStyles.SequenceEqual(PrototypeCombatStyles.All),
                "all prototype Styles known");
            Check(ReferenceEquals(PrototypeCombatStyles.SwordGod,
                    player.PrimaryCombatStyle),
                "Sword God is the immutable Primary Style");
            Equal(PrototypeCombatStyles.SwordGod.Id,
                PrototypeCombatStyles.PlayerProfile.PrimaryStyleId);
        }),
        ("Physical scaling combines percentages once and rounds at the damage boundary", () =>
        {
            Equal(1_000_000, PhysicalActionMath.ScaleFromPercents(100, shifted: false));
            Equal(850_000, PhysicalActionMath.ScaleFromPercents(100, shifted: true));
            Equal(1_062_500, CombatStyleRules.TechniqueDamageScaleMillionths(
                PrototypeCombatStyles.SwordGod.Techniques[1], shifted: true));
            Equal(3, PhysicalActionMath.ApplyDamageScale(4, 850_000));
            Equal(18, PhysicalActionMath.ApplyDamageScale(17, 1_062_500));
            Equal(1, PhysicalActionMath.ApplyDamageScale(1, 850_000));
        }),
        ("Malformed Style values are rejected at their public boundaries", () =>
        {
            var sword = PrototypeCombatStyles.SwordGod;
            Throws(() => new CombatStyleProfile([sword, sword], sword.Id));
            Throws(() => new CombatStyleProfile(
                PrototypeCombatStyles.All, "fixture:unknown"));
            Throws(() => CombatStyleRules.ApplyStance(
                new CharacterStats(100, 0, 10, 10, 10, 10, 10),
                new StanceModifiers(DefensePercent: -100), shifted: false));
            Throws(() => new PhysicalTechniqueDefinition(
                "", "Invalid", sword.Id, 100, 100,
                PrototypePhysicalActions.BasicAttack));
            Throws(() => new PhysicalTechniqueDefinition(
                "fixture:invalid", "Invalid", sword.Id, 0, 100,
                PrototypePhysicalActions.BasicAttack));
            Throws(() => new CombatStyleDefinition(
                "fixture:invalid", "Invalid", "Invalid", "INVALID",
                default, [sword.Techniques[0], sword.Techniques[0]]));
        }),
        ("Battle owns immediate free Style changes and preserves the turn-start identity", () =>
        {
            var battle = StyledBattle();
            var initial = battle.ReadCombatStyle(0)!;
            Check(initial.KnownStyles.Select(style => style.Id)
                .SequenceEqual(PrototypeCombatStyles.All.Select(style => style.Id)),
                "Battle snapshots all known Styles");
            Equal(PrototypeCombatStyles.SwordGod.Id, initial.ActiveStyle.Id);
            Equal(PrototypeCombatStyles.SwordGod.Id, initial.TurnStartStyleId);
            Check(!initial.Shifted, "Primary Style starts established");
            Equal(new CharacterStats(500, 20, 120, 80, 100, 100, 100),
                battle.Read(0).EffectiveStats);

            var beforeNoOp = battle.Events;
            Check(battle.TryChangeStyle(0, PrototypeCombatStyles.SwordGod.Id),
                "selecting the active Style succeeds");
            Check(battle.Events.SequenceEqual(beforeNoOp),
                "same-Style selection emits no duplicate event");

            Check(battle.TryChangeStyle(0, PrototypeCombatStyles.WaterGod.Id),
                "Water switch accepted");
            var shifted = battle.ReadCombatStyle(0)!;
            Equal(PrototypeCombatStyles.WaterGod.Id, shifted.ActiveStyle.Id);
            Equal(PrototypeCombatStyles.SwordGod.Id, shifted.TurnStartStyleId);
            Check(shifted.Shifted, "different Active and turn-start IDs shift");
            Equal(new CharacterStats(500, 20, 85, 117, 100, 113, 100),
                battle.Read(0).EffectiveStats);
            var changed = battle.Events.Last();
            Equal("StyleChanged", changed.Kind);
            Equal(0, changed.Source);
            Equal(0, changed.Target);
            Equal(PrototypeCombatStyles.WaterGod.Id, changed.Detail);
            Equal(0, battle.NextActorId);
            Equal(0, battle.Events.Count(e => e.Kind == "ActionStarted"));

            var beforeReject = battle.Events;
            Check(!battle.TryChangeStyle(0, "fixture:unknown"),
                "unknown Style rejected");
            Check(battle.Events.SequenceEqual(beforeReject),
                "unknown Style rejection is mutation-free");
            Check(battle.TryChangeStyle(0, PrototypeCombatStyles.SwordGod.Id),
                "return to turn-start accepted");
            Check(!battle.ReadCombatStyle(0)!.Shifted,
                "return to turn-start removes Shift");
            Equal(2, battle.Events.Count(e => e.Kind == "StyleChanged"));

            var finished = StyledBattle();
            finished.Finish();
            var sealedEvents = finished.Events;
            Check(!finished.TryChangeStyle(0, PrototypeCombatStyles.WaterGod.Id),
                "finished Battle rejects Style changes");
            Check(finished.Events.SequenceEqual(sealedEvents),
                "finished rejection emits no event");
        }),
        ("Shifted stance persists through enemy responses and establishes only on player return", () =>
        {
            var battle = StyledBattle(enemyCount: 2);
            Check(battle.TryChangeStyle(0, PrototypeCombatStyles.WaterGod.Id),
                "shift to Water");
            Check(battle.TakeTurn(new(0, null, -1, CommandKind.Defend)),
                "player commits Defend");
            var offTurnEvents = battle.Events;
            Check(!battle.TryChangeStyle(0, PrototypeCombatStyles.NorthGod.Id),
                "off-turn Style change rejected");
            Check(battle.Events.SequenceEqual(offTurnEvents),
                "off-turn rejection is mutation-free");
            Equal(PrototypeCombatStyles.SwordGod.Id,
                battle.ReadCombatStyle(0)!.TurnStartStyleId);
            Check(battle.ReadCombatStyle(0)!.Shifted,
                "Shift remains during first enemy response");

            Check(battle.TakeTurn(new(1, Scenario.Strike, 0)), "enemy one");
            Check(battle.ReadCombatStyle(0)!.Shifted,
                "Shift remains during second enemy response");
            Check(battle.TakeTurn(new(2, Scenario.Strike, 0)), "enemy two");
            Equal(0, battle.NextActorId);
            var established = battle.ReadCombatStyle(0)!;
            Check(!established.Shifted, "Style establishes when player turn returns");
            Equal(PrototypeCombatStyles.WaterGod.Id, established.TurnStartStyleId);
            Equal(new CharacterStats(500, 20, 85, 120, 100, 115, 100),
                battle.Read(0).EffectiveStats);
        }),
        ("Preparation injects a fresh player Style profile without styling monsters", () =>
        {
            var setup = Scenario.Setup(7);
            var player = new CharacterPreparation(setup.Actors[0].InitialStats);
            var first = player.BeginBattle(setup);
            Equal(PrototypeCombatStyles.SwordGod.Id,
                first.ReadCombatStyle(0)!.ActiveStyle.Id);
            Check(first.ReadCombatStyle(0)!.KnownStyles
                .SequenceEqual(PrototypeCombatStyles.All), "all player Styles copied");
            Equal<CombatStyleSnapshot?>(null, first.ReadCombatStyle(1));
            Equal<CombatStyleSnapshot?>(null, first.ReadCombatStyle(2));
            Equal(new CharacterStats(80, 12, 14, 6, 6, 6, 10),
                first.Read(0).EffectiveStats);

            Check(first.TryChangeStyle(0, PrototypeCombatStyles.WaterGod.Id),
                "first Battle changes Active Style");
            Check(first.TakeTurn(new(0, null, -1, CommandKind.Run)), "leave first Battle");
            player.CompleteBattle();
            var second = player.BeginBattle(Scenario.Setup(8));
            var reset = second.ReadCombatStyle(0)!;
            Equal(PrototypeCombatStyles.SwordGod.Id, reset.ActiveStyle.Id);
            Equal(PrototypeCombatStyles.SwordGod.Id, reset.TurnStartStyleId);
            Check(!reset.Shifted, "Active Style does not persist between Battles");
        }),
        ("Rejected Technique identities and targets preserve both Battle RNG streams", () =>
        {
            var technique = PrototypeCombatStyles.SwordGod.Techniques[0];
            var seed = FindTechniqueSeed(technique, shifted: false, hit: true);
            var rejected = StyledBattle(seed: seed, enemyCount: 2);
            var control = StyledBattle(seed: seed, enemyCount: 2);
            Kill(rejected, 1);
            Kill(control, 1);

            Check(!rejected.TakeTurn(new(0, null, 2, CommandKind.Technique,
                    PrototypeCombatStyles.WaterGod.Techniques[0].Id)),
                "foreign-Style Technique rejected");
            Check(!rejected.TakeTurn(new(0, null, 0, CommandKind.Technique, technique.Id)),
                "friendly Technique target rejected");
            Check(!rejected.TakeTurn(new(0, null, 1, CommandKind.Technique, technique.Id)),
                "dead Technique target rejected");
            Check(!rejected.TakeTurn(new(0, null, 99, CommandKind.Technique, technique.Id)),
                "missing Technique target rejected");
            Equal(0, rejected.NextActorId);
            Equal(4, rejected.Events.Count(e => e.Kind == "CommandRejected"));

            Check(rejected.TakeTurn(new(0, null, 2, CommandKind.Technique, technique.Id)),
                "valid Technique remains available");
            Check(control.TakeTurn(new(0, null, 2, CommandKind.Technique, technique.Id)),
                "control Technique accepted");
            Equal(control.Read(2).Hp, rejected.Read(2).Hp);
            Equal(control.Events.Any(e => e.Kind == "Missed"),
                rejected.Events.Any(e => e.Kind == "Missed"));
            Equal(control.Events.Single(e => e.Kind == "Damaged" && e.Target == 2).Amount,
                rejected.Events.Single(e => e.Kind == "Damaged" && e.Target == 2).Amount);
        }),
        ("Technique hit and miss commit the exact event order without sharing effect RNG", () =>
        {
            var straight = PrototypeCombatStyles.SwordGod.Techniques[0];
            var hit = StyledBattle(seed: FindTechniqueSeed(
                straight, shifted: false, hit: true));
            var hitStart = hit.Events.Length;
            Check(hit.TakeTurn(new(0, null, 1, CommandKind.Technique, straight.Id)),
                "Straight Slash hit accepted");
            var hitEvents = hit.Events.Skip(hitStart).ToArray();
            Check(hitEvents.Select(e => e.Kind).SequenceEqual(
                    new[] { "ActionStarted", "Damaged", "TurnEnded" }),
                "hit event order");
            Equal(straight.Id, hitEvents[0].Detail);

            var heavy = PrototypeCombatStyles.SwordGod.Techniques[1];
            var missSeed = FindMissSeedWithDistinctEffectRolls(heavy);
            var miss = StyledBattle(missSeed.Seed,
                new CharacterStats(500, 20, 0, 0, 0, 0, 0));
            miss.ApplyStatus(0, Scenario.Weakened, 3, 1);
            var enemyHp = miss.Read(1).Hp;
            var missStart = miss.Events.Length;
            Check(miss.TakeTurn(new(0, null, 1, CommandKind.Technique, heavy.Id)),
                "Heavy Slash miss commits");
            var missEvents = miss.Events.Skip(missStart).ToArray();
            Check(missEvents.Select(e => e.Kind).SequenceEqual(
                    new[] { "ActionStarted", "Missed", "StatusTick", "TurnEnded" }),
                "miss event order");
            Equal(heavy.Id, missEvents[0].Detail);
            Equal(heavy.Id, missEvents[1].Detail);
            Equal(enemyHp, miss.Read(1).Hp);
            Equal(1, miss.ReadStatus(0)!.RemainingTurns);
            Equal(1, miss.NextActorId);

            Check(miss.TakeTurn(new(1, Scenario.Strike, 0)), "enemy response");
            Equal(2 + missSeed.FirstEffectRoll,
                miss.Events.Last(e => e.Kind == "Damaged").Amount);
        }),
        ("Techniques reuse BASIC variance and combine direct physical multipliers once", () =>
        {
            var adaptive = PrototypeCombatStyles.NorthGod.Techniques[0];
            var sameSeed = FindTechniqueAndEffectSeed(
                adaptive, shifted: false, effectRoll: 2);
            var technique = StyledBattle(sameSeed,
                new CharacterStats(500, 20, 0, 0, 0, 0, 0),
                primary: PrototypeCombatStyles.NorthGod);
            var basic = StyledBattle(sameSeed,
                new CharacterStats(500, 20, 0, 0, 0, 0, 0),
                primary: PrototypeCombatStyles.NorthGod);
            Check(technique.TakeTurn(new(0, null, 1,
                CommandKind.Technique, adaptive.Id)), "Adaptive Cut");
            Check(basic.TakeTurn(new(0, Scenario.Strike, 1)), "BASIC");
            Equal(basic.Events.Single(e => e.Kind == "Damaged").Amount,
                technique.Events.Single(e => e.Kind == "Damaged").Amount);

            var heavy = PrototypeCombatStyles.SwordGod.Techniques[1];
            var combinedSeed = FindTechniqueAndEffectSeed(
                heavy, shifted: true, effectRoll: 0);
            var shifted = StyledBattle(combinedSeed,
                new CharacterStats(500, 20, 0, 0, 0, 0, 0),
                primary: PrototypeCombatStyles.WaterGod);
            Check(shifted.TryChangeStyle(0, PrototypeCombatStyles.SwordGod.Id),
                "shift from Water to Sword");
            Check(shifted.TakeTurn(new(0, null, 1,
                CommandKind.Technique, heavy.Id)), "shifted Heavy Slash");
            Equal(2, shifted.Events.Single(e => e.Kind == "Damaged").Amount);
        }),
        ("BASIC stays guaranteed but receives shifted physical damage without a hit draw", () =>
        {
            var heavy = PrototypeCombatStyles.SwordGod.Techniques[1];
            var missThenHit = FindTechniqueTransitionSeed(heavy);
            var afterBasic = StyledBattle(missThenHit);
            var afterDefend = StyledBattle(missThenHit);
            Check(afterBasic.TakeTurn(new(0, Scenario.Strike, 1)), "BASIC accepted");
            Check(!afterBasic.Events.Any(e => e.Kind == "Missed"),
                "BASIC has no miss seam");
            Check(afterDefend.TakeTurn(new(0, null, -1, CommandKind.Defend)),
                "control Defend");
            Check(afterBasic.TakeTurn(new(1, Scenario.Strike, 0)), "BASIC enemy response");
            Check(afterDefend.TakeTurn(new(1, Scenario.Strike, 0)), "control enemy response");
            Check(afterBasic.TakeTurn(new(0, null, 1, CommandKind.Technique, heavy.Id)),
                "Technique after BASIC");
            Check(afterDefend.TakeTurn(new(0, null, 1, CommandKind.Technique, heavy.Id)),
                "Technique after Defend");
            Check(afterBasic.Events.Any(e => e.Kind == "Missed" && e.Detail == heavy.Id),
                "first Technique roll is still the seeded miss");
            Equal(afterDefend.Events.Count(e => e.Kind == "Missed"),
                afterBasic.Events.Count(e => e.Kind == "Missed"));

            var shifted = StyledBattle(FindEffectVarianceSeed(2),
                new CharacterStats(500, 20, 0, 0, 0, 0, 999));
            Check(shifted.TryChangeStyle(0, PrototypeCombatStyles.WaterGod.Id),
                "shift BASIC");
            Check(shifted.TakeTurn(new(0, Scenario.Strike, 1)),
                "shifted BASIC accepted");
            Equal(3, shifted.Events.Single(e => e.Kind == "Damaged").Amount);
        }),
        ("Shift scaling ignores magical Prototype and Agility inputs", () =>
        {
            var establishedMagic = StyledBattle();
            var shiftedMagic = StyledBattle();
            Check(shiftedMagic.TryChangeStyle(0, PrototypeCombatStyles.WaterGod.Id),
                "shift for magical comparison");
            var fireball = FireballMagic.CreateAbility(
                new(PrototypeMagic.Fireball, 4, 4));
            Check(establishedMagic.TakeTurn(new(0, fireball, 1)), "established Fireball");
            Check(shiftedMagic.TakeTurn(new(0, fireball, 1)), "shifted Fireball");
            Equal(establishedMagic.Events.Single(e => e.Kind == "Damaged").Amount,
                shiftedMagic.Events.Single(e => e.Kind == "Damaged").Amount);

            var legacy = new Ability("fixture:prototype", 0,
                [new(OpKind.Damage, new(TargetScope.Selected), new(Base: 10))]);
            var establishedLegacy = StyledBattle();
            var shiftedLegacy = StyledBattle();
            Check(shiftedLegacy.TryChangeStyle(0, PrototypeCombatStyles.WaterGod.Id),
                "shift for Prototype comparison");
            Check(establishedLegacy.TakeTurn(new(0, legacy, 1)), "established Prototype");
            Check(shiftedLegacy.TakeTurn(new(0, legacy, 1)), "shifted Prototype");
            Equal(10, establishedLegacy.Events.Single(e => e.Kind == "Damaged").Amount);
            Equal(10, shiftedLegacy.Events.Single(e => e.Kind == "Damaged").Amount);

            var straight = PrototypeCombatStyles.SwordGod.Techniques[0];
            var seed = FindTechniqueSeed(straight, shifted: false, hit: true);
            var unusedBaseline = new CharacterStats(500, 20, 10, 10, 10, 10, 0);
            var unusedAltered = unusedBaseline.ToBuilder()
                .Set(StatId.Dexterity, 101)
                .Set(StatId.Speed, 102)
                .Set(StatId.Endurance, 103)
                .Set(StatId.Constitution, 104)
                .Set(StatId.Intelligence, 105)
                .Set(StatId.Reflex, 106)
                .Set(StatId.Balance, 107)
                .Set(StatId.MagicDexterity, 108)
                .Set(StatId.LegacyAgility, 999)
                .Build();
            var slow = StyledBattle(seed, unusedBaseline);
            var fast = StyledBattle(seed, unusedAltered);
            var slowStart = slow.Events.Length;
            var fastStart = fast.Events.Length;
            Check(slow.TakeTurn(new(0, null, 1, CommandKind.Technique, straight.Id)),
                "slow Technique");
            Check(fast.TakeTurn(new(0, null, 1, CommandKind.Technique, straight.Id)),
                "fast Technique");
            Check(slow.Events.Skip(slowStart).SequenceEqual(fast.Events.Skip(fastStart)),
                "unused expanded stats cannot affect Technique resolution");
            Equal(slow.NextActorId, fast.NextActorId);
        })
    ];

    private static BattleState StyledBattle(
        ulong seed = 7,
        CharacterStats? hero = null,
        CombatStyleDefinition? primary = null,
        int enemyCount = 1)
    {
        var profile = new CombatStyleProfile(
            PrototypeCombatStyles.All,
            (primary ?? PrototypeCombatStyles.SwordGod).Id);
        var actors = new List<ActorSeed>
        {
            new("fixture:hero", "hero", Side.Adventurers,
                hero ?? new CharacterStats(500, 20, 100, 100, 100, 100, 100),
                StyleProfile: profile)
        };
        for (var i = 0; i < enemyCount; i++)
            actors.Add(new($"fixture:enemy-{i}", null, Side.Monsters,
                new CharacterStats(500, 0, 0, 0, 0, 0, 0)));
        return new BattleState(new EncounterSetup([.. actors], seed));
    }

    private static ulong FindTechniqueSeed(
        PhysicalTechniqueDefinition technique,
        bool shifted,
        bool hit)
    {
        var chance = CombatStyleRules.HitChanceMillionths(technique, shifted);
        for (ulong seed = 0; seed < 100_000; seed++)
        {
            var roll = new DeterministicRng(
                seed, "battle.technique-hit").NextInclusive(999_999);
            if ((roll < chance) == hit) return seed;
        }
        throw new Exception("No deterministic Technique seed found.");
    }

    private static (ulong Seed, int FirstEffectRoll) FindMissSeedWithDistinctEffectRolls(
        PhysicalTechniqueDefinition technique)
    {
        var chance = CombatStyleRules.HitChanceMillionths(technique, shifted: false);
        for (ulong seed = 0; seed < 100_000; seed++)
        {
            if (new DeterministicRng(seed, "battle.technique-hit")
                    .NextInclusive(999_999) < chance) continue;
            var effect = new DeterministicRng(seed, "battle.effect");
            var first = effect.NextInclusive(2);
            if (first != effect.NextInclusive(2)) return (seed, first);
        }
        throw new Exception("No Technique miss seed with distinct effect rolls found.");
    }

    private static ulong FindTechniqueAndEffectSeed(
        PhysicalTechniqueDefinition technique,
        bool shifted,
        int effectRoll)
    {
        var chance = CombatStyleRules.HitChanceMillionths(technique, shifted);
        for (ulong seed = 0; seed < 100_000; seed++)
            if (new DeterministicRng(seed, "battle.technique-hit")
                    .NextInclusive(999_999) < chance &&
                new DeterministicRng(seed, "battle.effect").NextInclusive(2) == effectRoll)
                return seed;
        throw new Exception("No matching Technique and effect seed found.");
    }

    private static ulong FindTechniqueTransitionSeed(
        PhysicalTechniqueDefinition technique)
    {
        var chance = CombatStyleRules.HitChanceMillionths(technique, shifted: false);
        for (ulong seed = 0; seed < 100_000; seed++)
        {
            var rng = new DeterministicRng(seed, "battle.technique-hit");
            var firstHits = rng.NextInclusive(999_999) < chance;
            var secondHits = rng.NextInclusive(999_999) < chance;
            if (!firstHits && secondHits) return seed;
        }
        throw new Exception("No miss-then-hit Technique seed found.");
    }

    private static ulong FindEffectVarianceSeed(int expected)
    {
        for (ulong seed = 0; seed < 100_000; seed++)
            if (new DeterministicRng(seed, "battle.effect").NextInclusive(2) == expected)
                return seed;
        throw new Exception("No deterministic effect seed found.");
    }

    private static void Kill(BattleState battle, int actorId) =>
        battle.ChangeHp(actorId, -battle.Read(actorId).Hp, 0, "Damaged");

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Equal<T>(T expected, T actual) => Check(
        EqualityComparer<T>.Default.Equals(expected, actual),
        $"expected {expected}, got {actual}");

    private static void Throws(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is ArgumentException or
            InvalidOperationException or OverflowException) { return; }
        throw new Exception("Expected a rejected Combat Style value.");
    }
}
