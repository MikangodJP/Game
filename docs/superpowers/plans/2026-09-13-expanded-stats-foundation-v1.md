# Expanded Stats Foundation V1 Implementation Plan

> **Execution note:** Implement only after the owner approves the linked design.
> Execute sequentially in one working session. Keep every commit buildable, use
> focused tests during Tasks 1-3, and run the full visual/engine verifier exactly
> once in Task 4.

**Goal:** Replace the Phase 1A seven-position stat plumbing with a closed,
enum-indexed expanded stat foundation while preserving every current gameplay
formula, Combat Style rule, Fireball behavior, Status output, and scheduler
decision.

**Architecture:** `CharacterStats` becomes one immutable dense value block keyed
by `StatId`; `StatCatalog` owns stable identity and metadata; builders replace
large test/content constructors. Typed frozen modifiers feed one deterministic
`StatResolver`. Legacy constructor/property names temporarily map into the same
canonical block. Current HP/MP, future conditions, social/world state, and
derived results stay outside stored character stats.

**Tech Stack:** C# 12, .NET 8, immutable value data, checked fixed-point integer
math, console behavior tests, Godot 4.6.3 Mono, and PowerShell 7.

**Spec:**
`docs/superpowers/specs/2026-09-13-expanded-stats-foundation-v1-design.md`

## Global Constraints

- Do not start until the design is approved.
- Do not implement Flow, parry/counter, any Mastery system, survival or
  condition mechanics, social/world values, expanded Magic formulas, inventory,
  action speed, initiative, or a Status UI redesign.
- Do not assign production balance values to new stats. Old seven-stat inputs
  map exactly; all newly added slots default to zero only through that legacy
  bridge.
- Preserve the fixed round-robin scheduler and both existing RNG streams,
  including `battle.technique-hit`.
- Preserve BASIC ATTACK's guaranteed hit and all Style Shift damage/accuracy/
  stance rules.
- Preserve Fireball Size, Output, MP cost, Magic offense, and Resistance/
  MagicalDefense mitigation exactly.
- Preserve the current snapshot boundary: preparation resolves persistent
  equipment, Battle owns a copied value and applies battle-local modifiers.
- Do not edit or regenerate
  `probes/phase1a/golden/battle-20260909.log`.
- Gameplay code must identify stats by `StatId`, not display strings or
  reflection.
- Do not retain a second old seven-field stat record. Compatibility APIs must
  delegate to the canonical value block.
- Keep each commit green. A deliberately failing test/build is allowed only as
  the red step immediately before its implementation, never as a commit.

## Task 0: Reconfirm the Focused Baseline

**Files:** Inspect only.

**Purpose:** Establish the exact checkout and prepare the local .NET toolchain
without spending the full engine-QA budget.

- [ ] **Step 1: Confirm branch, commit, and clean scope**

Run from the repository root:

```powershell
git status --short --branch
git log -8 --oneline --decorate
git diff --check
```

Expected: implementation starts only after owner approval of these committed
design/plan files, there are no unexplained source changes, and whitespace
validation is clean. If the
tree is dirty, preserve unrelated user changes and stop if they overlap this
plan.

- [ ] **Step 2: Initialize the local .NET toolchain in this PowerShell session**

```powershell
. ./probes/phase1a/toolchain.ps1
$statsDotnet = Resolve-Dotnet8
$statsRoot = (Resolve-Path ./probes/phase1a).Path
Initialize-DotnetEnvironment -Dotnet $statsDotnet -ProjectRoot (Resolve-Path .).Path
& $statsDotnet restore "$statsRoot/Tests/Phase1A.Tests.csproj" --configfile "$statsRoot/NuGet.Config" -p:NuGetAudit=false
& $statsDotnet restore "$statsRoot/VisualTests/Phase1A.VisualTests.csproj" --configfile "$statsRoot/NuGet.Config" -p:NuGetAudit=false
```

Expected: both restores succeed without changing tracked files. If execution
resumes in a new shell, repeat this step before using `$statsDotnet`.

