using Phase1A.Encounter;
using Phase1A.Rules;

namespace Phase1A;

public static class Scenario
{
    public const ulong GoldenSeed = 20260909;
    public static readonly StatusDef Weakened = new("probe:status.weakened", 2);
    public static readonly Ability Strike = PrototypePhysicalActions.BasicAttack;
    public static readonly Ability Crush = new("probe:ability.crush", 2,
        [new(OpKind.ApplyStatus, new(TargetScope.Selected), new(CasterStrength: 0.5), Status: Weakened),
         new(OpKind.Damage, new(TargetScope.PriorTargets, 0), new(Base: 1, Variance: 1), DamageKind: DamageKind.Physical)]);
    public static readonly Ability Drain = new("probe:ability.drain", 3,
        [new(OpKind.Damage, new(TargetScope.Selected), new(Base: 4, CasterStrength: 0.7, Variance: 2)),
         new(OpKind.Heal, new(TargetScope.Self), new(PriorNode: 0, PriorAmount: 0.5))]);

    public static EncounterSetup Setup(ulong seed) => new(
        [new("probe:actor.adventurer", "adventurer-1", Side.Adventurers, new(80, 12, 12, 8, 6, 6, 10)),
         new("probe:monster.goblin", null, Side.Monsters, new(38, 2, 8, 5, 2, 3, 6)),
         new("probe:monster.wolf", null, Side.Monsters, new(46, 0, 10, 4, 1, 3, 12))], seed);

    public static EncounterResult Run(ulong seed)
    {
        var state = new BattleState(Setup(seed));
        var heroTurns = 0;
        for (var turn = 0; turn < 100 && !state.IsFinished; turn++)
        {
            var actorId = state.NextActorId;
            var actor = state.Read(actorId);
            var ability = Strike;
            // Scripted controller belongs to this fixture, never to the reusable op path.
            if (actorId == 0)
            {
                if (heroTurns % 3 == 0 && actor.Mp >= Crush.ManaCost) ability = Crush;
                else if (heroTurns % 3 == 1 && actor.Mp >= Drain.ManaCost) ability = Drain;
                heroTurns++;
            }
            else if (actorId == 1 && actor.Mp >= Crush.ManaCost) ability = Crush;
            if (!state.TakeTurn(new(actorId, ability, state.LivingEnemies(actorId)[0])))
                throw new InvalidOperationException("The fixture controller produced an illegal command.");
        }
        return state.Finish();
    }
}

public static class CanonicalLog
{
    public static string Format(EncounterResult result)
    {
        var text = new System.Text.StringBuilder();
        foreach (var e in result.EventLog)
            text.Append(FormattableString.Invariant($"{e.Sequence:D4}|{e.Kind}|{e.Source}|{e.Target}|{e.Detail}|{e.Amount}|{e.Value}\n"));
        return text.ToString();
    }
}
