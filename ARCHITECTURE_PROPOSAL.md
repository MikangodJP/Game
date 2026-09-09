# Architecture Proposal — Untitled 2D Fantasy RPG

**Status:** DRAFT — awaiting project owner approval
**Author:** Claude (Lead Architect / Game Systems Designer)
**Date:** 2026-09-09
**Supersedes:** nothing (first proposal)

> This document is a *proposal*, not the final documentation tree. Nothing in
> `docs/`, `src/`, `schema/`, or `content/` has been created yet. On approval,
> this file gets decomposed into the documentation hierarchy described in
> section K and then deleted. Do not treat it as the permanent design doc — a
> single giant design document is one of the failure modes we are trying to
> avoid.

---

## 0. Executive summary

**Recommended architecture:** *Rules Core + Shell* (hexagonal / ports-and-adapters),
with a **pure, engine-free, deterministic simulation core** consumed by a thin
Godot presentation shell, and **all game content as external validated data**
compiled into binary packs.

**Recommended engine/language:** **Godot 4 (.NET / C#)** for the shell;
**plain .NET class libraries (C#)** for the core. The core never references
Godot. The shell never contains a game rule.

**The single most important structural idea in this document:**

> **Ops are code. Content is data.**
> A small, hand-authored, reviewed, tested set of *operations* (roughly
> 150–400 of them, ever) defines everything the world can mechanically do.
> Content — items, monsters, spells, quests, summons — is data that *composes
> those ops*. Content can grow to a million rows without adding a line of
> rules code. Any request to "add a system" is really a request to add ops,
> and adding an op requires a written decision record.

This is what makes 10,000 items survivable, and it is what keeps AI-generated
content from ever becoming the authority on mechanics: generated content can
only recombine ops that a human already approved.

**The second most important idea:**

> **Battle and world state advance as pure functions:**
> `(State, Command, RngStream) -> (State', EventLog)`.
> Presentation is a *consumer of the event log*, never a participant in
> resolution. This one constraint buys deterministic replays, headless batch
> balance simulation, testable rules, AI opponents that can search, save
> integrity, and future networking — all at once. Violating it is the fastest
> way to kill this project.

---

# A. Architectural risks

Ranked by how likely they are to kill the project, not by how alarming they sound.

### A1. Save-compatibility collapse (CRITICAL — kills projects at year 2–4)
A 200-hour RPG whose content is constantly rebalanced will destroy its own
saves unless the save format is designed before the first save is written.
Two failure modes: saves that store *resolved values* (rebalancing does
nothing, or breaks builds) and saves that store *raw content IDs* with no
indirection (renaming or deleting content corrupts saves). Both are fatal once
players exist.
**Mitigation:** stable never-reused namespaced IDs, deprecation instead of
deletion, versioned save segments, an explicit migration chain, and a content
manifest hash stored in every save.

### A2. Balance-space explosion (CRITICAL)
"5,000 abilities" and "thousands of status-effect combinations" imply an
interaction matrix in the millions. No human and no AI can hand-verify it.
**This is the risk most likely to make the game bad rather than broken.**
**Mitigation:** a small closed set of *effect primitives* with formally defined
stacking and composition semantics; everything else is composition. Plus
automated batch balance simulation as a first-class tool, not an afterthought.

### A3. Content that is technically vast and experientially empty
Weapon Type × Material × Affinity × Trait × Enchantment × Origin × History
produces enormous numbers trivially. It also produces "Iron Longsword of Minor
Frost (Mediocre)" × 4,900. Diablo survives this via tight power bands and
drop-rate psychology; a JRPG cannot lean on that.
**Mitigation:** generation *grammars with legitimacy constraints* (not a free
cross-product), a hard cap on simultaneously-obtainable variants per tier, and
mandatory *authored anchors* — every generated family descends from a
handcrafted archetype that carries the identity.

### A4. Determinism erosion
Procedural generation + world simulation + floating point + hash-map iteration
order = bugs that cannot be reproduced, and generated worlds that differ
between machines or runs. Silently fatal for debugging at scale.
**Mitigation:** fixed-point or strictly disciplined deterministic math in the
core, ordered collections in all simulation paths, named/derived RNG streams,
and replay tests in CI.

### A5. God-object gravity and two-agent merge contention
`Character`, `Battle`, and `World` will accrete unless structurally prevented.
With two agents committing, hot files also become merge battlegrounds.
**Mitigation:** module ownership map, CI-enforced dependency rules, file-size
and type-size budgets, and a rule that both agents never hold the same file.

### A6. Cross-system coupling and circular dependencies
Quests reading combat internals; crafting reading dungeon generation; summons
reading UI. The classic slow death.
**Mitigation:** strict layer numbering with CI enforcement (section G), an
event bus used for *notification only* (never for control flow inside a
resolution step), and read-only query interfaces between peer domains.

### A7. AI-generated content contaminating the canon irreversibly
If generated content is indistinguishable from authored content in the repo,
quality collapse cannot be rolled back and you can never answer "who wrote this
rule?"
**Mitigation:** `provenance` is a mandatory field on every content row;
generated content lives in a separate tree; it can be filtered, quarantined,
regenerated, or bulk-deleted by provenance at any time.

### A8. Simulation cost of an ambitious living world
Hundreds of towns × factions × economies × monster ecosystems, ticking, is a
frame-budget and save-size disaster if simulated uniformly.
**Mitigation:** level-of-detail simulation (three fidelity tiers), event-driven
rather than per-tick updates where possible, and a strict per-frame simulation
budget.

### A9. Tooling debt
10,000 items cannot be maintained by hand-editing text, but building a GUI
editor early is a classic trap that eats a year.
**Mitigation:** text-first content plus a *validator + generator + query* CLI
set early; a GUI editor only if and when text authoring measurably fails.

### A10. Text and localization scale
Thousands of quests and item names with text embedded in mechanical content
files makes review, consistency, and localization impossible later.
**Mitigation:** all player-visible text is keyed from day one; content files
carry text *keys*, never strings.

### A11. Playtest throughput
You cannot manually playtest 5,000 abilities or 2,000 monsters. Without a
headless simulation harness, balance becomes vibes.
**Mitigation:** a headless core is a hard requirement, not a preference. This
is half the justification for the entire architecture.

### A12. Scope-shaped despair (the real number one in practice)
A project of this ambition dies from lack of a playable thing far more often
than from bad architecture. The architecture must produce a *playable vertical
slice* early and must never require the full system set to run.
**Mitigation:** the phase plan in section N, with a playable milestone at Phase
3, and a standing rule that every system ships a trivial-but-real version
before any system ships a deep version.

### A13. Documentation nobody reads because it is too large
Directly implied by the brief. Documentation must be navigable in under 60
seconds by an agent with no prior context.
**Mitigation:** the docs hierarchy in section K, hard size caps, and
machine-checkable links between spec IDs and code.

### A14. Moddability implemented as arbitrary script execution
"Mods can run scripts" adopted early means an unbounded API surface that
freezes your internals forever, plus a security problem.
**Mitigation:** data mods first (safe, and most of the value); sandboxed
scripting much later behind an explicitly versioned modding API.

---

# B. Candidate architectures

Three genuinely viable shapes. All three can make *a* game; they differ
enormously at the scale described.

## B1. Engine-Native / Scene-Driven
The engine *is* the architecture. Content lives in engine resources, systems
live in engine nodes and scripts, and the editor is the content tool.

**Pros:** fastest to first playable; no plumbing; an editor for free; minimal
abstraction; excellent for presentation-heavy work.

**Cons at target scale:**
- Content in engine resource files merges badly in git and cannot be diffed or
  bulk-validated well. With two AI agents committing, this hurts constantly.
- Rules become entangled with the engine lifecycle (`_ready`, `_process`, node
  trees). Testing a damage formula requires booting a game loop.
- No headless batch balance simulation without heroic effort — kills A11.
- Engine idioms leak into the domain permanently; the domain becomes
  unportable and hard to reason about.
- 10,000 items as 10,000 resource files is simultaneously an editor
  performance problem and a repository problem.

**Verdict:** correct for a 40-hour JRPG. Wrong for this brief.

## B2. Rules Core + Shell (hexagonal) — *recommended*
An engine-free, deterministic simulation core as a plain library. Content as
external text compiled to binary packs. The engine is one adapter among several
(others: the test harness, CLI tools, the batch simulator).

**Pros:**
- Rules are testable, benchmarkable, and *replayable* without an engine.
- Headless balance simulation is trivial, which is the only realistic answer to
  A2 and A11.
- Content is plain text: diffable, greppable, bulk-validated, agent-friendly,
  mod-friendly.
- The two agents can work in genuinely separate areas (core vs shell vs tools)
  with low merge contention.
- Engine replaceability becomes a real, affordable option later.
- Determinism is enforceable, because the core has no ambient engine state.

**Cons:**
- Real up-front cost before anything is on screen (mitigated by the phase plan).
- Requires discipline: every "just call the renderer from inside the rule"
  shortcut must be rejected, forever.
- Two representations of some concepts (core actor vs visual actor) plus a sync
  layer — a genuine ongoing tax, roughly 5–10% extra work on presentation
  features.
- Slightly slower iteration on presentation-coupled features.

## B3. Full ECS Everywhere
A data-oriented ECS (Bevy-style, or custom) as the universal model for content
*and* runtime; systems are scheduled queries.

**Pros:** excellent memory locality and performance headroom; composition is
native; adding a component is genuinely cheap; a great fit for large actor
counts (army combat, ecosystems).

**Cons at target scale:**
- ECS is a poor *authoring* model for deeply structured content. An item with
  nested conditional enchantment effects is a tree, not a row.
- Turn-based rule resolution wants explicit, auditable ordering. ECS schedulers
  push toward implicit ordering, which is exactly where balance bugs hide and
  where determinism dies.
- Game rules scatter across dozens of systems; "what happens when I cast Fire
  on a wet undead" becomes archaeology. Specification and review — Claude's
  core job — get much harder.
- It is also the pattern where AI agents most reliably produce subtle, silent
  bugs (wrong query filters, missed archetype transitions).

**Verdict:** ECS is a good *implementation technique inside bounded contexts*
and a bad *global architecture* for a rules-heavy, content-heavy RPG.

## Recommendation

**B2 (Rules Core + Shell)**, with **B3 techniques used tactically inside the
core** where actor counts justify them — struct-of-arrays storage for
battlefield actors and world entities — but **never** as a cross-cutting
scheduling religion. Rule resolution stays as explicit, ordered, readable
pipelines.

---

# B′. Engine and language choice

Evaluated against 2D quality, data-driven fit, iteration speed, tooling,
performance, moddability, ten-year maintainability, and AI-agent suitability.

| Option | 2D | Data-driven | Tooling | Perf | Mod | AI-agent fit | Verdict |
|---|---|---|---|---|---|---|---|
| **Godot 4 + C# (core in plain .NET)** | Excellent | Excellent | Very good | Good | Good | **Excellent** — one statically typed language, real refactoring, real test runners | **Recommended** |
| Godot 4 + GDScript only | Excellent | Good | Very good | Fair | Good | Poor at scale — dynamic typing makes large agent-driven refactors unsafe; weak test/bench ecosystem | Rejected for the core |
| Rust core + Godot shell (GDExtension) | Excellent | Excellent | Good | **Excellent** | Good | Good, but slow iteration; the FFI boundary is permanent friction | **Strong runner-up** |
| Rust + Bevy (all-in) | Immature 2D authoring; no editor | Excellent | Weak (you build the editor) | Excellent | Fair | Fair — you will spend years building an engine, not a game | Rejected |
| Unity | Very good | Good | Excellent | Good | Good | Good | Rejected — licensing/policy volatility is an unacceptable counterparty risk for a decade-long project; heavyweight for 2D |
| MonoGame / C# | Good (you build everything) | Excellent | You build it | Good | Good | Good | Rejected — no editor, no scene tooling; too much undifferentiated work |
| TypeScript / web | Good | Excellent | Excellent | Poor at scale | Excellent | Excellent | Rejected — long-term performance ceiling and platform path |
| LÖVE / Lua | Good | Good | Weak | Fair | Excellent | Fair | Rejected — weak typing and tooling at this scale |

### Recommendation: Godot 4 with .NET / C#

**Why:**
1. **One language across core, shell, tools, and tests.** For a two-AI-agent
   team this is worth more than any single technical feature. Codex writes
   tests, tools, and gameplay code in the same language and type system Claude
   specifies against.
2. **The core compiles and tests as an ordinary .NET library** — `dotnet test`,
   xUnit, property-based testing, BenchmarkDotNet — with no engine in the loop.
   This directly enables A11's headless balance simulation.
3. **Static typing with excellent refactoring tools.** At 300k+ lines, with AI
   agents performing mechanical refactors, this is not optional.
4. **Godot 4's 2D is genuinely first-class** (tilemaps, 2D lighting,
   pixel-perfect rendering, Control-node UI); it is small, free, open source,
   and carries no licensing counterparty risk over a decade.
5. **A clean moddability path:** data packs loaded from a mods directory, plus
   optional PCK mounting for assets.

**Honest downsides you are accepting:**
- Godot's C# support does **not** currently target web export, and
  mobile/console paths need extra work. If web or console-first is a real goal,
  this changes the calculus — see question O1.
- GDScript iterates faster for small UI glue. Mitigation: GDScript is *allowed*
  in the shell for scene glue and *forbidden* anywhere a rule lives.
- Godot's .NET tooling is less mature than Unity's. Acceptable.

**When to pick the runner-up instead:** if you want maximum determinism and
performance headroom and accept slower iteration, **Rust core + Godot shell**
is the more rigorous choice. The architecture below is deliberately
language-agnostic at the conceptual level, so switching the core's language
later means *rewriting the core, not redesigning the project*. That is an
expensive but survivable move — and it is only survivable because the core is
engine-free.

**Environment note:** this machine currently has the .NET 8 *runtime* but no
SDK, and no Godot install. Both are trivial setup steps, not blockers.

---

# C. The recommended architecture

## C.1 Layer model (core runtime architecture)

Nine layers. **Dependencies point strictly downward, with no exceptions.** The
layer number is part of the module's identity and is enforced in CI.

```
L8  Tools          content compiler, validators, generators, balance sim, importers
L7  Shell          Godot: rendering, input, audio, UI, scenes, save file IO
L6  Ports          interfaces the shell implements (IRenderSink, IClock, IStorage, IAudioCue)
L5  Application    game session, command dispatch, save/load orchestration, mode stack
L4  World          map, spatial, travel, factions, economy, world sim, quests, dialogue, events
L3  Encounter      combat, battlefield, turn scheduling, actor AI, loot resolution
L2  Rules          stats, effects, ops, items, abilities, magic, summons, progression, crafting
L1  Content        schemas, pack loading, registries, ID resolution, expression evaluation
L0  Foundation     IDs, fixed-point math, deterministic RNG, ordered collections, tags, errors
```

Two notes that matter more than they look:

- **L3 (Encounter) does not depend on L4 (World).** The world *hands the
  encounter a setup* and *receives a result*. Combat therefore never knows what
  a town is. This single rule is what lets you add army combat, arena modes,
  dream sequences, and monster-vs-monster ecology simulation later without
  touching combat.
- **L7 (Shell) depends on L6 (Ports) and L5 (Application) only.** The shell may
  read L0–L4 *types* for display, but may never call a mutating method on them.
  Enforced by a CI check on assembly references plus a mutation-surface audit.

## C.2 Entity model

Three distinct kinds of "thing." Conflating them is the classic RPG codebase
disaster, and it is the mistake most likely to be made by an agent moving fast.

| Kind | Lifetime | Mutable | Identified by | Example |
|---|---|---|---|---|
| **Definition** | Loaded from content packs, immutable, shared | No | `ContentId` (stable, namespaced) | `core:weapon.longsword`, `core:monster.frost_wolf` |
| **Instance** | Created at runtime, persisted in saves | Yes | `InstanceId` (stable within a save) | this specific sword with these rolled affixes |
| **Actor** | Exists inside a running scene or battle | Yes | `ActorId` (index + generation, session-scoped) | the frost wolf currently on the left flank |

Rules:
- Definitions are **never** mutated at runtime. Not for buffs, not for "just
  this once." A mutated definition is a save-corruption bug waiting years to
  fire.
- Instances reference definitions by `ContentId`, never by pointer, and never
  copy resolved stats into the save (see A1).
- Actors reference instances; actors are discarded when a scene ends. Anything
  that must survive lives on the instance.
- `ActorId` is a `(uint index, uint generation)` pair so that stale references
  are detectable rather than silently wrong.

### The universal glue: tags
Every definition, instance, and actor carries a `TagSet` — interned, ordered,
bitset-backed for hot tags. Rules query **tags, not types**:

```
element:fire   kind:undead   material:metal   size:large   trait:aquatic
origin:ancient faction:iron_covenant   damage:slashing   role:brute
```

This is the mechanism that makes 2,000 monsters tractable. "Fire deals +50% to
`kind:plant`" is one rule, authored once, applying to every plant that will ever
exist, including ones generated in 2032. Any rule expressed as
`if (monster.Id == "frost_wolf")` is an architecture violation and should be
rejected in review.

## C.3 Component / data model

Instances and actors are **composition of typed components**, not inheritance
hierarchies. There is no `Character : Entity` class tree, ever.

- A component is a plain data struct with no behavior. `Health`, `StatBlock`,
  `Equipment`, `SkillTracks`, `SummonBond`, `Inventory`, `AiProfile`,
  `FactionStanding`.
- Systems are stateless functions over `(Context, component views)`.
- Storage inside battle and world containers is struct-of-arrays where actor
  counts justify it (battlefield, world entities); ordinary dictionaries
  elsewhere. **Always ordered/deterministic-iteration collections in
  simulation paths.**
- Components are registered with a `ComponentId` and a schema version so the
  save system can migrate them independently (see C.20).

A player character, an NPC, a monster, and a summon are **the same thing** — an
actor with different components. There is no `PlayerCharacter` class. This is
what makes "the player can become a monster," "summons join the party," "an NPC
permanently joins as a party member," and "you play as your summon" cheap
instead of catastrophic.

## C.4 Stats and the modifier pipeline

- `StatBlock` is a fixed enum-indexed array of fixed-point values. Stats are
  declared **in content** (`schema/stats.json`) but compiled to a dense enum at
  build time — content-authored, code-fast.
- Modifiers never apply in insertion order. Every modifier declares a **layer
  number** in its schema:

```
100 base
200 flat additive        (+12 Attack)
300 percent additive     (+15% Attack, all summed then applied once)
400 multiplicative       (×1.5, applied in ascending source-priority order)
500 override / set
600 clamp                (min/max)
```

Within a layer, order is by `(layerNumber, sourcePriority, sourceContentId)` —
fully deterministic and independent of the order buffs happened to be applied.
This is a small decision that prevents an entire genre of unreproducible
balance bugs.

- Derived stats are declared as expressions in content, computed by a
  dependency-sorted pass, and cached with dirty-flagging.

## C.5 Effects and Ops — the spine of the whole design

**An Op is code. There will be a few hundred, forever.** An op is a pure,
registered, unit-tested function:

```
OpResult Execute(OpContext ctx, OpArgs args)
```

Examples of ops: `damage`, `heal`, `apply_status`, `remove_status`,
`modify_stat`, `move_actor`, `summon_actor`, `consume_resource`,
`transform_actor`, `grant_item`, `set_flag`, `spawn_encounter`,
`modify_reputation`, `teleport`, `resurrect`, `steal`, `copy_ability`.

**An Effect is data.** An effect node composes ops:

```toml
[[effect]]
op        = "damage"
target    = "selector:enemies.in_arc(120, 2)"
element   = "fire"
magnitude = "caster.attack * 1.4 + caster.skill(fire_magic) * 0.6"
condition = "target.has_tag('kind:plant') or weather == 'dry'"
timing    = "on_hit"
tags      = ["ability_damage", "aoe"]

[[effect]]
op       = "apply_status"
status   = "core:status.burning"
target   = "selector:same_targets"
duration = "3"
stacking = { mode = "refresh", max = 3 }
chance   = "0.35 + caster.skill(fire_magic) * 0.004"
```

Everything mechanical in the game compiles to trees of these: abilities, item
enchantments, monster passives, status effect ticks, terrain hazards, quest
rewards, faction consequences, crafting outcomes, divine blessings.

**Consequences of this choice, which are the point:**
- Adding 5,000 abilities adds zero rules code.
- Any generator (procedural or AI) can only produce effect trees over existing
  ops — it *cannot invent mechanics*. This is the hard boundary requested in the
  brief, made structural rather than aspirational.
- A new op requires an ADR, a spec entry, unit tests, and a balance note. That
  friction is deliberate. Op count is the project's primary complexity metric.
- The op registry is trivially introspectable, so tooling, validation, and
  documentation generate themselves.

### The expression language
Magnitudes and conditions are a **small non-Turing-complete expression DSL**:
arithmetic, comparison, boolean logic, a fixed function library, property
access on a typed context. No loops. No recursion. No I/O. No side effects.
Compiled to a bytecode tree at pack-build time, evaluated deterministically.

This is deliberately *not* embedded Lua. Embedding a general scripting language
in content means content can do anything, which means content can break
determinism, break saves, break performance, and become the authority on
mechanics. The DSL keeps rules authoritative while keeping content expressive.
The escape hatch is "an engineer adds an op," not "content writes a script."

### Status effects
A status is a definition holding: tags, duration model, stacking policy,
per-tick effect tree, on-apply/on-expire effect trees, and interaction rules.

**Pushback on the brief:** "thousands of status/effect combinations" is fine as
a *combination count* but disastrous as a *primitive count*. Target roughly
**60–120 primitive statuses**, with the thousands coming from combination,
magnitude, source, and interaction. If a designer proposes status #121, the
correct first question is which existing primitive it is a parameterization of.

## C.6 Item architecture

Four tiers, each with a distinct job:

```
ItemArchetype   authored identity      "longsword" — the feel, the animation, the role
ItemTemplate    generation grammar     what may legally combine, with power budgets
ItemDef         a concrete definition  resolved, ID'd, validated, saveable, may be generated
ItemInstance    runtime object         rolled values, durability, sockets, history, owner
```

Composition axes (the brief's list, formalized): `type`, `material`, `quality`,
`affinity`, `traits[]`, `enchantments[]`, `origin`, `history[]`.

**The mechanism that makes 10,000 items balanceable: a power budget.**
Every `ItemDef` has a computed power score derived from its stats and effects
via a cost table. Templates declare a target budget per tier and a tolerance
band. Validation *fails the build* on any item outside its band. Rebalancing
means changing the cost table, then re-validating 10,000 items in seconds.

**Legitimacy constraints** prevent nonsense and slop:
- a material-vs-type compatibility matrix (`cloth` cannot be a `greatsword`)
- affinity/material rules (`material:silver` + `affinity:holy` is natural;
  `material:bone` + `affinity:holy` requires an explicit `allow` exception)
- an affix-conflict matrix (no `+fire` and `−fire` on the same item)
- per-tier caps on the number of simultaneously obtainable variants in a family

**Authored anchors:** every generated family descends from an authored archetype
that owns the identity — name pattern, silhouette, sound, role. Generation
varies numbers and affixes; it does not invent identity. Named uniques
(target 300–600) are authored outright and are exempt from the grammar.

**Save note:** an `ItemInstance` stores `defId + rollSeed + explicit deltas`,
never a flattened stat block. Rebalancing then propagates correctly to existing
saves, which is precisely the A1 requirement.

## C.7 Ability architecture

`AbilityDef` = requirements, costs, targeting, timing, effect tree, tags,
presentation keys, AI hints.

The 5,000-ability figure decomposes as roughly
`~350 authored ability cores × ranks × modifier packages`. Modifiers are
metamagic-style transforms applied as data (`widen`, `chain`, `delay`,
`empower`, `split`, `linger`, `echo`), each a declarative rewrite over the
effect tree.

**AI hints are mandatory metadata**, not optional. Every ability declares what
it is *for* (`aiRole: nuke | control | heal | buff | debuff | mobility | setup`)
plus a rough value model. Without this, monster AI degenerates into either a
giant switch statement or random flailing, and 2,000 monsters becomes
unmanageable. This is cheap to author and enormously valuable.

## C.8 Magic architecture

**Magic is not a separate system.** Magic is abilities carrying magic tags plus
three pieces of content-declared structure:

1. **Elements and the interaction matrix** — a data table of element ×
   element/tag multipliers, reactions, and conversions. One table, authored,
   balanceable, testable. Not a switch statement.
2. **Resource models** — mana, stamina, health cost, charges, cooldown,
   reagents, corruption, favor. Declared per ability as a cost list; each cost
   kind is an op.
3. **Spell composition grammar** — `element × shape × delivery × modifier`, the
   same generation grammar machinery as items, with the same power budgeting.

Schools, casting time, interruption, counterspelling, ritual casting, and
concentration are all effect-tree and timing features — they need no bespoke
subsystem.

## C.9 Summoning architecture

The flagship system, and the one most likely to be architecturally mangled if
built as a special case. It must not be one.

**A summon is an ordinary Actor.** It has the same components as any monster or
party member. What makes it a summon is a relationship object:

```
SummonContract  (an Instance, persisted)
  ├── creatureInstanceId     the persistent creature (levels, skills, gear, memory)
  ├── acquisitionMethod      how it was obtained (op-driven, extensible)
  ├── bond                   affinity/trust/obedience tracks
  ├── terms                  costs, restrictions, obligations, taboos
  ├── summonCost             a cost list, same machinery as ability costs
  └── availability           conditions under which it can be called
```

- **Acquisition methods are ops**, so the brief's ten mechanisms (defeat,
  contract, raise, egg, capture, negotiate, divine recognition, construct,
  bind undead, tame) are ten content-declared paths over shared machinery, and
  an eleventh costs one op.
- **The creature persists independently of being summoned.** It levels, learns,
  equips, evolves, and remembers whether or not it is on the field. This is the
  key structural decision: it makes "summons gain levels / equip items /
  evolve / fuse / inherit traits / act independently" all *later content and
  ops*, not later rewrites.
- **Summoned actors are placed into an encounter by the same spawn path as any
  other actor.** Combat does not know what a summon is; it knows about actors
  with controllers.
- **Controller abstraction from day one:** every actor has a `Controller`
  (`Player`, `Ai(profile)`, `Scripted`, `Semi(autonomy rules)`). Independent
  summon action is then a controller choice, not a combat-system change. This
  one small piece of foresight is what makes army-scale gameplay possible
  later — but see the pushback in section C.22.
- **Fusion, evolution, and inheritance** are transforms over creature instances:
  `(inputs, recipe, seed) -> creature`. They belong in L2 alongside crafting,
  because they are structurally identical to crafting.

## C.10 Character progression architecture

The brief's model — `Swordsmanship 42 / Summoning 17 / Fire Magic 31 / Dragon
Affinity / Ancient Dragon Contract` — is a **skill-track + qualification**
system, not a class system.

```
SkillTracks       map<SkillId, value>, gained from use and from rewards
Attributes        a small stat spine (6–10 values)
Qualities         boolean/graded facts: affinities, contracts, titles, deeds, curses
Knowledge         recipes, locations, lore, languages, monster knowledge
```

**Qualification predicates** are the unlock mechanism, expressed in the same
DSL:

```toml
[qualification]
id = "core:spec.dragon_knight"
requires = """
  skill(swordsmanship) >= 40
  and skill(dragon_affinity) >= 1
  and has_quality('contract.dragon.*')
  and has_deed('slew_a_wyrm_alone')
"""
grants = ["core:ability.wyrm_stance", "core:passive.scaled_hide", "title:dragon_knight"]
```

Any content can carry a `requires` predicate: abilities, items, quests, dialogue
options, shops, NPC reactions, areas, endings.

**The discoverability problem is real and must be solved architecturally, not
by hoping.** With hundreds of qualifications over dozens of tracks, players will
never find combinations by accident, and "reward discovering combinations"
silently becomes "reward reading a wiki." Because predicates are structured data
rather than code, the engine can compute *near-misses* — qualifications the
player is within reach of — and feed a **Hint/Rumor layer**: NPC dialogue,
dreams, library books, and mentor comments that gesture at what you are close
to, without listing requirements. Budget real design time for this; it is the
difference between the system feeling mysterious and feeling broken.

## C.11 Class and specialization architecture

**There are no classes as containers.** A "class" is a **qualification plus a
grant package plus an identity label**. Consequences:

- Hundreds of specializations cost content, not code.
- Multiclassing is not a feature — it is the absence of a restriction.
- Hybrid identities ("magic swordsman") emerge from tracks rather than needing
  an authored intersection of every pair.
- Optional *starting archetypes* exist purely as onboarding presets: a bundle of
  starting skills and gear. They are a UX affordance, not a mechanical cage.

**Design caution:** fully classless systems have a known failure mode — every
optimized character converges to the same build, and identity dissolves. Plan
for **opportunity cost** mechanisms from the start: rising per-track costs,
mutually exclusive contracts and oaths, limited attunement/equipped-ability
slots, and reputation consequences. Whether to add hard exclusivity is a
genuine game-design decision for the owner (question O4).

## C.12 Monster architecture

```
MonsterArchetype   authored identity, silhouette, role, ecology, behavior profile
MonsterTemplate    variant grammar: tiers, elemental variants, mutations, elites
MonsterDef         a concrete definition (authored or generated, validated)
```

- Behavior is data: a **utility-AI profile** (scored considerations) plus
  optional behavior-tree overrides for authored bosses. Not code per monster.
  This is the only approach that survives 2,000 monsters.
- Ecology tags (`habitat:*`, `diet:*`, `social:*`, `activity:*`) are authored
  even before an ecosystem system exists, because retrofitting them across
  2,000 monsters later is miserable. They cost almost nothing now.
- Loot is a table reference plus tags, resolved by the loot system against the
  item grammar — monsters do not enumerate item IDs.
- Bosses are ordinary monsters with authored ability sets, scripted phase
  triggers (declarative), and an `authored` provenance that exempts them from
  generation grammars.

## C.13 Combat architecture

**Turn-based, timeline-scheduled, resolved as a pure function.**

```
BattleState × Command × RngStream  ->  BattleState′ × EventLog
```

The resolution pipeline, explicitly ordered and identical for every action:

```
1. Declare      command submitted by a controller
2. Validate     legality: resources, targets, timing, silence/stun, reach
3. Commit       pay costs, lock in the action
4. PreHooks     on_action_start reactions (counters, interrupts)
5. Targeting    selector evaluation, redirection, taunt/cover
6. Resolve      per-target: accuracy → mitigation → effect tree → application
7. PostHooks    on_hit / on_kill / on_damaged triggers
8. Reactions    queued reactions resolve, depth-capped
9. Cleanup      deaths, status ticks, timeline reinsertion
```

Non-negotiables:
- **The pipeline never touches rendering, audio, input, or the clock.** It emits
  events; the shell animates them. Animation length must never affect
  resolution.
- **Reaction depth is capped** (proposed 8) with a defined resolution on
  overflow. Infinite reaction chains are otherwise inevitable once content
  scales, and they are the classic "unshippable bug found in month 30."
- **The scheduler is pluggable** behind an interface (`ITurnScheduler`) so
  classic turn order, ATB, and action-point variants remain options without a
  rewrite. Recommended default: a **count-timeline (CTB)** scheduler — speed
  meaningfully affects turn frequency, turn order is visible and readable, and
  it supports delay/haste effects natively.
- **The event log is the contract with presentation**, with the shell free to
  compress, reorder for readability, or skip animations entirely.

This gives, from one design decision: deterministic replays for bug reports,
headless balance simulation, AI that can evaluate hypothetical states, save
integrity, and a future networking path.

## C.14 World and map architecture

```
World      persistent, saved, authoritative state (flags, factions, entities, time)
Region     authored or generated area group; owns a travel graph
Zone       a loadable map: tile layers, collision, spawns, triggers, connections
Scene      the transient runtime instantiation of a zone
```

- **World state and scene instance are strictly separate.** Unloading a zone
  must never lose game state; loading a zone must never be required to query
  game state. Getting this wrong is how RPGs end up unable to answer "is that
  chest already open?" without loading the map.
- Zones are tile-grid based with layers (ground, decor, collision, trigger,
  spawn, light). Authored in Godot's TileMap editor and **exported to the
  content pack format** — the shell's editor is a tool, not the runtime
  authority.
- Persistent per-zone deltas (opened chests, killed uniques, changed terrain)
  are stored sparsely in world state keyed by zone, never by rewriting maps.
- The travel graph is separate from the tile grid so that fast travel, world
  maps, dimensional travel, and procedural continents attach to a graph rather
  than to geometry.

## C.15 Quest and event architecture

- A quest is a **declarative state machine**: states, transitions guarded by
  condition predicates, and effect trees on entry/exit. No imperative quest
  scripts, ever — they are unreviewable, untestable, and unmergeable at
  thousands of quests.
- **World flags** are a typed, namespaced key-value store (`quest.*`, `world.*`,
  `faction.*`, `npc.*`), schema-declared so that validation catches typos and
  orphans. Untyped free-form flags are how quest systems rot.
- **The event bus** carries past-tense facts (`MonsterKilled`, `ItemAcquired`,
  `RegionEntered`, `NpcDied`, `DayElapsed`). Quests, factions, achievements, and
  the sim subscribe with declarative trigger patterns. The bus is for
  *notification*; it is never used for control flow inside a resolution step
  (that is what causes untraceable ordering bugs).
- Dialogue is a condition-gated graph; dialogue nodes may carry effect trees and
  requirement predicates, so dialogue reuses everything above.
- **Quest validation is a build step:** every quest is checked for reachability,
  dead ends, unreachable states, orphan flags, and unobtainable items. At
  thousands of quests this is the only thing standing between you and permanent
  softlocks.

## C.16 Procedural generation architecture

**Everything is seeded and hierarchical:**

```
worldSeed
  └── regionSeed  = hash(worldSeed, "region", regionId)
        └── zoneSeed = hash(regionSeed, "zone", zoneId)
              └── encounterSeed, lootSeed, nameSeed  (named sub-streams)
```

Named derived streams mean adding a new generator never shifts existing output —
a subtle but critical property. Without it, every generator change reshuffles the
entire world and invalidates every save and every test snapshot.

Two distinct modes, and the distinction must never be blurred:

| Mode | Runs at | Stored as | Used for |
|---|---|---|---|
| **Bake-time** | Build/authoring | Committed content rows | Item families, monster variants, named regions, anything balance-relevant or referenced by other content |
| **Runtime** | Play | Not saved; regenerated from seed + deltas | Dungeon layouts, wandering encounters, minor loot rolls |

Generators emit *content in the normal schema*, run through the *normal
validator*, and are then *indistinguishable from authored content mechanically*
while remaining *distinguishable by provenance*. There is no second content
system and no second rules path.

## C.17 World simulation architecture

Level-of-detail simulation, three tiers, because uniform simulation of hundreds
of settlements is a guaranteed performance and save-size failure (A8):

| Tier | Applies to | Update model | Cost |
|---|---|---|---|
| **Live** | The zone the player is in and its neighbors | Per-tick, full fidelity | Bounded actor count |
| **Warm** | Recently visited or player-relevant regions | Coarse periodic updates (in-game hours) | Aggregate only |
| **Cold** | Everywhere else | Lazy, computed on next observation from elapsed time | Near zero |

Cold regions are advanced by a **catch-up function** when observed, rather than
by having been ticked. Faction strength, economies, and populations therefore
evolve without cost. The catch-up function must be deterministic and must
produce the same result as N incremental updates within a stated tolerance —
this is a testable property, and it should be tested.

**Hard budget:** the entire world simulation gets a fixed per-frame millisecond
budget and a work queue. When it exceeds budget, it defers; it never stutters
the game. Simulation ambition must lose to frame rate, always.

## C.18 Crafting, alchemy, and enchanting

All three are the same machinery and must share it:

```
Recipe: (inputs[], stationTags[], requirements, seed) -> outputs[] + sideEffects[]
```

- Recipes may be **exact** (authored) or **grammar-driven** (transform rules:
  "any `material:metal` ingot + any `catalyst:fire` yields the fire-affinity
  variant of the base item").
- Grammar-driven recipes are what let crafting cover 10,000 items without
  10,000 recipes. Discovery, quality rolls, and mastery are modifiers on the
  same call.
- Summon fusion, weapon evolution, and monster breeding are all recipes over
  different input types. They live here, not in bespoke systems.

## C.19 Validation architecture

Validation is a **build gate**, not a runtime nicety. Five levels, all runnable
offline and in CI:

| Level | Checks | Failure mode |
|---|---|---|
| **V1 Schema** | JSON Schema conformance, types, required fields, enums | Build error |
| **V2 Reference** | Every `ContentId` resolves; no dangling refs; no orphan flags; no unreachable quest states | Build error |
| **V3 Rule** | Ops exist, arities match, expressions compile, selectors are valid, tags are declared | Build error |
| **V4 Balance** | Power budgets within band; stat outliers; cost/benefit curves; progression pacing | Build error for hard bands, report for soft ones |
| **V5 Semantic** | Legitimacy constraints, conflicting affixes, unobtainable content, unreachable qualifications, softlock analysis | Warning or error per rule |

Every validation rule has an ID (`V3.OP_ARITY`) so waivers are explicit,
attributable, and greppable — never a silently disabled check.

**The validator is the single most valuable tool in this project.** It should be
built in Phase 1, before there is much content to validate, because it is what
makes large-scale content safe to generate and safe to hand to an agent.

## C.20 Save system strategy

The most consequential subsystem for project longevity. Design decisions:

1. **Snapshot-based, not event-sourced.** Full event sourcing for a 200-hour RPG
   means unbounded replay cost and migration pain across years of rule changes.
   Snapshot is correct. (A command log may be kept separately for *debugging*
   and bug reports, never as the save format.)
2. **Segmented saves.** The save is a container of independently versioned
   segments: `party`, `inventory`, `world_flags`, `factions`, `zones_delta`,
   `quests`, `summons`, `sim_state`, `stats`, `meta`. Each carries its own
   schema version.
3. **A migration chain per segment.** `v3 -> v4 -> v5`, each migration a small
   tested pure function. Never a single monolithic upgrade path.
4. **References, not resolved values.** Instances store `defId + seed + explicit
   deltas`. Rebalancing propagates into existing saves. This is the direct
   answer to A1.
5. **Content manifest in the save.** Pack IDs, versions, and a content hash. On
   load, missing or changed content produces a *specific, actionable* warning
   ("mod X removed 3 items you were carrying") instead of a crash or silent
   corruption.
6. **Deprecation, never deletion.** Removed content becomes
   `deprecated: true, replacedBy: <id>`, and stays in a tombstone registry.
   Loaders redirect. IDs are never reused, ever.
7. **Deterministic serialization** — ordered keys, stable formats — so saves are
   diffable and testable.
8. **A save-compat CI gate:** a corpus of historical saves from every previous
   schema version is committed and loaded in CI on every build. This costs
   little and catches the class of bug that otherwise reaches players.

**Rule:** any change to a persisted structure requires a migration in the same
commit. CI enforces this by comparing segment schema versions against the
migration registry.

## C.21 Modding strategy

Three tiers, deliberately staged. Do not skip to tier 3.

| Tier | Capability | When | Risk |
|---|---|---|---|
| **1 — Data mods** | Add or override content packs: items, monsters, abilities, quests, maps, text, tuning tables | Phase 4 | Low. This is 80% of modding value |
| **2 — Asset mods** | Sprites, tilesets, audio, portraits, UI themes | Phase 5 | Low |
| **3 — Script mods** | Sandboxed logic via the expression DSL, later possibly custom ops | Post-1.0, if ever | High: freezes internals, security surface, save-compat hazards |

Design implications carried from the start (cheap now, impossible to retrofit):
- Namespaced IDs, so mods cannot collide (`mymod:sword.flame`).
- A declared load order with explicit override and patch semantics
  (`replace` / `merge` / `patch`).
- Every registry is populated from packs, so the base game is itself "just a
  pack" (`core`). If the base game loads through a path mods cannot use, the
  modding story is already dead.
- Saves record which packs were active.

## C.22 Aggressive pushback: ideas in the brief that will hurt you

I was asked not to flatter this. These are the parts of the vision that are
actively dangerous, in descending order of danger.

**1. "Thousands of status/effect combinations" as a primitive count — reject.**
Interaction complexity is quadratic. 120 primitives already yield ~7,000 pairs
you cannot test. Cap primitives hard and get scale from composition. *Adopt the
cap explicitly, in writing, or it will not hold.*

**2. "Summons act independently" + "army-scale gameplay" — defer, but reserve
the seam.** Independent AI allies are one of the hardest things in RPG design:
they invite frustration, they multiply combat length, they explode the AI
quality bar, and army scale breaks the turn-based model entirely (60 actors ×
turn-based = unplayable pacing). The architecture reserves the seam (the
Controller abstraction, C.9). Building it before the core game is fun would be a
serious mistake, and army combat should probably be a *separate encounter mode*
with a different scheduler rather than an extension of party combat.

**3. "Player-built towns" + world politics + economy simulation — this is a
second game.** Each is individually a full project. Together they will consume
years and produce systems that mostly do not interact with the RPG the player
is actually playing. If one is wanted, pick one, and pick it for the fantasy it
serves, not for the simulation elegance.

**4. Runtime LLM generation — reject as a runtime dependency, and be skeptical
even as a feature.** Latency, cost, offline play, determinism, save
compatibility, consistency across a 200-hour playthrough, and the risk of
generating content that contradicts canon all argue against it. Bake-time
generation with review keeps every benefit and none of the failure modes. The
architecture supports runtime generation as a strictly optional, quarantined,
schema-validated path (section J), but I recommend against shipping it in the
main line.

**5. A fully classless free-form skill system will homogenize builds** unless
opportunity cost is designed in deliberately (C.11). "Everyone eventually has
everything" is the default outcome of skill-by-use systems, and it destroys both
build identity and replay value. Decide early (O4) — retrofitting scarcity into
a generous system feels like a betrayal to players; retrofitting generosity into
a scarce one is painless.

**6. "10,000 items" is a bad target and a good side effect.** Do not aim at it.
Aim at ~40 archetypes × ~12 materials × ~25 affixes with legitimacy constraints,
plus ~400 authored uniques. If the number lands at 6,000 or 30,000, fine —
nobody should ever be counting.

**7. Do not build a GUI content editor before Phase 5.** Text plus a validator
plus generators covers it, and it is strictly better for AI agents and for git.
Custom editors are where solo/small projects go to die.

**8. Skill-number gating (`Swordsmanship >= 40`) creates an invisible discovery
problem** that will read to players as "the game has no content." Budget real
design effort for the Hint/Rumor layer (C.10) and treat it as a shipping
requirement of the progression system, not a polish item.

**9. Nostalgia has a floor.** "Deliberately modest visuals" is a sound budget
decision, but 2D JRPG audiences are far less tolerant of *bad* pixel art than of
*simple* pixel art. Budget for one competent artist or a coherent purchased
asset base. Programmer art at this scale reads as an unfinished project and will
suppress every other quality signal.

**10. Two AI agents will drift no matter what the documents say.** Documentation
does not constrain behavior; CI does. Every architectural rule in this document
that *can* be mechanically enforced *must* be mechanically enforced, or it will
be violated within months and nobody will notice until it is expensive.

---

# D. High-level system diagram

```
                    ┌───────────────────────────────────────────────────┐
                    │              L8  TOOLS (offline)                  │
                    │  content-compile · validate · generate · balance  │
                    │  sim · import-maps · replay · query               │
                    └───────────┬──────────────────────┬────────────────┘
                                │ reads/writes         │ drives headlessly
                                ▼                      ▼
┌──────────────────────────────────────────────────────────────────────────┐
│                        L7  SHELL  (Godot 4 / C#)                         │
│   Rendering · Sprites · TileMap · UI · Input · Audio · Scene flow · IO    │
│   consumes EventLog, sends Commands, never contains a rule               │
└───────────────┬──────────────────────────────────────┬───────────────────┘
                │ implements                           │ calls
                ▼                                      ▼
┌───────────────────────────────┐   ┌──────────────────────────────────────┐
│   L6  PORTS (interfaces)      │   │      L5  APPLICATION                 │
│  IRenderSink · IClock         │◄──┤  GameSession · CommandDispatch       │
│  IStorage · IAudioCue         │   │  ModeStack · SaveOrchestrator        │
│  IContentSource · IGenClient  │   │  (the only mutation entry point)     │
└───────────────────────────────┘   └──────────────┬───────────────────────┘
                                                   │
                    ┌──────────────────────────────┴────────────────┐
                    ▼                                               ▼
┌──────────────────────────────────────┐   ┌──────────────────────────────┐
│           L4  WORLD                  │   │        L3  ENCOUNTER         │
│  Map · Zones · Travel · WorldState   │──▶│  BattleState · Scheduler     │
│  Factions · Economy · WorldSim       │   │  Resolution pipeline         │
│  Quests · Dialogue · Events · Flags  │◄──│  ActorAI · Loot resolution   │
│                                      │   │                              │
│  hands down: EncounterSetup          │   │  hands back: EncounterResult │
│  MUST NOT depend on L3 internals     │   │  MUST NOT depend on L4       │
└──────────────────┬───────────────────┘   └──────────────┬───────────────┘
                   │                                      │
                   └──────────────┬───────────────────────┘
                                  ▼
┌──────────────────────────────────────────────────────────────────────────┐
│                              L2  RULES                                   │
│  Stats · Modifiers · Effects · OPS REGISTRY · Status · Items · Abilities  │
│  Magic · Summons · Progression · Qualifications · Crafting · Loot         │
│  Pure. Deterministic. No I/O. No engine. No clock.                        │
└───────────────────────────────────┬──────────────────────────────────────┘
                                    ▼
┌──────────────────────────────────────────────────────────────────────────┐
│                             L1  CONTENT                                  │
│  Schemas · Pack loading · Registries · ID resolution · Expression VM      │
│  Tombstones · Provenance · Localization keys                             │
└───────────────────────────────────┬──────────────────────────────────────┘
                                    ▼
┌──────────────────────────────────────────────────────────────────────────┐
│                           L0  FOUNDATION                                 │
│  ContentId · InstanceId · ActorId · Fixed-point math · Deterministic RNG  │
│  Ordered collections · TagSet · Result/Error · Diagnostics                │
└──────────────────────────────────────────────────────────────────────────┘

CONTENT PIPELINE (offline, left to right):
  content/*.toml ──▶ validate (V1–V5) ──▶ compile ──▶ packs/*.pack ──▶ L1 loads
       ▲                                                    │
       │                                                    ▼
  generators + AI candidates ──▶ review gate ──▶ committed as normal content
```

---

# E. Proposed repository tree

```
/
├── AGENTS.md                      shared agent rules (< 200 lines, hard cap)
├── CLAUDE.md                      Claude-specific; points at AGENTS.md
├── CODEX.md                       Codex-specific; points at AGENTS.md
├── README.md                      what this is, how to build, where to start
├── ARCHITECTURE_PROPOSAL.md       ← this file; deleted after decomposition
├── Directory.Build.props          shared build settings, analyzers, warnings-as-errors
├── GameProject.sln
│
├── docs/
│   ├── README.md                  the map. Read this first. Links everything below
│   ├── architecture/
│   │   ├── OVERVIEW.md            layers, the two big ideas, the shape
│   │   ├── LAYERS.md              what each layer is and is not
│   │   ├── DEPENDENCY_RULES.md    the authoritative table (mirrors deps.allow)
│   │   ├── DETERMINISM.md         RNG streams, fixed-point, ordering rules
│   │   ├── SAVE_COMPAT.md         segments, migrations, ID policy, tombstones
│   │   ├── PERFORMANCE.md         budgets, hot paths, measurement policy
│   │   ├── NAMING.md              all naming conventions
│   │   └── GLOSSARY.md            one definition per term, project-wide
│   ├── systems/                   one spec per system; each has a stable SPEC-ID
│   │   ├── _TEMPLATE.md
│   │   ├── SPEC-INDEX.md          id → file → version → status → owner
│   │   ├── effects-and-ops.md     (SPEC-CORE-001) the spine
│   │   ├── stats.md
│   │   ├── combat.md
│   │   ├── status-effects.md
│   │   ├── items.md
│   │   ├── abilities.md
│   │   ├── magic.md
│   │   ├── summoning.md
│   │   ├── progression.md
│   │   ├── qualifications.md
│   │   ├── monsters.md
│   │   ├── crafting.md
│   │   ├── loot.md
│   │   ├── quests.md
│   │   ├── dialogue.md
│   │   ├── factions.md
│   │   ├── economy.md
│   │   └── worldsim.md
│   ├── world/                     the fiction, not the code
│   │   ├── canon.md               cosmology, how magic works in-fiction, tone
│   │   ├── regions.md
│   │   ├── timeline.md
│   │   ├── factions.md
│   │   └── naming-and-culture.md  how names are formed per culture (feeds generators)
│   ├── content/
│   │   ├── authoring-guide.md     how to write a content file by hand
│   │   ├── schemas.md             narrative guide to schema/ (machine files are authoritative)
│   │   ├── provenance.md          authored vs generated vs ai; the review gate
│   │   ├── generation-grammars.md legitimacy constraints, power budgets
│   │   └── ai-generation.md       the optional AI layer, its boundaries, its prompts
│   ├── decisions/                 ADRs
│   │   ├── README.md              how to write one; the numbering rule
│   │   └── ADR-0001-rules-core-and-shell.md
│   ├── state/                     always-current, small, overwritten (never appended)
│   │   ├── CURRENT.md             ≤ 1 page: phase, in-flight, blocked, next
│   │   ├── KNOWN_PROBLEMS.md      table; rows deleted when fixed
│   │   ├── DECISION_QUEUE.md      things needing the owner's judgment
│   │   └── ROADMAP.md             phases and their exit criteria
│   └── handoff/                   inboxes, NOT diaries
│       ├── CLAUDE_TO_CODEX.md     ≤ 10 open entries, hard cap
│       └── CODEX_TO_CLAUDE.md     ≤ 10 open entries, hard cap
│
├── schema/                        MACHINE-AUTHORITATIVE contracts (JSON Schema)
│   ├── common/  ids.json  tags.json  expression.json  provenance.json
│   ├── defs/    item.json  ability.json  monster.json  status.json  quest.json …
│   ├── tables/  stats.json  elements.json  damage-types.json  skills.json
│   └── save/    segment-*.json
│
├── content/                       plain text, git-diffable, human-readable
│   ├── core/                      the "A" layer — authored canon
│   │   ├── tables/                stats, elements, interaction matrices, cost tables
│   │   ├── items/  abilities/  monsters/  status/  summons/  classes/
│   │   ├── quests/  dialogue/  factions/  zones/
│   │   └── text/                  localization keys → strings, per locale
│   └── generated/                 the "B" layer — provenance-tagged, regenerable
│       ├── items/  monsters/  zones/  lore/
│       └── MANIFEST.json          generator, version, seed, review status per batch
│
├── src/
│   ├── Game.Foundation/           L0
│   ├── Game.Content/              L1
│   ├── Game.Rules/                L2
│   ├── Game.Encounter/            L3
│   ├── Game.World/                L4
│   ├── Game.Application/          L5
│   ├── Game.Ports/                L6
│   └── tools/                     L8
│       ├── Game.Tools.Compiler/
│       ├── Game.Tools.Validate/
│       ├── Game.Tools.Generate/
│       ├── Game.Tools.BalanceSim/
│       ├── Game.Tools.MapImport/
│       └── Game.Tools.Query/      "which items use op X?" — agent's best friend
│
├── shell/
│   └── godot/                     L7 — the Godot project
│       ├── project.godot
│       ├── scenes/  ui/  actors/  maps/  fx/
│       ├── src/                   C# shell code (references Game.Application, Game.Ports)
│       └── assets/                sprites, tilesets, audio, fonts
│
├── tests/
│   ├── Game.Foundation.Tests/
│   ├── Game.Content.Tests/
│   ├── Game.Rules.Tests/
│   ├── Game.Encounter.Tests/
│   ├── Game.World.Tests/
│   ├── Game.Golden/               snapshot + replay + determinism tests
│   ├── Game.SaveCompat/           historical save corpus, loaded every build
│   └── Game.Balance/              batch simulation; produces reports, not verdicts
│
└── build/
    ├── deps.allow                 machine-readable dependency rules (CI-enforced)
    ├── ci/
    └── scripts/
```

**Why `schema/` and `content/` sit at the root rather than inside `src/`:** they
are first-class project artifacts with their own lifecycle, their own tooling,
and their own reviewers. Burying them inside code implies they are subordinate
to code. They are not — they are the majority of the project by volume and by
long-term effort.

---

# F. Major module descriptions

### `Game.Foundation` (L0)
Primitives with no game meaning whatsoever. `ContentId`, `InstanceId`,
`ActorId`, `TagSet`, fixed-point `Fx` arithmetic, the deterministic RNG
(splittable, named streams), ordered collection wrappers, `Result<T,Error>`,
diagnostics. **Contains zero game concepts.** If "damage" or "level" appears
here, the layer has been violated. Should stabilize within months and then
barely change — a good early signal of architectural health.

### `Game.Content` (L1)
Schema definitions, pack loading and merging, registries (one per definition
kind), ID resolution and tombstone redirection, the expression compiler and VM,
localization key resolution, provenance tracking. Knows *that* content exists
and *how it is shaped*; knows nothing about what any of it *means*. It can load
an `ItemDef` without knowing what an item does.

### `Game.Rules` (L2)
The mechanical heart. Stats and the modifier pipeline, the **op registry**,
effect trees and their evaluation, status effects, items, abilities, magic,
summon contracts, progression and qualifications, crafting, loot resolution.
Pure and deterministic: no I/O, no clock, no engine, no randomness except
through an explicitly passed `RngStream`. This is the assembly that must remain
100% unit-testable forever. It is also the assembly Claude specifies most
tightly and reviews most aggressively.

### `Game.Encounter` (L3)
Battlefield state, turn scheduling, the resolution pipeline, actor controllers
and AI, targeting and selectors, encounter-scoped loot. Consumes an
`EncounterSetup`, produces an `EncounterResult` and an `EventLog`. **Knows
nothing about towns, quests, factions, or maps.**

### `Game.World` (L4)
Zones and maps, spatial queries, travel graph, world state and flags, persistent
zone deltas, factions and reputation, economy, the LOD world simulation, quests,
dialogue, and the event bus. Builds `EncounterSetup` objects and consumes
`EncounterResult`s. This is the largest and most content-facing module, and the
one most likely to need splitting later — plan for `Game.World.Sim` and
`Game.World.Narrative` to separate around Phase 5.

### `Game.Application` (L5)
The session: game modes (exploration, battle, menu, dialogue) as a stack, the
command dispatcher, save/load orchestration, and the top-level tick. **The only
place where mutation of game state is initiated.** Every state change in the
game enters through here, which makes "what can change the world?" a question
with a finite, readable answer.

### `Game.Ports` (L6)
Interfaces only, no implementations. `IRenderSink`, `IAudioCue`, `IClock`,
`IStorage`, `IInputSource`, `IContentSource`, `IGenerationClient`,
`ITelemetry`. Small, stable, and versioned — a port changing is a notable event.

### `shell/godot` (L7)
Everything visible and audible. Scenes, sprites, tilemaps, UI, input mapping,
audio, particles, camera, save file IO. Consumes the `EventLog` and turns it
into animation; sends `Command`s. **Contains no game rule.** The test: if you
deleted the entire shell, every rule, every balance property, and every test
would still pass.

### `src/tools/*` (L8)
Offline CLIs. The compiler (text → packs), the validator (V1–V5), generators,
the balance simulator, the map importer, and a content query tool. These are
where Codex will spend a great deal of productive time, and they are the
project's actual force multiplier — more so than any gameplay feature.

---

# G. Dependency rules

**The rule, stated once:** a module may depend only on strictly lower layers,
plus explicitly whitelisted peers. All dependencies are declared in
`build/deps.allow` and enforced in CI by an assembly-reference check. A build
that violates them fails; there is no warning mode.

| Module | L | MAY depend on | MUST NOT depend on | Why the prohibition matters |
|---|---|---|---|---|
| `Game.Foundation` | 0 | BCL only | Everything else | Keeps primitives free of game meaning; makes them trivially testable and reusable |
| `Game.Content` | 1 | Foundation | Rules, Encounter, World, App, Ports, Shell | Loading must not require the rules; enables tools that read content without a game |
| `Game.Rules` | 2 | Foundation, Content | Encounter, World, App, Ports, Shell, **any clock, any I/O, any engine type** | Purity is what makes headless simulation and determinism possible. This is the single most important prohibition in the project |
| `Game.Encounter` | 3 | Foundation, Content, Rules | **World**, App, Ports, Shell | Combat must not know about towns, quests, or maps. This is what allows new encounter modes (arena, army, ecology sim) without touching combat |
| `Game.World` | 4 | Foundation, Content, Rules, Encounter (**public API only**) | App, Ports, Shell | World may *start* an encounter and *read its result*; it may never reach into battle internals |
| `Game.Application` | 5 | Foundation, Content, Rules, Encounter, World, Ports | Shell, Tools | The application orchestrates; it does not render. Keeps the shell replaceable |
| `Game.Ports` | 6 | Foundation (types only) | Everything above and beside it | Ports must be implementable by any shell, including a test double |
| `shell/godot` | 7 | Application, Ports, Godot; L0–L4 **types for display only** | Tools; **mutating any core state directly** | The shell must never become a place where rules hide |
| `Tools.*` | 8 | Anything | Shell | Tools drive the core headlessly; a tool that needs the shell is a design failure |

### Additional structural rules

1. **No circular dependencies at any granularity** — assembly, namespace, or
   type. Checked in CI.
2. **The event bus is notification, not control flow.** A resolution step may
   *emit* events; it may never *await* a subscriber. Subscribers run after the
   step completes, in declared priority order.
3. **Peer domains communicate via read-only query interfaces.** Quests read
   faction standing through `IFactionQuery`; they never hold a `FactionSystem`.
4. **Content never depends on code identity.** No content file may reference a
   C# type name. It references op IDs, tags, and content IDs.
5. **Ops may not call other systems' mutation APIs directly.** An op mutates via
   the effect application context, so that every mutation is logged, ordered,
   and replayable.
6. **Nothing below L5 may read the wall clock or the filesystem.** Time comes in
   as data.

### Deliberate non-dependencies worth calling out

- **Rules does not depend on Encounter.** A damage formula does not know a
  battle exists. This lets the same formula run in overworld hazards, crafting
  outcomes, and cutscenes.
- **Encounter does not depend on World.** Discussed above; the highest-value
  prohibition after Rules purity.
- **The AI generation client is a Port, not a dependency.** Nothing below L6
  knows that an LLM could exist. If the port is unimplemented, the game is
  fully playable — this is what makes the offline guarantee structural rather
  than a promise.

---

# H. Core data-flow examples

### H.1 The player attacks with a fire-affinity sword

```
Shell            input → Command{ Attack, actor: 0, target: 3, ability: core:ability.flame_cleave }
  ↓
Application      dispatch → validates it is this actor's turn → forwards to Encounter
  ↓
Encounter        1. Validate: MP cost, silence, reach, target legality
                 2. Commit: pay costs, emit CostPaid
                 3. PreHooks: target's "Riposte" passive registers a reaction
                 4. Targeting: selector enemies.in_arc(120,2) → actors [3, 4]
  ↓
Rules            5. Per target:
                    StatBlock resolution   (base → flat → %add → mult → clamp)
                    weapon affinity fire + ability element fire → element:fire
                    element matrix: fire vs kind:beast + tag:frost → ×1.75
                    magnitude expr evaluates over context (deterministic)
                    op:damage applied → 214
                    op:apply_status burning, chance 0.41, RNG stream "battle.effect"
  ↓
Encounter        6. PostHooks: on_hit triggers, weapon "Ember" enchant adds burn stack
                 7. Reactions: Riposte resolves (depth 1 of max 8)
                 8. Cleanup: deaths, timeline reinsertion
  ↓
EventLog         [ CostPaid, ActionStarted, Damaged(3,214,fire), StatusApplied(3,burning),
                   Damaged(4,88,fire), ReactionTriggered(3,riposte), Damaged(0,45),
                   TurnEnded(0) ]
  ↓
Shell            animates the log; length of animation cannot affect the result
```

Note what did **not** happen: no rule read the screen, no formula asked how long
an animation takes, no random number came from an ambient source. That is why
this exact sequence can be replayed byte-identically in a test, and why 100,000
simulated battles can run headless in seconds.

### H.2 A generated item is loaded and equipped

```
Build time   template weapon.sword.arming × material:meteoric_iron × affix:frost_2
             → legitimacy check (material/type matrix) → OK
             → power budget 312 vs tier band [280, 340] → OK
             → V1–V5 validation → emit content/generated/items/weapon_00417.toml
                with provenance{ generator: "item-grammar", version: 3, seed: 88121,
                                 reviewed: false }
Runtime      pack load → registry["gen:weapon.arming_meteoric_frost_2"]
Play         drop rolls instance → ItemInstance{ defId, rollSeed: 5512, deltas: {} }
Save         stores defId + rollSeed + deltas — never resolved stats
Rebalance    cost table changes → item's stats change → existing saves inherit it
```

### H.3 A quest completes and the world reacts

```
Encounter    EncounterResult{ killed: [core:monster.bandit_captain], ... }
  ↓
World        event bus ← MonsterKilled{ id, zone, byParty }
  ↓
Quests       declarative trigger matches quest "core:quest.road_wardens" state 3
             → transition to state 4 → effect tree runs:
                op:set_flag world.roads.bandits_broken = true
                op:modify_reputation faction:merchant_guild +15
                op:grant_item core:item.warden_seal
  ↓
Factions     reputation crosses a threshold → FactionStandingChanged
  ↓
WorldSim     merchant_guild (Warm tier) schedules: caravan frequency +1,
             road ambush rate −60% in region:eastvale
  ↓
Content gen  rumor generator (bake-time or cached) produces tavern dialogue
             referencing the deed, keyed to world.roads.bandits_broken
  ↓
Shell        notification + journal update from the event log
```

No system in that chain called another system's method directly except through
declared query interfaces. Each link is independently testable.

---

# I. Content and schema strategy

### Format
**TOML for authored content, JSON Schema as the contract, a binary pack as the
runtime format.**

- TOML because it is the most human-writable and most git-diff-friendly of the
  realistic options, and because it survives being edited by both humans and
  agents without whitespace catastrophes. (YAML is rejected: its implicit typing
  and indentation sensitivity cause real, silent content bugs at scale.)
- JSON Schema because it is machine-authoritative, tool-supported, and lets both
  agents validate before committing.
- A compiled binary pack because loading 10,000 TOML files at startup is a
  three-second stall; loading one indexed pack is milliseconds. Dev builds may
  load loose files with hot reload; shipping builds load packs.

### ID policy (this is a save-compat contract, not a style preference)

```
namespace:category.name          core:weapon.longsword
                                 core:monster.frost_wolf
                                 gen:weapon.arming_meteoric_frost_2
                                 mymod:ability.void_step
```

- IDs are **permanent**. Never renamed, never reused, never recycled.
- Deletion is `deprecated: true` + `replacedBy`, plus a tombstone entry.
- `core:` is the base game. `gen:` is machine-generated. Mods use their own.
- Display names live in localization, never in the ID. An item may be renamed
  freely; its ID never changes.

### Mandatory fields on every content row

```toml
id          = "core:weapon.longsword"
schema      = "item/1.4"
provenance  = { source = "authored", author = "claude", reviewed = true }
tags        = ["kind:weapon", "type:sword", "hands:one", "damage:slashing"]
nameKey     = "item.core.weapon.longsword.name"
descKey     = "item.core.weapon.longsword.desc"
```

`provenance` and `schema` on *every* row is what makes bulk operations,
migrations, and AI-content quarantine possible years later. Retrofitting them
across 10,000 rows is painful; adding them now is free.

### Layering and overrides
Packs load in declared order; later packs may `replace`, `merge`, or `patch`
earlier definitions. The base game is itself a pack (`core`), loaded through
exactly the path mods use. If the base game ever loads through a privileged
path, modding is dead and nobody will notice for two years.

### Tuning tables are content, not code
Element interaction matrices, XP curves, price curves, power-budget cost tables,
drop-rate curves, and difficulty scaling all live in `content/core/tables/`.
Rebalancing must never require a code change or a rebuild of the core.

### Text
All player-visible strings are keys resolved from `content/core/text/<locale>/`.
This costs a little friction now and saves the project later. Generated content
generates *text entries* alongside its rows, in the same keyed system.

---

# J. The AI-generation boundary

**The rule:** AI is a content *authoring* tool. It is never a runtime
dependency, never an authority on mechanics, and never a source of unvalidated
data.

### The pipeline

```
1. REQUEST      a generation request is a structured job:
                schema + constraints + world-canon context + seed + quota
                (never a free-form "invent a cool sword")

2. GENERATE     an LLM produces candidate rows in the normal content schema.
                It may only reference existing ops, tags, and content IDs.
                Offline. Batch. Reproducible by (prompt hash + seed + model id).

3. VALIDATE     the SAME validator as authored content: V1–V5, no exceptions,
                no relaxed mode. Rejections are logged with reasons and fed back.

4. CANON CHECK  automated checks against world canon: naming conventions per
                culture, timeline consistency, faction relationships, no
                references to nonexistent places, tone/vocabulary rules

5. REVIEW GATE  content is committed with reviewed:false. Depending on the
                content class it either (a) requires human/Claude approval
                before shipping, or (b) ships as low-stakes background content

6. BAKE         approved content becomes ordinary content rows, mechanically
                indistinguishable from authored content, permanently tagged by
                provenance
```

### What AI may and may not generate

| May generate | Must never generate |
|---|---|
| Flavor text, item descriptions, book contents | Ops, or anything defining what an op does |
| Minor NPC names, histories, daily routines | Balance numbers outside a validated budget |
| Rumors, tavern talk, regional gossip | Main-story content, major characters, endings |
| Legendary item *histories* (not their stats) | Tuning tables, XP curves, cost tables |
| Remote settlement layouts and populations | Anything that changes world canon |
| Dungeon variants within authored grammars | New tags, new stats, new element rules |
| Side quests within authored quest templates | Anything referenced by authored content |
| Monster *variants* of authored archetypes | New monster archetypes or their behaviors |

**The asymmetry is deliberate:** AI generates *the leaves*, never *the trunk*.
Something authored may reference something generated only through a stable
generated ID that has passed review — otherwise a regeneration silently breaks
authored content.

### Offline guarantee, structurally enforced
`IGenerationClient` lives in `Game.Ports` (L6). Nothing at L0–L5 knows it
exists. Shipping builds contain baked content only. If no generation client is
registered, the game is complete and correct. This is not a policy — it is a
dependency rule the CI check enforces.

### Review-gate economics (the part people get wrong)
Generating 5,000 rumors takes an afternoon. *Reviewing* 5,000 rumors does not.
Decide the review budget before generating, not after, and classify content by
required review level:

- **R0 — no review:** pure flavor with no mechanical or canon impact
  (a single tavern line). Spot-checked in samples.
- **R1 — automated only:** passes V1–V5 plus canon checks (monster variants,
  item affix rolls).
- **R2 — agent review:** Claude reviews for consistency and quality (side
  quests, settlement histories).
- **R3 — owner review:** anything touching canon, main story, or balance.

If a content class cannot be honestly assigned R0 or R1, generating thousands of
it is a plan to accumulate unreviewed debt, not a plan to make a game.

---

# K. Claude / Codex collaboration protocol

## K.1 Documentation architecture

Four kinds of document, each with a different lifetime. **Mixing them is what
creates unreadable documentation.**

| Kind | Lifetime | Mutability | Size cap | Location |
|---|---|---|---|---|
| **Rules** (how we work) | Permanent | Rarely edited | 200 lines | `AGENTS.md`, `CLAUDE.md`, `CODEX.md` |
| **Specs** (how a system works) | Long, versioned | Amended, versioned | 600 lines each | `docs/systems/`, `docs/architecture/` |
| **Decisions** (why we chose) | Permanent, immutable | Never edited; superseded | 150 lines each | `docs/decisions/` |
| **State** (what is true now) | Ephemeral | Overwritten | 1 page each | `docs/state/`, `docs/handoff/` |

The rule that keeps this working: **state files are overwritten, never appended.**
`CURRENT.md` describes now. It does not accumulate history. Git already stores
history perfectly, and no agent should ever have to read a chronological diary
to find out what is true today. This is the explicit anti-diary mechanism the
brief asked for.

### `AGENTS.md` (shared, hard cap 200 lines)
Contents: one-paragraph project description; the repo map; **the dependency
rules in summary**; build/test/validate commands; the naming conventions; the
"before you do X, do Y" list; where to find specs; how handoff works; the
non-negotiables. Nothing else. Specifically **not** game design.

### `CLAUDE.md` / `CODEX.md`
Each ≤ 60 lines. Role, ownership areas, what to escalate, what not to do,
pointer to `AGENTS.md` for everything shared. Duplicating shared rules into
both files guarantees they diverge — so they must not be duplicated.

### `docs/README.md`
The navigation map. An agent with zero context reads this file and knows where
to look for anything within 60 seconds. Treated as a product with a real user.

## K.2 Architectural Decision Records

`docs/decisions/ADR-NNNN-kebab-slug.md`, ≤ 150 lines, immutable once accepted:

```markdown
# ADR-0007: Count-timeline turn scheduling
Status: Accepted            (Proposed | Accepted | Superseded by ADR-NNNN)
Date: 2026-10-02
Deciders: Claude, Owner
Affects: SPEC-CBT-001, Game.Encounter

## Context
What forced a decision. Constraints. What we knew and did not know.

## Decision
What we chose, stated in one paragraph.

## Alternatives considered
What else, and why not — enough that nobody re-litigates it in a year.

## Consequences
Good and bad. Especially the bad. What this makes hard later.

## Enforcement
How CI or review prevents drift from this decision (if it can).
```

**An ADR is required for:** adding an op; changing a dependency rule; changing a
persisted schema; changing a core loop or resolution pipeline; adding a new
module; choosing a library; anything a future contributor would otherwise
reverse without knowing why.

**An ADR is not required for:** content, ordinary implementation, tests, or
anything reversible in an afternoon.

Never edit an accepted ADR. Supersede it. The record of a wrong turn is exactly
as valuable as the record of a right one.

## K.3 The handoff system

`docs/handoff/CLAUDE_TO_CODEX.md` and `CODEX_TO_CLAUDE.md` are **inboxes with a
hard cap of 10 open entries**. Not logs. Not diaries.

```markdown
## H-0042 · Implement the modifier pipeline
Status: open        (open | in-progress | blocked | done)
From: Claude → Codex        Opened: 2026-10-02

Spec:      SPEC-RUL-004 v2 §3 (docs/systems/stats.md)
Contract:  schema/tables/stats.json v1.2 — do not change without an ADR
Scope:     src/Game.Rules/Stats/*  and  tests/Game.Rules.Tests/Stats/*

Do:        layer-ordered modifier application; dirty-flag caching;
           deterministic tie-break by (layer, sourcePriority, sourceContentId)
Don't:     don't add stats to the enum — they are content-declared;
           don't cache across turn boundaries without benchmarking first
Done when: spec §3 examples pass as tests; 10k-modifier benchmark under 1 ms;
           order-independence property test green
Open Q:    should override (layer 500) beat clamp (600)? Claude to decide — blocking §3.4
```

**Protocol:**
1. The sender writes the entry, referencing a spec — a handoff entry never
   *contains* the design, it *points* at it. Design lives in specs; work items
   point at design.
2. The receiver sets `in-progress`, then `done` with a one-line result and the
   commit SHA.
3. Done entries are **deleted** in the next sweep. Git has them.
4. If open entries exceed 10, no new entries may be opened until the backlog
   clears. This is a real constraint, and it is the thing that prevents the
   handoff files from silently becoming the diaries the brief warned about.
5. Blocked entries with an `Open Q` are mirrored into
   `docs/state/DECISION_QUEUE.md` when they need the *owner*, not the other
   agent.

## K.4 Ownership map

Lives in `AGENTS.md`. Prevents both merge conflicts and architectural drift.

| Area | Primary | Secondary | Rule |
|---|---|---|---|
| `docs/architecture/`, `docs/systems/`, `docs/decisions/` | **Claude** | Codex may propose via handoff | Codex never edits a spec directly; it proposes an amendment |
| `schema/` | **Claude** | Codex implements against it | Schema change requires an ADR |
| `src/Game.Rules`, `Game.Encounter` | **Codex** | Claude reviews every PR | Claude specifies, Codex implements |
| `src/Game.World`, `Game.Application` | **Codex** | Claude reviews | — |
| `src/tools/` | **Codex** | — | Claude reviews the validator's rule set only |
| `shell/godot` | **Codex** | — | Claude reviews only for rule leakage |
| `content/core/tables/` (balance) | **Claude** | Codex may not change tuning | Balance is design, not implementation |
| `content/core/` (content rows) | Shared | — | Both, coordinated by area via handoff |
| `tests/` | **Codex** | Claude specifies required cases | — |
| `docs/state/` | Shared | — | Whoever lands a change updates `CURRENT.md` in the same commit |

## K.5 Review protocol

Claude reviews Codex's work against a fixed checklist, so review is consistent
rather than mood-dependent:

1. **Dependency direction** — any new reference crossing a layer boundary?
2. **Purity** — anything in L2 touching a clock, I/O, ambient randomness, or an
   engine type?
3. **Special-casing** — any comparison against a specific content ID in rules
   code? (Should be a tag.)
4. **Determinism** — unordered iteration in a simulation path? Unseeded random?
   Float where fixed-point is required?
5. **Save impact** — did a persisted structure change without a migration?
6. **Spec fidelity** — does the behavior match the spec, and does the spec
   reference in the code match reality?
7. **Op discipline** — was an op added without an ADR? Was a rule embedded in
   code that should have been content?
8. **Budgets** — file/type size, allocation in hot paths, op count.

Findings are classified `blocking` / `should-fix` / `note`. Claude does not
rewrite Codex's code during review; it files findings. If Claude finds itself
implementing, that is a signal the spec was inadequate — fix the spec.

---

# L. Git, worktree, and branch strategy

### Branches
```
main                    always builds, always validates, always green
claude/<topic>          specs, schemas, docs, ADRs, balance tables
codex/<topic>           implementation, tests, tools, shell
spike/<topic>           throwaway prototypes; never merged, deleted after learning
```

`main` is the only long-lived branch. No `develop`. A single developer with two
agents does not need a release train.

### Worktrees
```
C:\Users\mikan\gameproject            main  (integration, review, running the game)
C:\Users\mikan\gameproject-claude     claude/*  worktree
C:\Users\mikan\gameproject-codex      codex/*   worktree
```

```bash
git worktree add ../gameproject-claude -b claude/architecture
git worktree add ../gameproject-codex  -b codex/foundation
```

This lets both agents work simultaneously on one history without stepping on
each other's working tree. Combined with the ownership map, merge conflicts
should be rare rather than managed.

### Rules
1. **Small commits, one concern each.** A commit that touches a spec, an
   implementation, and content is three commits.
2. **Commit message format:** `area: imperative summary`
   Areas: `spec` `adr` `docs` `schema` `foundation` `content` `rules`
   `encounter` `world` `app` `shell` `tools` `test` `fix` `refactor` `build`
3. **Rebase before merging; keep history linear.** Bisect must stay usable —
   at this project's lifespan you will need it.
4. **Never two agents in one file in one session.** The ownership map exists to
   make this automatic.
5. **`docs/state/CURRENT.md` is updated in the same commit as the change it
   describes.** Not after. Not in a separate cleanup commit.
6. **Content-only commits are separate from code commits**, so content churn
   never obscures a code bisect.
7. **Tag every phase exit** (`phase-3-vertical-slice`) so you can always get
   back to a known-good scale point.

### CI gates on every push
```
build          all projects, warnings as errors
deps           assembly reference check against build/deps.allow
test           unit + property + golden + determinism
validate       content V1–V5 across all packs
savecompat     load the historical save corpus
docs           spec-reference check: every `// spec:` points at a real spec+version
budgets        file/type size caps, op count ceiling
```

Anything that can be enforced mechanically is enforced here, because — as noted
in C.22 — documentation does not constrain behavior and CI does.

---

# M. Testing and validation strategy

| Tier | What | Where | Gate |
|---|---|---|---|
| **T1 Unit** | Pure functions: formulas, modifier layers, selectors, expression VM | `Game.Rules.Tests` | Blocking |
| **T2 Property** | Invariants over generated inputs: HP never negative, modifier order-independence, power budget conservation, no unreachable qualification | `Game.Rules.Tests` | Blocking |
| **T3 Golden / snapshot** | Recorded battles replay to identical event logs; generator output matches snapshots | `Game.Golden` | Blocking |
| **T4 Determinism** | Same seed → byte-identical results, twice in a row and across processes | `Game.Golden` | Blocking |
| **T5 Content validation** | V1–V5 over every pack | `Tools.Validate` in CI | Blocking (V1–V3), report (V4–V5 soft) |
| **T6 Save compatibility** | Historical save corpus loads and plays | `Game.SaveCompat` | Blocking |
| **T7 Integration** | Headless scenarios: full dungeon run, quest chain, level-up path | `Game.World.Tests` | Blocking |
| **T8 Balance simulation** | 10k–1M simulated battles across builds/levels; win rates, TTK, ability usage | `Game.Balance` | **Report, with tripwires** |
| **T9 Shell smoke** | The game boots, loads a save, renders a battle | Godot headless | Blocking, minimal |

**Coverage targets:** `Game.Rules` ≥ 90%, `Game.Encounter` ≥ 80%,
`Game.World` ≥ 60%, `Game.Application` ≥ 50%, shell: none required. Coverage in
the shell is a waste of effort; coverage in Rules is the project's insurance
policy.

### The balance simulator deserves special emphasis
It is not a test — it is a **design instrument**, and it is the only honest
answer to A2/A11. It runs headless because the core is engine-free, which is
most of why the core is engine-free. It should produce, on demand:

- win rate by party build × encounter, across level bands
- time-to-kill distributions, and outliers by ability
- ability usage frequency (an ability used in <1% of simulated fights is either
  bad or misunderstood — both are worth knowing)
- damage share by element, weapon class, and skill track
- outlier detection: combinations exceeding a power band by >2σ
- progression pacing: expected level at each story gate

**Tripwires** (CI-visible, not build-failing): if any ability's DPS share moves
more than X% between builds, or any build's win rate crosses a threshold, the
report flags it. This is how you notice that a content batch broke balance
without a human playing 400 hours.

---

# M′. Performance considerations and budgets

A 2D game with modest visuals has enormous performance headroom. That headroom
will be spent by *simulation and content*, not by rendering — so the budgets
below are about the core, not the shell.

### Declared budgets (60 fps target, ~16.6 ms frame)

| Path | Budget | Notes |
|---|---|---|
| World simulation per frame | **≤ 2 ms** | Hard budget with a deferred work queue; it yields to frame rate always |
| Battle action resolution | **≤ 5 ms** | One player action, all effects, all reactions. Not per frame — per action |
| Exploration tick (entities, triggers, spatial) | **≤ 3 ms** | Live-tier entities only |
| Zone load | **≤ 500 ms** | Streaming/async where it exceeds this |
| Content pack load (startup) | **≤ 300 ms** | Indexed binary pack; lazily materialize rarely used defs |
| Save write | **≤ 200 ms** | Async, double-buffered; never blocks a frame |
| Headless battle (balance sim) | **≤ 1 ms** | The one that matters most: 100k sims must finish in ~2 minutes |

### Memory estimates at target scale
Definitions are shared and immutable, so they cost once:

```
10,000 items      × ~1.5 KB  ≈  15 MB
 5,000 abilities  × ~2.0 KB  ≈  10 MB
 2,000 monsters   × ~4.0 KB  ≈   8 MB
 tables, text, quests, zones ≈  40 MB
                              ─────────
 total content resident       ≈  75 MB
```

Trivial by modern standards. **Content volume is not a performance problem in
this architecture** — which is precisely why it is worth building this way.

### Where performance actually goes wrong
Ranked by likelihood, based on how systems like this fail:

1. **Expression evaluation in hot loops.** Compile expressions to a bytecode
   tree at pack-build time, never parse at runtime, and cache results that are
   invariant within a resolution step.
2. **Allocation churn during battle resolution.** Effect application must use
   pooled buffers and structs. No LINQ, no closures, no `params` arrays in the
   resolution pipeline. This is a spec-level constraint, not a later
   optimization.
3. **Stat recomputation.** Dirty-flag derived stats; never recompute the whole
   block per query. A naive implementation recomputes thousands of times per
   turn and nobody notices until Phase 5.
4. **Uniform world simulation.** Addressed by LOD (C.17). Without it, this is
   the number-one frame-rate killer at scale.
5. **Save serialization of dense world state.** Sparse deltas only; compaction
   pass on save. Target < 5 MB at 200 hours, measured from Phase 4 onward.
6. **Pathfinding and spatial queries** in crowded zones. Standard mitigations
   (grids, caching, budgeted requests); not novel, but must be budgeted.

### Policy
- **Measure before optimizing, and never optimize the core on intuition.** The
  headless harness makes real measurement cheap; there is no excuse for guessing.
- **Benchmarks are committed tests** (`Game.Balance`, BenchmarkDotNet) with
  regression thresholds, so a 3× slowdown is caught in the commit that caused it
  rather than six months later.
- **Determinism outranks speed in the core.** If a fast path is non-deterministic,
  it does not go in.
- **The shell may use every trick it likes** — it has no determinism or purity
  obligations. Optimize there freely.
- **Threading:** the core is single-threaded and deterministic by default.
  Parallelism is allowed only where it is provably order-independent (batch
  simulation across independent seeds, content compilation, asset loading).
  Parallel rule resolution is not on the table; it would trade the project's
  most valuable property for a speedup it does not need.

---

# N. Suggested implementation phases

Each phase has explicit exit criteria. **Do not begin a phase before the
previous phase's criteria are met** — this rule is the main defense against A12.

### Phase 0 — Decisions and scaffolding *(no gameplay)*
Decompose this proposal into `docs/`. Write ADR-0001 (Rules Core + Shell) and
ADR-0002 (engine/language). Create the repo skeleton, `deps.allow`, CI, the
`.editorconfig`/analyzer baseline, and `AGENTS.md`.
**Exit:** an agent with no context can read `docs/README.md` and correctly
answer "where does a new status effect go?"

### Phase 1 — Foundation and the content pipeline
`Game.Foundation` (IDs, fixed-point, RNG, tags), `Game.Content` (schemas,
packs, registries, expression VM), and the validator + compiler CLIs. About
**20 ops** to prove the model end to end.
**Exit:** a hand-written TOML item and ability compile, validate, load, and
evaluate their effect trees headlessly. Determinism tests pass.

### Phase 2 — Rules core and headless combat
Stats and modifiers, statuses, items, abilities, targeting, the resolution
pipeline, the turn scheduler, basic actor AI. Roughly **60 ops**.
**Exit:** a scripted battle runs headlessly from content, produces an event
log, replays byte-identically, and 10,000 simulated battles run in under a
minute. No graphics exist yet, and that is correct.

### Phase 3 — **PLAYABLE VERTICAL SLICE** *(the project's survival check)*
The Godot shell: one town, one dungeon, three party members, ~20 abilities,
~30 items, ~15 monsters, save/load, menus, a complete gameplay loop.
**Exit:** someone who is not you plays it for 30 minutes and wants to keep
playing. **If this milestone is not reached, no further systems should be
built** — every risk in section A is dwarfed by the risk of building systems
for a game that is not fun.

### Phase 4 — Depth systems, first pass
Progression tracks and qualifications, summoning v1 (contracts, one acquisition
method, persistent creatures), crafting v1, quests v1, generation grammars for
items, data mods.
**Exit:** 1,000 items and 200 abilities validate and load in budget; a
character can qualify for a specialization through play; a summon persists,
levels, and fights.

### Phase 5 — Scale and the world
World simulation LOD, factions, economy, procedural dungeons, the travel graph,
the balance simulator with tripwires, the Hint/Rumor discovery layer, more
summoning acquisition methods.
**Exit:** 5,000 items / 1,000 abilities / 500 monsters validate; the world sim
stays within its frame budget with 100 settlements; save size stays under
target.

### Phase 6 — The optional AI content layer
`IGenerationClient`, the generation job format, canon checks, the review gate
and its tooling, the first generated content batches at R0/R1.
**Exit:** a batch of 500 generated rumors and 200 item histories passes
validation and canon checks and ships baked; the game builds and plays with
generation entirely disabled.

### Phase 7+ — Depth passes
One flagship system at a time, each with a spec, an ADR, and a balance pass:
deep summoning, monster ecology, weapon evolution, guilds, and so on. Never two
at once.

---

# Appendix 1 — Naming conventions

| Thing | Convention | Example |
|---|---|---|
| C# types, methods, properties | `PascalCase` | `EffectResolver`, `ApplyStatus` |
| C# private fields | `_camelCase` | `_registry` |
| C# interfaces | `I` + PascalCase | `ITurnScheduler` |
| Content IDs | `namespace:category.name`, snake_case | `core:weapon.flame_tongue` |
| Tags | `category:value`, snake_case | `element:fire`, `kind:undead` |
| Ops | snake_case verb phrase | `apply_status`, `modify_reputation` |
| Events | past-tense PascalCase | `MonsterKilled`, `RegionEntered` |
| Commands | imperative PascalCase | `UseAbility`, `MoveParty` |
| World flags | dotted lowercase namespace | `world.roads.bandits_broken` |
| Text keys | `kind.namespace.category.name.field` | `item.core.weapon.longsword.name` |
| Content files | snake_case`.toml` | `flame_tongue.toml` |
| Doc files | kebab-case`.md` (state files ALLCAPS) | `status-effects.md`, `CURRENT.md` |
| Specs | `SPEC-<DOMAIN>-<NNN>` | `SPEC-CBT-001` |
| ADRs | `ADR-NNNN-kebab-slug.md` | `ADR-0007-count-timeline.md` |
| Handoff entries | `H-NNNN` | `H-0042` |
| Validation rules | `V<level>.<RULE_NAME>` | `V3.OP_ARITY` |
| Branches | `agent/topic` | `codex/modifier-pipeline` |
| Commits | `area: imperative summary` | `rules: add layer-ordered modifiers` |
| Phase tags | `phase-N-slug` | `phase-3-vertical-slice` |

**Vocabulary discipline:** `docs/architecture/GLOSSARY.md` holds exactly one
definition per term, and no synonyms are permitted. "Creature" and "monster" and
"entity" must not all mean the same thing in different files — that ambiguity is
how two agents quietly build two incompatible mental models of the same system.

---

# Appendix 2 — Content scale test

Testing the architecture against the brief's hypothetical scales.

| Scale | Verdict | Reasoning |
|---|---|---|
| **10,000 items** | ✅ Fine | ~1.5 KB per def ⇒ ~15 MB resident, shared and immutable. One indexed pack, loaded in ms. Balanceable *only* because of power budgets + cost tables; without those it would be unmanageable at 500 |
| **5,000 abilities** | ✅ Fine | ~350 cores × ranks × modifiers. Zero new code. Discoverability, not implementation, is the real constraint |
| **2,000 monsters** | ✅ Fine | Archetype + template + variants; behavior is data (utility profiles). Would be impossible with per-monster behavior code |
| **Hundreds of classes** | ✅ Fine | A class is a predicate + a grant package. Cost is content and *design attention*, not code |
| **Hundreds of summons** | ✅ Fine | Summons are actors + contracts, sharing all monster machinery |
| **Hundreds of towns** | ⚠️ Conditional | Fine for *data*; requires LOD simulation (C.17) and sparse world deltas. Uniform ticking would fail hard |
| **Thousands of quests** | ⚠️ Conditional | Fine structurally; requires automated reachability/softlock validation, otherwise guaranteed permanent softlocks |
| **Thousands of statuses** | ❌ Rejected as primitives | 60–120 primitives, thousands of *combinations*. See C.22 #1 |
| **Large procedural regions** | ✅ Fine | Hierarchical named seeds; regenerable, not saved; sparse deltas |
| **A 200-hour save** | ⚠️ Watch | Target < 5 MB. Risks: unbounded inventory, per-NPC memory, per-zone deltas. Needs explicit caps and periodic compaction; measure from Phase 4 |

**Where the architecture would actually strain first:** not item count or
monster count, but **quest/flag interdependency** and **NPC-scale world state**.
Those are the places to invest validation tooling early.

---

# Appendix 3 — Extensibility stress test

Could a future developer add these without an engine rewrite?

| System | Where it plugs in | Verdict |
|---|---|---|
| **Time magic** | New ops (`rewind_actor`, `shift_timeline`) + scheduler support for turn-position manipulation | ✅ Ops + one scheduler feature |
| **Monster fusion** | A recipe over creature instances (C.18) | ✅ Content only |
| **Weapon evolution** | A recipe over item instances + a history axis already in the item model | ✅ Content only |
| **Player-built towns** | New world-state entity kind + zone-delta authoring + a construction UI | ⚠️ Large but additive; no core changes. It is a *scope* problem, not an architecture problem |
| **Guilds** | Factions with membership + qualification predicates | ✅ Content only |
| **Politics** | Faction relations + world-sim rules + event triggers | ⚠️ Additive; belongs in `Game.World.Sim` |
| **Army combat** | A new encounter *mode*: alternate `ITurnScheduler` + aggregate actor components | ⚠️ Additive because Encounter never depended on World, and the scheduler was pluggable. Would have required a rewrite under most architectures |
| **Dimensional travel** | Travel-graph nodes across worlds; world state already keyed by region | ✅ Content + graph |
| **Procedural continents** | A generator at the region tier, above existing zone generators | ✅ Existing seed hierarchy |
| **Monster ecosystems** | World-sim rules over existing ecology tags (which is exactly why C.12 authors those tags now) | ✅ If tags exist. ❌ Expensive if retrofitted |
| **Family / lineage** | Creature instances + an inheritance recipe + persistent NPC relationships | ⚠️ Needs a persistent-relationship model; cheap if added by Phase 5, expensive later |
| **Divine contracts** | Summon contracts with different terms and cost models | ✅ Already modeled |

**The two that would hurt** are player-built towns and lineage — both because
they expand *persisted world state*, which is the hardest thing to change after
players have saves. If either is likely, say so now (question O2/O5): reserving
save-segment space and a relationship model in Phase 4 is nearly free, and
retrofitting them in Phase 8 is not.

---

# Appendix 4 — Preventing architecture decay

Ten mechanisms, in descending order of effectiveness. Note that the top four
are mechanical and the human ones come last — that ordering is deliberate.

1. **CI enforces every rule that can be enforced.** Dependency checks, size
   budgets, op-count ceilings, determinism tests, save-compat corpus, content
   validation, spec-reference checks. *Documentation does not constrain
   behavior; CI does.*
2. **The "no special case" check.** A CI grep fails the build on comparisons
   against specific content IDs inside `Game.Rules` and `Game.Encounter`. The
   fix is always "add a tag." This single check is what prevents the
   thousand-case switch statement the brief rightly fears.
3. **The op budget.** A declared ceiling per phase (P1: 25, P2: 70, P4: 140,
   P5: 220). Exceeding it requires an ADR that justifies the increase.
   **Op count is the project's primary complexity metric** — track it on every
   release like a build size.
4. **Spec-to-code linkage.** Code that implements a spec carries
   `// spec: SPEC-CBT-001 v3 §4.2`. CI verifies the spec and version exist.
   When a spec is revised, every stale reference surfaces immediately. This is
   what stops specs and code from silently diverging over years.
5. **Size caps, enforced.** `AGENTS.md` ≤ 200 lines. Specs ≤ 600. ADRs ≤ 150.
   `CURRENT.md` ≤ 1 page. Handoff ≤ 10 open entries. C# files ≤ 600 lines soft
   / 1,000 hard. Caps force decomposition at the moment it is cheap.
6. **The new-agent test, run for real each phase.** Start a fresh agent with no
   context and ask it to make a small correct change using only `docs/README.md`,
   `AGENTS.md`, and one spec. If it cannot, the documentation is broken —
   regardless of how complete it looks to someone who already knows the project.
   This is the single best measurement of documentation health, and it is
   cheap.
7. **Complexity metrics tracked per phase:** op count, special-case count,
   longest file, deepest inheritance (should stay ~1), circular-dependency count
   (must stay 0), content row count, average spec age, count of open
   `KNOWN_PROBLEMS`. Trends matter more than values.
8. **A deletion budget.** Every phase must delete something: a dead op, a
   superseded doc, an unused system, a resolved problem row. Projects that only
   add, rot.
9. **Quarterly architecture review** by Claude, producing ADRs and a refreshed
   `KNOWN_PROBLEMS.md`. Scheduled, not ad hoc.
10. **The escalation rule.** When Codex must add a special case to make
    something work, that is not a coding problem — it is a *specification*
    problem, and it goes back to Claude via handoff. Special cases are the
    visible symptom of a system that was modeled wrong, and treating them as
    normal implementation cost is how architectures die quietly.

---

# O. Decisions that genuinely require the project owner

I can make every technical call in this document. These are not technical.

**O1 — Target platforms.** PC-only? Steam Deck? Console later? Web ever?
*Why it matters:* this is the only thing that could overturn the Godot + C#
recommendation (C# has no web export path in Godot today).

**O2 — Party and control model.** A single protagonist? A fixed party? A
recruitable roster? Are summons party members, or a separate resource?
*Why it matters:* it determines the actor/party model in Phase 2, and it is
expensive to change afterward.

**O3 — Combat pacing target.** Roughly how long should a normal fight take —
30 seconds or 3 minutes? Do you want random encounters or visible ones?
*Why it matters:* it sets the scheduler choice, ability complexity ceiling, and
monster count per encounter. Everything downstream in combat depends on it.

**O4 — Build scarcity (the most consequential game-design question here).**
Can a character eventually master everything, or are there permanent,
irreversible choices? Is respec allowed?
*Why it matters:* per C.11, a fully generous classless system homogenizes
builds. Adding scarcity later feels like a betrayal to players; adding
generosity later is painless. **Decide before Phase 4.**

**O5 — Narrative spine.** Is there a strong authored main story, or is this
primarily a sandbox with regional arcs?
*Why it matters:* it determines how much content can be R0/R1 generated versus
authored, and it changes the quest architecture's emphasis.

**O6 — Art plan.** Who makes the sprites and tilesets? Commissioned artist,
purchased asset base, or you?
*Why it matters:* per C.22 #9, this is a real risk to the project's perceived
quality, and it has a long lead time. It should be resolved before Phase 3.

**O7 — Release intent.** Commercial, free, open source? Solo forever, or will
humans join?
*Why it matters:* it changes how formal the documentation and licensing need to
be, and whether asset licensing matters.

**O8 — Time budget.** Realistically, how many hours per week will you spend
reviewing and directing?
*Why it matters:* this affects phase sizing more than any technical decision in
this document. Two AI agents can produce far more code than one person can
meaningfully review, and unreviewed AI output is how a project becomes something
nobody understands.

**O9 — AI-generated content appetite.** Should generated content ship in the
main game, or as an optional "expanded world" layer the player enables?
*Why it matters:* the second option is far safer for quality and lets you be
much more generous with generation.

**O10 — Save and death model.** Multiple save slots? Permadeath or ironman
modes? Autosave frequency?
*Why it matters:* it constrains the save segment design in Phase 3.

**O11 — Multiplayer, ever?** Even co-op, even far in the future?
*Why it matters:* determinism currently comes free from the architecture. If
multiplayer is permanently off the table, some constraints could relax — though
I would keep them anyway for the testing benefits.

**O12 — Modding priority.** Headline feature, or a nice-to-have?
*Why it matters:* it decides whether Phase 4 includes the mod loader or defers
it to Phase 6.

**O13 — Core language, final call.** C# (recommended: iteration speed, one
language, agent-friendly) or Rust (more rigorous, slower, better performance
ceiling)?

**O14 — Does this proposal match the game you actually want?** The architecture
above is optimized for *systemic depth and content scale*. If what you actually
want most is a tightly authored 40-hour story JRPG with excellent combat, this
architecture is over-engineered and B1 would serve you better. Worth an honest
answer before Phase 0.

---

## Status: awaiting approval

No production code, no documentation tree, no schema, and no content has been
created. On approval — with whatever modifications you want — Phase 0 begins by
decomposing this document into `docs/`, writing ADR-0001 and ADR-0002, and
handing Codex the Phase 1 scaffolding brief.

