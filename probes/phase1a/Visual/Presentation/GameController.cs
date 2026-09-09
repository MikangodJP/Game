using Phase1A.Application;
using Phase1A.Encounter;
using Phase1A.Preparation;

namespace Phase1A.Visual.Presentation;

public enum GameMode { Field, Preparation, Battle, GameOver }

// Coordinates the existing preparation and battle controllers. Persistent state
// stays above both; neither renderer decides how battle results affect the field.
public sealed class GameController
{
    private readonly ulong seed;
    public GameState State { get; private set; }
    public GameMode Mode { get; private set; }
    public HarnessController Harness { get; private set; }

    public GameController(ulong seed = Scenario.GoldenSeed, CharacterPreparation? player = null)
    {
        this.seed = seed;
        State = new(seed, player);
        Harness = new(State.Player, seed: seed);
    }

    public bool StepField(int dx, int dy)
    {
        if (Mode != GameMode.Field) return false;
        var step = State.Field.TryMove(dx, dy);
        if (step.EncounterId is { } encounterId)
        {
            var battle = State.BeginEncounter(encounterId);
            Harness = new(State.Player, new BattleSession(battle), seed);
            Mode = GameMode.Battle;
        }
        return step.Moved;
    }

    public void Handle(UiInput input)
    {
        switch (Mode)
        {
            case GameMode.Field:
                if (input == UiInput.Confirm)
                {
                    Harness = new(State.Player, seed: seed);
                    Mode = GameMode.Preparation;
                }
                break;
            case GameMode.Preparation:
                Harness.Handle(input);
                if (Harness.ReturnToFieldRequested) Mode = GameMode.Field;
                break;
            case GameMode.Battle:
                if (input == UiInput.Restart) return;
                if (Harness.Mode == ScreenMode.Ended && input is UiInput.Confirm or UiInput.Back)
                {
                    var result = State.CompleteEncounter();
                    Mode = result.Outcome == Outcome.Defeat ? GameMode.GameOver : GameMode.Field;
                }
                else Harness.Handle(input);
                break;
            case GameMode.GameOver:
                if (input == UiInput.Restart)
                {
                    State = new(seed);
                    Harness = new(State.Player, seed: seed);
                    Mode = GameMode.Field;
                }
                break;
        }
    }
}
