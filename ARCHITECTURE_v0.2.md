# Architecture v0.2 — Untitled 2D Fantasy RPG

**Status:** DRAFT — awaiting project owner review
**Supersedes:** `ARCHITECTURE_PROPOSAL.md` (v0.1) for every section it covers
**Author:** Claude (Lead Architect), revising after Codex's implementation review
**Date:** 2026-09-09

> **How to read this document.** v0.2 is a *complete replacement* for the
> sections it touches. Sections of v0.1 not listed in Part 9 are unchanged and
> remain authoritative. On approval, v0.1 and v0.2 are merged into a single
> `docs/` tree and both files are deleted — two architecture documents is
> exactly the sprawl this project must avoid, and it is tolerable only during
> review.

> **Governing principle for this revision:** the response to a good critique is
> to *remove* a mechanism, not to add one. Part 2 is the ledger proving this
> revision is smaller than v0.1, not larger.

---

# Part 0 — Response matrix

## 0.1 Codex findings

| # | Finding | Verdict | Reasoning |
|---|---|---|---|
| 1 | Stable deterministic hash; don't rely on `string.GetHashCode()` | **ACCEPT (scoped)** | Correct and non-negotiable: .NET randomizes string hashing *per process*, so this breaks reproducibility on a single machine across two runs — it is not merely a cross-platform concern. Scoped smaller than Codex proposed: a stable hash is required only for seed derivation, content-ID→numeric-ID assignment, and any ordering that reaches simulation. Ordinary lookup dictionaries keep default hashing. The enforceable rule is *"no iteration over hash-ordered collections in simulation paths,"* which is narrower and easier to check than banning a method |
| 2 | Snapshot-in / delta-out combat | **ACCEPT** | Codex found a genuine hole in v0.1. Fully specified in Part 4.1–4.2, including the closed delta set and the battle-local/persistent boundary |
| 3 | `rollAlgoVersion` for generated items | **MODIFY** | The *problem* is real and I understated it in v0.1. The *mechanism* is too expensive: retaining and testing every historical generator version forever is an unbounded maintenance tax and a second versioning system. Instead, **store the resolved roll outcome, not the process** (Part 5.1). This deletes the versioning requirement entirely rather than managing it |
| 4 | Cut the expression language; structured descriptors, simple interpreter | **ACCEPT and GO FURTHER** | Codex is right and did not go far enough. v0.2 has **zero runtime expression languages**: magnitudes, selectors, *and* predicates are all structured JSON validated by JSON Schema, evaluated by one small tree-walking evaluator. Human-friendly surface syntax, if ever wanted, becomes a compile-time authoring-tool concern the runtime never sees (Part 4.4) |
| 5 | Insert Phase 1A walking skeleton | **ACCEPT** | Correct, and the sharpest finding in the review. v0.1 committed to a serialization format for a model it had never executed. Phase 1A/1B in Part 7, including the rule that 1A code is a *probe expected to be discarded* |
| 6 | Bounded world event cascade | **ACCEPT** | Correct omission in v0.1. Drained queue, depth cap, budget, diagnostics (Part 5.5) |
| 7 | Split `Game.World` at creation | **ACCEPT (refined)** | Correct — v0.1 admitted the module was oversized and deferred the split, which is the god-object pattern it warned against. Codex's three names left quests and factions in a circular relationship; v0.2 uses four sub-modules with an acyclic internal DAG (Part 3.2) |
| 8 | Simplify save migration to segment level | **ACCEPT** | Two migration granularities would eventually conflict. Component-level versioning is deleted |
| 9 | Replace "no LINQ" with measurable tests | **ACCEPT** | v0.1 stated a style preference as architecture. Replaced with allocation-asserting benchmarks (Part 8) |
| 10 | Add missing sections (UI, errors, debug tooling, localization, packaging) | **ACCEPT** | UI was the most significant omission — at 10,000 items, inventory UI is an architectural problem, not a presentation detail. Part 6 |

## 0.2 Content format — reconsidered from first principles

**Decision: plain JSON + JSON Schema.** This is a reversal of v0.1's TOML choice
and goes further than Codex's JSON5 suggestion.

The decisive argument is not tooling — it is that **finding 4 removed the reason
TOML looked attractive.** v0.1 chose TOML for flat, human-typed rows and pushed
complexity into embedded expression strings. Now that magnitudes, selectors, and
predicates are all *structured nested data*, the content is tree-shaped, which is
what JSON is for and what TOML's array-of-tables syntax handles worst.

Supporting reasons: one format end to end (schema, content, generation, saves,
tooling); JSON Schema validates the *entire* row including formerly-opaque
expression strings; LLM generation and constrained decoding are far better
supported for JSON than for any alternative; no custom parser, ever.

Accepted cost: no comments and no trailing commas. Mitigations: a `"$comment"`
field convention permitted by schema, schema-aware editor autocomplete, and a
validator whose parse errors cite line and column. If hand-authoring ergonomics
later prove to be a real bottleneck — measured, not asserted — JSONC can be
adopted for *authoring only*, compiled to JSON, without touching the runtime.

---

# Part 1 — Resolved decisions

Decisions closed by the owner, now binding:

| ID | Decision | Consequence |
|---|---|---|
| **R1** | PC-first: Windows, Linux (native or Proton), Steam Deck verified. No web. Consoles much later | Godot 4 + C# confirmed; the only argument against it is removed |
| **R2** | Systemic depth and combinatorial scale is the identity, not a 40-hour authored JRPG | The v0.1 architecture's premise is confirmed; O14 closed |
| **R3** | Multiplayer out of scope; do not encode "never" into domain concepts; pay no cost today | Determinism kept only to the level tests need; no netcode seams, no lockstep constraints, no artificial single-player assumptions baked into `EncounterSetup` or `Command` |
| **R4** | Reproducibility, not cross-platform byte-identity: same build + same content version + same seed ⇒ same result | **Fixed-point math is deleted** (Part 5.2). Large simplification |
| **R5** | C# provisional core language | Confirmed |
| **R6** | Core numeric stats may be a closed set in v1; tags must be mod-extensible; core stats must stay fast | Stat enum stays dense and compiled; tag representation redesigned (Part 4.8) |
| **R7** | No authoritative save-size target; compact structure and measurement instead; active inventory may be bounded; storage may be large | 5 MB target deleted; replaced with a measured budget and a bounded-active/unbounded-stored split (Part 5.4) |
| **R8** | Summons are **not** party slots. Encounter supports a variable actor count. v1 may use a 4-adventurer party plus a separate summon budget, not final | `Game.Encounter` contains no party-slot concept at all (Part 5.3) |
| **R9** | Owner arbitrates semantics; Claude owns architectural coherence; Codex owns implementation feasibility; neither wins by role; technical claims need evidence | Replaces v0.1's K.5, which let the architect win by default (Part 8.4) |

Architectural decisions resolved by this revision: content format (Part 0.2),
zero runtime expression languages (4.4), snapshot/delta combat (4.1), passives as
statuses (4.7), frozen modifier magnitudes (4.6), segment-only save migration
(5.4), World split (3.2), error philosophy (6.2).

---

# Part 2 — The reduction ledger

Proof that v0.2 is smaller than v0.1, not a pile of patches.

### Removed
| Mechanism | Why it is gone |
|---|---|
| Expression VM + bytecode compiler | Structured data needs no parser (4.4) |
| The selector mini-language | Structured selectors (4.4) |
| The predicate mini-language | Structured predicates (4.4) |
| Fixed-point arithmetic library | R4 makes it unnecessary (5.2) |
| `rollAlgoVersion` + retained historical generators | Storing outcomes removes the need to version the process (5.1) |
| Component-level save migration | Segment-level only (5.4) |
| "Passive" as a distinct content kind | Passives are statuses (4.7) |
| Dual content-loading paths (loose vs pack) | Packs only, with fast incremental builds (5.6) |
| TOML | One format: JSON (0.2) |
| Global tag bitset | Core bitmask + sorted array (4.8) |
| ADR-per-op | ADR per op *category* (8.4) |
| Coverage-percentage CI gates | Per-op and per-spec-example test requirements (8.1) |
| "No LINQ" style rule | Allocation-asserting benchmarks (8.2) |
| The 5 MB save target | Measured budget (5.4) |
| Cross-platform byte-identity requirement | R4 (5.2) |

### Added
| Mechanism | Justification |
|---|---|
| Closed `PersistentDelta` set | Makes the encounter boundary enforceable rather than aspirational (4.2) |
| Effect-node scratchpad (`prior(...)`) | One narrow, statically validated mechanism replacing the general expression power that was removed (4.5) |
| `World` split into 4 sub-modules | Partitioning, not new abstraction; no new concepts (3.2) |
| UI read-model projection | Was missing; prevents UI becoming a hidden mutation path (6.1) |
| Phase 1A | A probe, discarded afterward (7) |

**Net: fourteen mechanisms removed, five added, three of which replace something
larger.** One custom language remains in the entire project: none.

---

# Part 3 — Layers and dependency rules (revised)

## 3.1 Layers

Unchanged from v0.1 except for the L4 split:

```
L8  Tools          compiler, validator, generators, balance sim, replay viewer
L7  Shell          Godot: rendering, input, audio, UI, scenes, file IO
L6  Ports          interfaces the shell implements
L5  Application    session, command dispatch, save orchestration, read-model projection
L4  World          State ← Space ← Sim ← Narrative  (four modules, acyclic)
L3  Encounter      battle state, scheduling, resolution pipeline, actor AI
L2  Rules          stats, effects, ops, items, abilities, magic, summons, progression
L1  Content        schemas, packs, registries, ID resolution, structured-node evaluation
L0  Foundation     IDs, stable hash, RNG, ordered collections, tags, diagnostics
```

## 3.2 The `Game.World` split

Codex's proposed `Space / Simulation / Narrative` left a genuine cycle: quest
completion changes faction standing (Narrative → Sim) while faction standing
gates quests (Sim → Narrative). Four modules with an explicit base resolve it:

```
Game.World.State       ← world flags, calendar/time, persistent world facts.
                          The shared base. No behavior, only facts.
Game.World.Space       ← zones, tile data, spatial queries, travel graph,
                          persistent zone deltas, entity placement.
Game.World.Sim         ← factions, standing, economy, LOD simulation, ecology.
Game.World.Narrative   ← quests, dialogue, event triggers.
```

