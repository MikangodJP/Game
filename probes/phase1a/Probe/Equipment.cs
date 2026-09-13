using System.Collections.Immutable;

namespace Phase1A.Rules;

public enum EquipmentSlot { Weapon, Head, Body, Accessory }

public readonly struct EquipmentBonuses : IEquatable<EquipmentBonuses>
{
    private readonly ImmutableArray<int> values;

    internal EquipmentBonuses(ImmutableArray<int> values)
    {
        if (values.IsDefault || values.Length != (int)StatId.Count)
            throw new ArgumentException("Equipment bonuses require every StatId slot.", nameof(values));
        this.values = values;
    }

    public EquipmentBonuses(
        int MaxHp = 0, int MaxMp = 0, int Strength = 0, int Defense = 0,
        int Magic = 0, int Resistance = 0, int Agility = 0)
    {
        var builder = ImmutableArray.CreateBuilder<int>((int)StatId.Count);
        builder.Count = (int)StatId.Count;
        builder[(int)StatId.MaxHp] = MaxHp;
        builder[(int)StatId.MaxMp] = MaxMp;
        builder[(int)StatId.Strength] = Strength;
        builder[(int)StatId.PhysicalDefense] = Defense;
        builder[(int)StatId.Magic] = Magic;
        builder[(int)StatId.MagicalDefense] = Resistance;
        builder[(int)StatId.LegacyAgility] = Agility;
        values = builder.MoveToImmutable();
    }

    public int this[StatId id]
    {
        get
        {
            var index = (int)StatCatalog.Definition(id).Id;
            return values.IsDefault ? 0 : values[index];
        }
    }

    public int MaxHp => this[StatId.MaxHp];
    public int MaxMp => this[StatId.MaxMp];
    public int Strength => this[StatId.Strength];
    public int Defense => this[StatId.PhysicalDefense];
    public int Magic => this[StatId.Magic];
    public int Resistance => this[StatId.MagicalDefense];
    public int Agility => this[StatId.LegacyAgility];

    public static EquipmentBonuses Create(Action<EquipmentBonusBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new EquipmentBonusBuilder();
        configure(builder);
        return builder.Build();
    }

    internal ImmutableArray<StatModifier> ToModifiers(string sourceId, int sourcePriority = 0)
    {
        if (string.IsNullOrWhiteSpace(sourceId))
            throw new ArgumentException("Equipment source ID must not be blank.", nameof(sourceId));
        var result = ImmutableArray.CreateBuilder<StatModifier>();
        foreach (var definition in StatCatalog.Definitions)
        {
            var amount = this[definition.Id];
            if (amount != 0)
                result.Add(new(definition.Id, StatModifierOperation.FlatAdd,
                    amount, sourceId, sourcePriority));
        }
        return result.ToImmutable();
    }

    public static EquipmentBonuses operator +(EquipmentBonuses a, EquipmentBonuses b)
    {
        var builder = ImmutableArray.CreateBuilder<int>((int)StatId.Count);
        foreach (var definition in StatCatalog.Definitions)
            builder.Add(checked(a[definition.Id] + b[definition.Id]));
        return new(builder.MoveToImmutable());
    }

    public bool Equals(EquipmentBonuses other)
    {
        foreach (var definition in StatCatalog.Definitions)
            if (this[definition.Id] != other[definition.Id]) return false;
        return true;
    }
    public override bool Equals(object? obj) => obj is EquipmentBonuses other && Equals(other);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var definition in StatCatalog.Definitions) hash.Add(this[definition.Id]);
        return hash.ToHashCode();
    }
    public static bool operator ==(EquipmentBonuses left, EquipmentBonuses right) => left.Equals(right);
    public static bool operator !=(EquipmentBonuses left, EquipmentBonuses right) => !left.Equals(right);
}

public sealed class EquipmentBonusBuilder
{
    private readonly int[] values = new int[(int)StatId.Count];

    public EquipmentBonusBuilder Set(StatId id, int amount)
    {
        StatCatalog.Definition(id);
        values[(int)id] = amount;
        return this;
    }

    public EquipmentBonuses Build() => new(ImmutableArray.CreateRange(values));
}

public sealed record EquipmentDefinition(string Id, string DisplayName, EquipmentSlot Slot, EquipmentBonuses Bonuses);

// Immutable selection of definitions, not an inventory or generated-item instance system.
public sealed class EquipmentLoadout
{
    public static ImmutableArray<EquipmentSlot> Slots { get; } = [.. Enum.GetValues<EquipmentSlot>()];
    public static EquipmentLoadout Empty { get; } = new(new EquipmentDefinition?[Slots.Length].ToImmutableArray());
    private readonly ImmutableArray<EquipmentDefinition?> items;

    private EquipmentLoadout(ImmutableArray<EquipmentDefinition?> items) => this.items = items;

    public EquipmentDefinition? Get(EquipmentSlot slot) => items[Index(slot)];

    public EquipmentLoadout With(EquipmentSlot slot, EquipmentDefinition? item)
    {
        var index = Index(slot);
        if (item is not null && item.Slot != slot)
            throw new ArgumentException("Equipment is incompatible with this slot.", nameof(item));
        return new(items.SetItem(index, item));
    }

    public EquipmentBonuses Bonuses
    {
        get
        {
            var sum = new EquipmentBonuses();
            // Always sum in declared numeric slot order, never hash iteration order.
            foreach (var item in items)
                if (item is not null) sum += item.Bonuses;
            return sum;
        }
    }

    public ImmutableArray<StatModifier> Modifiers
    {
        get
        {
            var result = ImmutableArray.CreateBuilder<StatModifier>();
            foreach (var slot in Slots)
            {
                var item = Get(slot);
                if (item is not null)
                    result.AddRange(item.Bonuses.ToModifiers(item.Id, (int)slot));
            }
            return result.ToImmutable();
        }
    }

    private static int Index(EquipmentSlot slot)
    {
        var index = Slots.IndexOf(slot);
        return index >= 0 ? index : throw new ArgumentOutOfRangeException(nameof(slot));
    }
}

public static class PrototypeEquipment
{
    public static readonly EquipmentDefinition WoodenSword = new(
        "prototype:equipment.wooden_sword", "Wooden Sword", EquipmentSlot.Weapon, new(Strength: 3));
    public static readonly EquipmentDefinition LeatherArmor = new(
        "prototype:equipment.leather_armor", "Leather Armor", EquipmentSlot.Body, new(Defense: 4));
    public static readonly EquipmentDefinition ClothCap = new(
        "prototype:equipment.cloth_cap", "Cloth Cap", EquipmentSlot.Head, new(Defense: 1));
    public static readonly EquipmentDefinition CopperCharm = new(
        "prototype:equipment.copper_charm", "Copper Charm", EquipmentSlot.Accessory, new(MaxHp: 5));
    public static ImmutableArray<EquipmentDefinition> Items { get; } = [WoodenSword, ClothCap, LeatherArmor, CopperCharm];
}
