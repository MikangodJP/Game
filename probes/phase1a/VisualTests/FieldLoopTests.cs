using Phase1A;
using Phase1A.Encounter;
using Phase1A.Magic;
using Phase1A.Preparation;
using Phase1A.Rules;
using Phase1A.Visual.Presentation;
using Phase1A.World;

internal static class FieldLoopTests
{
    public static readonly (string Name, Action Run)[] All =
    [
        ("New game starts in Field and preparation edits the same player without starting battle", () =>
        {
            var game = new GameController();
            Equal(GameMode.Field, game.Mode);
            var player = game.State.Player;
            Equal(new TilePosition(2, 5), game.State.Field.PlayerPosition);
            Check(!game.State.InEncounter && !player.InBattle, "new game has no active encounter");
            Check(game.StepField(-1, 0), "walkable ground accepts movement");
            Check(!game.StepField(-1, 0), "solid map boundary rejects movement");
            Equal(new TilePosition(1, 5), game.State.Field.PlayerPosition);
            game.Handle(UiInput.Confirm);
            Equal(GameMode.Preparation, game.Mode);
            Check(ReferenceEquals(player, game.Harness.Preparation), "preparation reuses persistent player");
            Equal("Return to Field", game.Harness.PreparationActionLabel);
            Check(!game.StepField(1, 0), "preparation blocks background field movement");
            Equip(game, EquipmentSlot.Weapon, PrototypeEquipment.WoodenSword);
            Equip(game, EquipmentSlot.Body, PrototypeEquipment.LeatherArmor);
            game.Handle(UiInput.Back); // Slots -> preparation.
            game.Handle(UiInput.Down); game.Handle(UiInput.Confirm); // Return to Field.
            Equal(GameMode.Field, game.Mode);
            Equal(15, player.EffectiveStats.Strength); Equal(12, player.EffectiveStats.Defense);
            Check(!game.State.InEncounter, "preparation return cannot begin an encounter");
            game.Handle(UiInput.Confirm); game.Handle(UiInput.Back);
            Equal(GameMode.Field, game.Mode);
            Check(ReferenceEquals(player, game.State.Player), "reopening preparation does not replace the player");
        }),
        ("Enemy contact starts one existing battle and locks field movement and gear", () =>
        {
            var game = new GameController();
            Contact(game);
            var session = game.Harness.Session;
            var position = game.State.Field.PlayerPosition;
            var log = session.MachineText;
            for (var i = 0; i < 5; i++) Check(!game.StepField(1, 0), "battle blocks repeated movement frames");
            Check(ReferenceEquals(session, game.Harness.Session), "only one battle session created");
            Equal(position, game.State.Field.PlayerPosition);
            Equal(log, session.MachineText);
            Check(game.State.InEncounter && game.State.Player.InBattle, "contact owns an active encounter");
            Check(ReferenceEquals(game.State.Player, game.Harness.Preparation), "battle harness keeps same preparation owner");
            Check(!game.State.Player.TryEquip(EquipmentSlot.Weapon, PrototypeEquipment.WoodenSword), "gear locked during combat");
            game.Handle(UiInput.Restart); game.Handle(UiInput.Back);
            Equal(GameMode.Battle, game.Mode);
            Choose(game, "MAGIC"); Choose(game, "CHANTLESS"); Choose(game, "ELEMENTAL MAGIC"); Choose(game, "Water");
            Equal(ScreenMode.Wip, game.Harness.Mode);
            Check(!game.StepField(-1, 0), "WIP overlay does not enable field movement");
            Equal(log, session.MachineText);
        }),
        ("Victory applies HP MP and gear to the same player and preserves field and defeated encounter", () =>
        {
            var player = new CharacterPreparation(Scenario.Setup(Scenario.GoldenSeed).Actors[0].InitialStats, 70, 7);
            Check(player.TryEquip(EquipmentSlot.Weapon, PrototypeEquipment.WoodenSword), "sword equipped before field encounter");
            Check(player.TryEquip(EquipmentSlot.Body, PrototypeEquipment.LeatherArmor), "armor equipped before field encounter");
            var magic = new ChantlessMagicConfiguration(PrototypeMagic.Fireball, 5, 5);
            var game = new GameController(player: player);
            var field = game.State.Field;
            var map = field.Map;
            Contact(game);
            var contactPosition = field.PlayerPosition;
            Equal(70, game.Harness.Session.View.Hero.Hp); Equal(7, game.Harness.Session.View.Hero.Mp);
            Equal(18, game.Harness.Session.View.Hero.EffectiveStats.Strength);
            Equal(10, game.Harness.Session.View.Hero.EffectiveStats.Defense);
            Choose(game, "MAGIC"); Choose(game, "CHANTLESS"); Choose(game, "ELEMENTAL MAGIC"); Choose(game, "Fire");
            Equal(ScreenMode.MagicAdjustment, game.Harness.Mode);
            game.Handle(UiInput.Right); game.Handle(UiInput.Down); game.Handle(UiInput.Right);
            game.Handle(UiInput.Down); game.Handle(UiInput.Confirm);
            Equal(ScreenMode.Targets, game.Harness.Mode);
            game.Handle(UiInput.Confirm); FinishMessages(game);
            Equal(1, game.Harness.Session.View.Hero.Mp);
            Check(game.Harness.Session.Events.Any(e => e.Kind == "ActionStarted" && e.Detail == PrototypeMagic.Fireball.Id),
                "configured Fireball crosses the field encounter boundary");
            for (var turn = 0; turn < 20 && !game.Harness.Session.View.Finished; turn++)
            {
                OpenBasicTarget(game); game.Handle(UiInput.Confirm); FinishMessages(game);
            }
            Equal(Outcome.Victory, game.Harness.Session.Result!.Outcome);
            Equal(ScreenMode.Ended, game.Harness.Mode);
            var remainingHp = game.Harness.Session.View.Hero.Hp;
            Check(remainingHp > 0 && remainingHp < 70, "battle causes real resource loss");
            Equal(70, player.Hp); // Encounter owns its copy until result application.
            game.Handle(UiInput.Confirm);
            Equal(GameMode.Field, game.Mode);
            Check(ReferenceEquals(player, game.State.Player), "victory updates existing player");
            Check(ReferenceEquals(field, game.State.Field) && ReferenceEquals(map, field.Map), "field and map survive encounter");
            Equal(contactPosition, field.PlayerPosition);
            Equal(remainingHp, player.Hp); Equal(1, player.Mp);
            Equal(magic, player.LastUsedChantlessMagic(PrototypeMagic.Fireball));
            Equal(PrototypeEquipment.WoodenSword, player.Loadout.Get(EquipmentSlot.Weapon));
            Check(field.Encounters.Single().Defeated, "victory recorded in field state");
            Check(!game.State.InEncounter && !player.InBattle, "completed encounter released");
            Check(game.StepField(-1, 0) && game.StepField(1, 0), "walking resumes through defeated enemy tile");
            Equal(GameMode.Field, game.Mode);
            game.Handle(UiInput.Back);
            Equal(remainingHp, player.Hp);
            game.Handle(UiInput.Confirm);
            Equal(remainingHp, game.Harness.Preparation.Hp);
            game.Handle(UiInput.Back);
            Equal(GameMode.Field, game.Mode);
        }),
        ("Flee returns to precontact tile without healing and encounter can be retried", () =>
        {
            var game = new GameController();
            Contact(game);
            Choose(game, "DEFEND"); FinishMessages(game);
            var remainingHp = game.Harness.Session.View.Hero.Hp;
            Check(remainingHp < 80, "defend still takes reduced damage");
            Choose(game, "RUN"); FinishMessages(game);
            Equal(Outcome.Fled, game.Harness.Session.Result!.Outcome);
            game.Handle(UiInput.Back);
            Equal(GameMode.Field, game.Mode);
            Equal(new TilePosition(7, 5), game.State.Field.PlayerPosition);
            Check(!game.State.Field.Encounters.Single().Defeated, "flee does not defeat field encounter");
            Equal(remainingHp, game.State.Player.Hp);
            Check(game.StepField(1, 0), "explicit new contact allows retry");
            Equal(GameMode.Battle, game.Mode);
            Equal(remainingHp, game.Harness.Session.View.Hero.Hp);
            Choose(game, "RUN"); FinishMessages(game); game.Handle(UiInput.Confirm);
            Equal(GameMode.Field, game.Mode);
            Equal(remainingHp, game.State.Player.Hp);
        }),
        ("Defeat requires terminal acknowledgment and deliberate GameOver restart", () =>
        {
            var player = new CharacterPreparation(Scenario.Setup(Scenario.GoldenSeed).Actors[0].InitialStats, 1, 2);
            var game = new GameController(player: player);
            Contact(game);
            Choose(game, "DEFEND"); FinishMessages(game);
            Equal(Outcome.Defeat, game.Harness.Session.Result!.Outcome);
            game.Handle(UiInput.Restart);
            game.Harness.Handle(UiInput.Restart);
            Equal(GameMode.Battle, game.Mode); Equal(ScreenMode.Ended, game.Harness.Mode);
            game.Handle(UiInput.Confirm);
            Equal(GameMode.GameOver, game.Mode); Equal(0, player.Hp);
            Check(!game.StepField(1, 0), "dead player cannot explore");
            game.Handle(UiInput.Confirm); game.Handle(UiInput.Back);
            Equal(GameMode.GameOver, game.Mode);
            game.Handle(UiInput.Restart);
            Equal(GameMode.Field, game.Mode);
            Equal(new TilePosition(2, 5), game.State.Field.PlayerPosition);
            Check(!ReferenceEquals(player, game.State.Player), "only deliberate new game replaces persistent state");
            Equal(80, game.State.Player.Hp); Equal(12, game.State.Player.Mp);
            Check(!game.State.Field.Encounters.Single().Defeated, "new game resets encounter state");
        })
    ];

