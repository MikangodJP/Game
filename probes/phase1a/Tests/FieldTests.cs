using Phase1A;
using Phase1A.Application;
using Phase1A.Encounter;
using Phase1A.Preparation;
using Phase1A.Rules;
using Phase1A.World;

internal static class FieldTests
{
    public static readonly (string Name, Action Run)[] All =
    [
        ("New game owns one persistent player and starts on the reusable field spawn", () =>
        {
            var game = new GameState();
            Equal(new TilePosition(2, 5), game.Field.PlayerPosition);
            Equal(19, game.Field.Map.Width); Equal(11, game.Field.Map.Height);
            Equal(80, game.Player.Hp); Equal(12, game.Player.Mp);
            Check(!game.InEncounter && !game.Player.InBattle, "startup is outside battle");
            Equal(1, game.Field.Encounters.Length);
            Equal(new TilePosition(8, 5), game.Field.Encounters.Single().Position);
        }),
        ("Tile movement rejects boundaries obstacles diagonal moves and jumps", () =>
        {
            var field = new FieldState(PrototypeField.StartingMap);
            Check(field.TryMove(-1, 0).Moved, "move to x1");
            Check(!field.TryMove(-1, 0).Moved, "boundary x0 blocked");
            Check(!field.TryMove(2, 0).Moved && !field.TryMove(1, 1).Moved, "one cardinal step only");
            Check(!field.Map.IsWalkable(-1, 5) && !field.Map.IsWalkable(19, 5), "outside map blocked");
            Check(field.TryMove(1, 0).Moved && field.TryMove(1, 0).Moved && field.TryMove(1, 0).Moved, "walk to x4");
            Check(!field.TryMove(0, -1).Moved, "map obstacle at4,4 blocked");
            Equal(new TilePosition(4, 5), field.PlayerPosition);
        }),
        ("Contact locks field movement and prevents duplicate encounter triggers", () =>
        {
            var game = new GameState();
            var id = Contact(game);
            Equal(new TilePosition(8, 5), game.Field.PlayerPosition);
            Equal(id, game.Field.PendingEncounterId);
            Equal(new FieldMoveResult(false), game.Field.TryMove(1, 0));
            Throws(() => game.BeginEncounter("unknown"));
            var battle = game.BeginEncounter(id);
            Check(game.InEncounter && game.Player.InBattle, "battle ownership established");
            Throws(() => game.BeginEncounter(id));
            Throws(() => game.CompleteEncounter());
            Equal(new FieldMoveResult(false), game.Field.TryMove(-1, 0));
            Check(!battle.IsFinished, "premature completion cannot abort battle");
        }),
        ("Victory applies battle vitals once and preserves map position equipment and defeated enemy", () =>
        {
            var game = new GameState();
            var player = game.Player;
            var map = game.Field.Map;
            Check(player.TryEquip(EquipmentSlot.Weapon, PrototypeEquipment.WoodenSword), "sword");
            Check(player.TryEquip(EquipmentSlot.Body, PrototypeEquipment.LeatherArmor), "armor");
            var battle = game.BeginEncounter(Contact(game));
            Equal(15, battle.Read(0).Strength); Equal(12, battle.Read(0).Defense);
            Check(!player.TryEquip(EquipmentSlot.Weapon, null), "battle equipment locked");
            CompleteWithAttacks(battle);
            var hp = battle.Read(0).Hp;
            var mp = battle.Read(0).Mp;
            Check(hp > 0 && hp < 80, "complete battle caused persistent damage");
            var result = game.CompleteEncounter();
            Equal(Outcome.Victory, result.Outcome);
            Check(ReferenceEquals(player, game.Player) && ReferenceEquals(map, game.Field.Map), "same authoritative objects survive");
            Equal(hp, player.Hp); Equal(mp, player.Mp);
            Equal(new TilePosition(8, 5), game.Field.PlayerPosition);
            Check(game.Field.Encounters.Single().Defeated && !game.InEncounter, "victory removes field encounter");
            Equal(PrototypeEquipment.WoodenSword.Id, player.Loadout.Get(EquipmentSlot.Weapon)!.Id);
            Throws(() => game.CompleteEncounter());
            Check(game.Field.TryMove(-1, 0).Moved, "resume exploration");
            Equal<string?>(null, game.Field.TryMove(1, 0).EncounterId);
            Equal(hp, player.Hp);
        }),
        ("Escape returns to the safe contact origin with damage preserved and enemy still active", () =>
        {
            var game = new GameState();
            var battle = game.BeginEncounter(Contact(game));
            Check(battle.TakeTurn(new(0, Scenario.Strike, 1)), "hero action");
            Check(battle.TakeTurn(new(1, Scenario.Strike, 0)), "goblin action");
            Check(battle.TakeTurn(new(2, Scenario.Strike, 0)), "wolf action");
            Check(battle.TakeTurn(new(0, null, -1, CommandKind.Run)), "run");
            var hp = battle.Read(0).Hp;
            Equal(Outcome.Fled, game.CompleteEncounter().Outcome);
            Equal(hp, game.Player.Hp);
            Equal(new TilePosition(7, 5), game.Field.PlayerPosition);
            Check(!game.Field.Encounters.Single().Defeated, "escape cannot defeat enemy");
            Equal<string?>(null, game.Field.PendingEncounterId);
            var retry = game.Field.TryMove(1, 0);
            Check(retry.EncounterId is not null, "deliberate new contact can retry");
            var second = game.BeginEncounter(retry.EncounterId!);
            Equal(hp, second.Read(0).Hp); // No silent healing between encounters.
        }),
        ("Defeat persists zero HP and never marks the field enemy defeated", () =>
        {
            var player = new CharacterPreparation(Scenario.Setup(7).Actors[0].InitialStats, hp: 1);
            var game = new GameState(7, player);
            var battle = game.BeginEncounter(Contact(game));
            Check(battle.TakeTurn(new(0, Scenario.Strike, 1)), "hero");
            Check(battle.TakeTurn(new(1, Scenario.Strike, 0)), "enemy");
            Equal(Outcome.Defeat, game.CompleteEncounter().Outcome);
            Equal(0, player.Hp);
            Check(!game.Field.Encounters.Single().Defeated, "enemy remains alive");
        }),
        ("Terminal game encounters remain locked until coordinated acknowledgement", () =>
        {
            var game = new GameState();
            var battle = game.BeginEncounter(Contact(game));
            Check(battle.TakeTurn(new(0, null, -1, CommandKind.Run)), "run");
            Check(game.Player.InBattle && game.InEncounter, "terminal result is still pending acknowledgement");
            Check(!game.Player.TryEquip(EquipmentSlot.Weapon, PrototypeEquipment.WoodenSword), "pending equip rejected");
            Throws(() => game.Player.BeginBattle(Scenario.Setup(7)));
            Throws(() => game.Player.CompleteBattle());
            Equal(new FieldMoveResult(false), game.Field.TryMove(-1, 0));
            Equal(Outcome.Fled, game.CompleteEncounter().Outcome);
            Check(!game.Player.InBattle && !game.InEncounter, "coordinator releases both states once");
            Check(game.Player.TryEquip(EquipmentSlot.Weapon, PrototypeEquipment.WoodenSword), "field equipment available");
        }),
        ("Preparation applies only its own entry actor's sealed delta and rejects repeat application", () =>
        {
            var stats = new CharacterStats(30, 5, 10, 2, 0, 0, 0);
            var player = new CharacterPreparation(stats, hp: 20, mp: 4);
            var setup = new EncounterSetup([
                new("fixture:monster", null, Side.Monsters, stats),
                new("fixture:hero", "different-hero-id", Side.Adventurers, stats)], 7);
            var battle = player.BeginBattle(setup, actorId: 1);
            Throws(() => player.CompleteBattle());
            battle.ChangeHp(1, -3, 0, "Damaged");
            battle.ChangeMp(1, -2, 1);
            battle.Finish();
            Equal(Outcome.Aborted, player.CompleteBattle().Outcome);
            Equal(17, player.Hp); Equal(2, player.Mp);
            Throws(() => player.CompleteBattle());
            Equal(17, player.Hp);
        }),
        ("Pending terminal vitals are reconciled before an out-of-battle equipment change", () =>
        {
            var player = new CharacterPreparation(Scenario.Setup(7).Actors[0].InitialStats);
            Check(player.TryEquip(EquipmentSlot.Accessory, PrototypeEquipment.CopperCharm), "extra max HP");
            var battle = player.BeginBattle(Scenario.Setup(7));
            battle.ChangeHp(0, 5, 0, "Healed");
            battle.ChangeMp(0, -3, 0);
            battle.Finish();
            Check(player.TryEquip(EquipmentSlot.Accessory, null), "remove charm after terminal result");
            Equal(80, player.Hp); Equal(9, player.Mp);
            var next = player.BeginBattle(Scenario.Setup(7));
            Equal(80, next.Read(0).Hp); Equal(9, next.Read(0).Mp);
        }),
        ("Field movement does not consume combat RNG or change the unmodified golden fixture", () =>
        {
            var a = new GameState();
            var b = new GameState();
            b.Field.TryMove(0, -1); b.Field.TryMove(0, 1);
            var first = a.BeginEncounter(Contact(a));
            var second = b.BeginEncounter(Contact(b));
            Check(first.TakeTurn(new(0, Scenario.Strike, 1)), "first");
            Check(second.TakeTurn(new(0, Scenario.Strike, 1)), "second");
            Check(first.Events.SequenceEqual(second.Events), "movement must not shift battle stream");
        })
    ];

    private static string Contact(GameState game)
    {
        for (var i = 0; i < 6; i++)
        {
            var step = game.Field.TryMove(1, 0);
            Check(step.Moved, "approach must be walkable");
            if (step.EncounterId is { } id) return id;
        }
        throw new Exception("Expected field encounter contact.");
    }
    private static void CompleteWithAttacks(BattleState battle)
    {
        for (var i = 0; i < 100 && !battle.IsFinished; i++)
        {
            var actor = battle.NextActorId;
            Check(battle.TakeTurn(new(actor, Scenario.Strike, battle.LivingEnemies(actor)[0])), "ordered Strike");
        }
        Check(battle.IsFinished, "battle completed");
    }
    private static void Throws(Action action)
    {
        try { action(); }
        catch (InvalidOperationException) { return; }
        throw new Exception("Expected a rejected transition.");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Equal<T>(T expected, T actual) => Check(EqualityComparer<T>.Default.Equals(expected, actual), $"expected {expected}, got {actual}");
}
