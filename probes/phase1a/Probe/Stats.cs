using System.Collections.Immutable;

namespace Phase1A.Rules;

public readonly struct CharacterStats : IEquatable<CharacterStats>
{
    private readonly ImmutableArray<int> values;

    internal CharacterStats(ImmutableArray<int> values)
    {
        this.values = values;
        Validate();
    }

    // Temporary source-compatibility seam for the original seven-stat callers.
    public CharacterStats(
        int maxHp, int maxMp, int strength, int defense,
        int magic, int resistance, int agility)
    {
        var builder = ImmutableArray.CreateBuilder<int>((int)StatId.Count);
        builder.Count = (int)StatId.Count;
        builder[(int)StatId.MaxHp] = maxHp;
        builder[(int)StatId.MaxMp] = maxMp;
        builder[(int)StatId.Strength] = strength;
        builder[(int)StatId.PhysicalDefense] = defense;
        builder[(int)StatId.Magic] = magic;
        builder[(int)StatId.MagicalDefense] = resistance;
        builder[(int)StatId.LegacyAgility] = agility;
        values = builder.MoveToImmutable();
        Validate();
    }

    public int this[StatId id]
    {
        get
        {
            ValidateStorage();
            return values[(int)StatCatalog.Definition(id).Id];
        }
    }

    public int MaxHp => this[StatId.MaxHp];
    public int MaxMp => this[StatId.MaxMp];
    public int Strength => this[StatId.Strength];
    public int Defense => this[StatId.PhysicalDefense];
    public int Magic => this[StatId.Magic];
    public int Resistance => this[StatId.MagicalDefense];
    public int Agility => this[StatId.LegacyAgility];

    public static CharacterStats Create(Action<CharacterStatsBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new CharacterStatsBuilder();
        configure(builder);
        return builder.Build();
    }

    public CharacterStats With(StatId id, int value)
    {
        Validate();
        var definition = StatCatalog.Definition(id);
        if (value < definition.Minimum)
            throw new ArgumentOutOfRangeException(nameof(value));
        return new(values.SetItem((int)id, value));
    }

    public CharacterStatsBuilder ToBuilder()
    {
        Validate();
        return new(this);
    }

    public void Validate()
    {
        ValidateStorage();
        foreach (var definition in StatCatalog.Definitions)
            if (values[(int)definition.Id] < definition.Minimum)
                throw new ArgumentOutOfRangeException(
                    definition.Id.ToString(),
                    $"{definition.StableId} must be at least {definition.Minimum}.");
    }

    internal ImmutableArray<int> CopyValues()
    {
        Validate();
        return values;
    }

    private void ValidateStorage()
    {
        if (values.IsDefault || values.Length != (int)StatId.Count)
            throw new InvalidOperationException("CharacterStats is not initialized.");
    }

    public bool Equals(CharacterStats other)
    {
        if (values.IsDefault || other.values.IsDefault)
            return values.IsDefault == other.values.IsDefault;
        return values.AsSpan().SequenceEqual(other.values.AsSpan());
    }

    public override bool Equals(object? obj) => obj is CharacterStats other && Equals(other);

    public override int GetHashCode()
    {
        if (values.IsDefault) return 0;
        var hash = new HashCode();
        foreach (var value in values) hash.Add(value);
        return hash.ToHashCode();
    }

    public static bool operator ==(CharacterStats left, CharacterStats right) => left.Equals(right);
    public static bool operator !=(CharacterStats left, CharacterStats right) => !left.Equals(right);

    public override string ToString() => values.IsDefault
        ? "CharacterStats(uninitialized)"
        : $"CharacterStats({string.Join(',', values)})";
}

public sealed class CharacterStatsBuilder
{
    private readonly int[] values = new int[(int)StatId.Count];

    public CharacterStatsBuilder() { }

    internal CharacterStatsBuilder(CharacterStats source) =>
        source.CopyValues().CopyTo(values);

    public CharacterStatsBuilder Set(StatId id, int value)
    {
        StatCatalog.Definition(id);
        values[(int)id] = value;
        return this;
    }

    public CharacterStats Build() => new(ImmutableArray.CreateRange(values));
}

public static class StatResolver
{
    private const long PercentBasis = 10_000;

