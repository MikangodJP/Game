# Expanded Stats Foundation V1 Design

**Status:** Approved and implemented 2026-09-13
**Scope:** Phase 1A architectural foundation only; no new gameplay formulas or UI pages

## Decision Summary

V1 replaces the closed seven-position implementation of `CharacterStats` with
one immutable, enum-indexed value block. Gameplay code addresses values through
`StatId`; a complete `StatCatalog` owns stable persistence IDs, validation
metadata, short labels, and conceptual categories. The existing seven-argument
constructor and property names remain only as a temporary source-compatibility
facade over that same value block.

Current HP and MP remain mutable resources outside the stored stat block.
Future conditions and social/world values receive separate owners rather than
being inserted into character capabilities. Derived values remain calculations,
not authoritative stored fields.

The resolver preserves the current two-phase boundary:

1. persistent preparation resolves base values plus equipment, then
2. Battle copies that immutable result and applies battle-local Weakened and
   Style stance modifiers.

Within a resolution phase, arithmetic follows the existing architecture's
deterministic operation order: flat additions, summed additive percentages,
future multiplicative modifiers, future overrides, then an explicit
reject-or-clamp catalog boundary policy.

## 1. Repository Findings

The current repository is a C# Phase 1A rules probe with a Godot shell. The
relevant findings are:

- `CharacterStats` is a closed positional `readonly record struct` with
  `MaxHp`, `MaxMp`, `Strength`, `Defense`, `Magic`, `Resistance`, and
  `Agility`.
- `CharacterPreparation` owns persistent base stats, current HP/MP, equipment,
  known Magic, and known Combat Styles. Its `EffectiveStats` property resolves
  base stats plus equipment without mutating the base.
- `BeginBattle` copies already equipment-resolved stats and current vitals into
  an `ActorSeed`; `BattleState` holds no reference to the persistent character.
- `EquipmentBonuses` duplicates the seven positional stat fields. Loadouts sum
  flat bonuses in stable equipment-slot order.
- `BattleState.Read` subtracts the existing frozen Weakened amount from
  Strength and Defense, then applies the active Style stance. This establishes
  the current order `equipment-resolved snapshot -> Weakened -> stance`.
- Style stances currently modify only Strength, Defense, and Resistance.
  Positive stance values are attenuated during Style Shift; negative values are
  not. The separate physical-action Shift damage and Technique accuracy rules
  do not belong to stat resolution.
- Physical damage reads Strength and Defense. Fireball's Magical damage reads
  Magic and Resistance. Drain retains its separate prototype formula using
  Strength and full Defense.
- Agility has no rules consumer. It is displayed and copied, but it does not
  affect accuracy, turns, or the fixed round-robin scheduler.
- `ActorSnapshot` and `BattleSession.ActorView` already expose immutable stat
  values to presentation. The field Status controller and preparation screen
  hard-code the old seven labels.
- The Status renderer already accepts a row collection. The present limitation
  is the hard-coded projection, not a renderer that requires exactly seven
  rows.
- Tests construct many characters with seven positional integers and use
  record `with` expressions. A safe migration needs an explicit builder and a
  `With(StatId, value)` replacement before the positional form can disappear.
- The scheduler is fixed round-robin and the independent
  `battle.technique-hit` RNG stream is already established.
- There is no save loader or stat content schema in the probe. Persistence work
  here is therefore an ID and migration contract, not a serializer feature.
- No separate copy of the referenced legacy base-system document or its full
  formulas is present in the repository or attachment. The enumerated stat
  vocabulary is treated as product intent; no omitted formula is inferred.

The higher-level architecture places Stats in the pure Rules layer, requires
immutable read models above it, uses battle snapshot-in/delta-out boundaries,
and specifies deterministic modifier operation layers. This design keeps those
contracts.

## 2. Current Seven-Stat Ownership and Consumers