Internal dependency DAG, strictly acyclic:

```
State  ←  Space  ←  Sim  ←  Narrative
  ↑________________|__________|
```

The cycle is broken by direction: **Narrative reads Sim through a read-only
query interface (`IFactionQuery`); Sim never references Narrative.** Sim reacts
to narrative outcomes only by subscribing to the event queue. This is the same
technique as the L3/L4 boundary and it is the only sanctioned way peer domains
influence each other.

## 3.3 Dependency rules

The v0.1 table stands, with these amendments:

- `Game.Encounter` **must not** reference `Game.World.*` — unchanged, and now
  additionally must not contain any party, roster, or slot concept (R8).
- `Game.World.Narrative` may depend on `Sim`, `Space`, `State`.
  `Game.World.Sim` may depend on `Space`, `State`. `Space` may depend on
  `State`. `State` depends on nothing above L2.
- Nothing below L5 may reference a UI, view-model, or read-model type.
- `Game.Rules` purity is unchanged and remains the most important prohibition:
  no clock, no I/O, no engine types, no ambient randomness.

---

# Part 4 — Explicit contracts

This part exists because Codex asked for these to be defined rather than
implied. Everything here is normative.

## 4.1 Battle-local vs persistent state

**Rule: battle-local by default. Persistence is opt-in and declared in content.**

`BattleState` owns copies. `Game.Encounter` holds **no reference** to any
persistent instance, and has no access to a repository that could fetch one.

| Battle-local (discarded at battle end) | Persistent (leaves as a delta) |
|---|---|
| Position, facing, formation, cover | Current HP / MP / resource pools |
| Timeline position, action economy, delay | Death (and whether it is permanent) |
| Threat / aggro | Experience gained per track |
| Temporary statuses (the default) | Statuses declared `persistsOutsideBattle: true` |
| Per-battle counters, combo state | Item consumption, acquisition, durability change |
| AI memory and scoring caches | Summon bond changes |
| Reaction budget, cascade depth | Creature capture |
| Summoned actors marked `temporary` | Flags in the `encounter.*` namespace only |
| Hypothetical/preview state | Contract term changes (e.g. taboo violated) |

A status crossing the boundary requires `persistsOutsideBattle: true` in its
definition — visible in content, checkable by the validator, and impossible to
introduce accidentally in code.

## 4.2 `EncounterResult` — the closed delta set

```jsonc
EncounterResult {
  outcome:        "victory" | "defeat" | "fled" | "aborted",
  contentVersion, codeVersion, seed,        // reproducibility triple
  deltas:         PersistentDelta[],        // ordered; applied in order
  eventLog:       EventLog                  // diagnostic; not persisted by default
}
```

`PersistentDelta` is a **closed discriminated union**. `Game.Application`
switches exhaustively over it; the compiler enforces completeness.

```
VitalsChanged      { instanceId, hp, mp, resources[] }
Died               { instanceId, permanent }
ExperienceGained   { instanceId, trackId, amount }
StatusPersisted    { instanceId, statusId, remainingDuration, stacks }
StatusRemoved      { instanceId, statusId }
ItemConsumed       { instanceId, itemInstanceId, count }
ItemGained         { ownerInstanceId, itemInstanceRecord }
ItemDurabilityChanged { itemInstanceId, delta }
SummonBondChanged  { contractId, bondTrack, delta }
CreatureCaptured   { creatureRecord, viaContractId }
EncounterFlagSet   { flagId, value }        // `encounter.*` namespace only
```

**Why closed:** it makes "what can a battle change about the world?" a question
with a finite, readable, reviewable answer. A new persistent consequence
requires adding a delta type, which is a visible change subject to review and
save-migration checks. An open bag of mutations would make the L3/L4 boundary
decorative.

## 4.3 `OpResult` and runtime failure semantics

```csharp
readonly struct OpResult {
    OpStatus Status;   // Applied | NoOp | Rejected
    int      Amount;   // primary magnitude actually applied (0 if none)
    int      HitCount; // targets actually affected
    bool     Killed;   // this op caused at least one death
}
```

No exceptions in the pipeline. No transactions. **Effect application is
explicitly not atomic; partial application is defined behavior.** Adding
rollback would be the "respond to criticism with a new layer" trap; the ordering
tools in 4.5 handle the cases that need it.

Three failure classes, project-wide (generalized in 6.2):

1. **Content errors** — impossible at runtime for validated packs; caught at
   build. A mod pack failing validation is rejected *at load* with a specific
   message; the game runs without it.
2. **Semantic conditions** — target already dead, immune, resource short, cap
   reached. These are **not errors**: the op returns `NoOp` or `Rejected`, an
   `EffectSkipped` diagnostic event is emitted, and the tree continues.
3. **Impossible states** — programming errors. Throw in Debug and test builds
   (fail fast, find it in CI); in Release, emit a structured diagnostic, skip
   the node, and continue. A rules bug must never end a 200-hour session.

A node may declare `"required": true`, meaning `Rejected` aborts the remainder
of the tree. This is the sanctioned way to express "if the first part fails,
don't do the second."

