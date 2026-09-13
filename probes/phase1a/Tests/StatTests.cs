using System.Collections.Immutable;
using Phase1A;
using Phase1A.Encounter;
using Phase1A.Rules;

internal static class StatTests
{
    public static readonly (string Name, Action Run)[] All =
    [
        ("Expanded stat catalog is complete stable and isolates legacy Agility", () =>
        {
            var ids = Enum.GetValues<StatId>().Where(id => id != StatId.Count).ToArray();
            Equal(15, ids.Length);
            Check(StatCatalog.Definitions.Select(definition => definition.Id).SequenceEqual(ids),
                "catalog order must match dense enum order");
            Equal(15, StatCatalog.Definitions.Select(definition => definition.StableId)
                .Distinct(StringComparer.Ordinal).Count());
            Equal(StatId.Dexterity,
                StatCatalog.Definition(StatId.Dexterity).Id);
            Check(StatCatalog.TryFromStableId("core:stat.dexterity", out var dexterity) &&
                dexterity.Id == StatId.Dexterity, "stable ID lookup");
            Check(!StatCatalog.TryFromStableId("DEX", out _),
                "display labels must not act as gameplay IDs");
            Check(StatCatalog.Definitions.Single(definition => definition.CompatibilityOnly).Id ==
                StatId.LegacyAgility, "only legacy Agility is compatibility-only");
        }),
        ("Expanded stats build by ID and immutable copies compare structurally", () =>
        {
            var stats = ExpandedStats();
            Equal(100, stats[StatId.MaxHp]);
            Equal(20, stats[StatId.MaxMp]);
            Equal(12, stats[StatId.Strength]);
            Equal(8, stats[StatId.Dexterity]);
            Equal(7, stats[StatId.Speed]);
            Equal(9, stats[StatId.Endurance]);
            Equal(10, stats[StatId.Constitution]);
            Equal(11, stats[StatId.Intelligence]);
            Equal(13, stats[StatId.Reflex]);
            Equal(6, stats[StatId.Balance]);
            Equal(5, stats[StatId.PhysicalDefense]);
            Equal(4, stats[StatId.MagicalDefense]);
            Equal(3, stats[StatId.Magic]);
            Equal(2, stats[StatId.MagicDexterity]);
            Equal(1, stats[StatId.LegacyAgility]);

            var equalCopy = ExpandedStats();
            Equal(stats, equalCopy);
            Equal(stats.GetHashCode(), equalCopy.GetHashCode());
            Check(stats == equalCopy, "equal operators must use all stat values");

            var changed = stats.With(StatId.Dexterity, 99);
            Equal(8, stats[StatId.Dexterity]);
            Equal(99, changed[StatId.Dexterity]);
            Check(stats != changed, "changed immutable copy must not remain equal");
            Equal(stats, stats.ToBuilder().Build());
        }),
        ("Expanded stats reject invalid IDs uninitialized blocks and invalid values", () =>
        {
            var stats = ExpandedStats();
            Throws(() => _ = stats[(StatId)999]);
            Throws(() => _ = stats[StatId.Count]);
            Throws(() => stats.With((StatId)999, 1));
            Throws(() => stats.With(StatId.Count, 1));
            Throws(() => stats.With(StatId.MaxHp, 0));
            Throws(() => stats.With(StatId.Strength, -1));
            Throws(() => default(CharacterStats).Validate());
            Throws(() => CharacterStats.Create(_ => { }));
        }),
        ("Legacy seven-stat construction maps to one canonical expanded block", () =>
        {
            var stats = new CharacterStats(93, 17, 20, 9, 6, 5, 14);
            Equal(93, stats[StatId.MaxHp]);
            Equal(17, stats[StatId.MaxMp]);
            Equal(20, stats[StatId.Strength]);
            Equal(9, stats[StatId.PhysicalDefense]);
            Equal(6, stats[StatId.Magic]);
            Equal(5, stats[StatId.MagicalDefense]);
            Equal(14, stats[StatId.LegacyAgility]);
            Equal(stats[StatId.PhysicalDefense], stats.Defense);
            Equal(stats[StatId.MagicalDefense], stats.Resistance);
            Equal(stats[StatId.LegacyAgility], stats.Agility);
            foreach (var id in new[]
            {
                StatId.Dexterity, StatId.Speed, StatId.Endurance,
                StatId.Constitution, StatId.Intelligence, StatId.Reflex,
                StatId.Balance, StatId.MagicDexterity
            })
                Equal(0, stats[id]);
        }),
        ("PercentAdd entries sum once and never compound within their layer", () =>
        {
            var starting = new CharacterStats(100, 20, 100, 10, 10, 10, 10);
            var modifiers = ImmutableArray.Create(
                new StatModifier(StatId.Strength, StatModifierOperation.PercentAdd,
                    1_000, "fixture:percent-a"),
                new StatModifier(StatId.Strength, StatModifierOperation.PercentAdd,
                    2_000, "fixture:percent-b"));
            var forward = StatResolver.Resolve(new(starting)
            {
                Modifiers = modifiers,
                MinimumBehavior = StatMinimumBehavior.Reject
            });
            var reverse = StatResolver.Resolve(new(starting)
            {
                Modifiers = modifiers.Reverse().ToImmutableArray(),
                MinimumBehavior = StatMinimumBehavior.Reject
            });
            Equal(130, forward.Strength);
            Equal(forward, reverse);
        }),
        ("FlatAdd precedes summed PercentAdd with deterministic midpoint rounding", () =>
        {
            var starting = new CharacterStats(100, 20, 100, 10, 10, 10, 10);
            var resolved = StatResolver.Resolve(new(starting)
            {
                Modifiers =
                [
                    new(StatId.Strength, StatModifierOperation.PercentAdd,
                        5_000, "fixture:percent"),
                    new(StatId.Strength, StatModifierOperation.FlatAdd,
                        20, "fixture:flat")
                ],
                MinimumBehavior = StatMinimumBehavior.Reject
            });
            Equal(180, resolved.Strength);

            var orderedBySource = ImmutableArray.Create(
                new StatModifier(StatId.Strength, StatModifierOperation.FlatAdd,
                    int.MaxValue, "fixture:z"),
                new StatModifier(StatId.Strength, StatModifierOperation.FlatAdd,
                    -1, "fixture:a"));
            Equal(int.MaxValue, StatResolver.Resolve(new(
                new CharacterStats(100, 20, 1, 10, 10, 10, 10))
            {
                Modifiers = orderedBySource,
                MinimumBehavior = StatMinimumBehavior.Reject
            }).Strength);
            Equal(int.MaxValue, StatResolver.Resolve(new(
                new CharacterStats(100, 20, 1, 10, 10, 10, 10))
            {
                Modifiers = orderedBySource.Reverse().ToImmutableArray(),
                MinimumBehavior = StatMinimumBehavior.Reject
            }).Strength);

            var orderedByPriority = ImmutableArray.Create(
                new StatModifier(StatId.Strength, StatModifierOperation.FlatAdd,
                    int.MaxValue, "fixture:a", SourcePriority: 1),
                new StatModifier(StatId.Strength, StatModifierOperation.FlatAdd,
                    -1, "fixture:z", SourcePriority: 0));
            Equal(int.MaxValue, StatResolver.Resolve(new(
                new CharacterStats(100, 20, 1, 10, 10, 10, 10))
            {
                Modifiers = orderedByPriority.Reverse().ToImmutableArray(),
                MinimumBehavior = StatMinimumBehavior.Reject
            }).Strength);

            var midpoint = StatResolver.Resolve(new(
                new CharacterStats(100, 20, 5, 10, 10, 10, 10))
            {
                Modifiers =
                [new(StatId.Strength, StatModifierOperation.PercentAdd,
                    1_000, "fixture:midpoint")],
                MinimumBehavior = StatMinimumBehavior.Reject
            });
            Equal(6, midpoint.Strength);
        }),
        ("Resolution makes reject and clamp boundaries explicit", () =>
        {
            var starting = new CharacterStats(10, 0, 2, 1, 0, 0, 0);
            var modifiers = ImmutableArray.Create(
                new StatModifier(StatId.Strength, StatModifierOperation.FlatAdd,
                    -5, "fixture:penalty"));
            Throws(() => StatResolver.Resolve(new(starting)
            {
                Modifiers = modifiers,
                MinimumBehavior = StatMinimumBehavior.Reject
            }));
            var clamped = StatResolver.Resolve(new(starting)
            {
                Modifiers = modifiers,
                MinimumBehavior = StatMinimumBehavior.Clamp
            });
            Equal(0, clamped.Strength);

            var maximumClamped = StatResolver.Resolve(new(starting)
            {
                Modifiers =
                [new(StatId.MaxHp, StatModifierOperation.FlatAdd,
                    -20, "fixture:max-penalty")],
                MinimumBehavior = StatMinimumBehavior.Clamp
            });
            Equal(1, maximumClamped.MaxHp);
        }),
        ("Resolution rejects malformed modifiers unsupported operations and overflow", () =>
        {
            var starting = new CharacterStats(10, 0, int.MaxValue, 1, 0, 0, 0);
            Throws(() => StatResolver.Resolve(new(starting)
            {
                Modifiers =
                [new(StatId.Strength, StatModifierOperation.FlatAdd, 1, "fixture:overflow")],
                MinimumBehavior = StatMinimumBehavior.Reject
            }));
            Throws(() => StatResolver.Resolve(new(starting)
            {
                Modifiers =
                [new((StatId)999, StatModifierOperation.FlatAdd, 1, "fixture:bad-stat")],
                MinimumBehavior = StatMinimumBehavior.Reject
            }));
            Throws(() => StatResolver.Resolve(new(starting)
            {
                Modifiers =
                [new(StatId.Strength, (StatModifierOperation)999, 1, "fixture:bad-op")],
                MinimumBehavior = StatMinimumBehavior.Reject
            }));
            Throws(() => StatResolver.Resolve(new(starting)
            {
                Modifiers =
                [new(StatId.Strength, StatModifierOperation.FlatAdd, 1, " ")],
                MinimumBehavior = StatMinimumBehavior.Reject
            }));
            Throws(() => StatResolver.Resolve(new(starting)
            {
                MinimumBehavior = (StatMinimumBehavior)999
            }));
            Equal(starting, StatResolver.Resolve(new(starting)
            {
                MinimumBehavior = StatMinimumBehavior.Reject
            }));
        }),
        ("Base stats initialize full HP and MP with all seven effective values", () =>
        {
            var stats = new CharacterStats(93, 17, 20, 9, 6, 5, 14);
            var setup = Setup(stats);
            var state = new BattleState(setup);
            var actor = state.Read(0);
            Equal(stats, actor.EffectiveStats);
            Equal(93, actor.MaxHp); Equal(93, actor.Hp);
            Equal(17, actor.MaxMp); Equal(17, actor.Mp);
            Equal<int?>(null, setup.Actors[0].Hp);
            Equal<int?>(null, setup.Actors[0].Mp);
        }),
        ("Explicit injured vitals survive creation and invalid seed bounds are rejected", () =>
        {
            var setup = Setup(new(30, 5, 10, 2, 0, 0, 0));
            var injured = setup.Actors[0] with { Hp = 12, Mp = 2 };
            var state = new BattleState(setup with { Actors = setup.Actors.SetItem(0, injured) });
            Equal(12, state.Read(0).Hp); Equal(2, state.Read(0).Mp);
            foreach (var invalid in new[]
            {
                injured with { Hp = 31 }, injured with { Hp = -1 },
                injured with { Mp = 6 }, injured with { Mp = -1 }
            })
                Throws(() => new BattleState(setup with { Actors = setup.Actors.SetItem(0, invalid) }));
        }),
        ("Ordinary healing caps current HP at effective MaxHP", () =>
        {
            var setup = Setup(new(93, 17, 20, 9, 6, 5, 14));
            var state = new BattleState(setup with { Actors = setup.Actors.SetItem(0, setup.Actors[0] with { Hp = 90 }) });
            var result = EffectRunner.Apply(state, 0, 1,
                [new(OpKind.Heal, new(TargetScope.Self), new(Base: 100))], new(1, "battle.effect"));
            Equal(93, state.Read(0).Hp);
            Equal(3, result.Single().Result.Amount);
            Equal(3, state.Events.Single(e => e.Kind == "Healed").Amount);
        }),
        ("Existing MP mutation permits bounded restoration and rejects overflow without mutation", () =>
        {
            // No RestoreMana op is introduced for this task. This is the existing state boundary.
            var state = new BattleState(Setup(new(30, 5, 10, 2, 0, 0, 0)));
            state.ChangeMp(0, -3, 0);
            state.ChangeMp(0, 3, 0);
            Equal(5, state.Read(0).Mp);
            var events = state.Events;
            Throws(() => state.ChangeMp(0, 1, 0));
            Equal(5, state.Read(0).Mp);
            Check(events.SequenceEqual(state.Events), "invalid restoration must not append events");
        }),
        ("Ten more Strength adds ten physical damage with identical seeds and inputs", () =>
        {
            for (ulong seed = 0; seed < 16; seed++)
                Equal(10, StrikeDamage(20, 8, seed) - StrikeDamage(10, 8, seed));
        }),
        ("Twenty more Defense removes ten physical damage before the minimum", () =>
        {
            for (ulong seed = 0; seed < 16; seed++)
                Equal(10, StrikeDamage(30, 0, seed) - StrikeDamage(30, 20, seed));
            // Odd Defense uses floor(DEF / 2), not rounding to nearest.
            Equal(StrikeDamage(30, 8, 7), StrikeDamage(30, 9, 7));
            Equal(1, StrikeDamage(30, 9, 7) - StrikeDamage(30, 10, 7));
        }),
        ("Physical damage remains at least one including after Defend", () =>
        {
            for (ulong seed = 0; seed < 8; seed++)
            {
                Equal(1, StrikeDamage(0, 1000, seed));
                var state = new BattleState(Setup(new(30, 0, 0, 1000, 0, 0, 0), new(300, 0, 0, 0, 0, 0, 0), seed));
                Check(state.TakeTurn(new(0, null, -1, CommandKind.Defend)), "guard accepted");
                Check(state.TakeTurn(new(1, Scenario.Strike, 0)), "physical attack accepted");
                Equal(29, state.Read(0).Hp);
                Check(!state.Events.Any(e => e.Kind == "GuardBlocked"), "no zero-value block event");
            }
        }),
        ("Physical Defense mitigation precedes Defend and both precede the HP cap", () =>
        {
            var setup = Setup(new(30, 0, 0, 8, 0, 0, 0), new(300, 0, 10, 0, 0, 0, 0));
            var state = new BattleState(setup with { Actors = setup.Actors.SetItem(0, setup.Actors[0] with { Hp = 7 }) });
            Check(state.TakeTurn(new(0, null, -1, CommandKind.Defend)), "guard accepted");
            var hit = new Ability("fixture:physical", 0,
                [new(OpKind.Damage, new(TargetScope.Selected), new(Base: 5), DamageKind: DamageKind.Physical)]);
            Check(state.TakeTurn(new(1, hit, 0)), "attack accepted");
            // (5 + 10 - floor(8 / 2)) = 11; guard keeps 5; HP 7 -> 2.
            Equal(2, state.Read(0).Hp);
            Equal(5, state.Events.Single(e => e.Kind == "Damaged").Amount);
            Equal(6, state.Events.Single(e => e.Kind == "GuardBlocked").Amount);
        }),
        ("Physical evaluation freezes caster stats while later nodes see target weakness", () =>
        {
            var stats = new CharacterStats(100, 0, 10, 8, 6, 7, 9);
            var setup = Setup(stats);
            var state = new BattleState(setup);
            var before = state.Read(0);
            EffectRunner.Apply(state, 0, 0,
                [new(OpKind.ApplyStatus, new(TargetScope.Self), new(Base: 3), Status: Scenario.Weakened),
                 new(OpKind.Damage, new(TargetScope.Self), new(Base: 2), DamageKind: DamageKind.Physical)], new(7, "battle.effect"));
            // Frozen STR 10, current target DEF 8-3 = 5: 2+10-floor(5/2) = 10.
            Equal(90, state.Read(0).Hp);
            Equal(stats.With(StatId.Strength, 7).With(StatId.PhysicalDefense, 5),
                state.Read(0).EffectiveStats);
            Equal(stats, before.EffectiveStats);
            Equal(stats, setup.Actors[0].InitialStats);
        }),
        ("Magic Resistance and Agility do not alter any event in a complete replay", () =>
        {
            var baseline = Scenario.Run(Scenario.GoldenSeed);
            var setup = Scenario.Setup(Scenario.GoldenSeed);
            var altered = setup with
            {
                Actors = setup.Actors.Select((actor, i) => actor with
                {
                    InitialStats = actor.InitialStats
                        .With(StatId.Magic, 100 + i)
                        .With(StatId.MagicalDefense, 200 + i)
                        .With(StatId.LegacyAgility, 300 - i)
                }).ToImmutableArray()
            };
            var state = new BattleState(altered);
            var abilities = new[] { Scenario.Strike, Scenario.Crush, Scenario.Drain };
            foreach (var action in baseline.EventLog.Where(e => e.Kind == "ActionStarted"))
                Check(state.TakeTurn(new(action.Source, abilities.Single(a => a.Id == action.Detail), action.Target)), "same scheduled command accepted");
            Equal(CanonicalLog.Format(baseline), CanonicalLog.Format(state.Finish()));
            Equal(baseline.Outcome, state.Finish().Outcome);
        }),
        ("Physical classification leaves Drain's prototype path intact", () =>
        {
            Equal(DamageKind.Physical, Scenario.Strike.Effects.Single().DamageKind);
            Equal(DamageKind.Physical, Scenario.Crush.Effects.Single(n => n.Op == OpKind.Damage).DamageKind);
            Equal(DamageKind.Prototype, Scenario.Drain.Effects.Single(n => n.Op == OpKind.Damage).DamageKind);
            // Drain still scales Strength by .7 and subtracts full Defense, without magic stats.
            var state = new BattleState(Setup(new(100, 12, 10, 0, 999, 0, 0), new(300, 0, 0, 4, 0, 999, 0)));
            var variance = new DeterministicRng(7, "battle.effect").NextInclusive(2);
            Check(state.TakeTurn(new(0, Scenario.Drain, 1)), "Drain accepted");
            Equal(4 + 7 + variance - 4, state.Events.Single(e => e.Kind == "Damaged").Amount);
        }),
        ("Physical commands replay identically and result labels pin the new formula", () =>
        {
            var a = Scenario.Run(Scenario.GoldenSeed);
            var b = Scenario.Run(Scenario.GoldenSeed);
            Equal(CanonicalLog.Format(a), CanonicalLog.Format(b));
            Equal("phase1a-stats-1", a.CodeVersion);
            Equal("literals-stats-1", a.ContentVersion);
        })
    ];

