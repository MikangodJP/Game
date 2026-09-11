# Chantless Magic V1 Design

**Status:** Approved correction 2026-09-11

**Goal:** Deliver one configurable chantless Fireball whose cast is adjusted inside Battle and whose last successfully used values persist with the player.

## Scope

This slice provides one real Base Magic, Fireball, plus the Battle UI and persistence needed to adjust and cast it. Chanted casting, interruption, enemy-first resolution, area targeting, known-form efficiency, spell learning, multiple spells, elemental resistance, and progression remain out of scope.

The Field control menu is independent of Battle casting. `MAGIC` on the Field contains exactly `Spells / Information / Back`; it has no adjustment editor or cast configuration state. Passive Field Info and Placeholder panels remain dismissible with Enter or Escape.

## Battle Taxonomy

Battle `MAGIC` first contains:

1. `CHANTLESS`
2. `CHANT`
3. the automatically appended `Back`

`CHANTLESS` contains the complete existing ten-category Magic taxonomy. The visible `ELEMENTAL MAGIC > Fire` leaf is the entry point for the domain spell **Fireball**. The visible element label does not define the spell's identity: its definition, stable ID, ability, messages, persistence key, and tests all use Fireball.

`CHANT` is a passive WIP leaf displaying `Chanted magic is not implemented yet.` Enter or Escape dismisses it without consuming a turn.

`TRANSFORMATION MAGIC` remains a category under `CHANTLESS` with exactly Self Transformation, Beast Transformation, Material Transformation, Size Manipulation, and Polymorph. Those leaves retain their existing WIP behavior.

## Domain Model and Ownership

`CharacterPreparation` owns the persistent player state:

- a read-only learned Base Magic collection containing Fireball by default;
- last-successfully-used chantless configurations keyed by stable Base Magic ID;
- validation that saved configurations use the exact learned Base Magic definition.

This is deliberately spell-specific rather than one universal Size/Output pair. Fireball's first-use configuration is Size `1.00`, Output `1.00`.

`HarnessController` owns only the current cast draft. Opening Fire copies Fireball's last-successfully-used configuration. Arrow input mutates only that draft. A successful `BattleSession.SubmitFireball` result records the draft; no earlier navigation state can write persistent values.

## Quarter-Step Representation

Size and Output are integer quarter steps:

- minimum `1` = `0.25`
- maximum `16` = `4.00`
- default `4` = `1.00`

One central utility validates, clamps, formats, and converts the values. Display uses two decimal places and invariant formatting. Rules use integer/rational arithmetic so binary floating-point artifacts cannot enter state, UI, or tests.

## MP Cost

One calculator implements:

```text
ceil(BaseMpCost × Output × (0.5 + 0.5 × Size))
```

For quarter steps `S` and `O`, the exact equivalent is:

```text
ceil(BaseMpCost × O × (4 + S) / 32)
```

The result is at least 1. Fireball's base MP cost is 4. The Battle adjustment and ability consumption use this same calculator.

## Fireball Damage

Fireball's base damage is 8. Output scales base power; Size does not affect its current single target:

```text
scaled base = ceil(BaseDamage × OutputSteps / 4)
damage = max(1, scaled base + caster Magic - floor(target Resistance / 2))
```

Existing physical paths and guard handling remain unchanged.

## Battle Adjustment Interaction

Selecting `MAGIC > CHANTLESS > ELEMENTAL MAGIC > Fire` opens an opaque black, one-pixel-white-border modal inside the native 320×240 Battle screen. It displays Method `CHANTLESS`, Base `FIREBALL`, Size, Output, centralized MP cost, current MP, two 16-cell sliders, and `CAST`.

The selectable rows are Size, Output, and Cast. Up/Down clamp across those rows. Left/Right changes the selected numeric row by one exact quarter step and clamps at `0.25..4.00`. Enter acts only on Cast. Escape discards the current draft and returns to the still-selected Fire leaf.

Cast validates affordability before target selection:

- If MP is insufficient, Battle displays `Not enough MP.` without opening targets or changing HP, MP, events, enemy actions, or last-used state. Enter or Escape returns to the same adjustment with its draft and selected row intact, allowing correction.
- If MP is sufficient, the established living-enemy target picker opens.
- Escape from targeting returns to the same adjustment with its draft intact. A subsequent Escape from adjustment discards it and returns to Fire.
- Confirming a legal target builds Fireball from the draft, deducts MP once through the existing ability path, applies Output-scaled magical damage, then runs the normal enemy responses.

Last-used state changes only after that successful submission. Adjustment cancellation, target cancellation, insufficient MP, invalid/dead targets, and aborted casts do not update it. Reopening Fire in the same or a later battle copies the saved successful values.

## Testing and QA

Test-first coverage includes Field menu independence, the Chantless/Chant hierarchy, the exact Chant WIP message, complete Transformation taxonomy, exact quarter steps and clamps, draft cancellation, target cancellation, retained insufficient-MP drafts, unchanged failed submissions, same/later-battle persistence, central cost, Output damage, Size cost-only behavior, one MP deduction, and player-first enemy response.

Godot QA drives real keyboard/controller events and captures the Battle method tree, Chant WIP, adjustment modal, slider changes, MP cost, target return, fixed bounds, and black/white modal palette. Menu QA confirms Field Adjustment is absent.

The final gate is `pwsh ./probes/phase1a/launch-visual.ps1 -Verify`, followed by the normal-game acceptance flow.

## Correction Commit Boundaries

1. `fix: remove field magic adjustment`
2. `feat: adjust chantless magic during battle`
3. `feat: remember last successful magic configuration`