## 4.4 Structured nodes — no runtime languages

All three formerly-textual constructs are structured JSON.

**Magnitude — a formula descriptor, not an expression:**
```json
{ "base": 12,
  "scaling": [ { "from": "caster.stat.attack",       "coeff": 1.4 },
               { "from": "caster.skill.fire_magic",  "coeff": 0.6 } ],
  "clamp": { "min": 1, "max": 9999 } }
```

**Selector — structured:**
```json
{ "scope": "enemies", "shape": { "arc": { "degrees": 120, "maxTargets": 2 } } }
```

**Predicate — structured:**
```json
{ "all": [ { "hasTag": { "target": "kind:plant" } },
           { "gte":    [ { "caster.skill": "fire_magic" }, 20 ] } ] }
```

Properties this buys, which a text DSL cannot:
- JSON Schema validates the **entire** node, including what used to be an
  opaque string. Validation level V3 shrinks accordingly.
- The balance simulator can *reason about bounds* — "what is the maximum
  possible damage of this ability?" is answerable by walking the descriptor.
  An arbitrary expression is opaque, which quietly defeated v0.1's balance
  strategy.
- Generators and LLMs emit it natively under schema constraint.
- One tree-walking evaluator, no lexer, no parser, no VM, no bytecode.

**Deferred, not rejected:** if hand-authoring proves painful, a surface syntax
may later be added *to the authoring tool*, compiled to these structures. The
runtime never learns about it. Bytecode compilation is reconsidered only if a
committed benchmark shows evaluation is a real cost.

## 4.5 Effect-tree ordering and visibility

Normative answers to Codex's questions 4 and 5.

1. **Effect trees are ordered lists. Nodes execute in document order.**
2. **Later nodes observe earlier mutations.** A node that runs after a killing
   blow sees a dead target. This matches designer intuition and makes
   "damage, then if it died, restore MP" expressible.
3. **The caster snapshot is frozen at action start.** Caster stats and skill
   values are captured once, before node 1, and do not change during the tree —
   even if a node buffs the caster. This is what prevents self-amplifying
   combos, which are the actual source of unbounded scaling in systems like
   this.
4. **Target snapshots are taken per node, at that node's execution time.** A
   debuff applied by node 1 correctly weakens the target for node 2.
5. **Dead targets are skipped** unless the node declares `affectsDead: true`.
6. **Target list reuse is explicit.** `{"scope": "priorTargets", "of": "n1"}`
   references a named earlier node's resolved list rather than re-evaluating a
   selector.
7. **The node scratchpad** is the one narrow mechanism replacing removed
   expression power. A node may carry `"id"`, and a later node's formula may
   scale from `{ "from": "prior.n1.amount", "coeff": 0.5 }`. Exposed fields are
   a fixed set — `amount`, `hitCount`, `killed`, `applied` — references must
   point to an earlier node in the same tree, and this is checked **statically
   at validation**, not at runtime.

## 4.6 Stat and modifier evaluation ordering

Codex asked whether statuses→stats→formulas→stats is a real cycle. It was in
v0.1. It is now broken by one rule:

> **A modifier's magnitude is resolved once, at application time, and frozen.**

"+20% of the caster's Attack" becomes a fixed integer when the buff lands. It
does not re-evaluate. This eliminates the cycle, and it also eliminates a class
of bug where applying a new buff silently recomputes older ones.

Layer order is unchanged from v0.1:
```
100 base → 200 flat add → 300 percent add (summed, applied once)
        → 400 multiplicative → 500 override → 600 clamp
```
Ties within a layer break on `(layer, sourcePriority, sourceContentId)` — fully
deterministic and independent of application order.

**Escape hatch, explicit and bounded:** a modifier may declare
`"dynamic": true` with a `recomputeOn` trigger (e.g. `onTurnStart`,
`onHpChanged`). Validation statically rejects any dynamic modifier whose formula
reads a stat that the same modifier contributes to. This keeps
"scales with current HP" possible without reopening the cycle.

## 4.7 Passives

**A passive is not a distinct kind. A passive is a permanent, hidden, source-bound
Status.**

This deletes a subsystem rather than adding one. Item-granted, class-granted,
qualification-granted, form-granted, and status-granted passives all become "the
source grants a status," reusing one lifecycle, one stacking policy, one
ordering rule, and one save representation.

```json
{ "id": "core:status.scaled_hide",
  "hidden": true, "duration": { "mode": "permanent" },
  "sourceBound": true,
  "modifiers": [ { "stat": "defense", "layer": 300, "value": 15 } ],
  "triggers":  [ { "on": "on_damaged", "effects": [ ... ] } ] }
```

`sourceBound: true` means the status is removed automatically when its granting
source goes away — unequip the item, lose the qualification, leave the form.
Triggered passives ("on hit, 10% chance to burn") are a status with an `on_hit`
trigger, which the status system already supports.

## 4.8 Tags — mod-extensible, core-fast

Satisfying R6 (tags extensible, core stats fast, efficiency preserved):

- **Assignment:** every tag string across all loaded packs is interned to a
  `TagId` (int32) at pack-load, assigned in sorted order over the resolved pack
  set. Deterministic for a given pack set, which is all R4 requires.
