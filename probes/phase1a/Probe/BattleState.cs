using System.Collections.Immutable;
using Phase1A.Rules;

namespace Phase1A.Encounter;

public sealed record ActorSeed(
    string DefinitionId, string? InstanceId, Side Side,
    CharacterStats InitialStats, int? Hp = null, int? Mp = null);
public sealed record EncounterSetup(ImmutableArray<ActorSeed> Actors, ulong Seed);
public enum CommandKind { Ability, Defend, Run }
public sealed record Command(int ActorId, Ability? Ability, int TargetId, CommandKind Kind = CommandKind.Ability);
public enum Outcome { Victory, Defeat, Aborted, Fled }
public sealed record StatusSnapshot(string Id, int FrozenAmount, int RemainingTurns, int SourceId);
public sealed record VitalsChanged(string InstanceId, int Hp, int Mp);
public sealed record EncounterResult(
    Outcome Outcome, ulong Seed, string CodeVersion, string ContentVersion,
    ImmutableArray<VitalsChanged> Deltas, ImmutableArray<BattleEvent> EventLog);
public readonly record struct BattleEvent(
    int Sequence, string Kind, int Source = -1, int Target = -1, string Detail = "-", int Amount = 0, int Value = 0);

public sealed class BattleState : IEffectState
{
    private sealed class Actor(ActorSeed seed)
    {
        // Seed is an immutable value-only input snapshot, never a persistent instance.
        public ActorSeed Seed { get; } = seed;
        public int Hp = seed.Hp ?? seed.InitialStats.MaxHp;
        public int Mp = seed.Mp ?? seed.InitialStats.MaxMp;
        public ActiveStatus? Status;
        public bool Guarding;
    }
    private sealed record ActiveStatus(StatusDef Definition, int FrozenAmount, int RemainingTurns, int SourceId);

    private readonly List<Actor> _actors;
    private readonly List<BattleEvent> _events = [];
    private readonly DeterministicRng _rng;
    private readonly ulong _seed;
    private int _nextActor;
    private EncounterResult? _result;