| Value | Current owner | Current consumers | V1 role |
|---|---|---|---|
| MaxHP | `CharacterPreparation.BaseStats`; copied into Battle | HP bounds, healing, UI | Canonical stored capacity |
| MaxMP | same | MP bounds, Fireball affordability, UI | Canonical stored capacity |
| Strength | same | physical damage, Drain, Weakened, Styles, UI | Canonical stored capability |
| Defense | same | physical mitigation, Drain, Weakened, Styles, UI | Alias of PhysicalDefense |
| Magic | same | Fireball offense, UI | Canonical stored Magic capability |
| Resistance | same | Fireball mitigation, Styles, UI | Alias of MagicalDefense |
| Agility | same | copy/display only | Compatibility-only stored slot |
| current HP | `CharacterPreparation.Hp`; copied into Battle actor | damage/healing/result delta/UI | Mutable current resource, not a stat ID |
| current MP | `CharacterPreparation.Mp`; copied into Battle actor | costs/result delta/UI | Mutable current resource, not a stat ID |

There are two legitimate effective-stat contexts, not two authoritative stat
stores:

- `CharacterPreparation.EffectiveStats` is a pure projection of persistent
  base values and the current equipment loadout.
- `BattleState.Read(...).EffectiveStats` starts from the copied preparation
  projection and adds only battle-local modifiers.

The copied Battle value is an isolation snapshot. It does not compete with the
persistent base as an editable source of truth.

## 3. Proposed Stat Categories

`StatCategory` is metadata about persistent character capabilities. It is not
a rule for arithmetic and is not permission to put all numeric state in one
container.

| Category | Ownership and examples |
|---|---|
| ResourceCapacity | Persistent maxima such as MaxHP and MaxMP |
| CoreCapability | Persistent body/cognitive values such as STR, DEX, SPD, END, CON, INT, RFL |
| MagicCapability | Persistent broad Magic capability; future Magic values may join only when approved |
| TrainedCapability | Persistent usage-influenced values such as BAL, PhyDEF, MgkDEF, MDEX |
| Compatibility | Values retained only to preserve old callers/data, initially legacy AGI |

The other planned families have different owners:

- current HP/MP: current-resource state;
- SAT/HYD/NRG/OXY/TMP/PTY/TOX/STRS/CST/ADR and similar values: a future
  condition model;
- WGT/EQP: future encumbrance/equipment or condition projections after their
  semantics are defined;
- KRM/FAM/COR: future social/world-domain state, not physical capability stats;
- Hit Chance, Dodge, Crit, attack totals, defense totals, and action delay:
  derived calculation results.

### Alternatives considered

1. **Expand the positional record and add a builder.** This retains compile-time
   names but repeats every new field in constructors, equipment, Styles,
   resolvers, equality, and tests. It does not solve the scaling problem.
2. **Use one immutable enum-indexed dense block. Chosen.** It gives closed IDs,
   compact storage, deterministic iteration, structural equality, and generic
   modifier plumbing without accepting arbitrary strings.
3. **Split stored stats into several authoritative sub-records.** Body/Magic/
   trained groupings are readable, but cross-group modifiers and calculations
   would need extra routing, and moving a stat between conceptual categories
   would become a data migration. Categories are better metadata while values
   share the same owner and persistence lifetime.

## 4. V1 Implemented Stat Set

V1's canonical stored set is deliberately limited to the existing values and
the ten approved candidates.

| `StatId` | Stable ID | Short label | Category | Legacy default |
|---|---|---|---|---:|
| `MaxHp` | `core:stat.max-hp` | MAXHP | ResourceCapacity | old MaxHP |
| `MaxMp` | `core:stat.max-mp` | MAXMP | ResourceCapacity | old MaxMP |
| `Strength` | `core:stat.strength` | STR | CoreCapability | old Strength |
| `Magic` | `core:stat.magic` | MAG | MagicCapability | old Magic |
| `Dexterity` | `core:stat.dexterity` | DEX | CoreCapability | 0 |
| `Speed` | `core:stat.speed` | SPD | CoreCapability | 0 |
| `Endurance` | `core:stat.endurance` | END | CoreCapability | 0 |
| `Constitution` | `core:stat.constitution` | CON | CoreCapability | 0 |
| `Intelligence` | `core:stat.intelligence` | INT | CoreCapability | 0 |
| `Reflex` | `core:stat.reflex` | RFL | CoreCapability | 0 |
| `Balance` | `core:stat.balance` | BAL | TrainedCapability | 0 |
| `PhysicalDefense` | `core:stat.physical-defense` | PHYDEF | TrainedCapability | old Defense |
| `MagicalDefense` | `core:stat.magical-defense` | MGKDEF | TrainedCapability | old Resistance |
| `MagicDexterity` | `core:stat.magic-dexterity` | MDEX | TrainedCapability | 0 |
| `LegacyAgility` | `core:stat.legacy-agility` | AGI | Compatibility | old Agility |

