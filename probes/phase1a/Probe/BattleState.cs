using System.Collections.Immutable;
using Phase1A.Rules;
using Phase1A.Styles;

namespace Phase1A.Encounter;

public sealed record ActorSeed(
    string DefinitionId, string? InstanceId, Side Side,
    CharacterStats InitialStats, int? Hp = null, int? Mp = null,
    CombatStyleProfile? StyleProfile = null);
public sealed record EncounterSetup(ImmutableArray<ActorSeed> Actors, ulong Seed);
public enum CommandKind { Ability, Defend, Run, Technique }
public sealed record Command(
    int ActorId,
    Ability? Ability,
    int TargetId,
    CommandKind Kind = CommandKind.Ability,
    string? TechniqueId = null);
public enum Outcome { Victory, Defeat, Aborted, Fled }
public sealed record StatusSnapshot(string Id, int FrozenAmount, int RemainingTurns, int SourceId);
public sealed record CombatStyleSnapshot(
    ImmutableArray<CombatStyleDefinition> KnownStyles,
    CombatStyleDefinition ActiveStyle,
    string TurnStartStyleId,
    bool Shifted);
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
        public string? ActiveStyleId = seed.StyleProfile?.PrimaryStyleId;
        public string? TurnStartStyleId = seed.StyleProfile?.PrimaryStyleId;
    }
    private sealed record ActiveStatus(StatusDef Definition, int FrozenAmount, int RemainingTurns, int SourceId);

    private readonly List<Actor> _actors;
    private readonly List<BattleEvent> _events = [];
    private readonly DeterministicRng _rng;
    private readonly DeterministicRng _techniqueHitRng;
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
        _techniqueHitRng = new(_seed, "battle.technique-hit");
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
        var stats = StatResolver.Resolve(actor.Seed.InitialStats, weakness);
        var style = ReadCombatStyle(actorId);
        if (style is not null)
            stats = CombatStyleRules.ApplyStance(
                stats, style.ActiveStyle.Stance, style.Shifted);
        return new(actorId, actor.Seed.Side, actor.Hp, actor.Mp,
            stats, actor.Guarding);
    }
    public CombatStyleSnapshot? ReadCombatStyle(int actorId)
    {
        var actor = _actors[actorId];
        var profile = actor.Seed.StyleProfile;
        if (profile is null) return null;
        var active = profile.FindStyle(actor.ActiveStyleId!) ??
            throw new InvalidOperationException("Active Style is absent from its Battle profile.");
        var turnStart = actor.TurnStartStyleId ??
            throw new InvalidOperationException("Turn-start Style is absent from a styled actor.");
        return new(profile.KnownStyles, active, turnStart, active.Id != turnStart);
    }
    public bool TryChangeStyle(int actorId, string styleId)
    {
        if (IsFinished || actorId != _nextActor ||
            string.IsNullOrWhiteSpace(styleId)) return false;
        var actor = _actors[actorId];
        if (actor.Seed.StyleProfile?.FindStyle(styleId) is null) return false;
        if (actor.ActiveStyleId == styleId) return true;
        actor.ActiveStyleId = styleId;
        Emit("StyleChanged", actorId, actorId, styleId);
        return true;
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
        var currentActor = command.ActorId == _nextActor;
        PhysicalTechniqueDefinition? technique = null;
        if (currentActor && command.Kind == CommandKind.Technique &&
            !string.IsNullOrWhiteSpace(command.TechniqueId))
        {
            technique = ReadCombatStyle(command.ActorId)?.ActiveStyle.Techniques
                .FirstOrDefault(candidate => candidate.Id == command.TechniqueId);
        }
        var resolvedAbility = technique?.BaseAction ?? command.Ability;
        var detail = command.Kind == CommandKind.Technique
            ? command.TechniqueId ?? command.Kind.ToString()
            : command.Ability?.Id ?? command.Kind.ToString();
        var valid = currentActor && (command.Kind switch
        {
            CommandKind.Ability => command.Ability is { } ability &&
                command.TechniqueId is null &&
                command.TargetId >= 0 && command.TargetId < _actors.Count &&
                _actors[command.TargetId].Hp > 0 && _actors[command.ActorId].Mp >= ability.ManaCost,
            CommandKind.Technique => command.Ability is null && technique is not null &&
                LivingEnemies(command.ActorId).Contains(command.TargetId) &&
                _actors[command.ActorId].Mp >= technique.BaseAction.ManaCost,
            CommandKind.Defend or CommandKind.Run =>
                command.Ability is null && command.TechniqueId is null,
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
        detail = technique?.Id ?? resolvedAbility?.Id ?? command.Kind.ToString();
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
            var ability = resolvedAbility!; // Validated before committing the action.
            if (ability.ManaCost > 0) ChangeMp(command.ActorId, -ability.ManaCost, command.ActorId);
            var style = ReadCombatStyle(command.ActorId);
            var shifted = style?.Shifted == true;
            var physicalDamageScale = technique is null
                ? PhysicalActionMath.ScaleFromPercents(100, shifted)
                : CombatStyleRules.TechniqueDamageScaleMillionths(technique, shifted);
            if (technique is not null &&
                _techniqueHitRng.NextInclusive(999_999) >=
                    CombatStyleRules.HitChanceMillionths(technique, shifted))
            {
                Emit("Missed", command.ActorId, command.TargetId, technique.Id);
            }
            else
            {
                EffectRunner.Apply(this, command.ActorId, command.TargetId,
                    ability.Effects, _rng, physicalDamageScale);
            }
        }
        TickStatus(command.ActorId);
        Emit("TurnEnded", command.ActorId);
        if (!IsFinished)
            AdvanceTurn();
        return true;
    }
    private void AdvanceTurn()
    {
        do { _nextActor = (_nextActor + 1) % _actors.Count; }
        while (_actors[_nextActor].Hp == 0);
        var next = _actors[_nextActor];
        if (next.ActiveStyleId is not null)
            next.TurnStartStyleId = next.ActiveStyleId;
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
