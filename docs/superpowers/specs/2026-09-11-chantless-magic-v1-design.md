# Chantless Magic V1 Design

**Status:** Approved 2026-09-11

**Goal:** Deliver one persistent, configurable, chantless Fireball from the Field control menu through normal Battle resolution without changing chanted casting, action priority, or Transformation Magic.

## Scope

This slice contains two changes:

1. Enter dismisses passive Field-menu Info and Placeholder panels one layer, matching Escape. Interactive panels retain their own Enter behavior.
2. The player can configure and cast one chantless Base Magic: Fireball.

Chanted casting, interruption, enemy-first resolution, area targeting, known-form efficiency, spell learning, multiple spells, elemental resistance, and progression remain out of scope.

## Repository Integration Choice

The existing Battle hierarchy remains intact. Only the current WIP leaf at `MAGIC > ELEMENTAL MAGIC > Fire` becomes functional. The visible leaf may stay labelled `Fire` to preserve the existing elemental taxonomy, but the domain definition, stable ID, ability, messages, and tests identify the spell as `Fireball`.

The existing `TRANSFORMATION MAGIC` category and its five WIP leaves remain byte-for-byte equivalent in structure and behavior. No general Battle-menu reorganization is permitted.

Rejected alternatives:

- Adding a new Battle `CHANTLESS` category would restructure the existing ten-category Magic tree before chanted casting exists.
- Building a generic multi-form spell framework would add unused chant, AoE, and efficiency machinery.
- Keeping separate Field and Battle configurations would violate persistent single ownership.

## Domain Model and Ownership

`CharacterPreparation` remains the authoritative persistent player object. It owns:

- a read-only learned Base Magic collection containing Fireball by default;
- one current `ChantlessMagicConfiguration`;
- validation that configurations refer to a learned Base Magic;
- a battle lock preventing configuration changes while its encounter is active.

`BaseMagicDefinition` requires a stable ID, display name, base MP cost, and base damage. Fireball is the first literal definition. `ChantlessMagicConfiguration` requires a non-null Base Magic and valid Size and Output quarter-step values. Battle reads the player-owned configuration; it does not own a second authoritative copy.

## Quarter-Step Representation

Size and Output are integer quarter steps:

- minimum `1` = `0.25`
- maximum `16` = `4.00`
- default `4` = `1.00`

One central quarter-step utility validates, formats, and converts the values. UI and Battle do not repeat division-by-four logic. Display uses two decimal places and invariant formatting. Cost and damage calculations use integer/rational arithmetic so binary floating-point artifacts cannot enter state, UI, or tests.

## MP Cost

One dedicated calculator implements:

```text
ceil(BaseMpCost × Output × (0.5 + 0.5 × Size))
```

For quarter steps `S` and `O`, the exact equivalent is:

```text
ceil(BaseMpCost × O × (4 + S) / 32)
```

The result is at least 1. Fireball's base MP cost is 4. The Field preview and Battle consumption call the same calculator.

## Fireball Damage

Fireball's centralized base damage is 8, placing its default hit near the existing useful Strike range. Output scales base power; Size does not affect single-target damage:

```text
scaled base = ceil(BaseDamage × OutputSteps / 4)
damage = max(1, scaled base + caster Magic - floor(target Resistance / 2))
```

This adds the smallest explicit magical-damage path to the existing effect pipeline by using the already-defined Magic and Resistance stats. Existing physical and prototype damage paths remain unchanged. Existing guard handling remains the common post-mitigation behavior.

## Field Adjustment Interaction

`MAGIC > Adjustment` becomes an interactive panel with three selectable rows:

1. Size
2. Output
3. Apply

Base displays Fireball read-only. Up/Down clamp across the three rows. Left/Right change the selected numeric row exactly one quarter step and clamp at 0.25/4.00. A 16-cell, keyboard-only pixel slider reflects the exact step count. MP Cost updates from the central calculator.

Opening the panel copies the committed configuration into a controller-owned draft. Enter on Apply validates and commits the draft, closes only the Adjustment panel, and returns to the Magic submenu. Escape discards the draft and returns to the Magic submenu. Enter on Size or Output does nothing. No draft mutation reaches the player until Apply.

The Information leaf becomes a concise passive Info panel explaining chantless size/output manipulation. Enter or Escape dismisses it one layer without closing the Field control menu.

## Battle Flow

Selecting `MAGIC > ELEMENTAL MAGIC > Fire` performs an affordability check using the current player-owned configuration before target selection when the existing presentation flow allows it.

- If MP is insufficient, the player sees `Not enough MP.` immediately, no target picker opens, no action or enemy turn occurs, HP/MP stay unchanged, and the Battle menu remains valid.
- If MP is sufficient, the established living-enemy target picker opens.
- Confirming a target builds the Fireball ability from the same configuration, submits it through normal player-first `BattleState.TakeTurn`, deducts MP once, applies magical damage, then runs the existing enemy responses.
- Cancelling targeting spends nothing and returns to the Fire leaf selection.

The deterministic machine events retain the stable Fireball ability ID, MP delta, damage, and normal turn order. Readable Battle messages identify Fireball and include Size, Output, and MP Cost from the submitted configuration.

No action-order reversal, cast delay, or interruption state is introduced. A future chanted implementation can add a separate command-resolution policy without changing the chantless configuration or calculators.

## Testing and QA

Test-first coverage includes:

- passive Field Info/Placeholder Enter and Escape dismissal;
- exact quarter-step defaults, changes, clamps, conversion, and formatting;
- required Base Magic and default learned Fireball;
- Adjustment Apply/cancel transactions and persistence across Field/Battle/Field;
- exact MP examples, limits, monotonicity, and preview/execution identity;
- Fireball damage, Output influence, Size non-influence, single MP deduction, and player-first enemy response;
- pre-target insufficient-MP failure with no target picker, damage, resource mutation, or enemy action;
- preservation of Attack, Defend, Run, all Transformation Magic leaves, and the golden fixture;
- Menu QA rendering, slider change, Apply, cancel, viewport bounds, and palette;
- Battle and Field engine QA for casting and persistence.

The final gate is `pwsh ./probes/phase1a/launch-visual.ps1 -Verify`, followed by the requested normal-game acceptance flow.

## Commit Boundaries

1. `fix: allow enter to dismiss menu info panels`
2. `feat: add chantless magic configuration`
3. `feat: cast configured fireball in battle`

No additional implementation or documentation commit is added; supporting docs are included with the relevant boundary.
