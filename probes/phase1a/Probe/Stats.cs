namespace Phase1A.Rules;

// Temporary, closed stat set. Magic, Resistance and Agility have no gameplay consumers yet.
public readonly record struct CharacterStats(
    int MaxHp, int MaxMp, int Strength, int Defense, int Magic, int Resistance, int Agility)
{
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(MaxHp);
        ArgumentOutOfRangeException.ThrowIfNegative(MaxMp);
        ArgumentOutOfRangeException.ThrowIfNegative(Strength);
        ArgumentOutOfRangeException.ThrowIfNegative(Defense);
        ArgumentOutOfRangeException.ThrowIfNegative(Magic);
        ArgumentOutOfRangeException.ThrowIfNegative(Resistance);
        ArgumentOutOfRangeException.ThrowIfNegative(Agility);
    }
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
        return equipped with
        {
            Strength = Math.Max(0, equipped.Strength - existingWeakness),
            Defense = Math.Max(0, equipped.Defense - existingWeakness)
        };
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