    private static void Contact(GameController game)
    {
        for (var i = 0; i < 6; i++) Check(game.StepField(1, 0), "path from spawn to encounter is walkable");
        Equal(GameMode.Battle, game.Mode);
        Equal(new TilePosition(8, 5), game.State.Field.PlayerPosition);
        Equal(4, game.Harness.Session.Events.Length);
    }
    private static void Equip(GameController game, EquipmentSlot slot, EquipmentDefinition item)
    {
        var ui = game.Harness;
        if (ui.Mode == ScreenMode.Preparation) game.Handle(UiInput.Confirm);
        Equal(ScreenMode.EquipmentSlots, ui.Mode);
        var slotIndex = ui.Slots.ToList().IndexOf(slot);
        while (ui.SlotIndex != slotIndex) game.Handle(ui.SlotIndex < slotIndex ? UiInput.Down : UiInput.Up);
        game.Handle(UiInput.Confirm);
        var itemIndex = ui.EquipmentChoices.ToList().FindIndex(choice => choice?.Id == item.Id);
        Check(itemIndex >= 0, "compatible item available");
        while (ui.EquipmentIndex != itemIndex) game.Handle(ui.EquipmentIndex < itemIndex ? UiInput.Down : UiInput.Up);
        game.Handle(UiInput.Confirm);
    }
    private static void Choose(GameController game, string label)
    {
        var ui = game.Harness;
        Equal(ScreenMode.Menu, ui.Mode);
        var index = ui.Menu.CurrentEntries.ToList().FindIndex(entry => entry.Label == label);
        Check(index >= 0, "battle entry exists: " + label);
        while (ui.Menu.SelectedColumn > 0) game.Handle(UiInput.Left);
        while (ui.Menu.SelectedRow != index / ui.Menu.Columns)
            game.Handle(ui.Menu.SelectedRow < index / ui.Menu.Columns ? UiInput.Down : UiInput.Up);
        while (ui.Menu.SelectedColumn < index % ui.Menu.Columns) game.Handle(UiInput.Right);
        game.Handle(UiInput.Confirm);
    }
    private static void OpenBasicTarget(GameController game)
    {
        Choose(game, "ATTACK");
        Equal(ScreenMode.PhysicalActions, game.Harness.Mode);
        game.Handle(UiInput.Down);
        game.Handle(UiInput.Down);
        game.Handle(UiInput.Confirm);
        Equal(ScreenMode.Targets, game.Harness.Mode);
    }
    private static void FinishMessages(GameController game)
    {
        for (var i = 0; i < 20 && game.Harness.Mode == ScreenMode.Messages; i++) game.Handle(UiInput.Confirm);
        Check(game.Harness.Mode != ScreenMode.Messages, "battle text pages finish");
    }
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Equal<T>(T expected, T actual) => Check(EqualityComparer<T>.Default.Equals(expected, actual), $"expected {expected}, got {actual}");
}