    public static CharacterStats Resolve(StatResolutionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        request.StartingStats.Validate();
        if (!Enum.IsDefined(request.MinimumBehavior))
            throw new ArgumentOutOfRangeException(nameof(request.MinimumBehavior));

        var modifiers = request.Modifiers.IsDefault
            ? ImmutableArray<StatModifier>.Empty
            : request.Modifiers;
        foreach (var modifier in modifiers)
        {
            StatCatalog.Definition(modifier.Stat);
            if (!Enum.IsDefined(modifier.Operation))
                throw new ArgumentOutOfRangeException(nameof(modifier.Operation));
            if (string.IsNullOrWhiteSpace(modifier.SourceId))
                throw new ArgumentException("Modifier source ID must not be blank.", nameof(request));
        }

        var ordered = modifiers
            .OrderBy(modifier => modifier.Operation)
            .ThenBy(modifier => modifier.SourcePriority)
            .ThenBy(modifier => modifier.SourceId, StringComparer.Ordinal)
            .ThenBy(modifier => modifier.Stat)
            .ToArray();
        var values = request.StartingStats.CopyValues().ToArray();
        var percentages = new long[(int)StatId.Count];

        foreach (var modifier in ordered)
        {
            var index = (int)modifier.Stat;
            switch (modifier.Operation)
            {
                case StatModifierOperation.FlatAdd:
                    values[index] = checked(values[index] + modifier.Amount);
                    break;
                case StatModifierOperation.PercentAdd:
                    percentages[index] = checked(percentages[index] + modifier.Amount);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(modifier.Operation));
            }
        }

        foreach (var definition in StatCatalog.Definitions)
        {
            var index = (int)definition.Id;
            if (percentages[index] != 0)
            {
                var factor = checked(PercentBasis + percentages[index]);
                var numerator = checked((long)values[index] * factor);
                values[index] = checked((int)DivideRoundMidpointAwayFromZero(
                    numerator, PercentBasis));
            }
            if (values[index] >= definition.Minimum) continue;
            if (request.MinimumBehavior == StatMinimumBehavior.Clamp)
                values[index] = definition.Minimum;
            else
                throw new ArgumentOutOfRangeException(
                    nameof(request), $"{definition.StableId} resolved below {definition.Minimum}.");
        }

        return new(ImmutableArray.CreateRange(values));
    }

    public static CharacterStats Resolve(CharacterStats baseStats, int existingWeakness = 0, EquipmentBonuses equipment = default)
    {
        baseStats.Validate();
        ArgumentOutOfRangeException.ThrowIfNegative(existingWeakness);
        var equipped = Resolve(new(baseStats)
        {
            Modifiers = equipment.ToModifiers("probe:compat.equipment"),
            MinimumBehavior = StatMinimumBehavior.Reject
        });
        if (existingWeakness == 0) return equipped;
        return Resolve(new(equipped)
        {
            Modifiers =
            [
                new(StatId.Strength, StatModifierOperation.FlatAdd,
                    -existingWeakness, "probe:status.weakened"),
                new(StatId.PhysicalDefense, StatModifierOperation.FlatAdd,
                    -existingWeakness, "probe:status.weakened")
            ],
            MinimumBehavior = StatMinimumBehavior.Clamp
        });
    }

    private static long DivideRoundMidpointAwayFromZero(long numerator, long denominator)
    {
        var quotient = Math.DivRem(numerator, denominator, out var remainder);
        if (remainder * 2 >= denominator) return checked(quotient + 1);
        if (remainder * 2 <= -denominator) return checked(quotient - 1);
        return quotient;
    }
}

public static class PhysicalDamage
{
    public static int Calculate(int basePower, CharacterStats attacker, CharacterStats defender, int variance = 0)
    {
        // Integral math, with a wider intermediate to prevent silent overflow; no stat cap.
        return checked((int)Math.Max(1L,
            (long)basePower + attacker.Strength - defender.Defense / 2 + variance));
    }
}

public static class MagicalDamage
{
    public static int Calculate(int basePower, CharacterStats caster, CharacterStats target)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(basePower);
        return checked((int)Math.Max(1L,
            (long)basePower + caster.Magic - target.Resistance / 2));
    }
}
