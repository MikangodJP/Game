using System.Collections.Immutable;

namespace Phase1A.Rules;

public enum Side { Adventurers, Monsters }
public enum OpKind { Damage, Heal, ApplyStatus, SpendMana }
public enum DamageKind { Prototype, Physical, Magical }
public enum OpStatus { Applied, NoOp, Rejected }
public enum TargetScope { Selected, Self, Enemies, PriorTargets }

public readonly record struct ActorSnapshot(
    int Id, Side Side, int Hp, int Mp, CharacterStats EffectiveStats, bool Guarding = false)
{
    public int MaxHp => EffectiveStats.MaxHp;
    public int MaxMp => EffectiveStats.MaxMp;
    public int Strength => EffectiveStats.Strength;
    public int Defense => EffectiveStats.Defense;
}
public readonly record struct OpResult(OpStatus Status, int Amount = 0, int HitCount = 0, bool Killed = false);
public sealed record StatusDef(string Id, int Duration);
public readonly record struct Selector(TargetScope Scope, int PriorNode = -1);
public readonly record struct Magnitude(
    int Base = 0, double CasterStrength = 0, int PriorNode = -1, double PriorAmount = 0, int Variance = 0);
public sealed record EffectNode(
    OpKind Op, Selector Targets, Magnitude Magnitude,
    bool Required = false, bool AffectsDead = false, StatusDef? Status = null,
    DamageKind DamageKind = DamageKind.Prototype);
public sealed record Ability(string Id, int ManaCost, ImmutableArray<EffectNode> Effects);
public readonly record struct NodeResult(ImmutableArray<int> Targets, OpResult Result);

// Owned by Rules. No reference to the Encounter implementation or persistent state.
public interface IEffectState
{
    ActorSnapshot Read(int actorId);
    ImmutableArray<int> LivingEnemies(int casterId);
    void ChangeHp(int actorId, int delta, int sourceId, string cause);
    void ChangeMp(int actorId, int delta, int sourceId);
    void GuardBlocked(int actorId, int amount, int sourceId);
    void ApplyStatus(int actorId, StatusDef status, int frozenAmount, int sourceId);
    void Skipped(int casterId, int targetId, OpKind op, OpStatus status);
}

public readonly record struct OpContext(ActorSnapshot Caster, ActorSnapshot Target, IEffectState State);

public static class Op
{
    public static OpResult Execute(
        OpContext context,
        EffectNode node,
        int magnitude,
        int physicalDamageScaleMillionths = PhysicalActionMath.OneMillion)
    {
        var target = context.Target;
        int amount;
        switch (node.Op)
        {
            case OpKind.Damage:
                amount = node.DamageKind switch
                {
                    DamageKind.Physical => PhysicalActionMath.ApplyDamageScale(
                        PhysicalDamage.Calculate(magnitude,
                            context.Caster.EffectiveStats,
                            target.EffectiveStats),
                        physicalDamageScaleMillionths),
                    DamageKind.Magical => MagicalDamage.Calculate(magnitude, context.Caster.EffectiveStats, target.EffectiveStats),
                    DamageKind.Prototype => Math.Max(0, magnitude - target.Defense),
                    _ => throw new InvalidOperationException("Unknown probe damage kind.")
                };
                if (target.Guarding && amount > 0)
                {
                    // Disposable guard rule: floor kept post-defense damage, then clamp to HP.
                    // Physical damage keeps its final minimum of 1; legacy prototype damage may reach 0.
                    var kept = Math.Max(node.DamageKind == DamageKind.Physical ? 1 : 0, amount / 2);
                    if (kept < amount) context.State.GuardBlocked(target.Id, amount - kept, context.Caster.Id);
                    amount = kept;
                }
                amount = Math.Min(target.Hp, amount);
                if (amount == 0) return new(OpStatus.NoOp);
                context.State.ChangeHp(target.Id, -amount, context.Caster.Id, "Damaged");
                return new(OpStatus.Applied, amount, 1, amount == target.Hp);
            case OpKind.Heal:
                amount = Math.Min(magnitude, target.MaxHp - target.Hp);
                if (amount == 0) return new(OpStatus.NoOp);
                context.State.ChangeHp(target.Id, amount, context.Caster.Id, "Healed");
                return new(OpStatus.Applied, amount, 1);
            case OpKind.SpendMana:
                if (target.Mp < magnitude) return new(OpStatus.Rejected);
                if (magnitude == 0) return new(OpStatus.NoOp);
                context.State.ChangeMp(target.Id, -magnitude, context.Caster.Id);
                return new(OpStatus.Applied, magnitude, 1);
            case OpKind.ApplyStatus:
                if (node.Status is null) throw new InvalidOperationException("ApplyStatus needs a status literal.");
                if (magnitude == 0) return new(OpStatus.NoOp);
                context.State.ApplyStatus(target.Id, node.Status, magnitude, context.Caster.Id);
                return new(OpStatus.Applied, magnitude, 1);
            default:
                throw new InvalidOperationException("Unknown probe op.");
        }
    }
}