- [ ] **Step 3: Run only the headless baseline**

```powershell
& $statsDotnet build "$statsRoot/Tests/Phase1A.Tests.csproj" --configuration Debug --no-restore
& $statsDotnet "$statsRoot/Tests/bin/Debug/net8.0/Phase1A.Tests.dll"
& $statsDotnet build "$statsRoot/VisualTests/Phase1A.VisualTests.csproj" --configuration Debug --no-restore
& $statsDotnet "$statsRoot/VisualTests/bin/Debug/net8.0/Phase1A.VisualTests.dll"
```

Expected: the current core and presentation-model counts pass. Do not run
`launch-visual.ps1 -Verify` here; the one full gate is reserved for Task 4.

---

## Task 1: Add the Expanded Stat Domain and Legacy Construction Seam

**Commit:** `feat: add expanded stat identities`

**Files:**

- Create: `probes/phase1a/Probe/StatCatalog.cs`
- Modify: `probes/phase1a/Probe/Stats.cs`
- Modify: `probes/phase1a/Probe/CombatStyles.cs`
- Modify: `probes/phase1a/Tests/StatTests.cs`
- Modify: `probes/phase1a/Tests/EquipmentTests.cs`
- Modify: `probes/phase1a/Tests/CombatStyleTests.cs`
- Modify: `probes/phase1a/VisualTests/Program.cs`

The production change outside `Stats.cs` is only the mechanical replacement of
`CharacterStats` record `with` expressions in `CombatStyles.cs`.

**Interfaces:** Adds `StatId`, `StatCategory`, `StatDefinition`, `StatCatalog`,
the dense immutable `CharacterStats` value, `CharacterStatsBuilder`, indexer,
`With`, and legacy seven-stat mappings. No modifier or gameplay formula changes
yet.

- [ ] **Step 1: Write failing stat-domain tests**

Add tests to `StatTests.All` that assert:

1. `StatCatalog.Definitions` contains exactly the fifteen IDs from the spec,
   in enum order, excluding the `Count` sentinel;
2. every stable ID is nonblank and ordinal-unique, and only
   `LegacyAgility` is compatibility-only;
3. builder construction can set all V1 values by `StatId` and produces an
   immutable copy;
4. two separately built blocks with equal values compare equal and have equal
   hash codes;
5. `With` changes one slot without changing the source;
6. undefined enum values and `StatId.Count` reject;
7. missing/nonpositive MaxHP, negative values, and checked overflow reject;
8. the old constructor maps Defense to PhysicalDefense, Resistance to
   MagicalDefense, and Agility to LegacyAgility while new values equal zero.

Use the new builder in the test rather than a fifteen-argument constructor:

```csharp
var stats = CharacterStats.Create(builder => builder
    .Set(StatId.MaxHp, 100)
    .Set(StatId.MaxMp, 20)
    .Set(StatId.Strength, 12)
    .Set(StatId.Dexterity, 8)
    .Set(StatId.Speed, 7)
    .Set(StatId.Endurance, 9)
    .Set(StatId.Constitution, 10)
    .Set(StatId.Intelligence, 11)
    .Set(StatId.Reflex, 13)
    .Set(StatId.Balance, 6)
    .Set(StatId.PhysicalDefense, 5)
    .Set(StatId.MagicalDefense, 4)
    .Set(StatId.Magic, 3)
    .Set(StatId.MagicDexterity, 2));
```

- [ ] **Step 2: Confirm the red build**

```powershell
& $statsDotnet build "$statsRoot/Tests/Phase1A.Tests.csproj" --configuration Debug --no-restore
```

Expected: compilation fails only because the new stat-domain types/members do
not exist. Do not commit this state.

- [ ] **Step 3: Implement the closed catalog**

In `StatCatalog.cs`:

- define contiguous `StatId` values and terminal `Count` exactly as the spec;
- define `StatCategory` and `StatDefinition`;
- create one static immutable definitions array in enum order;
- provide checked `Definition(StatId)` and `TryFromStableId` lookups;
- validate catalog completeness, index alignment, unique stable IDs, labels,
  minima, and the one compatibility-only entry;