    public BattleState(EncounterSetup setup)
    {
        ArgumentNullException.ThrowIfNull(setup);
        if (setup.Actors.IsDefault) throw new ArgumentException("Actor snapshots must be initialized.", nameof(setup));
        foreach (var seed in setup.Actors)
        {
            ArgumentNullException.ThrowIfNull(seed);
            seed.InitialStats.Validate();
            if (seed.Hp is { } hp && (hp < 0 || hp > seed.InitialStats.MaxHp))
                throw new ArgumentOutOfRangeException(nameof(seed.Hp), "Initial HP must be within maximum HP.");
            if (seed.Mp is { } mp && (mp < 0 || mp > seed.InitialStats.MaxMp))
                throw new ArgumentOutOfRangeException(nameof(seed.Mp), "Initial MP must be within maximum MP.");
        }
        _seed = setup.Seed;
        _actors = setup.Actors.Select(seed => new Actor(seed)).ToList();
        _rng = new(_seed, "battle.effect");
        Emit("BattleStarted", value: _actors.Count);
        for (var i = 0; i < _actors.Count; i++)
        {
            var actor = _actors[i];
            Emit("ActorJoined", target: i, detail: actor.Seed.DefinitionId, amount: actor.Hp, value: actor.Mp);
        }
        _nextActor = _actors.FindIndex(actor => actor.Hp > 0);
    }
    public int NextActorId => !IsFinished ? _nextActor : -1;
    public bool IsFinished => _result is not null || !Alive(Side.Adventurers) || !Alive(Side.Monsters);
    public ImmutableArray<BattleEvent> Events => [.. _events];
    private bool Alive(Side side) => _actors.Any(actor => actor.Seed.Side == side && actor.Hp > 0);
    public ActorSnapshot Read(int actorId)
    {
        var actor = _actors[actorId];
        var weakness = actor.Status?.FrozenAmount ?? 0;
        return new(actorId, actor.Seed.Side, actor.Hp, actor.Mp,
            StatResolver.Resolve(actor.Seed.InitialStats, weakness), actor.Guarding);
    }
    public StatusSnapshot? ReadStatus(int actorId) => _actors[actorId].Status is { } status
        ? new(status.Definition.Id, status.FrozenAmount, status.RemainingTurns, status.SourceId) : null;
    public ImmutableArray<int> LivingEnemies(int casterId)
    {
        var result = ImmutableArray.CreateBuilder<int>();
        for (var i = 0; i < _actors.Count; i++)
            if (_actors[i].Hp > 0 && _actors[i].Seed.Side != _actors[casterId].Seed.Side) result.Add(i);
        return result.ToImmutable();
    }
    public bool TakeTurn(Command command)
    {
        if (IsFinished) return false;
        var detail = command.Ability?.Id ?? command.Kind.ToString();
        var valid = command.ActorId == _nextActor && (command.Kind switch
        {
            CommandKind.Ability => command.Ability is { } ability &&
                command.TargetId >= 0 && command.TargetId < _actors.Count &&
                _actors[command.TargetId].Hp > 0 && _actors[command.ActorId].Mp >= ability.ManaCost,
            CommandKind.Defend or CommandKind.Run => command.Ability is null,
            _ => false
        });
        if (!valid)
        {
            Emit("CommandRejected", command.ActorId, command.TargetId, detail);
            return false;
        }
        var actor = _actors[command.ActorId];
        if (actor.Guarding)
        {
            actor.Guarding = false;
            Emit("GuardEnded", command.ActorId, command.ActorId);
        }
        Emit("ActionStarted", command.ActorId, command.TargetId, detail);
        if (command.Kind == CommandKind.Run)
        {
            Emit("Fled", command.ActorId);
            Seal(Outcome.Fled); // Guaranteed prototype escape; no enemy response or later mutations.
            return true;
        }
        if (command.Kind == CommandKind.Defend)
        {
            actor.Guarding = true;
            Emit("Defended", command.ActorId, command.ActorId);
        }
        else
        {
            var ability = command.Ability!; // Validated before committing the action.
            if (ability.ManaCost > 0) ChangeMp(command.ActorId, -ability.ManaCost, command.ActorId);
            EffectRunner.Apply(this, command.ActorId, command.TargetId, ability.Effects, _rng);
        }
        TickStatus(command.ActorId);
        Emit("TurnEnded", command.ActorId);
        if (!IsFinished)
        {
            do { _nextActor = (_nextActor + 1) % _actors.Count; } while (_actors[_nextActor].Hp == 0);
        }
        return true;
    }
    public void ChangeHp(int actorId, int delta, int sourceId, string cause)
    {
        EnsureOpen();
        var actor = _actors[actorId];
        var previous = actor.Hp;
        var next = checked(previous + delta);
        if (next < 0 || next > Read(actorId).MaxHp) throw new InvalidOperationException("HP outside bounds.");
        actor.Hp = next;
        Emit(cause, sourceId, actorId, amount: Math.Abs(delta), value: next);
        if (previous > 0 && next == 0) Emit("Died", sourceId, actorId, actor.Seed.DefinitionId);
    }
    public void ChangeMp(int actorId, int delta, int sourceId)
    {
        EnsureOpen();
        var actor = _actors[actorId];
        var next = checked(actor.Mp + delta);
        if (next < 0 || next > Read(actorId).MaxMp) throw new InvalidOperationException("MP outside bounds.");
        actor.Mp = next;
        Emit("ManaChanged", sourceId, actorId, amount: delta, value: next);
    }
    public void GuardBlocked(int actorId, int amount, int sourceId)
    {
        EnsureOpen();
        Emit("GuardBlocked", sourceId, actorId, amount: amount);
    }
    public void ApplyStatus(int actorId, StatusDef status, int frozenAmount, int sourceId)
    {
        EnsureOpen();
        ArgumentOutOfRangeException.ThrowIfNegative(frozenAmount);
        var actor = _actors[actorId];
        var kind = actor.Status is null ? "StatusApplied" : "StatusRefreshed";
        actor.Status = new(status, frozenAmount, status.Duration, sourceId);
        Emit(kind, sourceId, actorId, status.Id, frozenAmount, status.Duration);
    }
    private void TickStatus(int actorId)
    {
        var actor = _actors[actorId];
        if (actor.Status is not { } status || actor.Hp == 0) return;
        var remaining = status.RemainingTurns - 1;
        Emit("StatusTick", status.SourceId, actorId, status.Definition.Id, value: remaining);
        actor.Status = remaining > 0 ? status with { RemainingTurns = remaining } : null;
        if (remaining == 0) Emit("StatusExpired", status.SourceId, actorId, status.Definition.Id);
    }
    public void Skipped(int casterId, int targetId, OpKind op, OpStatus status)
    {
        EnsureOpen();
        Emit("EffectSkipped", casterId, targetId, $"{op}:{status}");
    }

    public EncounterResult Finish()
    {
        if (_result is not null) return _result;
        var outcome = !Alive(Side.Adventurers) ? Outcome.Defeat : !Alive(Side.Monsters) ? Outcome.Victory : Outcome.Aborted;
        return Seal(outcome);
    }
    private EncounterResult Seal(Outcome outcome)
    {
        Emit("BattleEnded", detail: outcome.ToString());
        var deltas = ImmutableArray.CreateBuilder<VitalsChanged>();
        foreach (var actor in _actors)
            if (actor.Seed.InstanceId is { } id) deltas.Add(new(id, actor.Hp, actor.Mp));
        _result = new(outcome, _seed, "phase1a-stats-1", "literals-stats-1", deltas.ToImmutable(), [.. _events]);
        return _result;
    }
    private void EnsureOpen()
    {
        if (_result is not null) throw new InvalidOperationException("Encounter result already sealed.");
    }
    private void Emit(string kind, int source = -1, int target = -1, string detail = "-", int amount = 0, int value = 0) =>
        _events.Add(new(_events.Count, kind, source, target, detail, amount, value));
}