- **Core tags get stable low IDs.** Tags declared in the `core` pack occupy IDs
  `0..255` with hand-stable ordering. Every entity carries a 256-bit
  (`ulong[4]`) mask for these. Hot checks — `element:fire`, `kind:undead` — are
  a single AND.
- **Mod tags live above 255** in a small sorted `TagId[]` with binary search.
  Most entities carry fewer than 20 tags, so this is cheap and, crucially, does
  not require a bitset over an unbounded dynamic space.
- **Namespaces are schema-declared.** Unknown namespaces fail validation; mods
  declare their tag namespaces in their pack manifest.
- **Saves store tag strings, never `TagId`s.** IDs shift when the pack set
  changes; this is the single easiest way to corrupt a modded save and it is
  worth stating explicitly.

## 4.9 Provenance semantics

Codex objected to `author = "claude"` persisting into shipped content. Correct —
v0.1 conflated *who typed it* with *how it came to exist*. Attribution belongs
to git.

```json
"provenance": {
  "origin": "authored" | "generated" | "assisted",
  "generator": "item-grammar",        // present iff origin != "authored"
  "generatorVersion": 3,
  "reviewLevel":  "R0" | "R1" | "R2" | "R3",
  "reviewStatus": "unreviewed" | "machine_validated" | "reviewed" | "approved"
}
```

- `assisted` means a human or agent edited machine output — it is neither pure
  authored canon nor safely regenerable.
- No personal or agent names in shipped content.
- **Regeneration safety rule:** only `origin: generated` rows with
  `reviewStatus` below `reviewed` may be bulk-regenerated. Anything referenced
  by authored content is locked, because regenerating it would silently break
  the reference.
- `reviewStatus` only moves forward. Build configuration declares the minimum
  status shippable per review level.

---

# Part 5 — Revised subsystems

## 5.1 Generated item instances — store outcomes, not process

**Rejecting Codex's mechanism, accepting its problem.** `rollAlgoVersion` plus
retained historical generators means every generator version must be kept,
tested, and migrated forever — an unbounded tax and a second versioning system,
against the philosophy for this revision.

Instead, an item instance stores the **resolved outcome**:

```jsonc
ItemInstance {
  instanceId, defId,
  affixes: [ { affixId: "core:affix.frost_2", roll: 0.62 } ],   // percentile rolls
  deltas:  { durability: -14, socketed: [...] },
  origin:  { seed: 88121, generator: "item-grammar@3" }         // DIAGNOSTIC ONLY
}
```

- Stats are computed as `f(affixId, roll, current affix definition, current cost
  table)`. **Rebalancing still propagates into existing saves** — v0.1's
  headline save property is preserved.
- Changing the generation *algorithm* cannot alter existing items, because the
  outcome is stored rather than re-derived.
- `origin` is explicitly non-authoritative: never read by the loader, present
  only for bug reports. Marked as such in schema so nobody is tempted.
- Cost: roughly 60–100 bytes per generated item instead of 8. At a few thousand
  stored items that is well under a megabyte — irrelevant under R7.

**This removes a versioning mechanism instead of adding one.**

## 5.2 Determinism, right-sized

Under R4 the requirement is: *same build + same content version + same seed ⇒
same result, on the same platform.*

**Deleted:** fixed-point arithmetic, cross-platform byte-identity, and the
associated authoring tax on every formula in the game.

**Kept, because tests genuinely need it:**
1. **Integers for all game-meaningful quantities** — HP, damage, stats,
   durations. This is what the genre does anyway, and it removes most float
   questions at the source.
2. `double` permitted for bounded intermediates that are rounded to integer
   before they touch state.
3. **Transcendentals banned in simulation paths** (`Sin`, `Cos`, `Pow` with
   non-integer exponents). They are the actual reproducibility hazard and the
   game does not need them. A curve that wants one uses a content-authored
   lookup table.
4. **Named seed derivation** via the stable hash: `hash(parentSeed, streamName)`.
   Adding a new consumer never shifts existing streams — the property that keeps
   generator changes from reshuffling the world.
5. **Ordered iteration in simulation paths.** Enforced by review and by the
   replay tests, which fail loudly when violated.
6. Replay tests pin the content and code version explicitly.

**No multiplayer cost is paid** (R3), and nothing in the domain says multiplayer
is impossible — `Command` and `EncounterSetup` remain plain serializable data
because that is good design, not because of netcode.

## 5.3 Party, actors, and summon capacity

Per R8, **`Game.Encounter` contains no party-slot concept whatsoever.**

- `BattleState` holds a dense, append-capable `Actor[]` with stable
  `ActorId(index, generation)`. Each actor carries a `Side` and a
  `ControlSource` (`Player`, `Ai(profile)`, `Scripted`, `Semi(rules)`).
- **Actors may join mid-battle.** Summoning during combat appends an actor and
  the scheduler inserts it into the timeline. This is a first-class case, not a
  patch — the thing that breaks if fixed-size arrays are assumed.
- Dead actors keep their slot until battle end so `ActorId`s stay valid and the
  event log stays coherent.
- A configured `maxActors` per encounter is a **budget, not an architectural
  limit** (proposed 32 for normal encounters; a future army mode raises it and
  swaps the scheduler).