- use `StringComparer.Ordinal` for stable IDs.

Do not add IDs for HP, MP, conditions, derived values, or future omitted stats.
Do not serialize enum ordinals.

- [ ] **Step 4: Convert `CharacterStats` into the canonical dense value**

In `Stats.cs`, replace the positional record with one immutable fixed-width
value backed by an array/immutable array indexed by `StatId`. Implement:

```csharp
public int this[StatId id] { get; }
public static CharacterStats Create(Action<CharacterStatsBuilder> configure);
public CharacterStats With(StatId id, int value);
public CharacterStatsBuilder ToBuilder();
public void Validate();
```

Implement structural equality, hash code, `==`, and `!=` across all fifteen
slots. Reject an uninitialized/default block during validation. Validation uses
the catalog minimum for every entry.

Retain the exact old constructor shape:

```csharp
public CharacterStats(
    int maxHp, int maxMp, int strength, int defense,
    int magic, int resistance, int agility)
```

Retain read-only compatibility properties `MaxHp`, `MaxMp`, `Strength`,
`Defense`, `Magic`, `Resistance`, and `Agility`; each reads its canonical slot.
Also expose canonical named getters if they materially improve readability, but
do not duplicate storage.

- [ ] **Step 5: Mechanically replace stat `with` expressions**

Because `CharacterStats` is no longer a positional record, replace only
`CharacterStats` mutations with chained `With` calls. Leave `with` expressions
for `ActorSeed`, `EncounterSetup`, records, and views unchanged. Examples:

```csharp
stats.With(StatId.Strength, 7)
     .With(StatId.PhysicalDefense, 5)
```

Update `Stats.cs`, `CombatStyles.cs`, the three listed core test files, and the
one nested `EffectiveStats with { Strength = 999 }` in
`VisualTests/Program.cs`. This step changes construction syntax only; consumers
may still use old property getters until later tasks.

- [ ] **Step 6: Run focused green tests**

```powershell
& $statsDotnet build "$statsRoot/Tests/Phase1A.Tests.csproj" --configuration Debug --no-restore
& $statsDotnet "$statsRoot/Tests/bin/Debug/net8.0/Phase1A.Tests.dll"
& $statsDotnet build "$statsRoot/VisualTests/Phase1A.VisualTests.csproj" --configuration Debug --no-restore
& $statsDotnet "$statsRoot/VisualTests/bin/Debug/net8.0/Phase1A.VisualTests.dll"
```

Expected: all existing behavior plus the new domain tests pass; golden bytes are
unchanged.

- [ ] **Step 7: Audit and commit**

```powershell
rg -n "record struct CharacterStats|with \{ (MaxHp|MaxMp|Strength|Defense|Magic|Resistance|Agility)" probes/phase1a -g '*.cs'
git diff --check
git diff -- probes/phase1a/golden
git add probes/phase1a/Probe/StatCatalog.cs probes/phase1a/Probe/Stats.cs probes/phase1a/Probe/CombatStyles.cs probes/phase1a/Tests/StatTests.cs probes/phase1a/Tests/EquipmentTests.cs probes/phase1a/Tests/CombatStyleTests.cs probes/phase1a/VisualTests/Program.cs
git commit -m "feat: add expanded stat identities"
```

Expected: no stat-record `with` mutation remains, golden diff is empty, and the
commit contains no generated output.

---

## Task 2: Centralize Typed Modifier Resolution and Equipment

**Commit:** `feat: centralize stat modifier resolution`

**Files:**

- Create: `probes/phase1a/Probe/StatModifiers.cs`
- Modify: `probes/phase1a/Probe/Stats.cs`
- Modify: `probes/phase1a/Probe/Equipment.cs`
- Modify: `probes/phase1a/Probe/CharacterPreparation.cs`
- Modify: `probes/phase1a/Tests/StatTests.cs`
- Modify: `probes/phase1a/Tests/EquipmentTests.cs`

