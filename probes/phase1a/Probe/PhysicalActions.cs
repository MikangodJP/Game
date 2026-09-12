using System.Collections.Immutable;

namespace Phase1A.Rules;

public static class PrototypePhysicalActions
{
    public static readonly Ability BasicAttack = new(
        "probe:ability.strike",
        0,
        [new(OpKind.Damage, new(TargetScope.Selected),
            new(Base: 2, Variance: 2), DamageKind: DamageKind.Physical)]);
}

public static class PhysicalActionMath
{
    public const int OneMillion = 1_000_000;

    public static int ScaleFromPercents(int actionPercent, bool shifted)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(actionPercent);
        return checked((int)((long)OneMillion * actionPercent *
            (shifted ? 85 : 100) / 10_000));
    }

    public static int ApplyDamageScale(int damage, int scaleMillionths)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(damage);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scaleMillionths);
        var result = checked(((long)damage * scaleMillionths +
            OneMillion / 2) / OneMillion);
        return checked((int)Math.Max(1, result));
    }
}
