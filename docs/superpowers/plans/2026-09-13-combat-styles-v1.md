# Combat Styles V1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add three player-known Combat Styles, two Style-owned Techniques per
Style, BASIC ATTACK, and immediate free Style Shift behavior to the existing
physical Battle flow.

**Architecture:** Immutable prototype definitions and fixed-point rule helpers
live in the headless core. `BattleState` owns snapshotted known Styles, Active
Style, turn-start Style, Technique validation, the isolated Technique-hit RNG,
and physical damage scaling; Presentation consumes immutable views and routes
the approved ATTACK interaction without calculating rules.

**Tech Stack:** C# 12, .NET 8, immutable value data, deterministic integer RNG,
Godot 4.6.3 Mono, PowerShell 7, console behavior tests, and opt-in Godot
input/render QA.

**Spec:** `docs/superpowers/specs/2026-09-12-combat-styles-v1-design.md`

## Global Constraints

- Use only MaxHP, MaxMP, Strength, Defense, Magic, Resistance, and Agility;
  Agility remains unused.
- Preserve the fixed round-robin scheduler and existing BASIC ATTACK
  guaranteed-hit behavior.
- Use stable IDs for all rule decisions; display text is never an identity.
- All three Styles are known by default and every player Battle starts in Sword
  God Style.
- Style changes are immediate and free; Back never reverts a real change.
- `TurnStartStyleId` changes only when round-robin returns to that actor.
- While shifted, Technique Accuracy is ×0.85, every physical action's Damage is
  ×0.85, positive stance magnitudes are ×0.85, and negative stance magnitudes
  are ×1.00.
- BASIC ATTACK uses the existing Strike ability and performs no Technique hit
  roll; Techniques reuse the same underlying physical ability/effects.
- Technique hit resolution uses integer millionths and the independent
  `battle.technique-hit` stream.
- Do not add Style learning, rank, school, teacher, progression, quest,
  Favorite, Mastery, reaction, counter, combo, item, terrain, feint, or
  action-speed systems.
- Do not regenerate or edit `probes/phase1a/golden/battle-20260909.log`.

---

### Task 0: Establish the Clean Baseline

**Files:** Inspect only.

**Interfaces:** Consumes commit `bcdb01e`; produces fresh baseline evidence for
the exact tree from which implementation starts.

- [ ] **Step 1: Confirm branch and worktree state**

Run:

```powershell
git status --short --branch
git log -2 --oneline --decorate
```

Expected: `main` is ahead of `origin/main` only by the approved design and
implementation-plan commits, and no source or generated files are modified.

- [ ] **Step 2: Run the complete existing gate**

Run:

```powershell
pwsh ./probes/phase1a/launch-visual.ps1 -Verify
```

Expected: Debug and Release core tests pass, VisualTests pass, `qa.txt`,
`field-qa.txt`, and `menu-qa.txt` each end in `PASS ALL`, and no golden mismatch
or compiler warning is reported.

---

### Task 1: Define Prototype Styles and Pure Fixed-Point Rules

**Files:**

- Create: `probes/phase1a/Probe/PhysicalActions.cs`
- Create: `probes/phase1a/Probe/CombatStyles.cs`
- Create: `probes/phase1a/Tests/CombatStyleTests.cs`
- Modify: `probes/phase1a/Probe/Scenario.cs`
- Modify: `probes/phase1a/Probe/CharacterPreparation.cs`
- Modify: `probes/phase1a/Tests/Program.cs`

**Interfaces:**

- Consumes: existing `Ability`, `EffectNode`, `CharacterStats`, and
  `Scenario.Strike` behavior.
- Produces: `PrototypePhysicalActions.BasicAttack`, `PhysicalActionMath`,
  `StanceModifiers`, `PhysicalTechniqueDefinition`, `CombatStyleDefinition`,
  `CombatStyleProfile`, `CombatStyleRules`, and `PrototypeCombatStyles`.

- [ ] **Step 1: Read the test-quality rules before changing tests**

Run:

```powershell
Get-Content -Raw 'C:/Users/mikan/.codex/plugins/cache/openai-curated-remote/superpowers/6.3.0/skills/test-driven-development/writing-good-tests.md'
```

Use real definitions and pure calculations. Do not assert private fields or
copy production formulas into test helpers.

- [ ] **Step 2: Add failing definition, ownership, and calculation tests**

Create `CombatStyleTests.cs` with the existing console-test pattern and these
cases:

```csharp
using Phase1A;
using Phase1A.Preparation;
using Phase1A.Rules;
using Phase1A.Styles;

internal static class CombatStyleTests
{
    public static readonly (string Name, Action Run)[] All =
    [
        ("Prototype Styles have stable identities and exactly two owned Techniques", () =>
        {
            Check(PrototypeCombatStyles.All.Select(style => style.Id).SequenceEqual(new[]
            {
                "probe:style.sword-god", "probe:style.water-god", "probe:style.north-god"
            }), "stable Style IDs");
            Check(PrototypeCombatStyles.All.Select(style => style.JapaneseName)
                .SequenceEqual(new[] { "剣神流", "水神流", "北神流" }),
                "Japanese names");
            Check(PrototypeCombatStyles.All.Select(style => style.EnglishName)
                .SequenceEqual(new[]
                    { "Sword God Style", "Water God Style", "North God Style" }),
                "English names");
            Check(PrototypeCombatStyles.All.Select(style => style.BattleLabel)
                .SequenceEqual(new[] { "SWORD GOD", "WATER GOD", "NORTH GOD" }),
                "ASCII Battle labels");
            Check(PrototypeCombatStyles.All.All(style => style.Techniques.Length == 2),
                "each Style owns exactly two Techniques");
            Check(PrototypeCombatStyles.All.SelectMany(style => style.Techniques)
                .All(technique => technique.OwnerStyleId ==
                    PrototypeCombatStyles.All.Single(style =>
                        style.Techniques.Contains(technique)).Id),
                "Technique owner IDs match their containing Style");
            Equal(6, PrototypeCombatStyles.All.SelectMany(style => style.Techniques)
                .Select(technique => technique.Id).Distinct().Count());
            Check(PrototypeCombatStyles.All.SelectMany(style => style.Techniques)
                .Select(technique => technique.Id).SequenceEqual(new[]
                {
                    "probe:technique.sword-god.straight-slash",
                    "probe:technique.sword-god.heavy-slash",
                    "probe:technique.water-god.steady-cut",
                    "probe:technique.water-god.precise-cut",
                    "probe:technique.north-god.adaptive-cut",
                    "probe:technique.north-god.risky-cut"
                }), "stable Technique IDs");
            Check(PrototypeCombatStyles.All.SelectMany(style => style.Techniques)
                .Select(technique => technique.DisplayName).SequenceEqual(new[]
                {
                    "STRAIGHT SLASH", "HEAVY SLASH", "STEADY CUT",
                    "PRECISE CUT", "ADAPTIVE CUT", "RISKY CUT"
                }), "exact Technique display names");
            Check(PrototypeCombatStyles.All.SelectMany(style => style.Techniques)
                .All(technique => ReferenceEquals(technique.BaseAction, Scenario.Strike)),
                "Techniques reuse BASIC ATTACK");
        }),
        ("Prototype stance and Technique values are exact", () =>
        {
            Equal(new StanceModifiers(StrengthPercent: 20, DefensePercent: -20),
                PrototypeCombatStyles.SwordGod.Stance);
            Equal(new StanceModifiers(StrengthPercent: -15, DefensePercent: 20,
                ResistancePercent: 15), PrototypeCombatStyles.WaterGod.Stance);
            Equal(new StanceModifiers(StrengthPercent: 10, DefensePercent: -10,
                ResistancePercent: 10), PrototypeCombatStyles.NorthGod.Stance);
            Check(PrototypeCombatStyles.All.SelectMany(style => style.Techniques)
                .Select(technique =>
                    (technique.DamagePercent, technique.AccuracyPercent))
                .SequenceEqual(new[]
                {
                    (110, 100), (125, 85), (90, 110),
                    (100, 105), (100, 110), (115, 90)
                }), "exact Technique multipliers");
        }),
        ("Shifted stance attenuates only positive magnitudes", () =>
        {
            var stats = new CharacterStats(500, 200, 100, 100, 100, 100, 100);
            Equal(stats with { Strength = 120, Defense = 80 },
                CombatStyleRules.ApplyStance(
                    stats, PrototypeCombatStyles.SwordGod.Stance, shifted: false));
            Equal(stats with { Strength = 85, Defense = 120, Resistance = 115 },
                CombatStyleRules.ApplyStance(
                    stats, PrototypeCombatStyles.WaterGod.Stance, shifted: false));
            Equal(stats with { Strength = 110, Defense = 90, Resistance = 110 },
                CombatStyleRules.ApplyStance(
                    stats, PrototypeCombatStyles.NorthGod.Stance, shifted: false));
            Equal(stats with { Strength = 117, Defense = 80 },
                CombatStyleRules.ApplyStance(
                    stats, PrototypeCombatStyles.SwordGod.Stance, shifted: true));
            Equal(stats with { Strength = 85, Defense = 117, Resistance = 113 },
                CombatStyleRules.ApplyStance(
                    stats, PrototypeCombatStyles.WaterGod.Stance, shifted: true));
            Equal(stats with { Strength = 109, Defense = 90, Resistance = 109 },
                CombatStyleRules.ApplyStance(
                    stats, PrototypeCombatStyles.NorthGod.Stance, shifted: true));
            Equal(100, CombatStyleRules.ApplyStance(
                stats, PrototypeCombatStyles.NorthGod.Stance, shifted: true).Agility);
        }),
        ("Technique hit chances use exact millionths", () =>
        {
            var techniques = PrototypeCombatStyles.All
                .SelectMany(style => style.Techniques).ToArray();
            Check(techniques.Select(technique =>
                    CombatStyleRules.HitChanceMillionths(technique, false))
                .SequenceEqual(new[] { 900000, 765000, 990000, 945000, 990000, 810000 }),
                "established chances");
            Check(techniques.Select(technique =>
                    CombatStyleRules.HitChanceMillionths(technique, true))
                .SequenceEqual(new[] { 765000, 650250, 841500, 803250, 841500, 688500 }),
                "shifted chances");
        }),
        ("Preparation knows all Styles and fixes Sword God as Primary", () =>
        {
            var player = new CharacterPreparation(Scenario.Setup(7).Actors[0].InitialStats);
            Check(player.KnownCombatStyles.SequenceEqual(PrototypeCombatStyles.All),
                "all Styles known");
            Equal(PrototypeCombatStyles.SwordGod, player.PrimaryCombatStyle);
        }),
        ("Physical scaling combines percentages once and rounds at the damage boundary", () =>
        {
            Equal(1_000_000, PhysicalActionMath.ScaleFromPercents(100, false));
            Equal(850_000, PhysicalActionMath.ScaleFromPercents(100, true));
            Equal(1_062_500, CombatStyleRules.TechniqueDamageScaleMillionths(
                PrototypeCombatStyles.SwordGod.Techniques[1], shifted: true));
            Equal(3, PhysicalActionMath.ApplyDamageScale(4, 850_000));
            Equal(18, PhysicalActionMath.ApplyDamageScale(17, 1_062_500));
        }),
        ("Malformed Style definitions profiles and modifiers are rejected", () =>
        {
            var sword = PrototypeCombatStyles.SwordGod;
            Throws(() => new CombatStyleProfile(
                [sword, sword], sword.Id));
            Throws(() => new CombatStyleProfile(
                PrototypeCombatStyles.All, "fixture:unknown"));
            Throws(() => CombatStyleRules.ApplyStance(
                new CharacterStats(100, 0, 10, 10, 10, 10, 10),
                new StanceModifiers(DefensePercent: -100), shifted: false));
            Throws(() => new PhysicalTechniqueDefinition(
                "", "Invalid", sword.Id, 100, 100,
                PrototypePhysicalActions.BasicAttack));
            Throws(() => new PhysicalTechniqueDefinition(
                "fixture:invalid", "Invalid", sword.Id, 0, 100,
                PrototypePhysicalActions.BasicAttack));
        })
    ];

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Equal<T>(T expected, T actual) => Check(
        EqualityComparer<T>.Default.Equals(expected, actual),
        $"expected {expected}, got {actual}");

    private static void Throws(Action action)
    {
        try { action(); }
        catch (Exception error) when (error is ArgumentException or
            InvalidOperationException or OverflowException) { return; }
        throw new Exception("Expected a rejected Combat Style value.");
    }
}
```

