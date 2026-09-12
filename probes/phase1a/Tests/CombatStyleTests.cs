using Phase1A;
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
            Equal(stats with { Strength = 120, Defense = 80 },
                CombatStyleRules.ApplyStance(
                    stats, PrototypeCombatStyles.SwordGod.Stance, shifted: false));
            Equal(stats with { Strength = 85, Defense = 120, Resistance = 115 },
                CombatStyleRules.ApplyStance(
                    stats, PrototypeCombatStyles.WaterGod.Stance, shifted: false));
            Equal(stats with { Strength = 110, Defense = 90, Resistance = 110 },
                CombatStyleRules.ApplyStance(
                    stats, PrototypeCombatStyles.NorthGod.Stance, shifted: false));
            Equal(stats with { Strength = 117, Defense = 80 },
                CombatStyleRules.ApplyStance(
                    stats, PrototypeCombatStyles.SwordGod.Stance, shifted: true));
            Equal(stats with { Strength = 85, Defense = 117, Resistance = 113 },
                CombatStyleRules.ApplyStance(
                    stats, PrototypeCombatStyles.WaterGod.Stance, shifted: true));
            Equal(stats with { Strength = 109, Defense = 90, Resistance = 109 },
                CombatStyleRules.ApplyStance(
                    stats, PrototypeCombatStyles.NorthGod.Stance, shifted: true));
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
        })
    ];

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