**Interfaces:** Adds `StatModifierOperation`, `StatModifier`,
`StatResolutionRequest`, canonical resolver ordering, and typed equipment
bonuses. Preparation and previews migrate; Battle-local status/stance stays on
the compatibility call until Task 3.

- [ ] **Step 1: Write failing resolver and equipment tests**

Add tests covering:

- FlatAdd before PercentAdd regardless of input insertion order;
- additive percentage basis points summed once per stat;
- midpoint rounding away from zero;
- deterministic source ordering using operation, priority, source ID, and stat;
- checked overflow and final catalog-minimum rejection/clamp policy;
- empty modifiers returning an equal independent immutable value;
- invalid `StatId`, `Count`, blank source ID, and unsupported operation rejection;
- a test-only DEX item adding exactly +3 DEX without changing any other stat;
- legacy `EquipmentBonuses(Defense: ...)` mapping to PhysicalDefense;
- current preview, equip/unequip, maximum-resource, atomic rejection, and
  Battle-lock tests staying unchanged.

For the test-only item, use the typed construction seam rather than adding a
production item:

```csharp
var gloves = new EquipmentDefinition(
    "fixture:equipment.dex-gloves",
    "DEX Gloves",
    EquipmentSlot.Accessory,
    EquipmentBonuses.Create(builder =>
        builder.Set(StatId.Dexterity, 3)));
```

- [ ] **Step 2: Confirm the red build**

```powershell
& $statsDotnet build "$statsRoot/Tests/Phase1A.Tests.csproj" --configuration Debug --no-restore
```

Expected: failure is limited to missing typed modifier/equipment APIs.

- [ ] **Step 3: Implement typed frozen modifiers**

In `StatModifiers.cs`, add:

```csharp
public enum StatModifierOperation
{
    FlatAdd = 200,
    PercentAdd = 300
}

public readonly record struct StatModifier(
    StatId Stat,
    StatModifierOperation Operation,
    int Amount,
    string SourceId,
    int SourcePriority = 0);

public sealed record StatResolutionRequest(CharacterStats StartingStats)
{
    public ImmutableArray<StatModifier> Modifiers { get; init; } = [];
    public required StatMinimumBehavior MinimumBehavior { get; init; }
}
```

Amounts are already frozen. Do not add formulas, callbacks, dynamic modifiers,
multiplicative operations, or overrides. Validate every modifier before any
arithmetic. Define `StatMinimumBehavior.Reject` and `.Clamp`; every request must
choose one explicitly.

- [ ] **Step 4: Implement the canonical resolver**

Move all stat arithmetic behind
`StatResolver.Resolve(StatResolutionRequest request)`:

1. validate starting stats and copy their dense values;
2. sort modifiers by `(Operation, SourcePriority, SourceId, StatId)` using
   ordinal source comparison;
3. apply checked flat additions;
4. sum percent basis points per stat and apply once with a widened integer
   intermediate and `MidpointRounding.AwayFromZero` semantics;
5. reject below-minimum totals when `MinimumBehavior` is `Reject`, or clamp them
   to catalog minima when it is `Clamp`;
6. validate the result.

Do not rely on dictionary iteration. Persistent equipment uses `Reject`. Keep
the old resolver overload temporarily; when it receives both equipment and
Weakened, resolve equipment with `Reject` first and Weakened with `Clamp` second
so Battle remains behaviorally green until Task 3.

- [ ] **Step 5: Convert equipment storage and preparation**

Refactor `EquipmentBonuses` into an immutable dense signed bonus block keyed by
`StatId`, with a builder, indexer, deterministic nonzero enumeration, and
structural equality. Retain the old seven named constructor/getters as adapters
to the same block.

Add `EquipmentLoadout.Modifiers`, enumerating equipment in declared slot order
and stats in catalog order. Use each equipment definition ID as modifier
`SourceId`. Keep `Bonuses` only as a compatibility projection if an unmigrated
caller still requires it; do not make it a second authoritative store.

