using System.Collections.Immutable;

namespace Phase1A.Rules;

public enum StatModifierOperation
{
    FlatAdd = 200,
    PercentAdd = 300
}

public enum StatMinimumBehavior
{
    Reject,
    Clamp
}

public readonly record struct StatModifier(
    StatId Stat,
    StatModifierOperation Operation,
    int Amount,
    string SourceId,
    int SourcePriority = 0);

public sealed record StatResolutionRequest(CharacterStats StartingStats)
{
    public ImmutableArray<StatModifier> Modifiers { get; init; } = [];
    public required StatMinimumBehavior MinimumBehavior { get; init; }
}
