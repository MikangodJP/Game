# Minimal character stats — bounded prototype update

Historical record of the stat-only revision. The subsequent
[equipment and command-grid update](EQUIPMENT_REPORT.md) adds preparation and
moves the detailed stat display out of battle; its golden baseline is unchanged.
The implemented Expanded Stats Foundation V1 is recorded in the addendum at the
end; the original findings below remain historical evidence.

Verified 2026-09-09. This is a disposable stat model, not the owner's eventual
RPG stat design. No equipment, growth, new statuses, magic formulas, turn-speed
rules or generic modifier framework were implemented.

## Exact prototype profiles

| Actor | MaxHP | MaxMP | Strength | Defense | Magic | Resistance | Agility |
|---|---:|---:|---:|---:|---:|---:|---:|
| Adventurer | 80 | 12 | 12 | 8 | 6 | 6 | 10 |
| Goblin | 38 | 2 | 8 | 5 | 2 | 3 | 6 |
| Wolf | 46 | 0 | 10 | 4 | 1 | 3 | 12 |

The requested example numbers were used without adjustment. All stats are
integers. MaxHP must be positive; other base values must be nonnegative. No
gameplay stat caps exist. Omitted current HP/MP start at their maxima; explicit
injured/depleted snapshots must fit those maxima. Healing caps at MaxHP.
The existing state-level MP mutation permits bounded restoration and rejects
overflow without mutation. No restoration ability/op was added.

## Calculations and scope

Strike and the damage node of Crush are **Physical**:

```text
physical = max(1, BasePower + effective Strength - floor(effective Defense / 2) + variance)
guarded  = max(1, floor(physical / 2))  // only while Defending
applied  = min(current target HP, physical or guarded)
```

Strike: BasePower 2, uniform variance 0..2 inclusive.
Crush damage: BasePower 1, uniform variance 0..1 inclusive.
These draw from the unchanged named deterministic `battle.effect` stream.
`PhysicalDamage.Calculate` centralizes the integer calculation, using a wider
intermediate and a checked integer result, with no UI formula copies.

Defend still consumes an action and expires at the next accepted action.
Defense mitigation happens first, Defend second, remaining-HP clamping last.
The new requirement that physical damage remain at least 1 means Defend now
retains a one-point **physical** hit. Legacy prototype damage can still be
reduced to zero. Tests cover both paths and ensure zero prevention does not
emit a misleading GuardBlocked event.

Drain stays **Prototype**: round `4 + .7 * effective Strength + variance(0..2)`
away from zero at midpoints, subtract full effective Defense, minimum 0, then
guard and the HP cap. Its heal is half of actual applied damage, rounded away
from zero at midpoints. Magic and Resistance do not enter that path.

Magic, Resistance and Agility are stored/displayed only. A complete replay with
all three changed dramatically for every actor produces the same event bytes,
including the same turns, abilities, damage, healing and outcome.

## Ownership and compatibility

**No architectural blocker was found.** `CharacterStats` is a small immutable
value. `StatResolver.Resolve(baseStats, existingWeakness)` is the central
base-to-effective seam; the only non-base input is the already-existing frozen
Weakened amount. Its existing Strength/Defense subtraction and expiration were
preserved, without creating another modifier system.

`ActorSeed` carries a base-stat value and optional current vitals. BattleState
owns resource changes and validates bounds against effective maxima. Reads
project effective values into immutable `ActorSnapshot`s. The evaluator still
freezes caster stats once per action and captures targets at each node. These
contracts are covered by both old prototype tests and a new physical
self-weakening test.

The adapter copies effective values into the display model. The existing party
panel now displays HP/MP, STR/DEF, MAG/RES, AGI and status; no new menu is needed.
MAG/RES/AGI are muted to distinguish placeholders. Enemy profiles are available
in the immutable read model, without an extra enemy-inspection screen. Stats
inspection and all 149 WIP leaves leave the event stream and RNG unchanged.