Current HP and MP are intentionally absent from `StatId`. MET, LUC, GRW, MRES,
senses, regeneration values, EfMP, MxMP, GRP, FLX, SanDEF, and all variable
parameters remain outside V1. New stats default to zero only when importing an
old seven-stat construction or old save shape; new content and tests should set
intended values explicitly.

All V1 stored values are integers. `MaxHp` must be positive; every other stored
value must be nonnegative. V1 introduces no cap.

## 5. Compatibility Treatment for Defense, Resistance, and Agility

### Defense

`Defense` becomes a temporary source-level property alias for
`PhysicalDefense`. The old constructor's fourth argument maps to the canonical
PhysicalDefense slot. Physical damage, Drain, Weakened, prototype equipment,
prototype Styles, Status, and preparation UI keep their exact numerical
behavior while their internals migrate to `StatId.PhysicalDefense`.

There is never a separate authoritative Defense value.

### Resistance

`Resistance` becomes a temporary property alias for `MagicalDefense`. The old
constructor's sixth argument maps to the canonical MagicalDefense slot.
Fireball mitigation and current Style resistance modifiers continue to read
that exact value. MDEX, INT, or any other new stat does not enter Fireball V1.

There is never a separate authoritative Resistance value.

### Agility

Agility cannot be safely mapped to Speed, Reflex, or Dexterity because the
repository gives it no behavior and the concepts are not equivalent. Its exact
old value is retained in the compatibility-only `LegacyAgility` slot. The
temporary `Agility` property and seven-stat Status row read that slot.

New rules, including future Flow work, must not consume `LegacyAgility`.
Agility is removed only after all three gates are true:

1. no source caller uses the legacy constructor/property;
2. the old Status projection has been replaced by an approved grouped UI; and
3. a versioned persistence migration has mapped or retired historical AGI data
   without inventing DEX/SPD/RFL values.

The old aliases are migration APIs, not deprecation warnings in the first V1
commit. Adding `[Obsolete]` immediately would create warning noise before the
repository's own callers have migrated.

## 6. Stat Identity Representation

The core types are:

```csharp
public enum StatId
{
    MaxHp = 0,
    MaxMp,
    Strength,
    Magic,
    Dexterity,
    Speed,
    Endurance,
    Constitution,
    Intelligence,
    Reflex,
    Balance,
    PhysicalDefense,
    MagicalDefense,
    MagicDexterity,
    LegacyAgility,
    Count
}

public enum StatCategory
{
    ResourceCapacity,
    CoreCapability,
    MagicCapability,
    TrainedCapability,
    Compatibility
}

public sealed record StatDefinition(
    StatId Id,
    string StableId,
    string ShortLabel,
    StatCategory Category,
    int Minimum,
    bool CompatibilityOnly = false);
```

`Count` is a storage sentinel and is never a valid gameplay stat. `StatCatalog`
contains exactly one definition for every preceding enum value in numeric
order. Startup/tests reject gaps, duplicate enum IDs, duplicate stable IDs,
blank labels, mismatched indices, and invalid sentinels.

`CharacterStats` becomes an immutable value object backed by a fixed-width
array indexed by `StatId`. Its public contract includes:

```csharp
public int this[StatId id] { get; }
public CharacterStats With(StatId id, int value);
public CharacterStatsBuilder ToBuilder();
public void Validate();
```

`CharacterStatsBuilder` is the construction seam for tests and authored
literals:

```csharp
var stats = CharacterStats.Create(builder => builder
    .Set(StatId.MaxHp, 100)
    .Set(StatId.MaxMp, 20)
    .Set(StatId.Strength, 12)
    .Set(StatId.Dexterity, 8));
```