Update `CharacterPreparation.EffectiveStats`, `Preview`, and `TryEquip` to call
the request API with `Loadout.Modifiers`. Preserve all HP/MP clamping and atomic
rejection behavior. The four production items keep the exact numeric effects,
with old Defense bonuses now targeting PhysicalDefense.

- [ ] **Step 6: Run the focused core suite**

```powershell
& $statsDotnet build "$statsRoot/Tests/Phase1A.Tests.csproj" --configuration Debug --no-restore
& $statsDotnet "$statsRoot/Tests/bin/Debug/net8.0/Phase1A.Tests.dll"
```

Expected: all core tests pass, including generic DEX equipment and unchanged
golden replay. No visual/engine run is required because public preparation
values and outputs have not changed.

- [ ] **Step 7: Audit and commit**

```powershell
rg -n "StatResolver\.Resolve|EquipmentBonuses|\.Bonuses" probes/phase1a -g '*.cs'
git diff --check
git diff -- probes/phase1a/golden
git add probes/phase1a/Probe/StatModifiers.cs probes/phase1a/Probe/Stats.cs probes/phase1a/Probe/Equipment.cs probes/phase1a/Probe/CharacterPreparation.cs probes/phase1a/Tests/StatTests.cs probes/phase1a/Tests/EquipmentTests.cs
git commit -m "feat: centralize stat modifier resolution"
```

Expected: preparation uses typed modifiers, any compatibility call is clearly
limited to Battle/Style migration scheduled next, and golden files are untouched.

---

## Task 3: Migrate Battle, Styles, Physical Damage, and Magic Consumers

**Commit:** `feat: migrate battle stat consumers`

**Files:**

- Modify: `probes/phase1a/Probe/BattleState.cs`
- Modify: `probes/phase1a/Probe/CombatStyles.cs`
- Modify: `probes/phase1a/Probe/Rules.cs`
- Modify: `probes/phase1a/Probe/Stats.cs`
- Modify: `probes/phase1a/Tests/StatTests.cs`
- Modify: `probes/phase1a/Tests/EquipmentTests.cs`
- Modify: `probes/phase1a/Tests/CombatStyleTests.cs`
- Modify: `probes/phase1a/Tests/MagicTests.cs`
- Modify: `probes/phase1a/VisualTests/Program.cs`

**Interfaces:** `BattleState.Read` resolves all battle-local typed modifiers in
one request. Stances become generic StatId-based PercentAdd sources. Current
calculators read canonical IDs. No action, formula, RNG, or scheduler behavior
changes.

- [ ] **Step 1: Add failing Battle and compatibility regression tests**

Add or strengthen tests that prove:

1. a builder-created actor carrying nonzero DEX/SPD/END/CON/INT/RFL/BAL/MDEX
   enters Battle and emerges from `ActorSnapshot.EffectiveStats` unchanged;
2. Weakened subtracts frozen flats from Strength and PhysicalDefense before the
   active stance percentage;
3. all three established and Shifted stance results remain exactly equal to
   current expected values;
4. positive stance modifiers use ×0.85 under Shift, negative modifiers ×1.00;
5. a test-only typed stance can address a newly added stat without altering the
   production three Styles;
6. Physical damage and Drain use PhysicalDefense and produce their existing
   results through the `Defense` compatibility mapping;
7. Fireball uses Magic and MagicalDefense and remains unaffected by INT, CON,
   or MDEX;
8. LegacyAgility and all new unused values cannot perturb the current golden
   battle, Technique hit sequence, or fixed round-robin action order;
9. BASIC ATTACK remains guaranteed-hit and Style Shift still applies its
   physical damage penalty.

- [ ] **Step 2: Confirm the failing tests**

```powershell
& $statsDotnet build "$statsRoot/Tests/Phase1A.Tests.csproj" --configuration Debug --no-restore
```

Expected: new typed stance/Battle APIs fail to compile or the new generic
behavior assertions fail; all failures must be explained by this task.

- [ ] **Step 3: Make stance modifiers stat-typed**