Party composition and summon capacity are **Rules/Application concepts**
expressed as constraints when building an `EncounterSetup`:

```
SummonCapacity component  →  { capacity: int, used: int }
SummonContract            →  { capacityCost: int, duration model, terms }
```

This permits, with no architectural change: one powerful summon (cost 4 of 4),
several weak ones (cost 1 each), temporary summons (duration-limited, cost
released on expiry), persistent companions (cost 0, occupying a roster slot
instead), and summoner builds that raise capacity. The initial 4-adventurer
party is a *content and Application-layer configuration*, and nothing below L5
knows the number 4.

## 5.4 Save strategy, simplified

- **Segment-level migration only.** Component-level versioning is deleted;
  components serialize within their owning segment. One migration granularity,
  one registry, one chain per segment.
- **No fixed size target** (R7). Instead: a measured budget tracked from Phase 4
  with a CI report on a synthetic 200-hour save, and a stated structural policy —
  sparse zone deltas, aggregate sim state, no per-NPC memory without an explicit
  cap.
- **Bounded active inventory, unbounded storage.** The carried inventory is
  capped for gameplay and UI reasons; long-term storage is a separate segment
  with its own (large) limits and is not loaded into the active read model.
- Everything else from v0.1 C.20 stands: references not resolved values,
  content manifest in the save, deprecation with tombstones, deterministic
  serialization, historical save corpus in CI.
- **Corrupt saves fail loudly.** A save that fails to load is never partially
  applied and never overwritten. This is the one place where "continue anyway"
  is forbidden.

## 5.5 The world event queue

```
drain():
  while queue not empty and budget remains:
      event = queue.dequeue()
      for each subscriber in declared priority order:
          subscriber.handle(event)     // may enqueue, may not recurse
  if depth > maxCascade or budget exhausted:
      emit diagnostic, drop remainder, continue
```

- **Queue, never recursion.** Handlers enqueue; they never synchronously drain.
- **Cascade depth cap** and a **per-drain event budget**, both configured.
- Exceeding either is a diagnostic with the originating event chain attached —
  loud in dev, survivable in release.
- Subscribers run in declared priority order, so ordering is content-visible
  rather than registration-dependent.

## 5.6 One content-loading path

Packs only. The v0.1 dev/ship split meant the shipping path was the less-tested
one. Incremental pack compilation with a file watcher gives hot reload without a
second code path; the target is sub-second incremental builds, measured.

---

# Part 6 — Previously missing architecture

## 6.1 UI architecture

The largest omission in v0.1. At 10,000 items, inventory UI is an architectural
problem.

**Read models.** The core never exposes its object graph to the UI. `Game.
Application` projects **flat, immutable read models** — `PartyView`,
`InventoryPage`, `AbilityListView`, `EquipCompareView`. The UI renders read
models and sends `Command`s. Consequences: UI can never become a hidden mutation
path, and UI is testable against fixture read models with no game running.

**Inventory at scale.** The UI never receives the whole inventory. It issues a
query and receives a page:

```
InventoryQuery { filter: Predicate, sort: SortSpec, page: {offset, count} }
              → InventoryPage { rows: ItemRow[], totalCount }
```

Filtering and sorting happen in the core against an **inverted index over tags
and name keys**, built at pack load and updated incrementally on inventory
change. Query cost is independent of catalog size. This is the mechanism that
makes the content-scale target survivable in UI, and it does not exist unless it
is designed in.

**Virtualization.** Only visible rows are realized as Godot nodes, with a
recycled node pool. Non-negotiable at these counts.

**Equipment comparison** needs "what would my stats be if I equipped this?" —
which falls out free from snapshot-based stat resolution (4.6):
`Rules.EvaluateHypothetical(actorSnapshot, change) → StatSnapshot`. No
speculative mutation of real state, ever.

**UI state** (open panel, active filter, scroll position) is shell-local and
never enters the save except a small optional `ui_prefs` segment.

## 6.2 Runtime error and failure philosophy

Generalizes 4.3 to the whole project.

| Class | Detection | Debug/test build | Release build |
|---|---|---|---|
| Content error | Build-time validation | Build fails | Invalid pack rejected at load with a specific message; game runs without it |
| Semantic condition | Normal control flow | Diagnostic event | Diagnostic event; continue |
| Impossible state | Assertion | **Throw** | Structured diagnostic, skip the operation, continue |
| Save corruption | Load-time verification | Throw | **Fail loudly, refuse to load, never overwrite** |

The asymmetry is deliberate: a rules bug must not end a long session, but a save
bug must never be papered over.

## 6.3 Debug tooling and developer console

- **The console dispatches the same `Command`s as the UI.** It gets no
  privileged mutation path — otherwise it becomes an untested backdoor that
  diverges from real gameplay.
- Capabilities: spawn encounter, grant item, set flag, warp, dump actor
  snapshot, force seed, toggle sim tiers, print the read model.
- **Replay viewer:** load an `EventLog`, step forward and backward, inspect
  state at each step. This is the primary combat debugging tool and it exists
  only because the event log is the presentation contract.
- Compiled into debug and internal builds; excluded from release by build flag.
- Diagnostics are structured records with codes, reusing validation rule IDs
  where the same condition exists at both times.

