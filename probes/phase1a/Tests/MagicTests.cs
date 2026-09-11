using System.Collections.Immutable;
using Phase1A;
using Phase1A.Encounter;
using Phase1A.Magic;
using Phase1A.Preparation;
using Phase1A.Rules;

internal static class MagicTests
{
    public static readonly (string Name, Action Run)[] All =
    [
        ("Fireball is the required learned default chantless Base Magic", () =>
        {
            var player = Player();
            Equal(1, player.KnownBaseMagics.Count);
            Equal(PrototypeMagic.Fireball, player.KnownBaseMagics.Single());
            Equal(new ChantlessMagicConfiguration(PrototypeMagic.Fireball, 4, 4),
                player.LastUsedChantlessMagic(PrototypeMagic.Fireball));
            Throws(() => new ChantlessMagicConfiguration(null!, 4, 4));

            var unknown = new BaseMagicDefinition("fixture:magic.unknown", "Unknown", 2, 3);
            Throws(() => player.LastUsedChantlessMagic(unknown));
            var original = player.LastUsedChantlessMagic(PrototypeMagic.Fireball);
            Check(!player.TryRememberSuccessfulChantlessMagic(new(unknown, 5, 6)), "unlearned Base Magic rejected");
            var forged = new BaseMagicDefinition(PrototypeMagic.Fireball.Id, "Fireball", 1, 999);
            Check(!player.TryRememberSuccessfulChantlessMagic(new(forged, 5, 6)),
                "a matching ID cannot replace learned Base Magic data");
            Equal(original, player.LastUsedChantlessMagic(PrototypeMagic.Fireball));
        }),
        ("Quarter steps convert and format every allowed multiplier exactly", () =>
        {
            var displays = new[]
            {
                "0.25", "0.50", "0.75", "1.00", "1.25", "1.50", "1.75", "2.00",
                "2.25", "2.50", "2.75", "3.00", "3.25", "3.50", "3.75", "4.00"
            };
            for (var steps = 1; steps <= 16; steps++)
            {
                Equal(steps / 4m, QuarterStepMultiplier.ToDecimal(steps));
                Equal(displays[steps - 1], QuarterStepMultiplier.Format(steps));
            }
            Equal(4, QuarterStepMultiplier.DefaultSteps);
            Equal(1, QuarterStepMultiplier.Clamp(-100));
            Equal(16, QuarterStepMultiplier.Clamp(100));
            Throws(() => QuarterStepMultiplier.ToDecimal(0));
            Throws(() => QuarterStepMultiplier.Format(17));
        }),
        ("Chantless MP cost matches the exact rational formula", () =>
        {
            Equal(4, Cost(4, 4));
            Equal(6, Cost(8, 4));
            Equal(8, Cost(4, 8));
            Equal(12, Cost(8, 8));
            Equal(1, Cost(1, 1));
            Equal(40, Cost(16, 16));

            for (var output = 1; output <= 16; output++)
                for (var size = 1; size < 16; size++)
                    Check(Cost(size + 1, output) >= Cost(size, output), "Size cost is monotonic");
            for (var size = 1; size <= 16; size++)
                for (var output = 1; output < 16; output++)
                    Check(Cost(size, output + 1) >= Cost(size, output), "Output cost is monotonic");
        }),
        ("Output powers Fireball while Size does not multiply one target", () =>
        {
            var defaultDamage = Damage(new(PrototypeMagic.Fireball, 4, 4));
            var largerDamage = Damage(new(PrototypeMagic.Fireball, 16, 4));
            var strongerDamage = Damage(new(PrototypeMagic.Fireball, 4, 8));
            Equal(11, defaultDamage);
            Equal(defaultDamage, largerDamage);
            Check(strongerDamage > defaultDamage, "higher Output increases Fireball damage");
        }),
        ("Fireball uses existing Magic and Resistance stats", () =>
        {
            var configuration = new ChantlessMagicConfiguration(PrototypeMagic.Fireball, 4, 4);
            Equal(5, Damage(configuration, casterMagic: 0, targetResistance: 6));
            Equal(11, Damage(configuration, casterMagic: 6, targetResistance: 6));
            Equal(8, Damage(configuration, casterMagic: 6, targetResistance: 12));
        }),
        ("The Fireball ability and menu preview share one MP cost", () =>
        {
            var configuration = new ChantlessMagicConfiguration(PrototypeMagic.Fireball, 7, 9);
            var ability = FireballMagic.CreateAbility(configuration);
            Equal("probe:magic.fireball", ability.Id);
            Equal(13, ChantlessMagicCost.Calculate(configuration));
            Equal(13, ability.ManaCost);
            Equal(DamageKind.Magical, ability.Effects.Single().DamageKind);
        }),
        ("Last-used chantless configuration is spell-specific and accepts resolved battle values only", () =>
        {
            var player = Player();
            var changed = new ChantlessMagicConfiguration(PrototypeMagic.Fireball, 5, 6);
            Check(!player.TryRememberSuccessfulChantlessMagic(changed),
                "out-of-battle code cannot forge a successful cast");
            var battle = player.BeginBattle(Scenario.Setup(Scenario.GoldenSeed));
            Check(battle.TakeTurn(new(0, FireballMagic.CreateAbility(changed), 1)),
                "test Fireball resolves before its configuration is recorded");
            Check(player.TryRememberSuccessfulChantlessMagic(changed),
                "an owned active battle may record its resolved chantless cast");
            Equal(changed, player.LastUsedChantlessMagic(PrototypeMagic.Fireball));

            var unknown = new BaseMagicDefinition("fixture:magic.other", "Other", 3, 4);
            Check(!player.TryRememberSuccessfulChantlessMagic(new(unknown, 7, 8)),
                "one spell cannot overwrite another spell's saved values");
            Equal(changed, player.LastUsedChantlessMagic(PrototypeMagic.Fireball));
        })
    ];

    private static CharacterPreparation Player() =>
        new(Scenario.Setup(Scenario.GoldenSeed).Actors[0].InitialStats);

    private static int Cost(int size, int output) => ChantlessMagicCost.Calculate(
        new(PrototypeMagic.Fireball, size, output));

    private static int Damage(
        ChantlessMagicConfiguration configuration,
        int casterMagic = 6,
        int targetResistance = 6)
    {
        var setup = new EncounterSetup(ImmutableArray.Create(
            new ActorSeed("hero", "hero-1", Side.Adventurers, new(80, 100, 0, 0, casterMagic, 0, 0)),
            new ActorSeed("target", null, Side.Monsters, new(200, 0, 0, 0, 0, targetResistance, 0))), 7);
        var battle = new BattleState(setup);
        Check(battle.TakeTurn(new(0, FireballMagic.CreateAbility(configuration), 1)), "Fireball accepted");
        return battle.Events.Single(e => e.Kind == "Damaged").Amount;
    }

    private static void Throws(Action action)
    {
        try { action(); }
        catch (Exception) { return; }
        throw new Exception("expected exception");
    }

    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Equal<T>(T expected, T actual) =>
        Check(EqualityComparer<T>.Default.Equals(expected, actual), $"expected {expected}, got {actual}");
}
