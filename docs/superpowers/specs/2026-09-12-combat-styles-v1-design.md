# Combat Styles V1 Design

**Status:** Approved 2026-09-12

**Goal:** Add three mechanically distinct prototype Combat Styles, two
Style-owned Techniques per Style, and free in-turn Style switching to the
existing physical Battle flow without adding progression or broad combat
subsystems.

## Repository Context

The current closed stat set is MaxHP, MaxMP, Strength, Defense, Magic,
Resistance, and Agility. Physical damage uses Strength and Defense. Magical
damage uses Magic and Resistance. Agility is intentionally unused, and the
Battle scheduler is fixed round-robin.

The current physical player action is `Scenario.Strike`. It is an ordinary
guaranteed-hit `Ability` resolved through the existing physical damage and
effect pipeline. There is no general hit or accuracy system. This slice keeps
those boundaries: it introduces only a Technique-specific hit seam, does not
use Agility, and does not alter scheduling.

## Scope

The player knows all three prototype Styles by default:

| Stable ID | Japanese name | English name | Battle label |
|---|---|---|---|
| `probe:style.sword-god` | 剣神流 | Sword God Style | `SWORD GOD` |
| `probe:style.water-god` | 水神流 | Water God Style | `WATER GOD` |
| `probe:style.north-god` | 北神流 | North God Style | `NORTH GOD` |

Sword God Style is the immutable V1 Primary Style. Every player Battle starts
with both `ActiveStyleId` and `TurnStartStyleId` equal to
`probe:style.sword-god`. Active Style does not persist between Battles.

Style learning, ranks, schools, teachers, progression, quests, Favorites,
Mastery, true counters, reactions, combo rules, items, terrain interactions,
feints, and action-speed scheduling are out of scope.

## Domain Model and Ownership

One immutable Style definition owns:

- its stable Style ID;
- Japanese and English display names;
- its stance modifiers; and
- exactly two immutable Technique definitions.

One immutable Technique definition owns its stable Technique ID, display name,
owning Style ID, damage multiplier, accuracy multiplier, and reference to the
authoritative basic physical action. All six Techniques reuse the same
underlying Strike ability/effect definition as BASIC ATTACK. The Technique
definition supplies only identity and modifiers; it does not duplicate the
physical formula or effect nodes.

`CharacterPreparation` exposes a fixed read-only collection containing all
three known Styles and the fixed Sword God Primary Style. When it creates the
player's `ActorSeed`, it copies a value-only Style profile into the Battle
snapshot. Generic actors and the historical headless scenario may have no
Style profile.

`BattleState` owns mutable `ActiveStyleId` and `TurnStartStyleId` values for
each styled actor. The UI submits stable IDs. Battle resolves those IDs against
the actor's snapshotted known definitions, so a caller cannot supply forged
Technique multipliers or use a Technique owned by another Style.

Active Style is Battle-local and is not exported in `EncounterResult`.

## Prototype Style Definitions

Stances modify only existing stats:

| Style | Strength | Defense | Resistance |
|---|---:|---:|---:|
| Sword God Style | +20% | -20% | — |
| Water God Style | -15% | +20% | +15% |
| North God Style | +10% | -10% | +10% |

MaxHP, MaxMP, Magic, and Agility remain unchanged. North God is a distributed,
moderate offense/physical-risk/magical-resilience stance rather than a numeric
midpoint between the Sword and Water stances.

The Battle stat order is:

```text
equipment-resolved ActorSeed stats
→ existing frozen Weakened subtraction
→ Active Style stance
→ action resolution
```

Each modified stat is calculated from the post-Weakened value using exact
integer fixed-point arithmetic, then rounded to the nearest integer with
midpoints away from zero. Definitions must not reduce a stat below zero.

## Prototype Technique Definitions

All Techniques use the existing basic physical action as their underlying
effect:

| Style | Stable Technique ID | Display name | Damage | Accuracy |
|---|---|---|---:|---:|
| Sword God | `probe:technique.sword-god.straight-slash` | `STRAIGHT SLASH` | ×1.10 | ×1.00 |
| Sword God | `probe:technique.sword-god.heavy-slash` | `HEAVY SLASH` | ×1.25 | ×0.85 |
| Water God | `probe:technique.water-god.steady-cut` | `STEADY CUT` | ×0.90 | ×1.10 |
| Water God | `probe:technique.water-god.precise-cut` | `PRECISE CUT` | ×1.00 | ×1.05 |
| North God | `probe:technique.north-god.adaptive-cut` | `ADAPTIVE CUT` | ×1.00 | ×1.10 |
| North God | `probe:technique.north-god.risky-cut` | `RISKY CUT` | ×1.15 | ×0.90 |

These are prototype debug values, not final balance.

## Technique Hit Resolution

Only Technique commands use the new hit seam. BASIC ATTACK and every existing
non-Technique Ability preserve their current hit behavior.

The fixed V1 base Technique hit chance is 90%. The final chance is:

```text
0.90 × Technique Accuracy × (0.85 when Style Shifted, otherwise 1.00)
```

The current normal chances are therefore:

| Technique | Established | Style Shifted |
|---|---:|---:|
| Straight Slash | 90.000% | 76.500% |
| Heavy Slash | 76.500% | 65.025% |
| Steady Cut | 99.000% | 84.150% |
| Precise Cut | 94.500% | 80.325% |
| Adaptive Cut | 99.000% | 84.150% |
| Risky Cut | 81.000% | 68.850% |

Chances are represented as integer millionths. Battle draws an integer in
`0..999999` from a new deterministic stream named `battle.technique-hit`; a
roll below the calculated chance hits. This stream is separate from
`battle.effect`, so Technique hit checks cannot perturb existing damage
variance or the reviewed headless replay.

A missed Technique is a committed action. It emits a typed miss event, applies
no effects, ticks existing status duration, ends the actor's turn, and permits
the normal enemy response loop. A miss does not draw from `battle.effect`.

No existing stat influences this chance. Agility remains unused.

## Physical Damage Modifiers

The existing physical formula first resolves base power, Strength, Defense,
and variance. The action's combined physical damage multiplier is then applied
once, before Guard and current-HP clamping, using exact integer arithmetic and
nearest rounding with midpoints away from zero. The existing minimum physical
damage of one remains intact.

For a Technique:

```text
Technique Damage × (0.85 when Style Shifted, otherwise 1.00)
```

For BASIC ATTACK and any other existing physical Ability used by a styled
actor:

```text
0.85 when Style Shifted, otherwise 1.00
```

The Style Shift damage penalty applies only to physical damage nodes. Magical
and legacy Prototype damage nodes are unchanged.

BASIC ATTACK remains the existing guaranteed-hit Strike path and performs no
Technique hit roll. It nevertheless receives `Damage ×0.85` while Style
Shifted, preventing it from bypassing the direct physical-action cost of a
Style change.

Generic actors without a Style profile have a physical multiplier of 1.00.
This keeps the historical `Scenario.Run` golden log byte-for-byte unchanged.

## Style Shift Lifecycle

Changing Style is a Battle-owned, immediate free operation permitted only for
the actor whose turn is active. A valid change:

- resolves the requested stable ID from the actor's known Styles;
- updates `ActiveStyleId` immediately;
- emits a Style-changed event;
- consumes no action, MP, effect roll, or Technique-hit roll; and
- leaves `TurnStartStyleId` unchanged.

Selecting the already active Style is a successful no-op and emits no duplicate
event. An unknown Style or an off-turn change is rejected without state or event
mutation.

Style Shift is active exactly when:

```text
ActiveStyleId != TurnStartStyleId
```

While shifted:

- every Technique receives Accuracy ×0.85;
- every physical player action, including BASIC ATTACK, receives Damage ×0.85;
- each positive stance modifier magnitude receives ×0.85; and
- each negative stance modifier magnitude remains ×1.00.

Examples:

- shifted Sword God: Strength +17%, Defense -20%;
- shifted Water God: Strength -15%, Defense +17%, Resistance +12.75%;
- shifted North God: Strength +8.5%, Defense -10%, Resistance +8.5%.

