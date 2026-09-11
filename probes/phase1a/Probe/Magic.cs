using System.Collections.Immutable;
using System.Globalization;
using Phase1A.Rules;

namespace Phase1A.Magic;

public static class QuarterStepMultiplier
{
    public const int MinSteps = 1;
    public const int MaxSteps = 16;
    public const int DefaultSteps = 4;

    public static int Clamp(int steps) => Math.Clamp(steps, MinSteps, MaxSteps);

    public static decimal ToDecimal(int steps)
    {
        Validate(steps);
        return steps / 4m;
    }

    public static string Format(int steps) =>
        ToDecimal(steps).ToString("0.00", CultureInfo.InvariantCulture);

    public static int ScaleCeiling(int value, int steps)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        Validate(steps);
        return checked((int)(((long)value * steps + 3) / 4));
    }

    private static void Validate(int steps)
    {
        if (steps is < MinSteps or > MaxSteps)
            throw new ArgumentOutOfRangeException(nameof(steps));
    }
}

public sealed record BaseMagicDefinition
{
    public string Id { get; }
    public string DisplayName { get; }
    public int BaseMpCost { get; }
    public int BaseDamage { get; }

    public BaseMagicDefinition(string id, string displayName, int baseMpCost, int baseDamage)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Base Magic needs an ID.", nameof(id));
        if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("Base Magic needs a display name.", nameof(displayName));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(baseMpCost);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(baseDamage);
        Id = id;
        DisplayName = displayName;
        BaseMpCost = baseMpCost;
        BaseDamage = baseDamage;
    }
}

public static class PrototypeMagic
{
    public static BaseMagicDefinition Fireball { get; } =
        new("probe:magic.fireball", "Fireball", baseMpCost: 4, baseDamage: 8);
}

public sealed record ChantlessMagicConfiguration
{
    public BaseMagicDefinition BaseMagic { get; }
    public int SizeSteps { get; }
    public int OutputSteps { get; }

    public ChantlessMagicConfiguration(BaseMagicDefinition baseMagic, int sizeSteps, int outputSteps)
    {
        ArgumentNullException.ThrowIfNull(baseMagic);
        _ = QuarterStepMultiplier.ToDecimal(sizeSteps);
        _ = QuarterStepMultiplier.ToDecimal(outputSteps);
        BaseMagic = baseMagic;
        SizeSteps = sizeSteps;
        OutputSteps = outputSteps;
    }

    public ChantlessMagicConfiguration WithSizeSteps(int steps) =>
        new(BaseMagic, steps, OutputSteps);

    public ChantlessMagicConfiguration WithOutputSteps(int steps) =>
        new(BaseMagic, SizeSteps, steps);
}

public static class ChantlessMagicCost
{
    public static int Calculate(ChantlessMagicConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var numerator = checked((long)configuration.BaseMagic.BaseMpCost
            * configuration.OutputSteps * (4 + configuration.SizeSteps));
        return checked((int)Math.Max(1, (numerator + 31) / 32));
    }
}

public static class FireballMagic
{
    public static Ability CreateAbility(ChantlessMagicConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (configuration.BaseMagic != PrototypeMagic.Fireball)
            throw new ArgumentException("Fireball requires the Fireball Base Magic definition.", nameof(configuration));
        var power = QuarterStepMultiplier.ScaleCeiling(
            configuration.BaseMagic.BaseDamage, configuration.OutputSteps);
        return new(
            configuration.BaseMagic.Id,
            ChantlessMagicCost.Calculate(configuration),
            ImmutableArray.Create(new EffectNode(
                OpKind.Damage,
                new(TargetScope.Selected),
                new(Base: power),
                DamageKind: DamageKind.Magical)));
    }
}
