using Phase1A;
using Phase1A.Encounter;
using Phase1A.Preparation;
using Phase1A.Rules;

internal static class EquipmentTests
{
    private static readonly CharacterStats Base = new(80, 12, 12, 8, 6, 6, 10);
    public static readonly (string Name, Action Run)[] All =
    [
        ("Equipment adds exact flat bonuses without changing base stats", () =>
        {
            var character = new CharacterPreparation(Base);
            Check(character.TryEquip(EquipmentSlot.Weapon, PrototypeEquipment.WoodenSword), "sword equipped");
            Check(character.TryEquip(EquipmentSlot.Body, PrototypeEquipment.LeatherArmor), "armor equipped");
            Equal(15, character.EffectiveStats.Strength);
            Equal(12, character.EffectiveStats.Defense);
            Equal(Base, character.BaseStats);
            Check(character.TryEquip(EquipmentSlot.Head, PrototypeEquipment.ClothCap), "cap equipped");
            Check(character.TryEquip(EquipmentSlot.Accessory, PrototypeEquipment.CopperCharm), "charm equipped");
            Equal(Base.With(StatId.MaxHp, 85)
                    .With(StatId.Strength, 15)
                    .With(StatId.PhysicalDefense, 13),
                character.EffectiveStats);
            Equal(80, character.Hp); // Extra maximum HP grants no healing.
        }),
        ("Unequip restores a stat and replacement never stacks the previous slot item", () =>
        {
            var character = new CharacterPreparation(Base);
            var stronger = new EquipmentDefinition("fixture:sword", "Fixture Sword", EquipmentSlot.Weapon, new(Strength: 8));
            Check(character.TryEquip(EquipmentSlot.Weapon, PrototypeEquipment.WoodenSword), "first sword");
            Check(character.TryEquip(EquipmentSlot.Weapon, stronger), "replacement sword");
            Equal(20, character.EffectiveStats.Strength);
            Equal(stronger.Id, character.Loadout.Get(EquipmentSlot.Weapon)!.Id);
            Check(character.TryEquip(EquipmentSlot.Weapon, null), "unequip");
            Equal(Base, character.EffectiveStats);
            Equal<EquipmentDefinition?>(null, character.Loadout.Get(EquipmentSlot.Weapon));
        }),
        ("Wrong-slot equipment and invalid slots cannot change a loadout", () =>
        {
            var character = new CharacterPreparation(Base);
            var before = character.Loadout;
            Check(!character.TryEquip(EquipmentSlot.Body, PrototypeEquipment.WoodenSword), "incompatible slot rejected");
            Check(!character.TryEquip((EquipmentSlot)99, PrototypeEquipment.WoodenSword), "undefined slot rejected");
            Equal(before, character.Loadout);
            Equal(Base, character.EffectiveStats);
            Throws(() => before.With(EquipmentSlot.Head, PrototypeEquipment.LeatherArmor));
        }),
        ("Loadout copies and previews are immutable and slot order is stable", () =>
        {
            var character = new CharacterPreparation(Base, hp: 40, mp: 5);
            var original = character.Loadout;
            Equal(Base.With(StatId.Strength, 15),
                character.Preview(EquipmentSlot.Weapon, PrototypeEquipment.WoodenSword));
            Equal(Base, character.EffectiveStats);
            Equal(40, character.Hp); Equal(5, character.Mp);
            var changed = original.With(EquipmentSlot.Weapon, PrototypeEquipment.WoodenSword);
            Equal<EquipmentDefinition?>(null, original.Get(EquipmentSlot.Weapon));
            Equal(3, changed.Bonuses.Strength);
            Check(EquipmentLoadout.Slots.SequenceEqual(new[] { EquipmentSlot.Weapon, EquipmentSlot.Head, EquipmentSlot.Body, EquipmentSlot.Accessory }), "stable slot order");
            Equal(4, PrototypeEquipment.Items.Length);
            Equal(4, PrototypeEquipment.Items.Select(item => item.Id).Distinct().Count());
        }),
        ("Maximum resource increases do not heal and reductions clamp current values", () =>
        {
            var injured = new CharacterPreparation(Base, hp: 40, mp: 5);
            var raised = new EquipmentDefinition("fixture:resource-plus", "Fixture Resource Plus", EquipmentSlot.Accessory, new(MaxHp: 20, MaxMp: 8));
            Check(injured.TryEquip(EquipmentSlot.Accessory, raised), "raised maximum");
            Equal(100, injured.EffectiveStats.MaxHp); Equal(20, injured.EffectiveStats.MaxMp);
            Equal(40, injured.Hp); Equal(5, injured.Mp);
            Check(injured.TryEquip(EquipmentSlot.Accessory, null), "remove maximum bonus");
            Equal(40, injured.Hp); Equal(5, injured.Mp);
            // A signed flat-bonus fixture exercises lowering maxima; no such item ships.
            var full = new CharacterPreparation(Base);
            var reduced = new EquipmentDefinition("fixture:resource-minus", "Fixture Resource Minus", EquipmentSlot.Accessory, new(MaxHp: -20, MaxMp: -8));
            Check(full.TryEquip(EquipmentSlot.Accessory, reduced), "lower maximum");
            Equal(60, full.Hp); Equal(4, full.Mp);
            Check(full.TryEquip(EquipmentSlot.Accessory, null), "restore original maxima");
            Equal(80, full.EffectiveStats.MaxHp); Equal(12, full.EffectiveStats.MaxMp);
            Equal(60, full.Hp); Equal(4, full.Mp);
        }),
        ("Invalid equipment totals reject atomically without altering current resources", () =>
        {
            var character = new CharacterPreparation(Base);
            var invalid = new EquipmentDefinition("fixture:invalid", "Invalid", EquipmentSlot.Body, new(MaxHp: -80));
            Check(!character.TryEquip(EquipmentSlot.Body, invalid), "nonpositive max HP rejected");
            Equal(Base, character.EffectiveStats);
            Equal(80, character.Hp);
            Equal<EquipmentDefinition?>(null, character.Loadout.Get(EquipmentSlot.Body));
        }),
        ("Battle receives only resolved initial stats and subsequent copies cannot mutate it", () =>
        {
            var character = new CharacterPreparation(Base, hp: 40, mp: 5);
            Check(character.TryEquip(EquipmentSlot.Weapon, PrototypeEquipment.WoodenSword), "sword");
            Check(character.TryEquip(EquipmentSlot.Accessory, PrototypeEquipment.CopperCharm), "charm");
            var template = Scenario.Setup(Scenario.GoldenSeed);
            var battle = character.BeginBattle(template);
            Equal(Base.With(StatId.MaxHp, 85)
                    .With(StatId.Strength, 18)
                    .With(StatId.PhysicalDefense, 6),
                battle.Read(0).EffectiveStats);
            Equal(40, battle.Read(0).Hp); Equal(5, battle.Read(0).Mp);
            Equal(Base, template.Actors[0].InitialStats);
            var separateCopy = character.Loadout.With(EquipmentSlot.Weapon, null);
            Equal(0, separateCopy.Bonuses.Strength);
            var otherCharacter = new CharacterPreparation(Base);
            Check(otherCharacter.TryEquip(EquipmentSlot.Body, PrototypeEquipment.LeatherArmor), "other preparation can change");
            Equal(18, battle.Read(0).Strength);
            Equal(6, battle.Read(0).Defense);
        }),
        ("Active battles reject equip unequip and duplicate starts without an unlock flag", () =>
        {
            var character = new CharacterPreparation(Base);
            Check(character.TryEquip(EquipmentSlot.Weapon, PrototypeEquipment.WoodenSword), "sword");
            var battle = character.BeginBattle(Scenario.Setup(7));
            Check(character.InBattle, "active battle tracked");
            Check(!character.TryEquip(EquipmentSlot.Weapon, null), "active unequip rejected");
            Check(!character.TryEquip(EquipmentSlot.Body, PrototypeEquipment.LeatherArmor), "active equip rejected");
            Throws(() => character.BeginBattle(Scenario.Setup(8)));
            Equal(18, battle.Read(0).Strength);
            Check(Enum.GetNames<CommandKind>().SequenceEqual(
                new[] { "Ability", "Defend", "Run", "Technique" }),
                "Technique is a Battle command, not equipment");
            Check(battle.TakeTurn(new(0, null, -1, CommandKind.Run)), "escape");
            Check(!character.InBattle, "terminal result ends active lock");
            Check(character.TryEquip(EquipmentSlot.Weapon, null), "out-of-battle changes work again");
            Equal(18, battle.Read(0).Strength); // Even a sealed encounter retains its own snapshot.
        }),
        ("Wooden Sword adds four post-stance physical damage at identical seeds before HP caps", () =>
        {
            for (ulong seed = 0; seed < 12; seed++)
                Equal(4, Damage(seed, sword: true, armor: false, incoming: false) - Damage(seed, sword: false, armor: false, incoming: false));
        }),
        ("Leather Armor removes two incoming physical damage at identical seeds", () =>
        {
            for (ulong seed = 0; seed < 12; seed++)
                Equal(2, Damage(seed, sword: false, armor: false, incoming: true) - Damage(seed, sword: false, armor: true, incoming: true));
        }),
        ("Existing Weakened subtracts from equipped effective stats without removing gear", () =>
        {
            var character = new CharacterPreparation(Base);
            Check(character.TryEquip(EquipmentSlot.Weapon, PrototypeEquipment.WoodenSword), "sword");
            Check(character.TryEquip(EquipmentSlot.Body, PrototypeEquipment.LeatherArmor), "armor");
            var battle = character.BeginBattle(Scenario.Setup(7));
            battle.ApplyStatus(0, Scenario.Weakened, 4, 1);
            Equal(13, battle.Read(0).Strength); Equal(6, battle.Read(0).Defense);
            Equal(15, character.EffectiveStats.Strength); Equal(12, character.EffectiveStats.Defense);
            Equal(Base, character.BaseStats);
        })
    ];

    private static int Damage(ulong seed, bool sword, bool armor, bool incoming)
    {
        var character = new CharacterPreparation(Base);
        if (sword) Check(character.TryEquip(EquipmentSlot.Weapon, PrototypeEquipment.WoodenSword), "sword");
        if (armor) Check(character.TryEquip(EquipmentSlot.Body, PrototypeEquipment.LeatherArmor), "armor");
        var battle = character.BeginBattle(Scenario.Setup(seed));
        Check(battle.TakeTurn(new(0, Scenario.Strike, 1)), "hero attack");
        if (!incoming) return battle.Events.Single(e => e.Kind == "Damaged").Amount;
        Check(battle.TakeTurn(new(1, Scenario.Strike, 0)), "goblin attack");
        return battle.Events.Last(e => e.Kind == "Damaged").Amount;
    }
    private static void Throws(Action action)
    {
        try { action(); }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or OverflowException) { return; }
        throw new Exception("Expected a rejected operation.");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Equal<T>(T expected, T actual) => Check(EqualityComparer<T>.Default.Equals(expected, actual), $"expected {expected}, got {actual}");
}
