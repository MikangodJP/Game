using System.Collections.Immutable;
using Phase1A.Rules;

namespace Phase1A.Styles;

public readonly struct StanceModifiers : IEquatable<StanceModifiers>
{
    private readonly ImmutableArray<int> basisPoints;

    internal StanceModifiers(ImmutableArray<int> basisPoints)
    {
        if (basisPoints.IsDefault || basisPoints.Length != (int)StatId.Count)
            throw new ArgumentException(
                "Stance modifiers require every StatId slot.", nameof(basisPoints));
        this.basisPoints = basisPoints;
        CombatStyleRules.ValidateStance(this);
    }

    public StanceModifiers(
        int StrengthPercent = 0,
        int DefensePercent = 0,
        int ResistancePercent = 0)
    {
        var builder = ImmutableArray.CreateBuilder<int>((int)StatId.Count);
        builder.Count = (int)StatId.Count;
        builder[(int)StatId.Strength] = checked(StrengthPercent * 100);
        builder[(int)StatId.PhysicalDefense] = checked(DefensePercent * 100);
        builder[(int)StatId.MagicalDefense] = checked(ResistancePercent * 100);
        basisPoints = builder.MoveToImmutable();
        CombatStyleRules.ValidateStance(this);
    }

    public int this[StatId id]
    {
        get
        {
            var index = (int)StatCatalog.Definition(id).Id;
            return basisPoints.IsDefault ? 0 : basisPoints[index];
        }
    }

    public int StrengthPercent => this[StatId.Strength] / 100;
    public int DefensePercent => this[StatId.PhysicalDefense] / 100;
    public int ResistancePercent => this[StatId.MagicalDefense] / 100;

    public static StanceModifiers Create(Action<StanceModifierBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new StanceModifierBuilder();
        configure(builder);
        return builder.Build();
    }

    internal ImmutableArray<StatModifier> ToModifiers(
        string sourceId,
        bool shifted)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
            throw new ArgumentException("Style source ID must not be blank.", nameof(sourceId));
        CombatStyleRules.ValidateStance(this);
        var result = ImmutableArray.CreateBuilder<StatModifier>();
        foreach (var definition in StatCatalog.Definitions)
        {
            var amount = this[definition.Id];
            if (amount > 0 && shifted)
                amount = checked((int)((long)amount * 85 / 100));
            if (amount != 0)
                result.Add(new(definition.Id, StatModifierOperation.PercentAdd,
                    amount, sourceId));
        }
        return result.ToImmutable();
    }

    public bool Equals(StanceModifiers other)
    {
        foreach (var definition in StatCatalog.Definitions)
            if (this[definition.Id] != other[definition.Id]) return false;
        return true;
    }

    public override bool Equals(object? obj) =>
        obj is StanceModifiers other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var definition in StatCatalog.Definitions)
            hash.Add(this[definition.Id]);
        return hash.ToHashCode();
    }

    public static bool operator ==(StanceModifiers left, StanceModifiers right) =>
        left.Equals(right);

    public static bool operator !=(StanceModifiers left, StanceModifiers right) =>
        !left.Equals(right);
}

public sealed class StanceModifierBuilder
{
    private readonly int[] basisPoints = new int[(int)StatId.Count];

    public StanceModifierBuilder SetBasisPoints(StatId id, int amount)
    {
        StatCatalog.Definition(id);
        basisPoints[(int)id] = amount;
        return this;
    }

    public StanceModifiers Build() =>
        new(ImmutableArray.CreateRange(basisPoints));
}

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
        return StatResolver.Resolve(new StatResolutionRequest(stats)
        {
            Modifiers = stance.ToModifiers("probe:style.compat", shifted),
            MinimumBehavior = StatMinimumBehavior.Clamp
        });
    }

    public static ImmutableArray<StatModifier> ModifiersFor(
        CombatStyleDefinition style,
        bool shifted)
    {
        ArgumentNullException.ThrowIfNull(style);
        return style.Stance.ToModifiers(style.Id, shifted);
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
        foreach (var definition in StatCatalog.Definitions)
            if (stance[definition.Id] <= -10_000)
                throw new ArgumentOutOfRangeException(
                    nameof(stance),
                    "A stance cannot reduce a stat by 100% or more.");
    }
}

public static class PrototypeCombatStyles
{
    public static readonly CombatStyleDefinition SwordGod = new(
        "probe:style.sword-god",
        "剣神流",
        "Sword God Style",
        "SWORD GOD",
        StanceModifiers.Create(builder => builder
            .SetBasisPoints(StatId.Strength, 2_000)
            .SetBasisPoints(StatId.PhysicalDefense, -2_000)),
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
        StanceModifiers.Create(builder => builder
            .SetBasisPoints(StatId.Strength, -1_500)
            .SetBasisPoints(StatId.PhysicalDefense, 2_000)
            .SetBasisPoints(StatId.MagicalDefense, 1_500)),
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
        StanceModifiers.Create(builder => builder
            .SetBasisPoints(StatId.Strength, 1_000)
            .SetBasisPoints(StatId.PhysicalDefense, -1_000)
            .SetBasisPoints(StatId.MagicalDefense, 1_000)),
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