Register the suite in `Tests/Program.cs` after `MagicTests.All`.

- [ ] **Step 3: Run Debug core tests and confirm RED**

Run:

```powershell
pwsh ./probes/phase1a/verify.ps1 -Configuration Debug
```

Expected: compilation fails because `Phase1A.Styles`, the definition types, and
the new preparation properties do not exist.

- [ ] **Step 4: Add one authoritative BASIC ATTACK and generic damage scaling**

Create `PhysicalActions.cs` in `Phase1A.Rules`:

```csharp
namespace Phase1A.Rules;

public static class PrototypePhysicalActions
{
    public static readonly Ability BasicAttack = new("probe:ability.strike", 0,
        [new(OpKind.Damage, new(TargetScope.Selected), new(Base: 2, Variance: 2),
            DamageKind: DamageKind.Physical)]);
}

public static class PhysicalActionMath
{
    public const int OneMillion = 1_000_000;

    public static int ScaleFromPercents(int actionPercent, bool shifted)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(actionPercent);
        return checked((int)((long)OneMillion * actionPercent *
            (shifted ? 85 : 100) / 10_000));
    }

    public static int ApplyDamageScale(int damage, int scaleMillionths)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(damage);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(scaleMillionths);
        var result = checked(((long)damage * scaleMillionths +
            OneMillion / 2) / OneMillion);
        return checked((int)Math.Max(1, result));
    }
}
```

Replace `Scenario.Strike`'s literal construction with:

```csharp
public static readonly Ability Strike = PrototypePhysicalActions.BasicAttack;
```

- [ ] **Step 5: Add immutable Style data and pure rules**

Create `CombatStyles.cs` in `Phase1A.Styles` with this public surface:

```csharp
public readonly record struct StanceModifiers(
    int StrengthPercent = 0, int DefensePercent = 0,
    int ResistancePercent = 0);

public sealed record PhysicalTechniqueDefinition(
    string Id, string DisplayName, string OwnerStyleId,
    int DamagePercent, int AccuracyPercent, Ability BaseAction);

public sealed record CombatStyleDefinition(
    string Id, string JapaneseName, string EnglishName, string BattleLabel,
    StanceModifiers Stance, ImmutableArray<PhysicalTechniqueDefinition> Techniques);

public sealed record CombatStyleProfile(
    ImmutableArray<CombatStyleDefinition> KnownStyles, string PrimaryStyleId)
{
    public CombatStyleDefinition? FindStyle(string id) =>
        KnownStyles.FirstOrDefault(style => style.Id == id);
}

public static class CombatStyleRules
{
    public const int BaseTechniqueHitChanceMillionths = 900_000;
    public static CharacterStats ApplyStance(
        CharacterStats stats, StanceModifiers stance, bool shifted);
    public static int HitChanceMillionths(
        PhysicalTechniqueDefinition technique, bool shifted);
    public static int TechniqueDamageScaleMillionths(
        PhysicalTechniqueDefinition technique, bool shifted) =>
        PhysicalActionMath.ScaleFromPercents(technique.DamagePercent, shifted);
}
```

`ApplyStance` scales Strength, Defense, and Resistance only. MaxHP, MaxMP,
Magic, and Agility are copied through unchanged; do not add modifier fields or
rule branches for them. For a modifier `m`, use `m * 85` basis points when
`m > 0 && shifted`, otherwise use `m * 100`; calculate
`(value * (10000 + basisPoints) + 5000) / 10000` with checked `long`
intermediates. Reject modifiers at or below -100%.

Define `PrototypeCombatStyles.SwordGod`, `.WaterGod`, `.NorthGod`, `.All`, and
`.PlayerProfile` with every exact ID/name/value from the spec. All Techniques
reference `PrototypePhysicalActions.BasicAttack`. Implement construction-time
validation with redeclared init-property initializers on the positional records,
or equivalent explicit immutable class constructors, while preserving the call
signatures above. Validate nonempty unique IDs, two Techniques per Style,
matching owner IDs, positive multipliers, and a known Primary ID.

- [ ] **Step 6: Expose fixed Style knowledge from preparation**

Add and initialize:

```csharp
public IReadOnlyList<CombatStyleDefinition> KnownCombatStyles { get; }
public CombatStyleDefinition PrimaryCombatStyle { get; }

KnownCombatStyles = PrototypeCombatStyles.All;
PrimaryCombatStyle = PrototypeCombatStyles.SwordGod;
```

Do not add learning or Primary-selection methods.

- [ ] **Step 7: Run core tests GREEN in Debug and Release**

Run:

```powershell
pwsh ./probes/phase1a/verify.ps1 -Configuration Debug
pwsh ./probes/phase1a/verify.ps1 -Configuration Release
```

Expected: existing tests and all seven new cases pass; the golden remains exact.

- [ ] **Step 8: Audit and commit the domain slice**

Run:

```powershell
git diff --check
git diff --name-only -- probes/phase1a/golden
git status --short
```

