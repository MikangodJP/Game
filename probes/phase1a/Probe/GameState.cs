using Phase1A.Encounter;
using Phase1A.Preparation;
using Phase1A.World;

namespace Phase1A.Application;

// The application owns transitions and result application; Encounter never references this owner.
public sealed class GameState
{
    private readonly ulong seed;
    private BattleState? activeBattle;
    private string? activeEncounterId;
    public CharacterPreparation Player { get; }
    public FieldState Field { get; }
    public bool InEncounter => activeBattle is not null;

    public GameState(ulong seed = Scenario.GoldenSeed, CharacterPreparation? player = null)
    {
        this.seed = seed;
        Player = player ?? new CharacterPreparation(Scenario.Setup(seed).Actors[0].InitialStats);
        Field = new(PrototypeField.StartingMap);
    }

    public BattleState BeginEncounter(string id)
    {
        if (InEncounter || Player.Hp == 0 || Field.PendingEncounterId != id ||
            !Field.Encounters.Any(encounter => encounter.Id == id && !encounter.Defeated))
            throw new InvalidOperationException("Encounter requires live field contact and no active battle.");
        var battle = Player.BeginManagedBattle(Scenario.Setup(seed));
        activeBattle = battle;
        activeEncounterId = id;
        return battle;
    }

    public EncounterResult CompleteEncounter()
    {
        if (activeBattle is null || !activeBattle.IsFinished)
            throw new InvalidOperationException("A finished active encounter is required.");
        var result = Player.CompleteBattle(activeBattle);
        Field.CompleteEncounter(activeEncounterId!, result.Outcome == Outcome.Victory,
            result.Outcome is Outcome.Fled or Outcome.Aborted);
        activeBattle = null;
        activeEncounterId = null;
        return result;
    }
}
