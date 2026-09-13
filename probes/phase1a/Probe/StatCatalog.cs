using System.Collections.Immutable;

namespace Phase1A.Rules;

public enum StatId
{
    MaxHp = 0,
    MaxMp,
    Strength,
    Magic,
    Dexterity,
    Speed,
    Endurance,
    Constitution,
    Intelligence,
    Reflex,
    Balance,
    PhysicalDefense,
    MagicalDefense,
    MagicDexterity,
    LegacyAgility,
    Count
}

public enum StatCategory
{
    ResourceCapacity,
    CoreCapability,
    MagicCapability,
    TrainedCapability,
    Compatibility
}

public sealed record StatDefinition(
    StatId Id,
    string StableId,
    string ShortLabel,
    StatCategory Category,
    int Minimum,
    bool CompatibilityOnly = false);

public static class StatCatalog
{
    public static ImmutableArray<StatDefinition> Definitions { get; } =
    [
        new(StatId.MaxHp, "core:stat.max-hp", "MAXHP", StatCategory.ResourceCapacity, 1),
        new(StatId.MaxMp, "core:stat.max-mp", "MAXMP", StatCategory.ResourceCapacity, 0),
        new(StatId.Strength, "core:stat.strength", "STR", StatCategory.CoreCapability, 0),
        new(StatId.Magic, "core:stat.magic", "MAG", StatCategory.MagicCapability, 0),
        new(StatId.Dexterity, "core:stat.dexterity", "DEX", StatCategory.CoreCapability, 0),
        new(StatId.Speed, "core:stat.speed", "SPD", StatCategory.CoreCapability, 0),
        new(StatId.Endurance, "core:stat.endurance", "END", StatCategory.CoreCapability, 0),
        new(StatId.Constitution, "core:stat.constitution", "CON", StatCategory.CoreCapability, 0),
        new(StatId.Intelligence, "core:stat.intelligence", "INT", StatCategory.CoreCapability, 0),
        new(StatId.Reflex, "core:stat.reflex", "RFL", StatCategory.CoreCapability, 0),
        new(StatId.Balance, "core:stat.balance", "BAL", StatCategory.TrainedCapability, 0),
        new(StatId.PhysicalDefense, "core:stat.physical-defense", "PHYDEF", StatCategory.TrainedCapability, 0),
        new(StatId.MagicalDefense, "core:stat.magical-defense", "MGKDEF", StatCategory.TrainedCapability, 0),
        new(StatId.MagicDexterity, "core:stat.magic-dexterity", "MDEX", StatCategory.TrainedCapability, 0),
        new(StatId.LegacyAgility, "core:stat.legacy-agility", "AGI", StatCategory.Compatibility, 0, true)
    ];

    private static readonly IReadOnlyDictionary<string, StatDefinition> ByStableId =
        Definitions.ToDictionary(definition => definition.StableId, StringComparer.Ordinal);

    static StatCatalog()
    {
        var ids = Enum.GetValues<StatId>().Where(id => id != StatId.Count).ToArray();
        if (Definitions.Length != (int)StatId.Count || !Definitions.Select(definition => definition.Id).SequenceEqual(ids))
            throw new InvalidOperationException("Stat catalog must contain every StatId exactly once in numeric order.");
        if (Definitions.Any(definition => string.IsNullOrWhiteSpace(definition.StableId) ||
            string.IsNullOrWhiteSpace(definition.ShortLabel) || definition.Minimum < 0))
            throw new InvalidOperationException("Stat definitions require stable IDs, labels, and nonnegative minima.");
        if (ByStableId.Count != Definitions.Length)
            throw new InvalidOperationException("Stat stable IDs must be unique.");
        if (Definitions.Count(definition => definition.CompatibilityOnly) != 1 ||
            !Definition(StatId.LegacyAgility).CompatibilityOnly)
            throw new InvalidOperationException("LegacyAgility must be the only compatibility-only stat.");
    }

    public static StatDefinition Definition(StatId id)
    {
        var index = (int)id;
        return index >= 0 && index < Definitions.Length
            ? Definitions[index]
            : throw new ArgumentOutOfRangeException(nameof(id));
    }

    public static bool TryFromStableId(string stableId, out StatDefinition definition)
    {
        if (string.IsNullOrWhiteSpace(stableId))
        {
            definition = null!;
            return false;
        }
        return ByStableId.TryGetValue(stableId, out definition!);
    }
}