public static class EffectRunner
{
    public static ImmutableArray<NodeResult> Apply(
        IEffectState state, int casterId, int selectedId, ImmutableArray<EffectNode> nodes,
        DeterministicRng rng,
        int physicalDamageScaleMillionths = PhysicalActionMath.OneMillion)
    {
        var caster = state.Read(casterId); // One capture for the entire action.
        var prior = ImmutableArray.CreateBuilder<NodeResult>();
        foreach (var node in nodes)
        {
            var targets = node.Targets.Scope switch
            {
                TargetScope.Selected => ImmutableArray.Create(selectedId),
                TargetScope.Self => ImmutableArray.Create(casterId),
                TargetScope.Enemies => state.LivingEnemies(casterId),
                TargetScope.PriorTargets => Earlier(node.Targets.PriorNode).Targets,
                _ => throw new InvalidOperationException("Unknown probe selector.")
            };
            // All target snapshots belong to this node, after earlier nodes have applied.
            var snapshots = targets.Select(state.Read).ToArray();
            var amount = 0;
            var hits = 0;
            var killed = false;
            var status = OpStatus.NoOp;
            foreach (var target in snapshots)
            {
                var result = new OpResult(OpStatus.NoOp);
                if (target.Hp > 0 || node.AffectsDead)
                {
                    var formula = node.Magnitude;
                    var value = formula.Base + formula.CasterStrength * caster.Strength;
                    if (formula.PriorNode >= 0) value += formula.PriorAmount * Earlier(formula.PriorNode).Result.Amount;
                    if (formula.Variance > 0) value += rng.NextInclusive(formula.Variance);
                    if (!double.IsFinite(value) || value < 0) throw new InvalidOperationException("Invalid magnitude literal.");
                    var resolved = checked((int)Math.Round(value, MidpointRounding.AwayFromZero));
                    result = Op.Execute(new(caster, target, state), node, resolved,
                        physicalDamageScaleMillionths);
                }
                if (result.Status != OpStatus.Applied) state.Skipped(casterId, target.Id, node.Op, result.Status);
                amount = checked(amount + result.Amount);
                hits += result.HitCount;
                killed |= result.Killed;
                if (result.Status == OpStatus.Rejected) status = OpStatus.Rejected;
                else if (result.Status == OpStatus.Applied && status != OpStatus.Rejected) status = OpStatus.Applied;
            }
            prior.Add(new(targets, new(status, amount, hits, killed)));
            if (node.Required && status == OpStatus.Rejected) break;
        }
        return prior.ToImmutable();

        NodeResult Earlier(int index) => index >= 0 && index < prior.Count
            ? prior[index] : throw new InvalidOperationException("Prior reference must point backward in this effect list.");
    }
}

public sealed class DeterministicRng
{
    private ulong _state;
    public DeterministicRng(ulong seed, string stream)
    {
        // FNV-1a over eight little-endian seed bytes followed by UTF-8 stream bytes.
        _state = 14695981039346656037UL;
        for (var i = 0; i < 8; i++) Mix((byte)(seed >> (i * 8)));
        foreach (var value in System.Text.Encoding.UTF8.GetBytes(stream)) Mix(value);
    }
    private void Mix(byte value) => _state = unchecked((_state ^ value) * 1099511628211UL);
    private ulong Next()
    {
        // SplitMix64. Explicit overflow is part of the algorithm, not ambient runtime behavior.
        unchecked
        {
            var value = _state += 0x9E3779B97F4A7C15UL;
            value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
            value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
            return value ^ (value >> 31);
        }
    }
    public int NextInclusive(int maximum)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maximum);
        var bound = (ulong)maximum + 1;
        var threshold = unchecked(0UL - bound) % bound;
        ulong value;
        do { value = Next(); } while (value < threshold);
        return (int)(value % bound);
    }
}