Refactor `StanceModifiers` to store an immutable collection/dense block of
PercentAdd basis points keyed by `StatId`. Provide a builder/factory for new
code. Preserve the old `StrengthPercent`, `DefensePercent`, and
`ResistancePercent` constructor/getters only as adapters to Strength,
PhysicalDefense, and MagicalDefense.

Update the three prototype Style literals to typed IDs without changing their
values. `CombatStyleRules` remains responsible for validating that a negative
stance is greater than -100%, and for attenuating only positive magnitudes by
85% when shifted. It should return typed modifiers carrying the Style ID as
provenance; `StatResolver` performs the percentage arithmetic.

Do not change Technique definitions, accuracy, damage-scale helpers, Style IDs,
or active/turn-start lifecycle.

- [ ] **Step 4: Resolve Battle-local modifiers in one call**

In `BattleState.Read`:

- start with the copied `ActorSeed.InitialStats`;
- if Weakened is active, add FlatAdd modifiers of negative FrozenAmount for
  Strength and PhysicalDefense, using the status ID as provenance;
- if a Style is active, add its already Shift-adjusted PercentAdd modifiers;
- call the canonical resolver once with `StatMinimumBehavior.Clamp`;
- construct the same `ActorSnapshot` shape as before.

Keep the frozen status amount, status duration, target snapshot timing, Battle
copy ownership, and event flow unchanged. Remove the old weakness/equipment
resolver overload only after `rg` proves no caller remains.

- [ ] **Step 5: Move current calculations to canonical IDs**

Update `PhysicalDamage`, `MagicalDamage`, the Drain prototype path, and
`ActorSnapshot` convenience properties to read:

```text
Strength          -> StatId.Strength
Defense           -> StatId.PhysicalDefense
Magic             -> StatId.Magic
Resistance        -> StatId.MagicalDefense
```

Do not change any coefficient, integer division, minimum, guard ordering,
variance, targeting, or damage kind. Do not add derived values to
`CharacterStats`.

- [ ] **Step 6: Run core and headless presentation regressions**

```powershell
& $statsDotnet build "$statsRoot/Tests/Phase1A.Tests.csproj" --configuration Debug --no-restore
& $statsDotnet "$statsRoot/Tests/bin/Debug/net8.0/Phase1A.Tests.dll"
& $statsDotnet build "$statsRoot/VisualTests/Phase1A.VisualTests.csproj" --configuration Debug --no-restore
& $statsDotnet "$statsRoot/VisualTests/bin/Debug/net8.0/Phase1A.VisualTests.dll"
```

Expected: all tests pass; golden bytes, Fireball, six Techniques, BASIC ATTACK,
Style Shift, and round-robin behavior are unchanged.

- [ ] **Step 7: Audit canonical consumer use and commit**

```powershell
rg -n "\.Defense|\.Resistance|\.Agility" probes/phase1a/Probe -g '*.cs'
rg -n "battle\.technique-hit|NextActorId|HitChanceMillionths|TechniqueDamageScaleMillionths" probes/phase1a/Probe
git diff --check
git diff -- probes/phase1a/golden
git add probes/phase1a/Probe/BattleState.cs probes/phase1a/Probe/CombatStyles.cs probes/phase1a/Probe/Rules.cs probes/phase1a/Probe/Stats.cs probes/phase1a/Tests/StatTests.cs probes/phase1a/Tests/EquipmentTests.cs probes/phase1a/Tests/CombatStyleTests.cs probes/phase1a/Tests/MagicTests.cs probes/phase1a/VisualTests/Program.cs
git commit -m "feat: migrate battle stat consumers"
```

Expected: rule consumers use canonical IDs. Remaining old getters are limited to
the compatibility facade and UI migration explicitly scheduled in Task 4.

---

## Task 4: Preserve Status/Preparation Views, Document, and Run the One Full Gate

**Commit:** `feat: complete expanded stats foundation v1`

**Files:**

- Modify: `probes/phase1a/Visual/Presentation/FieldMenuController.cs`
- Modify: `probes/phase1a/Visual/Presentation/BattleSession.cs` only if the
  expanded-value projection test requires an explicit helper