The builder is mutable only while constructing. `Build` copies into an
immutable block and validates it. Gameplay never indexes by display text or by
an unvalidated persistence string. Undefined enum values and `StatId.Count`
throw `ArgumentOutOfRangeException`; unknown persistence IDs are handled at the
load boundary described in section 17.

The legacy seven-argument constructor and legacy getters delegate to these same
canonical slots. They do not own a second record or cache.

## 7. Character State Ownership

`CharacterPreparation` remains the sole persistent player owner in the probe:

- `BaseStats`: canonical immutable `CharacterStats`;
- `Loadout`: immutable equipment selection;
- `Hp` and `Mp`: mutable current resources bounded by resolved maxima;
- known Magic and last-successful Magic configuration: unchanged;
- known/Primary Combat Styles: unchanged.

`EffectiveStats` remains a computed projection. Equipment previews resolve a
hypothetical loadout without mutating the real preparation.

`BeginBattle` continues to copy effective persistent stats and current vitals
into `ActorSeed`. `BattleState` owns its copy and battle-local status/Style
state. `EncounterResult` continues to export only current vitals. Expanded base
stats, equipment, and active Style do not leak through the result boundary.

The immutable Battle copy is intentionally frozen for the encounter. Changing
equipment or future character growth outside the Battle cannot mutate an
existing Battle.

## 8. Modifier Pipeline

Modifier **source** and modifier **operation** are separate concepts.
Equipment, growth, traits, statuses, Styles, and future conditions identify why
a modifier exists. Arithmetic order is determined by its operation, not by
which subsystem happened to append it first.

The canonical V1 modifier is stat-typed:

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
```

Percent amounts use basis points: `2_000` means +20.00%, and `-1_500` means
-15.00%. Source IDs are stable provenance/tie-break data; they are not used to
identify stats or branch on gameplay behavior.

The complete intended arithmetic order is:

```text
100  starting stored/snapshotted value
200  flat additions, deterministically ordered
300  additive percentages, summed and applied once
400  multiplicative modifiers (reserved; not implemented in V1)
500  override/set (reserved; not implemented in V1)
600  catalog boundary policy: reject or clamp, chosen by resolution context
```

Ties are ordered by `(operation, sourcePriority, SourceId, StatId)`. V1 supports
only FlatAdd and PercentAdd. Unsupported operations and unknown stats are
rejected rather than silently ignored.

Resolution occurs in two explicit contexts:

```text
Persistent preparation:
Base -> future growth -> equipment -> future persistent traits
     -> operation layers -> persistent EffectiveStats

Battle-local:
copied persistent EffectiveStats -> frozen temporary status modifiers
     -> active Style stance modifiers -> future condition modifiers
     -> operation layers -> Battle EffectiveStats
```

The source arrows show ownership/collection, while the numbered operation
layers define arithmetic. In current V1 all equipment and Weakened contributions
are flat, while stance contributions are additive percentages, so the observable
order remains exactly:

```text
Base + equipment -> copied Battle snapshot -> Weakened subtraction
                 -> Style stance percentage -> effective value
```

Modifier magnitudes are fixed integers or basis points before resolution. V1
does not add dynamic formulas or self-referential modifiers. Checked arithmetic
is required. The request makes its terminal minimum behavior explicit:

- persistent preparation uses `Reject`, preserving atomic rejection of
  equipment that would make MaxHP nonpositive or any value negative;
- Battle-local resolution uses `Clamp`, preserving Weakened's existing floor of
  zero before the stance result is returned.

MaxHP must remain at least one and all other V1 values at least zero. A caller
cannot silently inherit a default policy at a persistence or Battle boundary.

## 9. Effective-Value Calculation

`StatResolver` becomes the single arithmetic seam. The preferred API avoids
another positional parameter list:

```csharp
public sealed record StatResolutionRequest(CharacterStats StartingStats)
{
    public ImmutableArray<StatModifier> Modifiers { get; init; } = [];
    public required StatMinimumBehavior MinimumBehavior { get; init; }
}

