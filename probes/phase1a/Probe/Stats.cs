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
    public static CharacterStats Resolve(CharacterStats baseStats, int existingWeakness = 0, EquipmentBonuses equipment = default)
    {
        baseStats.Validate();
        ArgumentOutOfRangeException.ThrowIfNegative(existingWeakness);
        var equipped = new CharacterStats(
            checked(baseStats.MaxHp + equipment.MaxHp), checked(baseStats.MaxMp + equipment.MaxMp),
            checked(baseStats.Strength + equipment.Strength), checked(baseStats.Defense + equipment.Defense),
            checked(baseStats.Magic + equipment.Magic), checked(baseStats.Resistance + equipment.Resistance),
            checked(baseStats.Agility + equipment.Agility));
        equipped.Validate();
        // Equipment is already frozen for battle. Preserve the existing frozen Weakened amount.
        return equipped
            .With(StatId.Strength, Math.Max(0, equipped.Strength - existingWeakness))
            .With(StatId.PhysicalDefense, Math.Max(0, equipped.Defense - existingWeakness));
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