Later stat contributions have one resolver entry point, and physical damage
already consumes its result. No equipment API or modifier abstraction was built.
The original three NON-BLOCKER findings remain in [REPORT.md](REPORT.md).

## Verification and complete battles

Run from the repository root:

```powershell
.\probes\phase1a\launch-visual.ps1          # interactive window
.\probes\phase1a\launch-visual.ps1 -Verify  # all tests + real Godot QA
```

Verified with .NET SDK 8.0.425 and Godot 4.6.3 .NET on Windows x64:

| Check | Result |
|---|---|
| Core build and tests, Debug | 37/37; zero warnings/errors |
| Core build and tests, Release | 37/37; zero warnings/errors |
| Godot C# build | Zero warnings/errors |
| Presentation model tests | 9/9 |
| Real Godot rendering/input QA | 135 checks passed; 11 native 320×240 screenshots |
| Current golden and cross-process/culture replay | Exact byte match |
| Headless complete scenario, seed 20260909 | Victory, 15 actions, Adventurer HP46/MP0 |
| Visual QA's restarted Defend-then-Attack sequence | Victory, Adventurer HP7/MP12 |

The distinct final vitals come from different command sequences, not different
combat rules. The visual harness has only Attack/Defend/Run while the headless
fixture uses Crush and Drain too.

The twelve focused core tests cover default maxima, explicit seed bounds,
healing/MP bounds, Strength and Defense comparisons across identical seeds,
odd Defense rounding, minimum damage, guard ordering, frozen snapshots,
placeholder-stat independence, Drain compatibility and version labels.
For unsaturated hits, +10 Strength yielded exactly +10 damage and +20 Defense
yielded exactly -10 damage across 16 seeds.

The real-engine checks exercise the existing WIP/navigation/three-command
paths plus the new stat projection, then finish an ordinary battle. Screenshots
were visually checked for legibility and clipping. Logs, PNGs and `qa.txt`
ending in `PASS ALL` are under the ignored `artifacts/visual/` directory.
Controller events were injected through Godot; no physical controller claim is
made. The existing English bitmap font and fixed inputs remain prototype limits.

## Intentional golden revision

The old 25 tests first ran against the changed rules: the two old-golden
comparisons failed as expected; the remaining behavior tests and twelve new
stat tests passed. The new full event log was then inspected before acceptance.
An independent RNG/formula check reproduced its first seven damages:
`13, 1, 10, 12, 1, 11, 13`.

| | Pre-stat baseline | Current stat baseline |
|---|---|---|
| File | `golden/archive/battle-20260909.pre-stats.log` | `golden/battle-20260909.log` |
| Events / bytes | 88 / 3,080 | 73 / 2,590 |
| Actions | 20 | 15 |
| Outcome / hero vitals | Victory / HP10 MP0 | Victory / HP46 MP0 |
| Code / content labels | `phase1a-1` / `literals-1` | `phase1a-stats-1` / `literals-stats-1` |

Old SHA256:
`412e6d9a1a1a7bd1f27a51779b3512419681fe045e5283fb9975c2e47f5c98f2`

Current SHA256:
`6d8bcd6f0f8eb977f05572e2b383fc3e997aa24548a5a8700a7ac0816c29b6dd`

Changes are caused by the requested profiles, physical Defense mitigation and
minimum damage. No cosmetic stat events were added. The seven-field canonical
format, invariant formatting, LF/UTF-8 encoding, RNG algorithm and scripted
controller policy are unchanged. Normal verification never regenerates a
baseline. The archived old file is retained as evidence, not executed through
a second legacy rules engine.

## Files added or changed

- Added `Probe/Stats.cs` and `Tests/StatTests.cs`.
- Changed `Probe/Rules.cs`, `Probe/BattleState.cs`, `Probe/Scenario.cs` and
  `Tests/Program.cs` for effective stats, physical paths and the revised fixture.
