# Phase 1A findings — 2026-09-09

> Historical pre-stat findings. The original golden is now preserved in
> `golden/archive/battle-20260909.pre-stats.log`. Current stats, formula and
> verification are documented in [STAT_SYSTEM_REPORT.md](STAT_SYSTEM_REPORT.md).
> The three deferred NON-BLOCKER findings below remain applicable.

The disposable probe resolves a complete battle end to end. No Phase 1A blocker
was discovered. The three pre-implementation NON-BLOCKER findings remain deferred
below; none required production infrastructure to make this battle work.

## Verified result

- Seed `20260909`: **Victory**, **20 actions**, **88 events**.
- Final persistent delta: `VitalsChanged("adventurer-1", hp: 10, mp: 0)`.
- Goblin and Wolf both reach zero HP. Their battle-local identities do not appear
  in the persistent delta list. The input snapshots remain unchanged.
- **Debug: 18/18 tests passed. Release: 18/18 tests passed.** Both builds reported
  zero warnings and zero errors, using the commands in `README.md`.
- In each configuration, two fresh OS processes (`en-US`, `tr-TR`) produce the
  exact checked-in golden bytes. Both configurations match the same golden file.
  Cross-platform identity has not been tested or claimed.
- Tests cover lethal actual damage, dead-target filtering and `affectsDead`,
  frozen caster/live-per-node target values, `priorTargets`, applied-amount
  scaling, rounding, required rejection versus NoOp, partial application,
  action cost retention, status expiration, invalid commands, defeat, input
  ownership, result sealing, RNG stream isolation, and regression bytes.
- An independent review identified a NoOp diagnostic path that could append to
  the state log after `Finish()`. Its failing regression test was reproduced;
  the path now respects result sealing and the regression passes.

The golden was inspected against the mechanics, not accepted merely because it
was generated. The first Crush applies weakness 5 and deals 11 damage. The
weakened Goblin's Crush uses its starting Attack 3, applies `round(1.5) = 2`
weakness, then deals 3 damage against the adventurer's newly reduced defense.
The first Drain deals 10 actual damage and heals 5. Across the battle the hero
takes 80 damage, heals 10, and spends 12 MP: starting 80 HP / 12 MP becomes
10 HP / 0 MP. Enemy damage totals are exactly 38 and 46, including lethal caps.

## What the required shapes turned out to be

| Shape | What execution demonstrated | Phase 1B implication |
|---|---|---|
| `BattleState` | Must own mutable actors, active status **instances**, turn position, RNG state, and ordered events. Inputs are immutable snapshots; reads return value snapshots. Dead actor indices remain valid. `Finish()` seals an immutable result. | Keep definitions, battle-owned state, and persistent identity separate. No mutable actor/status collection needs to escape this owner. Do not expose the live state as the result or UI data. |
| `OpContext` | Needs a once-captured caster snapshot, the current node's target snapshot, and a narrow mutation/logging capability. A lower-level `IEffectState` interface sufficed; Rules never references the concrete `BattleState`. | Place the contract below its Encounter implementation. Context snapshots must distinguish action-time caster values from node-time target values. Effect-list scratch storage belongs to the evaluator, not persistent actor state. |
| `EffectNode` | Plain ordered data was sufficient: op kind, selector, magnitude descriptor, `required`, `affectsDead`, and an optional status reference. Prior references need both the earlier **resolved target IDs** and its aggregate **actual result**. | Make reference identity, order, scope, and aggregate semantics explicit before freezing the schema. The probe uses backward node indices; named content references can resolve to those indices during extraction. No parser or callable content object was needed. |
| `Op` | A small dispatcher applies one mechanical operation through the context and returns `Applied/NoOp/Rejected`, **actual** amount, hit count and killed flag. Damage uses the target snapshot's current defense and caps its report at remaining HP. | Keep selection, node ordering and scratchpad aggregation outside the op. Keep mutations and event emission on the same path. A required rejection stops later nodes without undoing earlier effects or action costs. |
| `EncounterResult` | Needs outcome, seed/version metadata, an immutable event list, and ordered persistent consequences addressed by **InstanceId**, not ActorId. A typed `VitalsChanged[]` was enough for this scenario. | Preserve the snapshot-in / delta-out boundary. The full delta union is still unimplemented; the probe does not claim exhaustive handling of all v0.2 delta kinds. Sealing must include diagnostic/NoOp paths, not only HP/MP writes. |