Commit:

```powershell
git add probes/phase1a/Probe/PhysicalActions.cs probes/phase1a/Probe/CombatStyles.cs probes/phase1a/Probe/Scenario.cs probes/phase1a/Probe/CharacterPreparation.cs probes/phase1a/Tests/CombatStyleTests.cs probes/phase1a/Tests/Program.cs
git commit -m "feat: define prototype combat styles"
```

---

### Task 2: Make Battle Own Style State and Physical Resolution

**Files:**

- Modify: `probes/phase1a/Probe/Rules.cs`
- Modify: `probes/phase1a/Probe/BattleState.cs`
- Modify: `probes/phase1a/Probe/CharacterPreparation.cs`
- Modify: `probes/phase1a/Tests/CombatStyleTests.cs`
- Modify: `probes/phase1a/Tests/EquipmentTests.cs`
- Modify: `probes/phase1a/Tests/FieldTests.cs`

**Interfaces:**

- Consumes: `PrototypeCombatStyles.PlayerProfile`, `CombatStyleRules`,
  `PhysicalActionMath`, and the existing `EffectRunner`.
- Produces: optional `ActorSeed.StyleProfile`, `CombatStyleSnapshot`,
  `ReadCombatStyle(int)`, `TryChangeStyle(int,string)`,
  `CommandKind.Technique`, optional `Command.TechniqueId`, Technique misses,
  and per-action physical damage scaling.

- [ ] **Step 1: Add failing ownership and lifecycle tests**

Add this real-Battle fixture to `CombatStyleTests.cs`:

```csharp
private static BattleState StyledBattle(
    ulong seed = 7, CharacterStats? hero = null,
    CombatStyleDefinition? primary = null, int enemyCount = 1)
{
    var profile = new CombatStyleProfile(
        PrototypeCombatStyles.All, (primary ?? PrototypeCombatStyles.SwordGod).Id);
    var actors = new List<ActorSeed>
    {
        new("fixture:hero", "hero", Side.Adventurers,
            hero ?? new CharacterStats(500, 20, 100, 100, 100, 100, 100),
            StyleProfile: profile)
    };
    for (var i = 0; i < enemyCount; i++)
        actors.Add(new($"fixture:enemy-{i}", null, Side.Monsters,
            new CharacterStats(500, 0, 10, 0, 0, 0, 0)));
    return new BattleState(new([.. actors], seed));
}
```

Test initial Sword state; known Style snapshots; one `StyleChanged` event per
actual change; same-Style success without an event; unknown/off-turn rejection
without mutation; away-and-back removal of Shift; and persistence of shifted
stance across two enemy actions. Also finish a fixture and prove further Style
changes are rejected. Compare a later valid action against an untouched
same-seed fixture to prove rejected changes and away/back changes consume
neither effect nor Technique-hit RNG. Use exact assertions:

```csharp
var battle = StyledBattle(enemyCount: 2);
Equal(PrototypeCombatStyles.SwordGod.Id,
    battle.ReadCombatStyle(0)!.ActiveStyle.Id);
Check(battle.TryChangeStyle(0, PrototypeCombatStyles.WaterGod.Id), "shift");
Equal(new CharacterStats(500, 20, 85, 117, 100, 113, 100),
    battle.Read(0).EffectiveStats);
Check(battle.TakeTurn(new(0, null, -1, CommandKind.Defend)), "Defend");
Check(battle.ReadCombatStyle(0)!.Shifted, "shift survives enemy responses");
Check(battle.TakeTurn(new(1, Scenario.Strike, 0)), "enemy one");
Check(battle.ReadCombatStyle(0)!.Shifted, "still shifted");
Check(battle.TakeTurn(new(2, Scenario.Strike, 0)), "enemy two");
Check(!battle.ReadCombatStyle(0)!.Shifted, "established on player return");
Equal(new CharacterStats(500, 20, 85, 120, 100, 115, 100),
    battle.Read(0).EffectiveStats);
```

Create a `CharacterPreparation`, begin a Battle, and assert the player has all
three Styles with Sword active while monster snapshots return null.

- [ ] **Step 2: Add failing Technique and penalty tests**

Add tests for:

- foreign-Style Technique rejection before commit;
- dead, friendly, and missing Technique-target rejection with the turn still
  available;
- rejected Techniques preserving both RNG streams, demonstrated by the next
  valid same-seed Technique matching an untouched control;
- deterministic hit and miss using `battle.technique-hit`;
- miss event order, no damage, status tick, and turn advancement;
- Adaptive Cut and BASIC ATTACK producing identical damage variance when both
  have a 1.00 damage multiplier;
- BASIC ATTACK remaining guaranteed-hit even at a seed whose Technique roll
  misses, and not consuming `battle.technique-hit` as shown by the following
  Technique matching a Defend-based same-seed control;
- shifted BASIC physical damage ×0.85;
- combined Technique Damage and Shift multiplication applied once;
- Magical and Prototype nodes remaining unscaled; and
- Agility changes not affecting hit chance or deterministic Technique results.

Use public RNG helpers:

```csharp
private static ulong FindTechniqueSeed(
    PhysicalTechniqueDefinition technique, bool shifted, bool hit)
{
    var chance = CombatStyleRules.HitChanceMillionths(technique, shifted);
    for (ulong seed = 0; seed < 100_000; seed++)
    {
        var roll = new DeterministicRng(
            seed, "battle.technique-hit").NextInclusive(999_999);
        if ((roll < chance) == hit) return seed;
    }
    throw new Exception("No deterministic Technique seed found.");
}

private static ulong FindEffectVarianceSeed(int expected)
{
    for (ulong seed = 0; seed < 100_000; seed++)
        if (new DeterministicRng(
            seed, "battle.effect").NextInclusive(2) == expected) return seed;
    throw new Exception("No deterministic effect seed found.");
}
```

Pin BASIC damage with a zero-Strength fixture and variance 2:

```csharp
var seed = FindEffectVarianceSeed(2);
var basic = StyledBattle(
    seed, new CharacterStats(500, 20, 0, 0, 0, 0, 999));
Check(basic.TryChangeStyle(0, PrototypeCombatStyles.WaterGod.Id), "shift");
Check(basic.TakeTurn(new(0, Scenario.Strike, 1)), "shifted BASIC");
Equal(3, basic.Events.Single(e => e.Kind == "Damaged").Amount);
```

For combined Technique damage, calculate the expected value from the public
`PhysicalDamage.Calculate`, `PhysicalActionMath.ApplyDamageScale`, and
`CombatStyleRules.TechniqueDamageScaleMillionths` APIs using the same seed.

Update `EquipmentTests` before production changes. Out-of-Battle expectations
remain unchanged; Battle snapshots use established Sword stance:

```csharp
Equal(Base with { MaxHp = 85, Strength = 18, Defense = 6 },
    battle.Read(0).EffectiveStats);
Equal(18, battle.Read(0).Strength);
Equal(6, battle.Read(0).Defense);
Check(Enum.GetNames<CommandKind>().SequenceEqual(
    new[] { "Ability", "Defend", "Run", "Technique" }),
    "no equipment battle command");
```

Rename the existing Wooden Sword damage test and change its same-seed delta
from 3 to 4 because established Sword scales the snapshotted +3 Strength:

```csharp
for (ulong seed = 0; seed < 12; seed++)
    Equal(4, Damage(seed, sword: true, armor: false, incoming: false) -
        Damage(seed, sword: false, armor: false, incoming: false));
```

In `FieldTests`, change the equipped Battle snapshot assertion from `15/12` to
the established Sword values:

```csharp
Equal(18, battle.Read(0).Strength);
Equal(10, battle.Read(0).Defense);
```

Keep the out-of-Battle equipment assertions at `15/12`; do not change Field
movement, persistence, encounter, or outcome expectations.

- [ ] **Step 3: Run Debug core tests and confirm RED**

Run:

```powershell
pwsh ./probes/phase1a/verify.ps1 -Configuration Debug
```

Expected missing APIs: `StyleProfile`, `CombatStyleSnapshot`,
`ReadCombatStyle`, `TryChangeStyle`, `CommandKind.Technique`, and
`TechniqueId`.

- [ ] **Step 4: Snapshot and resolve Battle-owned Style state**

Extend `ActorSeed` at the end so positional callers keep compiling:

```csharp
public sealed record ActorSeed(
    string DefinitionId, string? InstanceId, Side Side,
    CharacterStats InitialStats, int? Hp = null, int? Mp = null,
    CombatStyleProfile? StyleProfile = null);
```

Add:

```csharp
public sealed record CombatStyleSnapshot(
    ImmutableArray<CombatStyleDefinition> KnownStyles,
    CombatStyleDefinition ActiveStyle,
    string TurnStartStyleId,
    bool Shifted);
```

Store nullable Active/turn-start IDs in `BattleState.Actor`, initialized from
the validated profile Primary ID. Implement `ReadCombatStyle(int)` by resolving
the Active ID from the snapshotted profile. Implement:

```csharp
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
```

`Read` first calls the existing `StatResolver.Resolve`, then applies the active
stance. Extract next-actor advancement into a helper and set the newly selected
actor's turn-start ID to its Active ID only there.

In `CharacterPreparation.BeginBattleCore`, attach:

```csharp
var styleProfile = new CombatStyleProfile(
    [.. KnownCombatStyles], PrimaryCombatStyle.Id);
var seed = template.Actors[actorId] with
{
    InitialStats = EffectiveStats,
    Hp = Hp,
    Mp = Mp,
    StyleProfile = styleProfile
};
```

- [ ] **Step 5: Thread one physical scale through the effect path**

Extend signatures with a default:

```csharp
public static OpResult Execute(
    OpContext context, EffectNode node, int magnitude,
    int physicalDamageScaleMillionths = PhysicalActionMath.OneMillion);

public static ImmutableArray<NodeResult> Apply(
    IEffectState state, int casterId, int selectedId,
    ImmutableArray<EffectNode> nodes, DeterministicRng rng,
    int physicalDamageScaleMillionths = PhysicalActionMath.OneMillion);
```

Apply `PhysicalActionMath.ApplyDamageScale` only to the result of
`PhysicalDamage.Calculate`. Leave Magical and Prototype cases unchanged, and
leave Guard/HP clamping after scaling.

- [ ] **Step 6: Resolve authoritative Technique commands**

Extend:

```csharp
public enum CommandKind { Ability, Defend, Run, Technique }
public sealed record Command(
    int ActorId, Ability? Ability, int TargetId,
    CommandKind Kind = CommandKind.Ability, string? TechniqueId = null);
```

Append Technique so the existing underlying values for Ability, Defend, and
Run remain unchanged.

Create `_techniqueHitRng` with stream `battle.technique-hit`. Resolve a
Technique ID only from the Active Style's two definitions. Enforce the command
shapes: Ability has a non-null Ability and null Technique ID; Technique has a
null caller Ability and nonempty Technique ID; Defend/Run have neither.
Technique commands additionally require a legal living enemy; use the resolved
definition's `BaseAction`. After resolution, set `ActionStarted.Detail` to the
stable Technique ID (and retain the underlying Ability ID for BASIC/legacy
Ability commands).

After `ActionStarted` and cost commitment:

```csharp
var style = ReadCombatStyle(command.ActorId);
var shifted = style?.Shifted == true;
var damageScale = technique is null
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
        ability.Effects, _rng, damageScale);
}
```

Hit and miss share status ticking, `TurnEnded`, and scheduler advancement.
Styleless actors remain scale 1.00 and never enter the Technique branch.

- [ ] **Step 7: Run all core tests GREEN**

Run:

```powershell
pwsh ./probes/phase1a/verify.ps1 -Configuration Debug
pwsh ./probes/phase1a/verify.ps1 -Configuration Release
```

Expected: all new rule cases, equipment cases, and the byte-exact golden pass
with zero warnings.

- [ ] **Step 8: Audit and commit Battle rules**

```powershell
git diff --check
git diff --name-only -- probes/phase1a/golden
git add probes/phase1a/Probe/Rules.cs probes/phase1a/Probe/BattleState.cs probes/phase1a/Probe/CharacterPreparation.cs probes/phase1a/Tests/CombatStyleTests.cs probes/phase1a/Tests/EquipmentTests.cs probes/phase1a/Tests/FieldTests.cs
git commit -m "feat: resolve battle style shifting"
```

---

### Task 3: Add the Physical Style Presentation Flow

**Files:**

- Modify: `probes/phase1a/Visual/Presentation/BattleSession.cs`
- Modify: `probes/phase1a/Visual/Presentation/HarnessController.cs`
- Modify: `probes/phase1a/VisualTests/Program.cs`
- Modify: `probes/phase1a/VisualTests/FieldLoopTests.cs`

**Interfaces:**

- Consumes: Battle Style snapshots, free changes, Technique commands, and the
  existing target/message loop.
- Produces: immutable Style/Technique views, `SubmitBasicAttack`,
  `SubmitTechnique`, and `ScreenMode.PhysicalActions`.

- [ ] **Step 1: Write failing read-model and controller tests**

Assert a new `BattleSession` exposes known labels
`SWORD GOD / WATER GOD / NORTH GOD`, Active/turn-start Sword, Shift false, and
`STRAIGHT SLASH / HEAVY SLASH`. Assert session switching to Water immediately
replaces those with `STEADY CUT / PRECISE CUT`.

Add this interaction test:

```csharp
var ui = Started();
Choose(ui, "ATTACK");
Equal(ScreenMode.PhysicalActions, ui.Mode);
Check(ui.PhysicalActionLabels.SequenceEqual(
    new[] { "STRAIGHT SLASH", "HEAVY SLASH", "BASIC ATTACK" }),
    "Sword physical actions");
var actions = ui.Session.Events.Count(e => e.Kind == "ActionStarted");
Check(ui.Session.IsPlayerTurn, "player turn active");
ui.Handle(UiInput.Right);
Equal("probe:style.water-god",
    ui.Session.View.PhysicalStyle!.ActiveStyleId);
Check(ui.PhysicalActionLabels.SequenceEqual(
    new[] { "STEADY CUT", "PRECISE CUT", "BASIC ATTACK" }),
    "Water physical actions");
Check(ui.Session.View.PhysicalStyle.Shifted, "switch is real");
Check(ui.Session.IsPlayerTurn, "switch keeps the turn");
Equal(actions, ui.Session.Events.Count(e => e.Kind == "ActionStarted"));
ui.Handle(UiInput.Down); ui.Handle(UiInput.Down);
ui.Handle(UiInput.Left);
Equal(2, ui.PhysicalActionIndex);
Check(!ui.Session.View.PhysicalStyle!.Shifted,
    "return to turn-start removes Shift");
ui.Handle(UiInput.Confirm);
Equal(ScreenMode.Targets, ui.Mode);
ui.Handle(UiInput.Back);
Equal(ScreenMode.PhysicalActions, ui.Mode);
Equal(2, ui.PhysicalActionIndex);
ui.Handle(UiInput.Back);
Equal(ScreenMode.Menu, ui.Mode);
```

Add seeded Technique hit/miss presentation tests with exact messages:
`Adventurer uses Straight Slash on Goblin!` and
`Adventurer misses Goblin with Heavy Slash.`. Assert BASIC produces the
existing `Adventurer attacks Goblin!` and `ActionStarted.Detail ==
Scenario.Strike.Id`.

Add an edge-navigation assertion that repeated Right at North and repeated Left
at Sword stay clamped, retain the selected row, consume no turn, and add no
duplicate `StyleChanged` event at the edge.

Update all direct root-ATTACK target assumptions in Program and FieldLoop tests
to traverse the Physical screen.

- [ ] **Step 2: Run VisualTests and confirm RED**

Run:

```powershell
& ./.tools/dotnet/dotnet.exe restore ./probes/phase1a/VisualTests/Phase1A.VisualTests.csproj --configfile ./probes/phase1a/NuGet.Config -p:NuGetAudit=false
& ./.tools/dotnet/dotnet.exe build ./probes/phase1a/VisualTests/Phase1A.VisualTests.csproj --no-restore
```

Expected: the VisualTests build fails because the new view records, session
methods, mode, and controller properties are absent.

- [ ] **Step 3: Expose immutable Presentation views and submit routes**

Add:

```csharp
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
```

Add nullable `PhysicalStyleView? PhysicalStyle` to `BattleView` and map it fresh
from `battle.ReadCombatStyle(0)` on every read.

Add:

```csharp
public bool IsPlayerTurn => !battle.IsFinished && battle.NextActorId == 0;
public bool TryChangeStyle(string styleId) =>
    battle.TryChangeStyle(0, styleId);
public bool SubmitBasicAttack(int targetId) =>
    battle.LivingEnemies(0).Contains(targetId) &&
    Resolve(new(0, Scenario.Strike, targetId));
public bool SubmitTechnique(string techniqueId, int targetId) =>
    battle.LivingEnemies(0).Contains(targetId) &&
    Resolve(new(0, null, targetId, CommandKind.Technique, techniqueId));
```

Keep `Submit(MenuAction.Attack, target)` as a compatibility delegation to
`SubmitBasicAttack`. Build the default session with a local helper that creates
a `CharacterPreparation` from `Scenario.Setup(seed)` and starts that setup, so
direct presentation fixtures receive the same Style profile as Field Battles.

