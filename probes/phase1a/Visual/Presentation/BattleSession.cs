using System.Collections.Immutable;
using Phase1A.Encounter;
using Phase1A.Magic;
using Phase1A.Preparation;
using Phase1A.Rules;
using Phase1A.Styles;

namespace Phase1A.Visual.Presentation;

public sealed record ActorView(
    int Id, string Name, int Hp, int MaxHp, int Mp, int MaxMp,
    string Status, bool Guarding, CharacterStats EffectiveStats);
public sealed record StyleOptionView(string Id, string BattleLabel);
public sealed record TechniqueView(string Id, string DisplayName);
public sealed record PhysicalStyleView(
    ImmutableArray<StyleOptionView> KnownStyles,
    int ActiveIndex,
    string ActiveStyleId,
    string ActiveLabel,
    string TurnStartStyleId,
    bool Shifted,
    ImmutableArray<TechniqueView> Techniques);
public sealed record BattleView(
    ActorView Hero,
    ImmutableArray<ActorView> Enemies,
    bool Finished,
    Outcome? Outcome,
    PhysicalStyleView? PhysicalStyle);

// Only this adapter owns the simulation reference. UI sees values and submits typed choices.
public sealed class BattleSession
{
    private readonly BattleState battle;
    public EncounterResult? Result { get; private set; }
    public ImmutableArray<BattleEvent> Events => battle.Events;
    public ImmutableArray<string> LastMessages { get; private set; } = [];
    public BattleSession(ulong seed = Scenario.GoldenSeed) : this(CreatePlayerBattle(seed)) { }
    public BattleSession(BattleState battle)
    {
        ArgumentNullException.ThrowIfNull(battle);
        this.battle = battle;
        LastMessages = Present(Events);
    }
    public BattleView View => new(
        Read(0), [Read(1), Read(2)], battle.IsFinished, Result?.Outcome,
        ReadPhysicalStyle());
    public bool IsPlayerTurn => !battle.IsFinished && battle.NextActorId == 0;
    private ActorView Read(int id)
    {
        var actor = battle.Read(id);
        var status = battle.ReadStatus(id);
        return new(id, Name(id), actor.Hp, actor.MaxHp, actor.Mp, actor.MaxMp,
            actor.Guarding ? "DEFENDING" : status is null ? "READY" : "WEAKENED",
            actor.Guarding, actor.EffectiveStats);
    }
    private PhysicalStyleView? ReadPhysicalStyle()
    {
        var style = battle.ReadCombatStyle(0);
        if (style is null) return null;
        var activeIndex = -1;
        for (var index = 0; index < style.KnownStyles.Length; index++)
            if (style.KnownStyles[index].Id == style.ActiveStyle.Id)
            {
                activeIndex = index;
                break;
            }
        if (activeIndex < 0)
            throw new InvalidOperationException("Active Style is absent from the read model.");
        return new(
            style.KnownStyles.Select(definition =>
                new StyleOptionView(definition.Id, definition.BattleLabel)).ToImmutableArray(),
            activeIndex,
            style.ActiveStyle.Id,
            style.ActiveStyle.BattleLabel,
            style.TurnStartStyleId,
            style.Shifted,
            style.ActiveStyle.Techniques.Select(definition =>
                new TechniqueView(definition.Id, definition.DisplayName)).ToImmutableArray());
    }
    public bool TryChangeStyle(string styleId) => battle.TryChangeStyle(0, styleId);
    public bool SubmitBasicAttack(int targetId) =>
        IsPlayerTurn && battle.LivingEnemies(0).Contains(targetId) &&
        Resolve(new(0, Scenario.Strike, targetId));
    public bool SubmitTechnique(string techniqueId, int targetId) =>
        IsPlayerTurn && !string.IsNullOrWhiteSpace(techniqueId) &&
        battle.LivingEnemies(0).Contains(targetId) &&
        Resolve(new(0, null, targetId, CommandKind.Technique, techniqueId));
    public bool Submit(MenuAction action, int targetId = -1)
    {
        if (action == MenuAction.Attack) return SubmitBasicAttack(targetId);
        if (battle.IsFinished || battle.NextActorId != 0 || action == MenuAction.None) return false;
        var command = action switch
        {
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
    private static readonly IReadOnlyDictionary<string, string> TechniqueMessageNames =
        PrototypeCombatStyles.All.SelectMany(style => style.Techniques)
            .ToDictionary(technique => technique.Id,
                technique => ToMessageName(technique.DisplayName), StringComparer.Ordinal);
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
                "ActionStarted" when TechniqueMessageNames.TryGetValue(e.Detail, out var technique) =>
                    $"{Name(e.Source)} uses {technique} on {Name(e.Target)}!",
                "Missed" when TechniqueMessageNames.TryGetValue(e.Detail, out var missedTechnique) =>
                    $"{Name(e.Source)} misses {Name(e.Target)} with {missedTechnique}.",
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
    private static string ToMessageName(string displayName) => string.Join(' ',
        displayName.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => char.ToUpperInvariant(word[0]) + word[1..].ToLowerInvariant()));

    private static BattleState CreatePlayerBattle(ulong seed)
    {
        var setup = Scenario.Setup(seed);
        var player = setup.Actors[0];
        var preparation = new CharacterPreparation(
            player.InitialStats, player.Hp, player.Mp);
        return preparation.BeginBattle(setup);
    }
}