- Modify: `probes/phase1a/Visual/BattleScreen.cs`
- Modify: `probes/phase1a/VisualTests/FieldMenuTests.cs`
- Modify: `probes/phase1a/VisualTests/Program.cs`
- Modify: `probes/phase1a/Visual/MenuQa.cs` only if an existing assertion names
  the migrated accessors
- Modify: `probes/phase1a/Visual/HarnessQa.cs` only if an existing assertion
  names the migrated accessors
- Modify: `probes/phase1a/README.md`
- Modify: `probes/phase1a/STAT_SYSTEM_REPORT.md`
- Modify: `probes/phase1a/MENU_ARCHITECTURE.md`

Do not touch optional files whose source remains correct. Stage only files with
intentional diffs.

**Interfaces:** Presentation keeps immutable `CharacterStats` projections and
the exact old visible rows, but reads canonical IDs explicitly. Catalog category
metadata is the future grouped-display seam; no tabs/groups are implemented.

- [ ] **Step 1: Write read-model characterization tests first**

Add assertions that:

- `BattleSession.ActorView.EffectiveStats` exposes nonzero new fixture values
  from a copied Battle snapshot and cannot mutate the Battle;
- the field Status rows remain exactly `NAME, HP, MP, STR, DEF, MAG, RES, AGI`
  in that order and with existing values;
- DEF reads PhysicalDefense, RES reads MagicalDefense, and AGI reads
  LegacyAgility;
- expanded V1 values are deliberately absent from the current Status page;
- preparation changed-stat output remains the current seven visible labels and
  does not dump every catalog entry.

Do not add grouped pages or new production-visible rows.

- [ ] **Step 2: Confirm the characterization baseline**

```powershell
& $statsDotnet build "$statsRoot/VisualTests/Phase1A.VisualTests.csproj" --configuration Debug --no-restore
& $statsDotnet "$statsRoot/VisualTests/bin/Debug/net8.0/Phase1A.VisualTests.dll"
```

Expected: the behavior-level assertions pass before the mechanical accessor
migration. This is a characterization step: canonical IDs and compatibility
aliases intentionally return the same value, so manufacturing a failing UI test
would require false behavior. The source audit in Step 6 distinguishes the
implementation; the tests pin its unchanged output.

- [ ] **Step 3: Migrate the existing flat projections**

Update `FieldMenuController.StatusRows` and `BattleScreen.ChangedStats` to use an
explicit seven-entry `(label, StatId)` order:

```text
STR -> Strength
DEF -> PhysicalDefense
MAG -> Magic
RES -> MagicalDefense
AGI -> LegacyAgility
```

HP and MP continue to combine mutable current values with MaxHp/MaxMp. Keep the
existing NAME row and every visible string. The projection list is presentation
policy; do not move it into Rules or infer it through reflection.

Keep `ActorView.EffectiveStats` as an immutable value projection. If no source
change is required in `BattleSession.cs`, leave the file untouched.

- [ ] **Step 4: Run focused headless tests before documentation**

```powershell
& $statsDotnet build "$statsRoot/Tests/Phase1A.Tests.csproj" --configuration Debug --no-restore
& $statsDotnet "$statsRoot/Tests/bin/Debug/net8.0/Phase1A.Tests.dll"
& $statsDotnet build "$statsRoot/VisualTests/Phase1A.VisualTests.csproj" --configuration Debug --no-restore
& $statsDotnet "$statsRoot/VisualTests/bin/Debug/net8.0/Phase1A.VisualTests.dll"
```

Expected: all headless tests pass with unchanged visible output.

- [ ] **Step 5: Update current documentation**

In `README.md` and `STAT_SYSTEM_REPORT.md`, document:

- the canonical V1 stat table and stable IDs;
- current HP/MP separation;
- Defense -> PhysicalDefense and Resistance -> MagicalDefense aliases;
- isolated LegacyAgility and zero legacy defaults for new values;
- builder/indexer usage;
- typed modifier operation order and persistent/Battle snapshot phases;
- unchanged physical, Drain, Fireball, Style/Shift, Technique hit, BASIC, and
  round-robin behavior;