## 6.4 Localization and CJK

- Keys everywhere (v0.1 unchanged). **ICU MessageFormat** for plurals, gender,
  and interpolation — a standard with existing C# libraries, not a custom
  language. Validation checks that every translation's placeholders match the
  source string's.
- **CJK is a Phase 3 decision, not a later one**, because it collides with the
  pixel-art aesthetic: a 6px bitmap font cannot render CJK. Recommended
  strategy — bitmap font for UI chrome and Latin, dynamic font (Noto Sans CJK
  subset) with a runtime atlas for content text. This must be decided alongside
  the art plan, and it is a genuine open question (Part 9).
- Layout: reserve ~2× width expansion (German), and CJK line-breaking differs
  from Latin — Godot handles this acceptably but it must be tested with real
  strings in Phase 3, not assumed.
- Generated content declares `localeCoverage`; untranslated generated content
  falls back rather than shipping mixed-language text.

## 6.5 Packaging and build

- The .NET SDK plus Godot export templates are the toolchain; content packs are
  compiled as a pre-export step and their hash is embedded in the build.
- Version string is `<semver>+<contentHash>` so any bug report identifies the
  exact content set — which is what makes R4's reproducibility usable in
  practice.
- **Steam Deck:** verify controller navigation and 1280×800 text readability
  from Phase 3. Deck readability is a real constraint on the inventory UI and is
  cheaper to design for than to retrofit.
- Linux via native export first, Proton as fallback.
- Mods load from a directory outside the packed game data.
- CI produces validated content packs, headless test results, a benchmark
  report, and export artifacts.

---

# Part 7 — Revised implementation phases

### Phase 0 — Decisions and minimal scaffolding
Merge v0.1 + v0.2 into `docs/`, write ADR-0001 (Rules Core + Shell), ADR-0002
(Godot + C#), ADR-0003 (JSON content format), ADR-0004 (no runtime expression
language). Repo skeleton, `deps.allow`, CI, `AGENTS.md`.
**Exit:** a cold agent can read `docs/README.md` and correctly answer "where
does a new status effect go?"

### Phase 1A — Walking skeleton *(NEW — timebox 1–2 weeks)*
The smallest headless model that resolves a real battle end to end:
**3 abilities, 2 monsters, 1 status effect, all as C# literals.** No JSON, no
schema, no packs, no validator, no registries.

Purpose: **discover** what `Op`, `OpContext`, `EffectNode`, and `BattleState`
actually need to be, by executing them, before any serialization format is
frozen around them.

> **This code is a probe and is expected to be substantially discarded in 1B.**
> Stating this explicitly matters: without it, a good engineer will build 1A to
> production standard and then defend it. Deliberate throwaway is the point.

**Exit:** a battle resolves; the same seed twice produces an identical event
log; and a short written report states what the real shape of `Op`,
`OpContext`, and the effect node turned out to be. That report is the input to
1B's schema.

### Phase 1B — Extraction to data
Now build it: `Game.Foundation` (IDs, stable hash, RNG, tags),
`Game.Content` (JSON schemas, packs, registries, structured-node evaluator),
validator, compiler. ~20 ops.

**Exit criterion, and it is a good one:** the Phase 1A battle runs again,
driven entirely from JSON content through compiled packs, and produces an
**event log byte-identical to 1A's**. The throwaway phase leaves behind a golden
file. Validation V1–V3 green.

### Phase 2 — Rules core and combat depth
Stats and the modifier pipeline, statuses (including passives), items,
abilities, targeting, the full resolution pipeline, the CTB scheduler,
mid-battle actor insertion, utility AI. ~60 ops. Snapshot/delta boundary
implemented and tested.
**Exit:** scripted battles run headlessly from content, replay identically, and
10,000 simulated battles complete within a measured and *stated* budget — the
number comes from Phase 1B benchmarks, not from a guess. (v0.1's ≤1 ms and
"10,000 in a minute" were inconsistent by 6×; both are withdrawn.)

### Phase 3 — Playable vertical slice
Godot shell, one town, one dungeon, 4 adventurers, ~20 abilities, ~30 items,
~15 monsters, save/load, menus, inventory UI with virtualization, font and CJK
strategy decided, Steam Deck readability checked.
**Exit:** measurable proxies replacing v0.1's unfalsifiable one — a new player
completes the dungeon without guidance, no softlocks, unprompted session length
≥25 minutes, and no P1 defects in the combat log.

### Phase 4 — Depth systems, first pass
Progression tracks and qualifications, summoning v1 (contracts, capacity budget,
one acquisition method, persistent creatures), crafting v1, quests v1, item
generation grammars, data mods.
**Exit:** 1,000 items and 200 abilities validate and load within budget; a
character qualifies for a specialization through play; a summon persists,
levels, and fights; save size measured and reported.

### Phase 5 — Scale and world
World LOD simulation, factions, economy, procedural dungeons, travel graph,
balance simulator with tripwires, the Hint/Rumor discovery layer.
**Exit:** 5,000 items / 1,000 abilities / 500 monsters validate; world sim
within frame budget at 100 settlements.

