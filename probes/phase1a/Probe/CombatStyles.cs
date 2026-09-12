using System.Collections.Immutable;
using Phase1A.Rules;

namespace Phase1A.Styles;

public readonly record struct StanceModifiers(
    int StrengthPercent = 0,
    int DefensePercent = 0,
    int ResistancePercent = 0);

public sealed record PhysicalTechniqueDefinition
{
    public string Id { get; }
    public string DisplayName { get; }
    public string OwnerStyleId { get; }
    public int DamagePercent { get; }
    public int AccuracyPercent { get; }
    public Ability BaseAction { get; }

    public PhysicalTechniqueDefinition(
        string id,
        string displayName,
        string ownerStyleId,
        int damagePercent,
        int accuracyPercent,
        Ability baseAction)
    {
        Id = RequireText(id, nameof(id));
        DisplayName = RequireText(displayName, nameof(displayName));
        OwnerStyleId = RequireText(ownerStyleId, nameof(ownerStyleId));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(damagePercent);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(accuracyPercent);
        ArgumentNullException.ThrowIfNull(baseAction);
        DamagePercent = damagePercent;
        AccuracyPercent = accuracyPercent;
        BaseAction = baseAction;
    }

    private static string RequireText(string value, string parameter)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Technique text must not be empty.", parameter);
        return value;
    }
}

public sealed record CombatStyleDefinition
{
    public string Id { get; }
    public string JapaneseName { get; }
    public string EnglishName { get; }
    public string BattleLabel { get; }
    public StanceModifiers Stance { get; }
    public ImmutableArray<PhysicalTechniqueDefinition> Techniques { get; }

    public CombatStyleDefinition(
        string id,
        string japaneseName,
        string englishName,
        string battleLabel,
        StanceModifiers stance,
        ImmutableArray<PhysicalTechniqueDefinition> techniques)
    {
        Id = RequireText(id, nameof(id));
        JapaneseName = RequireText(japaneseName, nameof(japaneseName));
        EnglishName = RequireText(englishName, nameof(englishName));
        BattleLabel = RequireText(battleLabel, nameof(battleLabel));
        CombatStyleRules.ValidateStance(stance);
        if (techniques.IsDefault || techniques.Length != 2)
            throw new ArgumentException(
                "A Combat Style must own exactly two Techniques.", nameof(techniques));
        if (techniques.Any(technique => technique is null))
            throw new ArgumentException("Technique definitions must not be null.", nameof(techniques));
        if (techniques.Any(technique => technique.OwnerStyleId != Id))
            throw new ArgumentException(
                "Every Technique must identify its owning Style.", nameof(techniques));
        if (techniques.Select(technique => technique.Id).Distinct(StringComparer.Ordinal).Count() !=
            techniques.Length)
            throw new ArgumentException("Technique IDs must be unique.", nameof(techniques));
        Stance = stance;
        Techniques = techniques;
    }

    private static string RequireText(string value, string parameter)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("Style text must not be empty.", parameter);
        return value;
    }
}

public sealed record CombatStyleProfile
{
    public ImmutableArray<CombatStyleDefinition> KnownStyles { get; }
    public string PrimaryStyleId { get; }

    public CombatStyleProfile(
        ImmutableArray<CombatStyleDefinition> knownStyles,
        string primaryStyleId)
    {
        if (knownStyles.IsDefaultOrEmpty)
            throw new ArgumentException("At least one known Style is required.", nameof(knownStyles));
        if (knownStyles.Any(style => style is null))
            throw new ArgumentException("Known Style definitions must not be null.", nameof(knownStyles));
        if (knownStyles.Select(style => style.Id).Distinct(StringComparer.Ordinal).Count() !=
            knownStyles.Length)
            throw new ArgumentException("Known Style IDs must be unique.", nameof(knownStyles));
        if (knownStyles.SelectMany(style => style.Techniques)
                .Select(technique => technique.Id)
                .Distinct(StringComparer.Ordinal).Count() !=
            knownStyles.Sum(style => style.Techniques.Length))
            throw new ArgumentException(
                "Known Technique IDs must be globally unique.", nameof(knownStyles));
        if (string.IsNullOrWhiteSpace(primaryStyleId) ||
            !knownStyles.Any(style => style.Id == primaryStyleId))
            throw new ArgumentException(
                "Primary Style must be present in known Styles.", nameof(primaryStyleId));
        KnownStyles = knownStyles;
        PrimaryStyleId = primaryStyleId;
    }