- Changed `Visual/Presentation/BattleSession.cs`, `Visual/BattleScreen.cs`,
  `Visual/HarnessQa.cs` and `VisualTests/Program.cs` for projection/display/QA.
- Updated the active golden; added its pre-stat archive and an LF rule for that
  archive in `.gitattributes`.
- Updated `README.md`; added this report and historical-version pointers in
  `REPORT.md` and `VISUAL_HARNESS_REPORT.md`.
- Architecture documents, original sprites, menu hierarchy, input bindings and
  launch scripts were not changed.

---

## Expanded Stats Foundation V1 addendum — 2026-09-13

The former seven-position value is now one immutable dense block keyed by the
closed `StatId` enum. `StatCatalog` defines the persistence-safe identities in
deterministic enum order:

| StatId | Stable ID | StatId | Stable ID |
|---|---|---|---|
| MaxHp | `core:stat.max-hp` | MaxMp | `core:stat.max-mp` |
| Strength | `core:stat.strength` | Magic | `core:stat.magic` |
| Dexterity | `core:stat.dexterity` | Speed | `core:stat.speed` |
| Endurance | `core:stat.endurance` | Constitution | `core:stat.constitution` |
| Intelligence | `core:stat.intelligence` | Reflex | `core:stat.reflex` |
| Balance | `core:stat.balance` | PhysicalDefense | `core:stat.physical-defense` |
| MagicalDefense | `core:stat.magical-defense` | MagicDexterity | `core:stat.magic-dexterity` |
| LegacyAgility | `core:stat.legacy-agility` | | |

Current HP and MP remain mutable resources outside the stored block. The legacy
constructor and properties are aliases over the same storage: Defense maps to
PhysicalDefense, Resistance to MagicalDefense, and Agility only to the isolated
LegacyAgility slot. The legacy bridge supplies zero for DEX/SPD/END/CON/INT/RFL/
BAL/MDEX; it never reinterprets Agility as one of them. New fixtures can use the
builder and `StatId` indexer without widening constructors.

`StatModifier` freezes one typed contribution with source provenance. The
resolver validates all input before arithmetic, orders modifiers by operation,
priority, ordinal source ID, and stat, applies checked FlatAdd values, then sums
all PercentAdd basis points for each stat and applies the sum exactly once.
Independent percentages in one layer therefore do not compound. Results use
deterministic midpoint-away-from-zero rounding and an explicit final `Reject` or
`Clamp` policy.

Persistent preparation resolves base plus equipment with `Reject`. Battle owns
the copied result and resolves frozen Weakened flats before active Style
percentages with `Clamp`, preserving the established sequence:

```text
base + equipment -> copied Battle snapshot -> Weakened -> Style
```

The existing derived calculators now read canonical IDs: physical and Drain use
Strength/PhysicalDefense, while Fireball uses Magic/MagicalDefense. No formula,
coefficient, RNG stream, BASIC guaranteed-hit rule, Technique hit chance, Style
Shift rule, or fixed round-robin scheduling changed. New unused values and
LegacyAgility cannot affect these paths.

Variable condition, physiology, environment, and social/world parameters remain
separate future owners. Flow is not implemented; its future effective-stat
inputs are Reflex, Dexterity, and Balance, while Technique Mastery belongs to a
separate future Technique-keyed owner. Derived results are calculations rather
than stored stats. No new balance values, conditions, hit/dodge/critical system,
initiative, action delay, progression, inventory, save format, or grouped Status
page was added. The visible Status and equipment comparison remain the exact
legacy seven-label projection.

The one complete gate on 2026-09-13 passed 94/94 core tests in both Debug and
Release and 40/40 headless presentation tests. The C# builds reported zero
warnings and zero errors. Godot completed 282 Battle, 193 Field, and 87 Field
Menu checks; `qa.txt`, `field-qa.txt`, and `menu-qa.txt` each ended in
`PASS ALL`. The reviewed golden replay remained byte-exact.