### Phase 6 — Optional AI content layer
`IGenerationClient`, generation job format, canon checks, review-gate tooling,
first R0/R1 batches.
**Exit:** a generated batch ships baked; the game builds and plays with
generation entirely disabled.

### Phase 7+ — Depth passes
One flagship system at a time, each with a spec, an ADR, and a balance pass.

---

# Part 8 — Testing, validation, and collaboration

## 8.1 Testing changes from v0.1
- **Coverage-percentage gates are deleted.** Replaced with: every op has tests;
  every normative example in a spec is a test; every delta type has an
  application test. Coverage is reported, never gated.
- Added: **snapshot/delta boundary tests** — assert that no persistent state
  changes except through a declared delta, verified by a test harness that
  freezes instances during battle.
- Added: **static effect-tree validation tests** — scratchpad references resolve
  backward, dynamic modifiers do not self-reference, no possible reaction cycle.
- Retained: replay/determinism (now version-pinned per R4), save-compat corpus,
  golden logs, property tests, headless integration.

## 8.2 Performance rules
Stylistic prohibitions are replaced with **committed benchmarks carrying
allocation assertions** — `BenchmarkDotNet` with a memory diagnoser and a
threshold, run in CI, failing on regression. If a LINQ query in the resolution
pipeline allocates nothing measurable, it is fine. The rule is the number.

## 8.3 Validation
V1–V5 retained. V3 shrinks substantially because structured nodes are now
schema-validated rather than requiring a bespoke expression checker.

## 8.4 Collaboration protocol changes (per R9)
- v0.1's K.5 made the architect win by default. Replaced: **technical claims are
  settled by evidence** — a benchmark, a failing test, a reproducible example,
  or cited runtime documentation. Architectural and game-design claims are
  settled by written argument. Neither role wins automatically. Genuinely
  unresolved semantic questions escalate to the owner rather than being decided
  by whoever holds the file.
- Codex may make **editorial and clarifying** spec amendments directly, marked
  as such. Only semantic changes route through handoff. v0.1's bottleneck is
  removed.
- **ADR per op *category*, not per op.** Per-op ADRs were friction that would
  have been routed around within weeks. Op count remains the primary complexity
  metric; the ceiling remains.
- A finding that a spec forced a special case is a **specification bug**, filed
  against Claude — unchanged from v0.1 and reaffirmed.

---

# Part 9 — Decision status

## 9.1 Resolved
R1–R9 (Part 1), plus: content format = JSON; zero runtime expression languages;
snapshot-in/delta-out; closed delta set; passives are statuses; frozen modifier
magnitudes; effect ordering and visibility; tag representation; provenance
semantics; segment-only migration; World split; error philosophy; one loading
path; item roll outcomes stored not re-derived.

## 9.2 Still open — owner decision needed
| ID | Question | Needed by |
|---|---|---|
| **O3** | Combat pacing target (fight length, visible vs random encounters) | Phase 2 |
| **O4** | Build scarcity — can a character eventually master everything? Respec? | Phase 4 |
| **O6** | Art plan — who makes sprites and tilesets? | Phase 3 (long lead) |
| **O8** | Realistic weekly review hours | Phase 0 sizing |
| **O10** | Save/death model — slots, permadeath, autosave frequency | Phase 3 |
| **N1** | **Localization scope for v1** — English only, or CJK from the start? Collides with pixel-font choice | Phase 3, with O6 |
| **N2** | v1 party size and summon capacity model — 4 + separate budget is assumed provisional | Phase 4 |

## 9.3 Deliberately deferred
O5 narrative spine (Phase 5) · O7 release intent (Phase 6) · O9 AI content
appetite (Phase 6) · script mods (post-1.0) · army-scale combat (post Phase 7) ·
console ports · multiplayer (R3) · surface syntax for content authoring (only if
measured to be needed) · bytecode evaluation (only if profiling demands it).

---

# Part 10 — Recorded disagreements

For the record, per R9, so neither agent's position is lost:

1. **Item roll versioning (Codex finding 3).** Codex proposed
   `rollAlgoVersion` with retained historical generators; I propose storing
   resolved outcomes instead. Both solve the correctness problem. My argument is
   maintenance cost and mechanism count; Codex's approach has a smaller save
   footprint (8 bytes vs ~80 per item), which R7 makes irrelevant. **If Codex
   can show a case where the stored outcome cannot reproduce a needed property,
   I withdraw.**
2. **Expression language scope (finding 4).** Codex proposed a reduced DSL; I
   propose none at all. The risk of my position is authoring ergonomics for
   hand-written content. I consider this acceptable because generators author
   most content and surface syntax can be added tool-side later. **If Phase 1B
   shows hand-authoring is materially slowed, the tool-side syntax moves from
   deferred to scheduled.**
3. **Stable-hash scope (finding 1).** I narrowed Codex's ban. If any case is
   found where a default-hashed collection reaches simulation output despite the
   iteration-order rule, the broader ban is correct and I withdraw the narrowing.

---

## Status: awaiting project owner review

No production code, no `docs/` tree, no schema, and no content has been created.
No git operations have been performed. On approval, Phase 0 merges v0.1 and v0.2
into `docs/`, writes ADR-0001 through ADR-0004, and hands Codex the Phase 1A
brief — with its throwaway status stated in the brief itself.