Map Technique `ActionStarted` and `Missed` events through a stable-ID dictionary
created from `PrototypeCombatStyles.All`; retain the existing Strike message.
For prose only, convert each all-caps Technique display name to deterministic
title case by lowercasing invariantly and uppercasing each word's first ASCII
letter. This yields the exact `uses Straight Slash` / `misses ... with Heavy
Slash` messages without using that text for identity or rule decisions.

- [ ] **Step 4: Implement the PhysicalActions controller state**

Add `PhysicalActions` between Menu and MagicAdjustment in `ScreenMode`. Replace
the pending Attack variant with:

```csharp
public enum TargetAction { None, BasicAttack, Technique, Fireball }
```

Add:

```csharp
private string? pendingTechniqueId;
public int PhysicalActionIndex { get; private set; }
public PhysicalStyleView PhysicalStyle => Session.View.PhysicalStyle ??
    throw new InvalidOperationException(
        "The player Battle requires a Style profile.");
public IReadOnlyList<string> PhysicalActionLabels =>
    PhysicalStyle.Techniques.Select(technique => technique.DisplayName)
        .Append("BASIC ATTACK").ToArray();
```

On root ATTACK, enter PhysicalActions at row zero. Up/Down clamp across rows
0..2. Left/Right clamp across known Style order and call `TryChangeStyle`
immediately while preserving the row. Enter stores either the current
Technique ID or BASIC and opens existing Targets. Back returns to root without
another core call.

Target Back returns physical targets to PhysicalActions with row intact and
returns Fireball to MagicAdjustment. Target confirmation dispatches Technique,
BASIC, or Fireball explicitly. After an action, clear pending identity and use
the existing message/root reset. Also clear pending physical identity when a
target is cancelled or PhysicalActions returns to root; preserve only the
visible Style and selected row required by the navigation contract.

Set the physical breadcrumb to `ATTACK > {ActiveLabel}` plus ` > SHIFT` when
shifted. BASIC target breadcrumb is `BASIC ATTACK > CHOOSE TARGET`; Technique
target breadcrumb uses the Technique display name.

- [ ] **Step 5: Update presentation helpers and expectations**

Add:

```csharp
static void OpenPhysical(HarnessController ui)
{
    Choose(ui, "ATTACK");
    Equal(ScreenMode.PhysicalActions, ui.Mode);
}

static void OpenBasicTarget(HarnessController ui)
{
    OpenPhysical(ui);
    ui.Handle(UiInput.Down);
    ui.Handle(UiInput.Down);
    ui.Handle(UiInput.Confirm);
    Equal(ScreenMode.Targets, ui.Mode);
}
```

Use these in every affected test. Keep WIP RNG comparisons on BASIC ATTACK.
Update Battle effective stats to bare Sword `STR 14 / DEF 6` and equipped Sword
`STR 18 / DEF 10`; preparation values remain unchanged. In the existing Wooden
Sword presentation check, update the same-seed dealt-damage delta from `3` to
`4`: the weapon raises pre-stance STR by 3, and Sword's +20% stance turns the
Battle STR difference into 4.

- [ ] **Step 6: Run core and VisualTests GREEN**

Run:

```powershell
pwsh ./probes/phase1a/verify.ps1 -Configuration Debug
pwsh ./probes/phase1a/verify.ps1 -Configuration Release
& ./.tools/dotnet/dotnet.exe restore ./probes/phase1a/VisualTests/Phase1A.VisualTests.csproj --configfile ./probes/phase1a/NuGet.Config -p:NuGetAudit=false
& ./.tools/dotnet/dotnet.exe build ./probes/phase1a/VisualTests/Phase1A.VisualTests.csproj --no-restore
& ./.tools/dotnet/dotnet.exe ./probes/phase1a/VisualTests/bin/Debug/net8.0/Phase1A.VisualTests.dll
```

Expected: both core configurations and all VisualTests pass with zero warnings.
The Godot source/QA transition remains isolated to Task 4.

- [ ] **Step 7: Audit and commit Presentation**

```powershell
git diff --check
git add probes/phase1a/Visual/Presentation/BattleSession.cs probes/phase1a/Visual/Presentation/HarnessController.cs probes/phase1a/VisualTests/Program.cs probes/phase1a/VisualTests/FieldLoopTests.cs
git commit -m "feat: add physical style battle flow"
```

---

### Task 4: Render and Drive the Flow in Godot

**Files:**

- Modify: `probes/phase1a/Visual/BattleScreen.cs`
- Modify: `probes/phase1a/Visual/HarnessQa.cs`
- Modify: `probes/phase1a/Visual/FieldQa.cs`

**Interfaces:** Consumes `PhysicalActions` Presentation state and existing
`DrawCommandGrid`; produces the visible one-column screen, render-observation
values for the existing partial-class QA, and real-input acceptance coverage.

- [ ] **Step 1: Add failing real-input QA**

In HarnessQa, start Battle, press Enter on ATTACK, and assert the exact Sword
labels. Press D and assert `ATTACK > WATER GOD > SHIFT` plus Water's two labels
and BASIC. After one rendered frame, assert the renderer selected one column
and those exact labels:

```csharp
await Frames();
Check(lastDrawnCommandColumns == 1, "Physical Style renders one column");
Check(lastDrawnCommandLabels.SequenceEqual(
    new[] { "STEADY CUT", "PRECISE CUT", "BASIC ATTACK" }),
    "renderer draws the live Water action list");
```

Capture `04-physical-water-shift.png`. Press A and assert Shift is removed.
Select BASIC, cancel target back to row two, then Back to root.

In FieldQa, switch to Water, Back to root, reopen ATTACK, and assert Water
remains active and shifted. Return to Sword before the guaranteed-hit victory
loop. Update every existing Attack sequence to choose BASIC as row three and
update Battle stat expectations to bare `14/6` and equipped `18/10`.