    private static EncounterSetup Setup(CharacterStats hero, CharacterStats? enemy = null, ulong seed = 7) => new(
        [new("fixture:hero", "hero", Side.Adventurers, hero),
         new("fixture:enemy", null, Side.Monsters, enemy ?? new(300, 0, 0, 0, 0, 0, 0))], seed);

    private static CharacterStats ExpandedStats() => CharacterStats.Create(builder => builder
        .Set(StatId.MaxHp, 100)
        .Set(StatId.MaxMp, 20)
        .Set(StatId.Strength, 12)
        .Set(StatId.Dexterity, 8)
        .Set(StatId.Speed, 7)
        .Set(StatId.Endurance, 9)
        .Set(StatId.Constitution, 10)
        .Set(StatId.Intelligence, 11)
        .Set(StatId.Reflex, 13)
        .Set(StatId.Balance, 6)
        .Set(StatId.PhysicalDefense, 5)
        .Set(StatId.MagicalDefense, 4)
        .Set(StatId.Magic, 3)
        .Set(StatId.MagicDexterity, 2)
        .Set(StatId.LegacyAgility, 1));

    private static int StrikeDamage(int strength, int defense, ulong seed)
    {
        var state = new BattleState(Setup(new(300, 0, strength, 0, 0, 0, 0), new(300, 0, 0, defense, 0, 0, 0), seed));
        Check(state.TakeTurn(new(0, Scenario.Strike, 1)), "Strike accepted");
        return state.Events.Single(e => e.Kind == "Damaged").Amount;
    }
    private static void Throws(Action action)
    {
        try { action(); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or OverflowException) { return; }
        throw new Exception("Expected a bounds rejection.");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Equal<T>(T expected, T actual) => Check(EqualityComparer<T>.Default.Equals(expected, actual), $"expected {expected}, got {actual}");
}
