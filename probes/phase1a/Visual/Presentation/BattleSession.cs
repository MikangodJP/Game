using System.Collections.Immutable;
using Phase1A.Encounter;
using Phase1A.Magic;
using Phase1A.Rules;

namespace Phase1A.Visual.Presentation;

public sealed record ActorView(
    int Id, string Name, int Hp, int MaxHp, int Mp, int MaxMp,
    string Status, bool Guarding, CharacterStats EffectiveStats);
public sealed record BattleView(ActorView Hero, ImmutableArray<ActorView> Enemies, bool Finished, Outcome? Outcome);

// Only this adapter owns the simulation reference. UI sees values and submits typed choices.
public sealed class BattleSession
{
    private readonly BattleState battle;
    public EncounterResult? Result { get; private set; }
    public ImmutableArray<BattleEvent> Events => battle.Events;
    public ImmutableArray<string> LastMessages { get; private set; } = [];
    public BattleSession(ulong seed = Scenario.GoldenSeed) : this(new BattleState(Scenario.Setup(seed))) { }
    public BattleSession(BattleState battle)
    {
        ArgumentNullException.ThrowIfNull(battle);
        this.battle = battle;
        LastMessages = Present(Events);
    }
    public BattleView View => new(Read(0), [Read(1), Read(2)], battle.IsFinished, Result?.Outcome);
    private ActorView Read(int id)
    {
        var actor = battle.Read(id);
        var status = battle.ReadStatus(id);
        return new(id, Name(id), actor.Hp, actor.MaxHp, actor.Mp, actor.MaxMp,
            actor.Guarding ? "DEFENDING" : status is null ? "READY" : "WEAKENED",
            actor.Guarding, actor.EffectiveStats);
    }
    public bool Submit(MenuAction action, int targetId = -1)
    {
        if (battle.IsFinished || battle.NextActorId != 0 || action == MenuAction.None) return false;
        if (action == MenuAction.Attack && !battle.LivingEnemies(0).Contains(targetId)) return false;
        var command = action switch
        {
            MenuAction.Attack => new Command(0, Scenario.Strike, targetId),
            MenuAction.Defend => new Command(0, null, -1, CommandKind.Defend),
            MenuAction.Run => new Command(0, null, -1, CommandKind.Run),
            _ => null
        };
        if (command is null) return false;
        return Resolve(command);
    }
    public bool CanSubmitFireball(ChantlessMagicConfiguration configuration)
    {
        var ability = FireballMagic.CreateAbility(configuration);
        return !battle.IsFinished && battle.NextActorId == 0 && battle.Read(0).Mp >= ability.ManaCost;
    }
    public void ShowInsufficientFireball(ChantlessMagicConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        LastMessages = ["Not enough MP."];
    }
    public bool SubmitFireball(ChantlessMagicConfiguration configuration, int targetId)
    {
        var ability = FireballMagic.CreateAbility(configuration);
        if (!CanSubmitFireball(configuration) || !battle.LivingEnemies(0).Contains(targetId)) return false;
        return Resolve(new(0, ability, targetId), configuration);
    }
    private bool Resolve(Command command, ChantlessMagicConfiguration? fireball = null)
    {
        var firstEvent = Events.Length;
        if (!battle.TakeTurn(command)) return false;
        // This harness gives enemies only Strike. Original Scenario.Run and its golden stay intact.
        while (!battle.IsFinished && battle.NextActorId != 0)
        {
            var enemy = battle.NextActorId;
            if (!battle.TakeTurn(new(enemy, Scenario.Strike, battle.LivingEnemies(enemy)[0])))
                throw new InvalidOperationException("Harness enemy controller submitted an invalid attack.");
        }
        if (battle.IsFinished) Result = battle.Finish();
        var messages = Present(Events.Skip(firstEvent)).ToBuilder();
        if (fireball is not null)
        {
            messages.Insert(Math.Min(1, messages.Count),
                $"Size {QuarterStepMultiplier.Format(fireball.SizeSteps)} | " +
                $"Output {QuarterStepMultiplier.Format(fireball.OutputSteps)} | " +
                $"MP {ChantlessMagicCost.Calculate(fireball)}");
        }
        LastMessages = messages.ToImmutable();
        return true;
    }
    public string MachineText => string.Concat(Events.Select(e => FormattableString.Invariant(
        $"{e.Sequence:D4}|{e.Kind}|{e.Source}|{e.Target}|{e.Detail}|{e.Amount}|{e.Value}\n")));
    private static string Name(int id) => id switch { 0 => "Adventurer", 1 => "Goblin", 2 => "Wolf", _ => "Unknown" };
    private static ImmutableArray<string> Present(IEnumerable<BattleEvent> events)
    {
        var messages = ImmutableArray.CreateBuilder<string>();
        foreach (var e in events)
        {
            string? message = e.Kind switch
            {
                "BattleStarted" => "Battle begins!",
                "ActorJoined" when e.Target != 0 => $"A {Name(e.Target)} appeared!",
                "ActionStarted" when e.Detail == Scenario.Strike.Id => $"{Name(e.Source)} attacks {Name(e.Target)}!",
                "ActionStarted" when e.Detail == PrototypeMagic.Fireball.Id => $"{Name(e.Source)} casts Fireball on {Name(e.Target)}!",
                "Damaged" => $"{Name(e.Target)} takes {e.Amount} damage!",
                "Healed" => $"{Name(e.Target)} recovers {e.Amount} HP.",
                "Defended" => $"{Name(e.Source)} defends!",
                "GuardBlocked" => $"{Name(e.Target)} blocks {e.Amount} damage.",
                "Fled" => $"{Name(e.Source)} fled from battle.",
                "StatusApplied" or "StatusRefreshed" => $"{Name(e.Target)} is Weakened!",
                "StatusExpired" => $"{Name(e.Target)} is no longer Weakened.",
                "Died" => $"{Name(e.Target)} is defeated!",
                "EffectSkipped" => $"{Name(e.Target)} is unaffected.",
                "CommandRejected" => "That command cannot be used.",
                "BattleEnded" => e.Detail switch { "Victory" => "Victory!", "Defeat" => "Defeat.", "Fled" => "Escape successful.", _ => "Battle stopped." },
                _ => null
            };
            if (message is not null) messages.Add(message);
        }
        return messages.ToImmutable();
    }
}