public static CharacterStats Resolve(StatResolutionRequest request);
```

Temporary compatibility overloads translate the old
`Resolve(baseStats, existingWeakness, equipment)` call into two internal
requests when both sources are present: equipment resolves with `Reject`, then
Weakened resolves with `Clamp`. They are removed after all repository callers
use typed modifier producers.

Resolution rules are:

1. validate the starting block and every modifier;
2. copy values into a working dense array;
3. apply flat additions with checked integer arithmetic;
4. sum percent basis points per stat, then apply once using a widened integer
   intermediate and midpoint rounding away from zero;
5. apply future operation layers only when separately approved;
6. either reject a below-minimum result or clamp it to the catalog minimum,
   according to the request's explicit policy;
7. validate the immutable result.

An empty request returns an equal immutable value. Resolution never mutates the
input, a loadout, Battle state, or a view model.

## 10. Derived-Calculation Seam

Derived outputs are pure calculators over effective snapshots and action
context. V1 migrates only calculations already used:

- `PhysicalDamage` reads Strength and PhysicalDefense;
- `MagicalDamage` reads Magic and MagicalDefense;
- Drain's prototype path reads Strength and PhysicalDefense and otherwise stays
  unchanged.

These calculators remain dedicated named APIs rather than entries in
`CharacterStats`. Future calculators may return values such as Hit Chance,
Dodge, Crit, Physical Attack Power, Physical Defense Total, or Action Delay,
but those values are not stored or persisted unless profiling later proves a
cache necessary.

V1 adds no DEX/RFL/SPD accuracy, dodge, critical, initiative, or action-speed
formula. The existing Technique-only hit seam and its independent RNG stream
remain unchanged.

## 11. Equipment Integration

Equipment remains immutable definitions plus an immutable loadout. The storage
inside `EquipmentBonuses` migrates from seven named fields to typed FlatAdd
modifiers, while the type name and old named constructor/getters can temporarily
translate old fixtures.

New equipment content is built with stat IDs, for example a test-only DEX item
uses `StatId.Dexterity` rather than adding another field to
`EquipmentBonuses`. `EquipmentLoadout` emits modifiers in declared numeric slot
order, with the item ID as provenance. No dictionary iteration can affect the
result.

The existing four prototype items retain their exact bonuses:

- Wooden Sword: Strength +3;
- Cloth Cap: PhysicalDefense +1;
- Leather Armor: PhysicalDefense +4;
- Copper Charm: MaxHP +5.

Preview/equip/unequip resource behavior remains unchanged: increasing a maximum
does not heal, decreasing one clamps the current resource, invalid totals reject
without changing the loadout, and equipment cannot change during Battle.

## 12. Style Stance Integration

`StanceModifiers` becomes a typed PercentAdd modifier collection instead of a
three-field limit. The existing constructor/getters may serve as a temporary
adapter, but each current field maps to the canonical ID:

- StrengthPercent -> Strength;
- DefensePercent -> PhysicalDefense;
- ResistancePercent -> MagicalDefense.

`CombatStyleRules.ApplyStance` delegates stat arithmetic to `StatResolver`.
`CombatStyleRules` still owns Style-specific transformation: when Shifted, it
multiplies only positive stance modifier magnitudes by 0.85 before handing them
to the resolver; negative stance modifiers remain at 1.00. Existing fixed-point
rounding stays exact.

The three prototype stance definitions do not change:

| Style | STR | PHYDEF (old DEF) | MGKDEF (old RES) |
|---|---:|---:|---:|
| Sword God | +20% | -20% | 0% |
| Water God | -15% | +20% | +15% |
| North God | +10% | -10% | +10% |

Style Shift physical damage, Technique hit resolution, BASIC ATTACK guaranteed
hit, active/turn-start Style lifecycle, navigation, and action order remain
outside this migration and unchanged.

## 13. Magic Compatibility

The current broad `Magic` stat remains canonical. `Resistance` maps one-to-one
to `MagicalDefense`, so Fireball keeps the exact formula:

```text
max(1, scaled base power + Magic - floor(MagicalDefense / 2))
```

Fireball Size, Output, MP cost, targeting, event output, and last-successful
configuration are unchanged. INT, CON, MDEX, MxMP, EfMP, and other future values
have no Magic effect in V1. Drain remains a prototype physical-strength path
and is not reclassified as Magic.

## 14. Status UI Extension Seam

V1 preserves the current flat Status rows exactly:

```text
NAME, HP, MP, STR, DEF, MAG, RES, AGI
```

`DEF`, `RES`, and `AGI` use compatibility projections. New V1 stats are not
automatically dumped onto this page. Preparation comparison likewise continues
to show the current supported set.

The future seam is catalog metadata plus an application-level projection:

```csharp
StatGroupView(GroupId, Label, ImmutableArray<StatRowView> Rows)
```

A later approved UI slice can map catalog categories into BODY, COMBAT, MAGIC,
SENSES, RESISTANCES, and CONDITION groups. Presentation receives immutable
rows; it does not inspect core objects or discover properties with reflection.
The existing row-based renderer can consume the eventual projection without a
rules-layer UI dependency.

## 15. Future Variable Parameter Ownership

Variable parameters are not added to `CharacterStats` in V1. A future
`ConditionState`-like owner should hold changing physiological, mental, and
environmental state using its own stable `ConditionId` catalog, update cadence,
bounds, and persistence rules. It may emit frozen `StatModifier` values into
resolution when a condition actually affects a capability.

This separation prevents a hunger value from being mistaken for a trainable
ability and prevents stat equipment from directly editing environmental state.
It also lets frequently changing conditions use a lifecycle different from
base character growth.

WGT and EQP are not classified yet because the supplied vocabulary does not
define whether they are stored measurements, inventory/load projections, or
condition inputs. KRM, FAM, and COR should be owned by future social/world
models unless later evidence establishes a character-capability meaning. None
of these ambiguities blocks V1 because no ID or storage is created for them.

## 16. Flow Extension Seam

Flow is not implemented. The foundation exposes its future stat inputs without
coupling Stats to Combat Styles:

```csharp
public readonly record struct FlowStatInputs(
    int Reflex,
    int Dexterity,
    int Balance,
    int TechniqueMastery);
