using System.Collections.Immutable;

namespace Phase1A.Rules;

public enum EquipmentSlot { Weapon, Head, Body, Accessory }

public readonly record struct EquipmentBonuses(
    int MaxHp = 0, int MaxMp = 0, int Strength = 0, int Defense = 0,
    int Magic = 0, int Resistance = 0, int Agility = 0)
{
    public static EquipmentBonuses operator +(EquipmentBonuses a, EquipmentBonuses b) => new(
        checked(a.MaxHp + b.MaxHp), checked(a.MaxMp + b.MaxMp), checked(a.Strength + b.Strength),
        checked(a.Defense + b.Defense), checked(a.Magic + b.Magic),
        checked(a.Resistance + b.Resistance), checked(a.Agility + b.Agility));
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
