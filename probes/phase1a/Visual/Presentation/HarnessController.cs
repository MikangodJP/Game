using Phase1A.Encounter;
using Phase1A.Preparation;
using Phase1A.Rules;

namespace Phase1A.Visual.Presentation;

public enum ScreenMode { Preparation, EquipmentSlots, EquipmentItems, Menu, Targets, Messages, Wip, Ended, MachineLog }
public enum UiInput { Up, Down, Left, Right, Confirm, Back, Menu, Debug, Restart }

public sealed class HarnessController
{
    private int messageOffset;
    private ScreenMode debugReturnMode;
    private readonly EncounterSetup template;
    private BattleSession? session;
    public BattleSession Session => session ?? throw new InvalidOperationException("Start battle from preparation first.");
    public CharacterPreparation Preparation { get; private set; }
    public bool IsWorldBound { get; }
    public string PreparationActionLabel => IsWorldBound ? "Return to Field" : "Start Battle";
    public bool ReturnToFieldRequested { get; private set; }
    public bool IsPreparing => Mode is ScreenMode.Preparation or ScreenMode.EquipmentSlots or ScreenMode.EquipmentItems;
    public int PreparationIndex { get; private set; }
    public IReadOnlyList<EquipmentSlot> Slots { get; } = Array.AsReadOnly(Enum.GetValues<EquipmentSlot>());
    public int SlotIndex { get; private set; }
    public EquipmentSlot SelectedSlot => Slots[SlotIndex];
    // None is a real choice; the explicit Back cell is index EquipmentChoices.Count.
    public IReadOnlyList<EquipmentDefinition?> EquipmentChoices => PrototypeEquipment.Items
        .Where(item => item.Slot == SelectedSlot).Cast<EquipmentDefinition?>().Prepend(null).ToArray();
    public int EquipmentIndex { get; private set; }
    public CharacterStats PreviewStats => Mode == ScreenMode.EquipmentItems && EquipmentIndex < EquipmentChoices.Count
        ? Preparation.Preview(SelectedSlot, EquipmentChoices[EquipmentIndex]) : Preparation.EffectiveStats;
    public BattleMenu Menu { get; } = new();
    public ScreenMode Mode { get; private set; }
    public int TargetIndex { get; private set; }
    public int DebugOffset { get; private set; }
    public string WipLabel { get; private set; } = "";
    public IReadOnlyList<string> BattleLines { get; private set; } = [];
    public IReadOnlyList<ActorView> Targets => session is null ? [] : Session.View.Enemies.Where(e => e.Hp > 0).ToArray();
    public string Breadcrumb => Mode switch
    {
        ScreenMode.Preparation => "PREPARATION",
        ScreenMode.EquipmentSlots => "EQUIPMENT",
        ScreenMode.EquipmentItems => "EQUIPMENT > " + SelectedSlot,
        ScreenMode.Targets => "ATTACK > CHOOSE TARGET",
        _ => Menu.Breadcrumb
    };
    public HarnessController(ulong seed = Scenario.GoldenSeed)
    {
        template = Scenario.Setup(seed);
        Preparation = new(template.Actors[0].InitialStats);
    }
    public HarnessController(CharacterPreparation preparation, BattleSession? battleSession = null, ulong seed = Scenario.GoldenSeed)
    {
        ArgumentNullException.ThrowIfNull(preparation);
        template = Scenario.Setup(seed);
        Preparation = preparation;
        IsWorldBound = true;
        session = battleSession;
        if (session is not null)
        {
            Mode = ScreenMode.Menu;
            BattleLines = session.LastMessages.TakeLast(3).ToArray();
        }
    }
    public void Handle(UiInput input)
    {
        if (IsPreparing)
        {
            HandlePreparation(input);
            return;
        }
        if (input == UiInput.Debug)
        {
            if (Mode == ScreenMode.MachineLog) Mode = debugReturnMode;
            else { debugReturnMode = Mode; Mode = ScreenMode.MachineLog; DebugOffset = 0; }
            return;
        }
        switch (Mode)
        {
            case ScreenMode.MachineLog:
                if (input == UiInput.Back) Mode = debugReturnMode;
                else
                {
                    var delta = input switch { UiInput.Up => -1, UiInput.Down => 1, UiInput.Left => -7, UiInput.Right => 7, _ => 0 };
                    DebugOffset = Math.Clamp(DebugOffset + delta, 0, Math.Max(0, Session.Events.Length - 7));
                }
                break;
            case ScreenMode.Wip:
                if (input is UiInput.Confirm or UiInput.Back) Mode = ScreenMode.Menu;
                break;
            case ScreenMode.Messages:
                if (input == UiInput.Back)
                {
                    BattleLines = Session.LastMessages.TakeLast(3).ToArray();
                    Mode = Session.View.Finished ? ScreenMode.Ended : ScreenMode.Menu;
                }
                else if (input == UiInput.Confirm)
                {
                    messageOffset += 3;
                    if (messageOffset < Session.LastMessages.Length) ShowPage();
                    else Mode = Session.View.Finished ? ScreenMode.Ended : ScreenMode.Menu;
                }
                break;
            case ScreenMode.Ended:
                if (input == UiInput.Restart && !IsWorldBound)
                {
                    session = null; Preparation = new(template.Actors[0].InitialStats);
                    Menu.Reset(); Mode = ScreenMode.Preparation;
                    PreparationIndex = 0; SlotIndex = 0; EquipmentIndex = 0;
                    TargetIndex = 0; DebugOffset = 0; BattleLines = [];
                }
                break;
            case ScreenMode.Targets:
                if (input == UiInput.Back) Mode = ScreenMode.Menu;
                else if (input == UiInput.Left) TargetIndex = Math.Max(0, TargetIndex - 1);
                else if (input == UiInput.Right) TargetIndex = Math.Min(Targets.Count, TargetIndex + 1);
                else if (input == UiInput.Confirm)
                {
                    if (TargetIndex == Targets.Count) Mode = ScreenMode.Menu;
                    else Submit(MenuAction.Attack, Targets[TargetIndex].Id);
                }
                break;
            case ScreenMode.Menu:
                if (input == UiInput.Up) Menu.Move(0, -1);
                else if (input == UiInput.Down) Menu.Move(0, 1);
                else if (input == UiInput.Left) Menu.Move(-1, 0);
                else if (input == UiInput.Right) Menu.Move(1, 0);
                else if (input == UiInput.Back) Menu.Back();
                else if (input == UiInput.Confirm)
                {
                    var choice = Menu.Confirm();
                    switch (choice.Kind)
                    {
                        case ChoiceKind.Attack: TargetIndex = 0; Mode = ScreenMode.Targets; break;
                        case ChoiceKind.Defend: Submit(MenuAction.Defend); break;
                        case ChoiceKind.Run: Submit(MenuAction.Run); break;
                        case ChoiceKind.Wip: WipLabel = choice.Label; Mode = ScreenMode.Wip; break;
                    }
                }
                break;
        }
    }
    private void HandlePreparation(UiInput input)
    {
        var delta = input switch { UiInput.Up => -1, UiInput.Down => 1, _ => 0 };
        if (Mode == ScreenMode.Preparation)
        {
            PreparationIndex = Math.Clamp(PreparationIndex + delta, 0, 1);
            if (input == UiInput.Back && IsWorldBound) ReturnToFieldRequested = true;
            if (input != UiInput.Confirm) return;
            if (PreparationIndex == 0) Mode = ScreenMode.EquipmentSlots;
            else if (IsWorldBound) ReturnToFieldRequested = true;
            else
            {
                session = new(Preparation.BeginBattle(template));
                Menu.Reset(); Mode = ScreenMode.Menu;
                BattleLines = Session.LastMessages.TakeLast(3).ToArray();
            }
        }
        else if (Mode == ScreenMode.EquipmentSlots)
        {
            SlotIndex = Math.Clamp(SlotIndex + delta, 0, Slots.Count);
            if (input == UiInput.Back || input == UiInput.Confirm && SlotIndex == Slots.Count)
                Mode = ScreenMode.Preparation;
            else if (input == UiInput.Confirm)
            {
                var equipped = Preparation.Loadout.Get(SelectedSlot);
                EquipmentIndex = Math.Max(0, EquipmentChoices.ToList().FindIndex(item => item?.Id == equipped?.Id));
                Mode = ScreenMode.EquipmentItems;
            }
        }
        else if (Mode == ScreenMode.EquipmentItems)
        {
            EquipmentIndex = Math.Clamp(EquipmentIndex + delta, 0, EquipmentChoices.Count);
            if (input == UiInput.Back || input == UiInput.Confirm && EquipmentIndex == EquipmentChoices.Count)
                Mode = ScreenMode.EquipmentSlots;
            else if (input == UiInput.Confirm && Preparation.TryEquip(SelectedSlot, EquipmentChoices[EquipmentIndex]))
                Mode = ScreenMode.EquipmentSlots;
        }
    }
    private void Submit(MenuAction action, int target = -1)
    {
        if (!Session.Submit(action, target)) return;
        messageOffset = 0; Mode = ScreenMode.Messages; ShowPage();
    }
    private void ShowPage() => BattleLines = Session.LastMessages.Skip(messageOffset).Take(3).ToArray();
}