One concrete dependency direction was enough: `Rules` defines the data and the
small effect-state interface; `Encounter` implements it; the fixture/CLI calls
Encounter. C# namespaces make that direction visible here, but no assembly-level
dependency gate has been built or claimed. Phase 1A has no `Game.Content` module;
the data-versus-meaning boundary remains an extraction task for 1B.

## Probe assumptions, not newly approved production semantics

These choices fill only the gaps needed to execute the fixture:

- **Scheduling:** round-robin over stable input indices, skipping dead actors.
  The scheduler has no fixed party-size logic; the three-actor setup is fixture
  data. No slot reuse or mid-battle insertion is exercised. A 100-action fixture
  guard returns `Aborted` if neither side wins. Production CTB is not implemented.
- **Controller:** hero rotates Crush / Drain / Strike, falling back to Strike
  when MP is insufficient; Goblin uses Crush while it can afford it; Wolf uses
  Strike. Everyone selects the first living enemy in stable actor order. This
  policy lives in the scenario fixture, not the shared rules.
- **Weakened:** subtracts a frozen integer from Attack and Defense, floored at
  zero. It lasts through two affected actor turns and decrements at their turn
  cleanup; self-application therefore counts the current turn. Reapplication
  replaces the magnitude/source and refreshes duration. It is battle-local.
  Only expiration is exercised in the shipped scenario; this is not a general
  stacking, trigger, modifier-layer, or persistence implementation.
- **Execution:** action cost is validated and paid before effect evaluation.
  The evaluator captures caster stats once before node 1. Targets are captured
  for each node after earlier nodes mutate state. Nodes run before the next node,
  in stable target order. Prior results aggregate a node's actual applied amounts
  across its targets. Reused target lists retain IDs that have since died; the
  new node still applies its own dead-target filter.
- **Arithmetic:** game state is integer. Formula intermediates are `double`,
  rounded once using `MidpointRounding.AwayFromZero` before application; damage
  then subtracts defense and is capped at remaining HP. Drain scales actual
  applied damage, not requested damage. Caster Attack is the only stat input
  supported by these magnitude descriptors.
- **Randomness:** FNV-1a over eight little-endian seed bytes followed by UTF-8
  stream-name bytes initializes SplitMix64. All overflow is explicit. Bounded
  draws use rejection sampling. The combat stream is `battle.effect`; a positive
  variance consumes one bounded draw per eligible target. No ambient random,
  clock, hash-collection iteration or culture-dependent formatting enters results.
- **Failures:** semantic skips/rejections follow v0.2's rules. Invalid literals
  and programming errors fail fast in this probe, including optimized Release
  builds. Production recovery, pack validation and save safety are not implemented.
- **Result:** only terminal/current persistent HP/MP are exported. This is a
  vitals-boundary probe, not save/load, status persistence, or a full delta applier.
  Calling `Finish()` early explicitly returns `Aborted`; applying aborted results
  to a session has not been specified or implemented.

## Three deferred findings for Phase 1B

| Classification | Finding and required follow-up | Why it did not block 1A |
|---|---|---|
| **NON-BLOCKER** | v0.2 §4.2 `StatusPersisted` lacks the frozen modifier values required by §4.6, and needs sufficient source/instance identity to preserve source-bound behavior. Amend this boundary before freezing related persistent schemas. | The only status is battle-local. Frozen values and source IDs exist in battle state, and no status delta is exported. |
| **NON-BLOCKER** | §4.6's `(layer, sourcePriority, sourceContentId)` does not fully order multiple modifiers from the same definition. Define a stable final tie-breaker before implementing order-sensitive layers. | The probe has one active weakness slot per actor and no override/multiplicative modifier layers. It does not claim to have solved general modifier ordering. |
| **NON-BLOCKER** | §4.2's compiler-enforced closed-delta exhaustiveness needs a concrete C# implementation and pinned compiler support. Normal class/record inheritance plus a switch does not supply that guarantee; dedicated native union/closed-hierarchy support was in C# 15 preview at review time. | The probe targets C# 12 and exposes one typed vitals-delta collection. It does not adopt preview language features or pretend this is the full union. |

## Handoff

Preserve the fixture, test expectations, explicit evaluation decisions, and golden
bytes as the input to Phase 1B. Replace the experiment's single status slot,
round-robin runner, inline dispatcher and convenient literal representations as
the real model requires. The successful battle establishes a workable execution
boundary; it does not validate production saves, dynamic modifiers, reactions,
summon insertion, world integration, or performance budgets.