Place both deliberate Style-switch sequences after the existing WIP-navigation
checks that compare `MachineText`, or recapture that baseline immediately after
the switch sequence. A Style switch correctly appends a core event, so an old
`untouched`/`beforeWip` value must not be compared across it.

In HarnessQa, update the Wooden Sword same-seed damage delta from `3` to `4`.
In FieldQa, do not compare `Session.View.Hero.EffectiveStats` directly with the
out-of-Battle preparation stats: derive the Battle expectation by applying the
unshifted Sword stance to that preparation snapshot. Keep the later persistent
preparation/equipment assertions unchanged, because stance projection must not
mutate preparation state.

After the existing guaranteed BASIC comparison in HarnessQa, finish its
messages, reopen ATTACK on row zero, confirm Straight Slash against Goblin, and
assert both the exact ActionStarted detail
`probe:technique.sword-god.straight-slash` and the visible
`Adventurer uses Straight Slash on Goblin!` message. With the default
`20260909` seed this first Technique check is deterministically a hit. Capture
the message screen so real-input QA covers an executed Technique as well as the
three-row selection screen.

- [ ] **Step 2: Run full verification and confirm renderer/QA RED**

```powershell
pwsh ./probes/phase1a/launch-visual.ps1 -Verify
```

Expected: compilation fails on the newly referenced render-observation fields,
which are intentionally absent at this RED step. A toolchain/import error is
not the expected result.

- [ ] **Step 3: Render through the existing command grid**

Add private fields to `BattleScreen`:

```csharp
private IReadOnlyList<string> lastDrawnCommandLabels = [];
private int lastDrawnCommandColumns;
```

After each `DrawCommands` branch chooses `entries` and `columns`, assign those
two fields immediately before `DrawCommandGrid`. Insert this branch between
Targets and ordinary Menu:

```csharp
else if (ui.Mode == ScreenMode.PhysicalActions)
{
    entries = ui.PhysicalActionLabels;
    selected = ui.PhysicalActionIndex;
    columns = 1;
}
```

Do not add a modal, font, color, animation, or alternate input path.

- [ ] **Step 4: Run full verification GREEN and inspect the capture**

```powershell
pwsh ./probes/phase1a/launch-visual.ps1 -Verify
Get-Item ./probes/phase1a/artifacts/visual/04-physical-water-shift.png
```

Expected: all tests and three QA reports pass. Inspect that exact PNG with the
workspace image viewer for breadcrumb, Water labels, BASIC, cursor, clipping,
overlap, and palette.

- [ ] **Step 5: Audit and commit engine QA**

```powershell
git diff --check
git add probes/phase1a/Visual/BattleScreen.cs probes/phase1a/Visual/HarnessQa.cs probes/phase1a/Visual/FieldQa.cs
git commit -m "test: cover combat styles in godot"
```

Never stage `probes/phase1a/artifacts`.

---

### Task 5: Document and Verify the Complete Slice

**Files:**

- Modify: `probes/phase1a/README.md`
- Modify: `probes/phase1a/MENU_ARCHITECTURE.md`

**Interfaces:** Consumes verified behavior; produces current operator and
architecture documentation without changing rules.

- [ ] **Step 1: Update current behavior documentation**

In README, replace direct ATTACK targeting with the Style screen controls. Add
the three Style IDs, six Technique IDs, both modifier tables, 90% Technique
base chance, `battle.technique-hit`, Shift lifecycle, and BASIC guaranteed-hit
plus shifted Damage ×0.85. State that Agility and round-robin are unchanged.
Add PhysicalActions.cs, CombatStyles.cs, and CombatStyleTests.cs to the file
table. Use the fresh successful counts printed by Task 4.

- [ ] **Step 2: Append the implemented architecture section**

Add `## 22. Combat Styles V1 extension` to MENU_ARCHITECTURE.md. Record the
approved UI, immediate/back-persistent switching, turn-start boundary, exact
stances/Techniques, BASIC reuse, hit stream, physical-action penalty, stable
events, and explicit exclusions in present tense. Leave historical sections
unchanged.

- [ ] **Step 3: Audit documentation and protected paths**

```powershell
rg -n "Agility remains unused|battle.technique-hit|BASIC ATTACK|probe:style.sword-god|probe:technique.north-god.risky-cut" probes/phase1a/README.md probes/phase1a/MENU_ARCHITECTURE.md
git diff --check
git diff --name-only bcdb01e..HEAD -- probes/phase1a/golden
git ls-files | rg '(^|/)\.DS_Store$|(^|/)\.tools/|(^|/)(bin|obj|artifacts|\.godot)/'
```

Expected: required terms are present; whitespace and golden queries are clean;
the final generated-file query returns no tracked path.

- [ ] **Step 4: Run the exact final gate**

```powershell
pwsh ./probes/phase1a/launch-visual.ps1 -Verify
Get-Content ./probes/phase1a/artifacts/visual/qa.txt -Tail 1
Get-Content ./probes/phase1a/artifacts/visual/field-qa.txt -Tail 1
Get-Content ./probes/phase1a/artifacts/visual/menu-qa.txt -Tail 1
git diff --check
```

Expected: zero failures/warnings, three `PASS ALL` lines, byte-exact golden, and
no diff-check output.

- [ ] **Step 5: Commit documentation**

```powershell
git add probes/phase1a/README.md probes/phase1a/MENU_ARCHITECTURE.md
git commit -m "docs: document combat styles v1"
```

- [ ] **Step 6: Verify the exact committed result**

```powershell
git status --short --branch
git log -7 --oneline --decorate
git diff bcdb01e..HEAD --check
git diff --stat bcdb01e..HEAD
pwsh ./probes/phase1a/launch-visual.ps1 -Verify
```

Expected: clean tree, five implementation commits after the plan/design
history, clean cumulative diff, and a second fresh full pass. Report actual
core, VisualTests, Battle QA, Field QA, and Menu QA counts, unchanged golden,
and the inspected Style-screen screenshot.
