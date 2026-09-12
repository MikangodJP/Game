using Phase1A.Encounter;
using Phase1A.Magic;
using Phase1A.Rules;
using Phase1A.Styles;

namespace Phase1A.Preparation;

// Higher-layer preparation for the disposable harness. Encounter only receives copied values.
public sealed class CharacterPreparation
{
    private BattleState? activeBattle;
    private string? activeInstanceId;
    private bool coordinatorOwnsCompletion;
    private readonly Dictionary<string, ChantlessMagicConfiguration> lastUsedChantlessMagic;
    public CharacterStats BaseStats { get; }
    public CharacterStats EffectiveStats => StatResolver.Resolve(BaseStats, equipment: Loadout.Bonuses);
    public int Hp { get; private set; }
    public int Mp { get; private set; }
    public EquipmentLoadout Loadout { get; private set; } = EquipmentLoadout.Empty;
    public IReadOnlyList<BaseMagicDefinition> KnownBaseMagics { get; }
    public IReadOnlyList<CombatStyleDefinition> KnownCombatStyles { get; }
    public CombatStyleDefinition PrimaryCombatStyle { get; }
    public bool InBattle => activeBattle is not null && (coordinatorOwnsCompletion || !activeBattle.IsFinished);

    public CharacterPreparation(CharacterStats baseStats, int? hp = null, int? mp = null)
    {
        baseStats.Validate();
        BaseStats = baseStats;
        KnownBaseMagics = Array.AsReadOnly(new[] { PrototypeMagic.Fireball });
        KnownCombatStyles = PrototypeCombatStyles.All;
        PrimaryCombatStyle = PrototypeCombatStyles.SwordGod;
        lastUsedChantlessMagic = KnownBaseMagics.ToDictionary(
            magic => magic.Id,
            magic => new ChantlessMagicConfiguration(
                magic, QuarterStepMultiplier.DefaultSteps, QuarterStepMultiplier.DefaultSteps));
        Hp = hp ?? baseStats.MaxHp;
        Mp = mp ?? baseStats.MaxMp;
        if (Hp < 0 || Hp > baseStats.MaxHp) throw new ArgumentOutOfRangeException(nameof(hp));
        if (Mp < 0 || Mp > baseStats.MaxMp) throw new ArgumentOutOfRangeException(nameof(mp));
    }

    public ChantlessMagicConfiguration LastUsedChantlessMagic(BaseMagicDefinition baseMagic)
    {
        ArgumentNullException.ThrowIfNull(baseMagic);
        if (!KnownBaseMagics.Contains(baseMagic))
            throw new ArgumentException("Base Magic must be known by this player.", nameof(baseMagic));
        return lastUsedChantlessMagic[baseMagic.Id];
    }

    // The battle coordinator calls this only after its submitted cast resolves.
    public bool TryRememberSuccessfulChantlessMagic(ChantlessMagicConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        if (activeBattle is null || !KnownBaseMagics.Contains(configuration.BaseMagic)) return false;
        lastUsedChantlessMagic[configuration.BaseMagic.Id] = configuration;
        return true;
    }

    public bool TryEquip(EquipmentSlot slot, EquipmentDefinition? item)
    {
        if (InBattle) return false;
        if (activeBattle is not null) CompleteBattle();
        try
        {
            var next = Loadout.With(slot, item);
            var stats = StatResolver.Resolve(BaseStats, equipment: next.Bonuses);
            Loadout = next;
            // A higher maximum does not grant healing; lower maxima clamp current resources.
            Hp = Math.Min(Hp, stats.MaxHp);
            Mp = Math.Min(Mp, stats.MaxMp);
            return true;
        }
        catch (Exception error) when (error is ArgumentException or OverflowException)
        {
            return false;
        }
    }

    public CharacterStats Preview(EquipmentSlot slot, EquipmentDefinition? item) =>
        StatResolver.Resolve(BaseStats, equipment: Loadout.With(slot, item).Bonuses);

    public BattleState BeginBattle(EncounterSetup template, int actorId = 0) => BeginBattleCore(template, actorId, false);

    internal BattleState BeginManagedBattle(EncounterSetup template, int actorId = 0) => BeginBattleCore(template, actorId, true);

    private BattleState BeginBattleCore(EncounterSetup template, int actorId, bool managedCompletion)
    {
        if (InBattle) throw new InvalidOperationException("Equipment preparation already has an active battle.");
        if (activeBattle is not null) CompleteBattle();
        ArgumentNullException.ThrowIfNull(template);
        if (template.Actors.IsDefault || actorId < 0 || actorId >= template.Actors.Length)
            throw new ArgumentOutOfRangeException(nameof(actorId));
        var styleProfile = new CombatStyleProfile(
            [.. KnownCombatStyles], PrimaryCombatStyle.Id);
        var seed = template.Actors[actorId] with
        {
            InitialStats = EffectiveStats,
            Hp = Hp,
            Mp = Mp,
            StyleProfile = styleProfile
        };
        var instanceId = seed.InstanceId ?? throw new InvalidOperationException("The persistent player requires an instance ID.");
        if (template.Actors.Count(actor => actor.InstanceId == instanceId) != 1)
            throw new InvalidOperationException("The persistent player's instance ID must be unique in the encounter.");
        var setup = template with { Actors = template.Actors.SetItem(actorId, seed) };
        activeBattle = new BattleState(setup);
        activeInstanceId = instanceId;
        coordinatorOwnsCompletion = managedCompletion;
        return activeBattle;
    }

    // No caller-supplied result: only this player's own finished encounter may update its vitals.
    public EncounterResult CompleteBattle()
    {
        if (coordinatorOwnsCompletion)
            throw new InvalidOperationException("The game coordinator must complete this encounter.");
        return ApplyOwnedResult();
    }

    private EncounterResult ApplyOwnedResult()
    {
        if (activeBattle is null || !activeBattle.IsFinished)
            throw new InvalidOperationException("A finished owned encounter is required before applying its result.");
        var result = activeBattle.Finish();
        var vitals = result.Deltas.Single(delta => delta.InstanceId == activeInstanceId);
        var stats = EffectiveStats;
        if (vitals.Hp < 0 || vitals.Hp > stats.MaxHp || vitals.Mp < 0 || vitals.Mp > stats.MaxMp)
            throw new InvalidOperationException("Encounter result contains invalid player vitals.");
        Hp = vitals.Hp;
        Mp = vitals.Mp;
        activeBattle = null;
        activeInstanceId = null;
        coordinatorOwnsCompletion = false;
        return result;
    }

    internal EncounterResult CompleteBattle(BattleState expectedBattle)
    {
        if (!ReferenceEquals(activeBattle, expectedBattle))
            throw new InvalidOperationException("The encounter is not owned by this player or was already applied.");
        return ApplyOwnedResult();
    }
}