```

The record above is an illustrative future adapter contract, not a V1 type.
A future Flow calculator can build the first three values from one effective
`CharacterStats` snapshot:

```csharp
effective[StatId.Reflex]
effective[StatId.Dexterity]
effective[StatId.Balance]
```

Technique Mastery comes from a separate future mastery owner keyed by Technique
ID. It is never inserted into `CharacterStats`. FAIL/PARTIAL DEFLECT/PARRY/
COUNTER bands, timing, reactions, and counter resolution remain future Combat
Style design work.

## 17. Persistence Considerations

No save system exists in the probe, so V1 implements only persistence-safe
identity metadata and catalog validation. Future serialization must:

- write stable catalog IDs such as `core:stat.dexterity`, never enum ordinals or
  display labels;
- place character stats in a versioned save segment;
- serialize deterministically in `StatCatalog` order;
- reject an unsupported future segment version before constructing gameplay
  state;
- reject unknown or duplicate stat IDs in a supported version with a specific
  diagnostic rather than silently creating a string-key entry;
- migrate old names explicitly: MaxHP/MaxMP/Strength/Magic map directly,
  Defense maps to PhysicalDefense, Resistance maps to MagicalDefense, and
  Agility maps to LegacyAgility;
- initialize newly introduced V1 IDs to zero only while migrating the old
  seven-stat schema;
- preserve deprecated IDs as migration/tombstone entries and never reuse them.

If forward-compatible opaque preservation is later required for mods, it
belongs in the save/content boundary as an extension payload. Opaque values
must not enter the typed gameplay `CharacterStats` block.

## 18. Migration Strategy

Migration is incremental and keeps every commit buildable:

1. Add `StatId`, `StatCatalog`, dense immutable `CharacterStats`, builder, and
   structural tests. Preserve the seven-argument constructor and old getters;
   migrate repository `with` expressions to `With(StatId, value)` in the same
   commit.
2. Add typed modifiers and central resolution. Convert equipment and stance
   modifier containers behind compatibility constructors, then move preparation
   preview/effective calculation to the new request API.
3. Move Battle's Weakened, Style stance application, physical damage, Drain,
   and Magical damage consumers to canonical IDs. Preserve the copied Battle
   snapshot and all existing formulas, Style/Shift rules, RNG streams, and
   round-robin order.
4. Confirm `ActorSnapshot`, `ActorView`, Status, preparation comparisons, tests,
   and documentation use the canonical block while retaining exact old output.
   Add tests proving the new values survive construction, equipment, Battle
   snapshotting, and read-model projection without gaining unapproved behavior.

After these commits, new code uses `StatId` and builders. The old constructor,
`Defense`, `Resistance`, `Agility`, and compatibility modifier getters remain
only for explicitly listed UI/legacy call sites. A repository check tracks their
remaining references. They are removed under the gates in section 5, never by
maintaining a second model.

No save schema is fabricated during this migration. When persistence arrives,
its initial implementation includes the versioned mapping and historical-shape
tests in the same change.

## 19. Test Strategy

Implementation is test-first and focused by boundary:

- catalog completeness, stable-ID uniqueness, enum validity, compatibility
  flags, and minimum metadata;
- builder construction of every V1 stat without positional arguments;
- immutable structural equality, `With`, copy isolation, and invalid/overflow
  rejection;
- exact legacy seven-stat mapping, especially Defense/Resistance/Agility;
- deterministic flat and percent ordering, percentage summing, rounding,
  clamping, source ordering, and empty-resolution identity;
- generic test-only equipment modifying a new stat without production content
  changes;
- exact existing equipment preview/equip/resource behavior;
- exact existing Weakened-before-stance results and three prototype stances in
  established and Shifted cases;
- unchanged physical damage, Drain, Fireball, Technique accuracy, BASIC ATTACK,
  round-robin scheduling, event logs, and golden replay;
- expanded values surviving Battle snapshot/read models while remaining absent
  from the current Status output;
- old Status and preparation rows remaining byte-for-byte/text-for-text
  compatible.

Each implementation commit runs the narrow core or presentation executable that
covers its boundary. The repository's full verifier runs once at the final gate
in Debug and Release, followed by the established visual/Godot QA only if the
implementation plan's final task reaches those files. This design-only task
does not run engine QA.

## 20. Explicit Non-Goals

Expanded Stats Foundation V1 does not implement or rebalance:

- Flow, parry, deflect, counter, or reactions;
- Technique Mastery, Style Mastery, ranks, or progression;
- hunger, hydration, energy, oxygen, temperature, stress, toxicity,
  adrenaline, sanity, or other condition/survival systems;
- KRM/FAM/COR social or world systems;
- inventory, equipment durability, encumbrance, or full EQP/WGT semantics;
- a complete derived-stat formula suite;
- DEX/RFL/SPD accuracy, dodge, crit, initiative, or action delay;
- a scheduler rewrite or any use of Agility in turn order;
- new Status tabs, groups, pages, or a preparation UI redesign;
- expanded Magic formulas or effects from INT/CON/MDEX/MxMP/EfMP;
- new production equipment, Styles, Techniques, spells, or stat balance values;
- content schemas, save serialization, or migration runtime;
- procedural world work or unrelated battle restructuring.

## Self-Review Record

- **Ownership:** base capabilities, current resources, Battle-local state,
  future conditions, social/world values, and derived results have distinct
  owners.
- **Single truth:** legacy names are aliases into one immutable value block; no
  parallel old/new stat record is stored.
- **Migration:** each old field has an explicit mapping and removal gate; new
  old-shape defaults are explicit.
- **Scope:** only the V1 IDs, typed modifiers, current calculators, compatibility
  projections, and tests are planned. Future formulas/types shown as
  illustrative seams are marked non-V1.
- **Compatibility:** current equipment, Weakened ordering, three stances, Style
  Shift, Technique hit RNG, BASIC ATTACK, Fireball, Drain, current UI text,
  Battle snapshot ownership, and round-robin order are pinned by regression
  tests.
- **Assumptions:** no unavailable legacy formula or undocumented meaning is
  inferred. WGT/EQP and social-value semantics remain outside the stat catalog.

No blocking architectural question remains for this foundation. Production
starting values/formulas for the new stats and the eventual grouped Status UX
are intentionally deferred content/design decisions, not gaps to fill in V1.