Switching away and then back to the turn-start Style before acting makes the
comparison equal again and removes all direct Style Shift penalties. The
Style-changed events remain as an accurate history of the free changes.

`TurnStartStyleId` is refreshed to the current Active Style only when the fixed
round-robin scheduler advances back to that actor for its next turn. Rejected
commands and free Style changes never refresh it. Consequently, the shifted
stance remains in force during enemy responses before the next player turn.

## Battle Interaction

The Battle root remains the existing 3×3 grid. Selecting `ATTACK` opens one
Physical Style interaction screen containing exactly three vertical actions:

1. the current Style's first Technique;
2. the current Style's second Technique; and
3. `BASIC ATTACK`.

The screen shows the current ASCII Battle label because the existing bitmap
font cannot render Japanese glyphs. Domain data retains both Japanese and
English names.

Controls are:

- Left/Right: move through the known Style order Sword → Water → North and
  apply each change immediately, clamping at the ends;
- Up/Down: select one of the two current Techniques or BASIC ATTACK;
- Enter: retain the chosen action and open the existing living-enemy target
  picker;
- Escape: return to the Battle root without reverting Active Style.

The selected row is preserved when Style changes, so the two Technique rows
update in place and BASIC ATTACK remains the third row. Target cancellation
returns to the Physical Style screen with its Style and selected row intact. A
second Escape returns to the root. Neither operation consumes a turn or reverts
Style.

After a confirmed action resolves, the existing message flow and enemy response
loop run. Returning for the next player turn opens at the Battle root, with the
Style now established.

## Events and Presentation

The core adds typed events for a real Style change and a missed Technique.
`ActionStarted.Detail` uses the stable Technique ID for Technique actions and
continues to use `probe:ability.strike` for BASIC ATTACK. Readable messages map
definitions to their display names; persistent or behavioral decisions never
use display text.

The Battle read model exposes immutable values for known Styles, Active Style,
turn-start Style, shifted state, and currently available Techniques. UI code
does not calculate ownership, hit chance, stance stats, or damage modifiers.

## Rejection and Boundary Rules

- A Technique not owned by the Active Style is rejected before action commit.
- A dead, friendly, missing, or otherwise illegal target follows the existing
  command-rejection boundary and leaves the turn available.
- A rejected Technique does not perform a hit or effect draw.
- Free Style changes are unavailable after Battle has finished.
- Backing out of ATTACK or target selection changes no event or state beyond
  Style changes the player already made deliberately.
- Fireball, Defend, Run, equipment locking, result application, and Field state
  retain their existing ownership and command paths.

## Testing and Verification

Core test-first coverage will establish:

- exact Style and Technique IDs, names, ownership, counts, and multipliers;
- all three known Styles and Sword God as Primary/default;
- immediate free changes, clamped navigation, no-op same-Style selection,
  off-turn rejection, Back persistence, and away/back Shift removal;
- turn-start refresh only when round-robin returns to the player;
- exact established and shifted stance values, including positive attenuation
  and full negative modifiers;
- exact Technique hit probabilities, deterministic hit/miss results, a separate
  hit stream, and no Agility dependency;
- miss event order and normal turn/status/enemy-response behavior;
- Technique ownership rejection;
- Technique damage multipliers and the shifted combined multiplier;
- BASIC ATTACK's existing guaranteed hit plus shifted Damage ×0.85;
- no modifier on magical or legacy Prototype damage; and
- unchanged historical golden bytes.

Presentation tests will cover the three-row Physical Style screen, immediate
left/right changes, Technique-list replacement, BASIC ATTACK, selection
retention, target confirmation/cancellation, root Back behavior, readable
Technique/miss messages, and continued Magic/Defend/Run behavior.

Godot QA will drive the real input path and check the visible Style label,
Technique rows, BASIC ATTACK row, switching, target return, action messages,
window bounds, and the existing limited palette.

The final gate is:

```powershell
pwsh ./probes/phase1a/launch-visual.ps1 -Verify
```

It must pass Debug and Release core tests, presentation tests, all three Godot
QA runs, and the byte-exact historical golden comparison without regenerating
the baseline.