    public CombatStyleDefinition? FindStyle(string id) =>
        KnownStyles.FirstOrDefault(style => style.Id == id);
}

public static class CombatStyleRules
{
    public const int BaseTechniqueHitChanceMillionths = 900_000;

    public static CharacterStats ApplyStance(
        CharacterStats stats,
        StanceModifiers stance,
        bool shifted)
    {
        stats.Validate();
        ValidateStance(stance);
        var result = stats with
        {
            Strength = ApplyModifier(stats.Strength, stance.StrengthPercent, shifted),
            Defense = ApplyModifier(stats.Defense, stance.DefensePercent, shifted),
            Resistance = ApplyModifier(stats.Resistance, stance.ResistancePercent, shifted)
        };
        result.Validate();
        return result;
    }

    public static int HitChanceMillionths(
        PhysicalTechniqueDefinition technique,
        bool shifted)
    {
        ArgumentNullException.ThrowIfNull(technique);
        var chance = checked((long)BaseTechniqueHitChanceMillionths *
            technique.AccuracyPercent * (shifted ? 85 : 100) / 10_000);
        return checked((int)Math.Min(PhysicalActionMath.OneMillion, chance));
    }

    public static int TechniqueDamageScaleMillionths(
        PhysicalTechniqueDefinition technique,
        bool shifted)
    {
        ArgumentNullException.ThrowIfNull(technique);
        return PhysicalActionMath.ScaleFromPercents(
            technique.DamagePercent, shifted);
    }

    internal static void ValidateStance(StanceModifiers stance)
    {
        if (stance.StrengthPercent <= -100 || stance.DefensePercent <= -100 ||
            stance.ResistancePercent <= -100)
            throw new ArgumentOutOfRangeException(
                nameof(stance), "A stance cannot reduce a stat by 100% or more.");
    }

    private static int ApplyModifier(int value, int percent, bool shifted)
    {
        var basisPoints = checked((long)percent *
            (percent > 0 && shifted ? 85 : 100));
        var factor = checked(10_000L + basisPoints);
        var numerator = checked((long)value * factor);
        return checked((int)((numerator + 5_000L) / 10_000L));
    }
}

public static class PrototypeCombatStyles
{
    public static readonly CombatStyleDefinition SwordGod = new(
        "probe:style.sword-god",
        "剣神流",
        "Sword God Style",
        "SWORD GOD",
        new(StrengthPercent: 20, DefensePercent: -20),
        [
            new("probe:technique.sword-god.straight-slash", "STRAIGHT SLASH",
                "probe:style.sword-god", 110, 100,
                PrototypePhysicalActions.BasicAttack),
            new("probe:technique.sword-god.heavy-slash", "HEAVY SLASH",
                "probe:style.sword-god", 125, 85,
                PrototypePhysicalActions.BasicAttack)
        ]);

    public static readonly CombatStyleDefinition WaterGod = new(
        "probe:style.water-god",
        "水神流",
        "Water God Style",
        "WATER GOD",
        new(StrengthPercent: -15, DefensePercent: 20, ResistancePercent: 15),
        [
            new("probe:technique.water-god.steady-cut", "STEADY CUT",
                "probe:style.water-god", 90, 110,
                PrototypePhysicalActions.BasicAttack),
            new("probe:technique.water-god.precise-cut", "PRECISE CUT",
                "probe:style.water-god", 100, 105,
                PrototypePhysicalActions.BasicAttack)
        ]);

    public static readonly CombatStyleDefinition NorthGod = new(
        "probe:style.north-god",
        "北神流",
        "North God Style",
        "NORTH GOD",
        new(StrengthPercent: 10, DefensePercent: -10, ResistancePercent: 10),
        [
            new("probe:technique.north-god.adaptive-cut", "ADAPTIVE CUT",
                "probe:style.north-god", 100, 110,
                PrototypePhysicalActions.BasicAttack),
            new("probe:technique.north-god.risky-cut", "RISKY CUT",
                "probe:style.north-god", 115, 90,
                PrototypePhysicalActions.BasicAttack)
        ]);

    public static readonly ImmutableArray<CombatStyleDefinition> All =
        [SwordGod, WaterGod, NorthGod];

    public static readonly CombatStyleProfile PlayerProfile =
        new(All, SwordGod.Id);
}