- the future Flow input seam and separate Technique Mastery ownership;
- explicit exclusions.

Append a concise implemented Expanded Stats V1 section to
`MENU_ARCHITECTURE.md`. Preserve its historical sections and exact current
Status-page description. State that catalog metadata enables a later grouped
projection but no UI groups were added.

Use fresh test counts only after the final verifier reports them.

- [ ] **Step 6: Perform the pre-gate scope audit**

```powershell
rg -n "core:stat\.(dexterity|physical-defense|magical-defense|legacy-agility)|LegacyAgility|battle\.technique-hit|round-robin" probes/phase1a/README.md probes/phase1a/STAT_SYSTEM_REPORT.md probes/phase1a/MENU_ARCHITECTURE.md
rg -n "Flow|Technique Mastery|Hit Chance|Action Delay" probes/phase1a/Probe -g '*.cs'
git diff --check
git diff --name-only HEAD -- probes/phase1a/golden
git ls-files | rg '(^|/)(bin|obj|artifacts|\.godot)/|(^|/)\.DS_Store$|(^|/)\.tools/'
```

Expected: required documentation terms are present; the production-code search
shows no Flow/Mastery/new-derived implementation; golden and generated-file
queries are empty.

- [ ] **Step 7: Run the full verifier exactly once**

```powershell
pwsh ./probes/phase1a/launch-visual.ps1 -Verify
Get-Content ./probes/phase1a/artifacts/visual/qa.txt -Tail 1
Get-Content ./probes/phase1a/artifacts/visual/field-qa.txt -Tail 1
Get-Content ./probes/phase1a/artifacts/visual/menu-qa.txt -Tail 1
```

Expected: Debug and Release core suites, VisualTests, Godot build/import, and
the three established engine-QA paths all pass; each report ends in `PASS ALL`;
the golden replay remains byte-exact; no warning is reported. Do not rerun this
full gate unless it fails and a scoped fix is made.

- [ ] **Step 8: Record fresh counts, audit, and commit**

Update only count text that the successful gate proves, then run:

```powershell
git diff --check
git diff --name-only HEAD -- probes/phase1a/golden
git status --short
git add probes/phase1a/Visual/Presentation/FieldMenuController.cs probes/phase1a/Visual/BattleScreen.cs probes/phase1a/VisualTests/FieldMenuTests.cs probes/phase1a/VisualTests/Program.cs probes/phase1a/README.md probes/phase1a/STAT_SYSTEM_REPORT.md probes/phase1a/MENU_ARCHITECTURE.md
git diff --cached --name-only
git commit -m "feat: complete expanded stats foundation v1"
git status --short --branch
git log -4 --oneline --decorate
```

Add any conditionally modified `BattleSession.cs`, `MenuQa.cs`, or
`HarnessQa.cs` explicitly before committing. Never stage
`probes/phase1a/artifacts`, `.tools`, `bin`, `obj`, or `.godot`.

Expected: exactly four implementation commits correspond to the four requested
boundaries; the final tree is clean except for pre-existing unrelated user
changes; golden files are unchanged.

## Stop Conditions

Stop before restructuring unrelated systems and report the conflict if any of
the following occurs:

- preserving Battle snapshot isolation would require a live reference back to
  `CharacterPreparation`;
- Fireball compatibility would require changing Size/Output/MP/damage rules;
- Style migration would require changing current stance values, Shift rules,
  Technique hit RNG, or action flow;
- the dense enum block cannot preserve deterministic equality/iteration without
  introducing arbitrary string-key gameplay state;
- current save/runtime code appears that was absent during design inspection
  and establishes a conflicting persisted stat identity;
- a proposed change needs the scheduler, Flow/Mastery, condition, inventory, or
  UI redesign excluded by the approved spec.

Do not solve such a conflict by broadening scope. Preserve the failing evidence,
leave the last committed boundary green, and ask the owner for an architectural
decision.
