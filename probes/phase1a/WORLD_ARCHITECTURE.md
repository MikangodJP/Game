# World Engine Architecture — procedural overworld, smooth movement, command menu

**Status:** APPROVED — owner decisions recorded 2026-09-10. Implementation not started.
**Date:** 2026-09-09, revised 2026-09-10
**Scope:** procedural chunk terrain · mountain-forest biome · infinite exploration ·
smooth tile movement · chunk-safe encounters · TAB command menu · System → For Testing ·
test-item spawning. Nothing else.

---

## 0. Approved decisions

| # | Decision | Effect on this document |
|---|---|---|
| **D1** | Encounter density starts near **1/192 eligible tiles**, not 1/96. Use deterministic world-space **cell** placement (0 or 1 candidate per cell) rather than an independent per-tile roll, to avoid clustering. Keep configurable. | §7 rewritten |
| **D2** | Step duration **140 ms**, in one constant. Held movement chains directly into the next tile with **no artificial pause**. Sprite animation driven separately from logical movement. | §8 revised |
| **D3** | **No silently inert menu command.** Search / Interact is omitted from this implementation rather than shipped as a no-op. | §10 revised |
| **D4** | Legacy `StartingMap` is **fixture-only** — removed from the playable startup path. Finite interiors arrive later as an explicit finite-map abstraction, not by reusing it. | §10, §14 revised |
| **D5** | **Seam-freedom claim corrected.** World-coordinate pure sampling removes *base-terrain* seams and load-order dependence only. Multi-tile features carry their own obligation. | §3, §7, §12, §15 revised |
| **D6** | Real inventory approved. `EquipmentChoices` migrates off `PrototypeEquipment.Items`; starting gear is granted through the same pipeline; **no separate cheat-item storage path**. | §11 revised |
| **D7** | `GameMode.Menu` approved. **No redundant Field-specific overlay guards** — mode switching already provides the gating. | §10 confirmed |
| **D8** | Chunk/world model approved as proposed: pure terrain function, chunks as cache, 16×16, 3×3 resident, centralized `FloorDiv`/`Mod`, world-space player position, base terrain separated from `WorldState`, offline navigability audit. | §6, §7 confirmed |

---

## 1. Current architecture

Inspected the working tree (all of this is uncommitted on top of `43249e9`).
The project is a .NET 8 domain library plus a Godot 4.6.3 host, with a genuinely
clean engine-free boundary already in place.

```
Probe/  (net8.0, zero engine dependency)
  Phase1A.Rules        Stats.cs · Equipment.cs · Rules.cs      ops, RNG, effect evaluator
  Phase1A.Encounter    BattleState.cs                          battle-owned state, sealed EncounterResult
  Phase1A.Preparation  CharacterPreparation.cs                 PERSISTENT PLAYER: base stats, HP/MP, loadout
  Phase1A.World        Field.cs                                TilePosition, FieldMapDefinition, FieldState
  Phase1A.Application  GameState.cs                            owns Player + Field, encounter begin/complete

Visual/  (Godot host, 320×240, 16px tiles, nearest-neighbour integer scaling)
  Presentation/GameController.cs      GameMode { Field, Preparation, Battle, GameOver } + input routing
  Presentation/HarnessController.cs   ScreenMode sub-states for preparation/battle screens
  Presentation/BattleSession.cs       sole holder of the BattleState reference; value views out
  Presentation/Menu.cs                BattleMenu: nested frame stack, 3×3 root grid, 149 WIP leaves
  BattleScreen.cs                     Godot root node: single input entry point, _Process held-move, battle draw
  FieldScreen.cs                      child Node2D, field draw
  PixelArt.cs                         5×7 bitmap font, creature sprites
```

Tests: 48 core checks (40 field-related in `Tests/FieldTests.cs`), 33 field-loop
checks in `VisualTests/FieldLoopTests.cs`, plus a byte-exact golden battle log
(SHA256-pinned, `.gitattributes`-protected).

**What is already correct and must be preserved:**

- `BattleState` takes an immutable `ActorSeed` snapshot in, returns typed
  `VitalsChanged` deltas out. `Rules` never references `BattleState`.
- `CharacterPreparation` is the single authoritative player. `GameState`
  coordinates; `BattleSession` is the only holder of a battle reference.
- One Godot input entry point (`BattleScreen._UnhandledInput`), with mode-gated
  routing and `ClearMovement()` on every mode change.
- `FieldState.PlayerPosition` is a value on a domain object, not on a UI node.
- World/field data has no engine, combat, or input dependency.

This is a good foundation. The expansion is mostly *replacing one finite-map
assumption* and *adding three new subsystems*, not restructuring.

---

## 2. Architectural problems blocking this expansion

Only problems that directly block the five requested features.

| # | Problem | Evidence | Blocks |
|---|---|---|---|
| **P1** | Finite rectangle is baked into the terrain query. Bounds and terrain are the same test. | `Field.cs:18` — `IsWalkable(x,y) => x >= 0 && y >= 0 && x < Width && y < Height && Rows[y][x] == '.'` | Infinite world |
| **P2** | Terrain is a raw `char` with binary walkability. No terrain metadata table. | `Rows[y][x] == '.'`; renderer branches on `!IsWalkable` at `FieldScreen.cs:33` | Biome variety, centralized terrain rules (§14) |
| **P3** | Encounter identity is an **array index into a finite map definition**. | `FieldState.defeated` is `bool[]` sized `map.Encounters.Length`; `CompleteEncounter` scans `Map.Encounters[index]` | Chunk-safe encounters; defeat would not survive unload |
| **P4** | `FieldState` fuses three different lifetimes: static map data, player position, and world mutations. | `Field.cs:42-64` | Base-vs-mutable world split (§7) |
| **P5** | Movement is instantaneous; there is no render position. | `TryMove` assigns `PlayerPosition = next` directly (`Field.cs:73`) | Smooth movement |
| **P6** | No camera. Renderer iterates the entire map at a fixed pixel origin. | `FieldScreen.cs:27-31`, origin `(8, 32)` | Infinite world rendering |
| **P7** | No `Menu` game mode; `Preparation` is entered by Confirm directly from Field. | `GameController.cs:43-48` | TAB menu |
| **P8** | **No inventory exists at all.** Equipment options read the global prototype array directly. | `HarnessController.cs:24-25` reads `PrototypeEquipment.Items` | Test-item spawning through a normal pipeline (§21) |
| **P9** | Encounter content is hardcoded to one scenario. | `BattleSession` hardcodes `Read(1), Read(2)` and a 3-way `Name(id)` switch | Encounters inside chunks |

P8 is the one people will underestimate: "give a test item" has no destination
today. An inventory has to exist before the developer menu can be honest about
using the normal pipeline.

---

## 3. Approaches considered

### Option A — Pure terrain function + chunk cache *(recommended)*
`Terrain(seed, worldX, worldY)` is a **pure function**. A chunk is nothing but a
memoised rectangle of it. `ChunkManager` is a cache, not an authority.

- **Benefits:** **base-terrain** seams are impossible by construction — adjacent
  chunks sample the same function at the same world coordinates. No load-order
  dependence, so anti-pattern "generating terrain based on chunk load order"
  cannot occur. Unloading is free (pure eviction). Any coordinate is queryable
  without loading its chunk, which makes tests, navigability audits, and the
  encounter probe trivial. Smallest amount of new state in the project.
- **Drawbacks:** features larger than one tile (rivers, structures, clusters)
  need to be expressed as functions of a feature-origin hash rather than as a
  stateful pass, and **they do not inherit seam-freedom automatically** — see the
  feature placement rule in §7. Slightly more CPU per tile than a cached
  generation pass, mitigated by chunk memoisation.
- **Migration cost:** low. `FieldMapDefinition` moves behind an interface; the
  chunk cache is new but small.
- **Scalability:** good to excellent. Rivers/roads/landmarks remain feasible via
  domain-warp and feature-origin hashing; only very large multi-chunk structures
  would eventually want Option B's machinery.

### Option B — Chunk-as-entity with generation passes
Chunks are generated objects with lifecycle and multi-pass generation, possibly
consulting neighbours for cross-boundary features.

- **Benefits:** the natural home for procedural structures, towns, and caves
  later; a pass can reason about a whole chunk at once.
- **Drawbacks:** neighbour-consulting passes reintroduce load-order sensitivity
  and seam bugs — the exact class of failure §5 and §30 warn about. Requires
  careful "generate to stage N-1 before neighbour reads" discipline. Materially
  more state and more test surface.
- **Migration cost:** medium-high. **Scalability:** highest, but not needed yet.

### Option C — Sliding finite window
Keep `FieldMapDefinition` and regenerate its contents around the player.

- **Benefits:** smallest diff; renderer barely changes.
- **Drawbacks:** player coordinates stay window-local, which violates §9 and the
  "player position stored only as local chunk position" anti-pattern; world state
  keyed to window offsets; boundary handling becomes special-cased everywhere.
- **Verdict:** rejected. It looks cheap and creates exactly the trap this task
  exists to avoid.

### Recommendation: **Option A**

It is the simplest architecture that satisfies every hard requirement, and its
central invariant — *terrain is a pure function of (seed, x, y)* — mechanically
eliminates three of the listed failure modes rather than requiring discipline to
avoid them. Upgrade path to B is documented in §12 and is additive.

---

## 4. Proposed module structure

New and changed modules only. Battle, Rules, Stats, Equipment are untouched.

```
Probe/
  World/
    WorldCoords.cs      floor-division chunk math. THE single place negative coords are handled
    Terrain.cs          TerrainType enum + ONE TerrainDef metadata table
    WorldGenerator.cs   pure Terrain(seed,x,y); integer value noise; encounter probe
    ChunkManager.cs     chunk cache: load 3×3, LRU evict, single owner of chunk lifecycle
    WorldState.cs       sparse mutations + defeated-encounter set. The only saved world data
    IWorldSource.cs     TerrainAt(x,y) + optional bounds. ProceduralWorld now; FiniteMap later
    FieldPlayer.cs      logical position, move origin, facing, progress. Advance(elapsedMs)
    FieldState.cs       slimmed coordinator: world source + player + world state
    FiniteMap.cs        existing row-string map, retained as an IWorldSource test fixture
  Items.cs              ItemDefinition, ItemRegistry, Inventory, developer flag
  GameState.cs          MODIFIED: owns WorldState; wires inventory

Visual/
  Presentation/
    MenuTree.cs         EXTRACTED from Menu.cs: frame stack, cursor, columns, Back
    BattleMenu.cs       existing 3×3 battle tree, now a MenuTree instance
    FieldMenuController.cs   root/items/equipment/status/system/testing state
    GameController.cs   MODIFIED: + GameMode.Menu, + Advance(elapsed)
  FieldScreen.cs        MODIFIED: camera, chunk-window rendering, terrain table, render position
  MenuScreen.cs         NEW: black panels, white borders, cursor, bottom info window
  BattleScreen.cs       MODIFIED: route Menu mode, drive Advance. Battle drawing unchanged
```

---

## 5. State ownership table

| State | Owner | Persistent | Generated | Saved |
|---|---|---|---|---|
| World seed | `GameState` | yes | no | **yes** |
| Base terrain | `WorldGenerator` (pure fn) | no | yes | **no** — reproducible from seed |
| Loaded chunks | `ChunkManager` | no | yes | no — pure cache |
| Terrain metadata table | `Terrain` (static) | no | no | no — code |
| World modifications | `WorldState` | yes | no | **yes** (sparse) |
| Defeated encounters | `WorldState` | yes | no | **yes** (coord set) |
| Encounter existence | `WorldGenerator` (pure fn) | no | yes | no |
| Player world position | `FieldPlayer.Logical` | yes | no | **yes** |
| Player render position | `FieldPlayer` (derived) | no | no | no |
| Move origin / progress / facing | `FieldPlayer` | no | no | facing only |
| Player stats / HP / MP | `CharacterPreparation` | yes | no | **yes** |
| Equipment loadout | `CharacterPreparation` | yes | no | **yes** |
| Inventory | `CharacterPreparation.Inventory` | yes | no | **yes** |
| Item definitions | `ItemRegistry` (static) | no | no | no — code |
| Game mode | `GameController` | no | no | no |
| Menu cursor / stack | `FieldMenuController` | no | no | no |
| Battle turn state | `BattleState` | no | no | no |
| Held input | `BattleScreen` | no | no | no |

No duplicate sources of truth: position exists once (`FieldPlayer.Logical`),
player RPG state exists once (`CharacterPreparation`), terrain exists as a
function plus a sparse override.

---

## 6. World and chunk data model

**Chunk size: 16×16 tiles. Loaded set: 3×3 around the player. LRU cache: 32 chunks.**

Derived from the actual renderer, not chosen by convention: the viewport is
320×240 at 16 px per tile = **20×15 visible tiles**. With the camera centred, the
view reaches 10 tiles horizontally and 8 vertically from the player. A 16-wide
chunk therefore always covers the overhang into the neighbouring chunk, so 3×3
is sufficient at every position including chunk corners. 32×32 chunks would hold
9,216 tiles resident for no benefit and produce a larger generation hitch on
crossing. 3×3 × 256 = **2,304 resident tiles**; the LRU cache of 32 chunks caps
memory at ~8,192 tiles, which is negligible.

### Negative coordinates — designed explicitly

C# integer division truncates toward zero, which is the standard source of the
off-by-one at the origin. All conversion happens in exactly one place:

```csharp
// WorldCoords.cs — the ONLY place this math exists.
public const int ChunkSize = 16;

public static int FloorDiv(int value, int size) =>
    value >= 0 ? value / size : -(((-value) + size - 1) / size);

public static int Mod(int value, int size)
{
    var m = value % size;
    return m < 0 ? m + size : m;
}

public static ChunkPos ChunkOf(int worldX, int worldY) =>
    new(FloorDiv(worldX, ChunkSize), FloorDiv(worldY, ChunkSize));

public static (int lx, int ly) LocalOf(int worldX, int worldY) =>
    (Mod(worldX, ChunkSize), Mod(worldY, ChunkSize));
```

Worked check at the boundary: world `-1` → `FloorDiv(-1,16) = -(((1)+15)/16) = -1`,
`Mod(-1,16) = 15`. So world tile `-1` is chunk `-1`, local `15` — adjacent to
world tile `0` at chunk `0`, local `0`. Continuous across zero in all four
directions. The example from the brief holds: with size 32, world `(71,-18)` →
chunk `(2,-1)`, local `(7,14)`.

**Invariant to test:** for all `w` in `[-1000, 1000]`,
`ChunkOf(w)*ChunkSize + LocalOf(w) == w`.

---

## 7. Procedural generation strategy

### Determinism
Reuse the probe's proven primitive: FNV-1a over inputs, finalised with SplitMix64
(`Rules.cs` already contains this and it is covered by the golden test). Terrain
uses a **hash**, not a stream — no traversal order, no mutable state, no
`Math.random`, no clock.

```
Hash(seed, x, y, salt) -> uint
```

`salt` is a per-feature constant (`ELEVATION`, `FOREST`, `DETAIL`, `CORRIDOR`,
`ENCOUNTER`), so adding a feature later never shifts existing fields — the same
named-stream discipline the architecture already uses for RNG.

### Coherent noise
Integer value noise: hash the four lattice corners of a cell, interpolate with a
smoothstep on fixed-point weights, return `0..255`. Sampled at **world**
coordinates, never chunk-local — this is what makes seams impossible.

Three fields at different cell sizes:

| Field | Cell size | Role |
|---|---|---|
| `elevation` | 48 tiles | mountain mass vs lowland |
| `forest` | 20 tiles | woodland density |
| `detail` | 7 tiles | breaks up hard bands, scatters rock |
| `corridor` | 64 tiles | navigability backbone (below) |

### Terrain classification — 5 types, the minimum that reads as mountain forest

```
if corridor-band(x,y)              -> Grass          walkable   (carved clearing)
elevation >= 205                   -> Mountain       solid
elevation >= 178                   -> Rock           solid
elevation >= 150 && detail > 96    -> Grass          walkable   (highland meadow)
elevation >= 150                   -> Rock           solid      (scattered outcrop)
forest >= 190 && detail > 64       -> DenseForest    solid
forest >= 140                      -> Forest         walkable, encounter-capable
otherwise                          -> Grass          walkable
```

`Grass · Forest · DenseForest · Rock · Mountain`. Ordered first-match, so the
rules are readable and each is independently testable.

### Navigability — practical, not pathfinding

Two mechanisms, neither costing runtime pathfinding:

1. **Corridor carving.** A low-frequency `corridor` field; where
   `|corridor(x,y) - 128| < 10`, force the tile walkable. Because it is a pure
   function of world coordinates, this produces a connected web of clearings
   that crosses chunk boundaries seamlessly and cannot be interrupted by chunk
   loading. This is the primary guarantee.
2. **Deterministic spawn search.** Spawn is the first walkable tile found by a
   fixed outward spiral from `(0,0)` — never a hardcoded coordinate that might
   land inside a mountain.

Verified **offline, not at runtime**: a test flood-fills a 128×128 sample region
for several seeds and asserts that the largest connected walkable component
contains ≥ 85% of all walkable tiles, and that no walkable tile is an isolated
1-tile pocket surrounded by solids. Cheap to run in CI, zero runtime cost. This
is the honest answer to §4 — a guarantee measured in tests rather than claimed.

### Encounters in an infinite world — cell placement (D1)

An independent per-tile roll was rejected: Bernoulli placement clusters, and at
1/96 the 20×15 viewport would average ~3 candidates on screen before terrain
filtering. Instead, encounters are placed **at most one per world-space cell**,
which bounds local density by construction rather than by luck.

```
EncounterCellSize      = 8      // tiles per cell edge  — configurable
EncounterCellOccupancy = 3      // 1 in N cells carries a candidate — configurable

HasEncounter(seed, x, y):
    cell        = (FloorDiv(x, CellSize), FloorDiv(y, CellSize))
    if Hash(seed, cell.x, cell.y, ENCOUNTER_CELL) % CellOccupancy != 0: return false
    slot        = Hash(seed, cell.x, cell.y, ENCOUNTER_SLOT)
    chosenLocal = (slot % CellSize, (slot / CellSize) % CellSize)
    if (Mod(x, CellSize), Mod(y, CellSize)) != chosenLocal: return false
    return Terrain(seed, x, y).EncounterCapable
```

Upper bound: one candidate per `8 × 8 × 3 = 192` tiles, so **≤ ~1.5 candidates
per screen** before terrain filtering, and never two adjacent. Still a pure
O(1) function of world coordinates — no neighbour scan, no chunk state. Both
constants live in one place and are the tuning knobs for playtesting.

Note the effective rate is `1/192 × P(tile is encounter-capable)`, so it is a
ceiling rather than an exact rate; if playtesting wants encounters *denser*
relative to eligible terrain, lower `EncounterCellOccupancy` before shrinking
`EncounterCellSize`, since cell size is what guarantees spacing.

Identity is derived from coordinates, never from an array index:
`EncounterId = (x, y)`. `WorldState.Defeated` is a `HashSet<(int,int)>`.

```
EffectiveEncounter(x,y) = HasEncounter(x,y) && !WorldState.Defeated.Contains((x,y))
```

Chunk unload/reload therefore cannot resurrect anything, because defeat is
recorded in `WorldState` keyed by world coordinate, entirely outside the chunk
cache. This directly replaces P3's `bool[]`.

### Feature placement rule — the limit of the seam guarantee (D5)

**Correction to the original draft.** Pure world-coordinate sampling guarantees
seam-freedom and load-order independence for **single-tile base terrain only**.
It does *not* automatically extend to anything occupying more than one tile.

A multi-tile feature — structure, path, rock formation, lake, tree cluster, ruin —
that is placed by asking "does a feature start inside *this chunk*?" will be
truncated at chunk borders, because a feature whose origin lies in a neighbouring
chunk will be missing from this one. That is a real seam and it is not prevented
by anything in §7 above.

**Binding rule for every future multi-tile feature:**

> Placement and shape must derive from **world coordinates**, and a tile must
> determine its own contents by examining every feature origin whose influence
> radius could reach it — regardless of which chunk that origin falls in.

Concretely, the pattern that satisfies this:

```
TerrainAt(x, y):
    base = Classify(x, y)
    for each feature cell within (maxFeatureRadius / featureCellSize) of (x, y):
        origin = DeterministicOrigin(seed, featureCell)      // may be in another chunk
        if origin exists and Covers(origin, x, y):
            base = ApplyFeature(base, origin, x, y)
    return base
```

The neighbourhood scan is bounded by a declared `maxFeatureRadius`, so the query
stays O(1) with a constant factor, remains pure, and still never requires a
neighbouring *chunk* to have been generated — only that neighbouring feature
*cells* be sampled, which is free.

Any feature exceeding `maxFeatureRadius` (a town, a dungeon footprint) does not
belong in this generator and should be introduced as an explicit finite-map
region, per D4.

**No such features are implemented now.** The mountain-forest biome is
single-tile classification only. This rule exists so the first person to add one
does not silently reintroduce seams.

### Base world vs world state

```
EffectiveTerrain(x,y) = WorldState.Overrides.TryGetValue((x,y)) ?? Generate(seed,x,y)
```

`WorldState` holds only a sparse dictionary of overridden tiles and the defeated
set. Generated terrain is never stored persistently.

---

## 8. Movement model

```csharp
sealed class FieldPlayer {
    public TilePosition Logical { get; }      // authoritative
    public TilePosition Origin  { get; }      // tile the current step began from
    public Facing Facing { get; }
    public int ProgressMs { get; }            // 0..StepDurationMs
    public bool IsMoving => Origin != Logical;
}
```

**Logical position commits at the START of a step.** Collision, chunk-manager
update, and the encounter check all fire exactly once, at commit. The visual then
interpolates `Origin → Logical`. This is what keeps animation from touching game
logic 60×/second, and it means the world is never in an ambiguous half-state.

Render position is **derived, never stored as truth**:

```
renderX = Origin.X + (Logical.X - Origin.X) * ProgressMs / StepDurationMs
```

Time enters as data, exactly as the rest of the domain does — `Advance(int
elapsedMs)` is called by the presentation layer; the domain never reads a clock.
`Advance` mutates only `ProgressMs`, and at most once per call completes a step.

**Input buffering: none.** While `IsMoving`, direction input only updates the
held-direction set. On step completion, if a direction is still held, the next
step begins immediately. That yields §12's preferred behaviour — hold to walk
smoothly, release to stop after the current tile — with no queue.

**`StepDurationMs = 140`, in exactly one constant (D2)** — matching the existing
0.14 s repeat interval, so walking speed is unchanged from today while motion
becomes continuous. Tunable toward 160–180 ms later for a slower classic feel;
no other file may hardcode a movement duration.

**No artificial pause between tiles.** This requires deleting existing
behaviour, not just adding to it: `BattleScreen` currently applies an initial
`movementDelay = 0.18` on key-press before the 0.14 s repeat
(`BattleScreen.cs:104, 45`). That is keyboard-repeat pacing for discrete
teleport steps, and if retained it becomes a visible 40 ms stall between the
first and second tile. Under the new model the presentation layer no longer
schedules steps at all — it calls `Advance(elapsedMs)` every frame, and step
chaining happens inside `FieldPlayer`:

```
Advance(elapsedMs):
    ProgressMs += elapsedMs
    while ProgressMs >= StepDurationMs and IsMoving:
        ProgressMs -= StepDurationMs          // carry the remainder, never discard it
        CompleteStep()                        // Origin = Logical
        if a direction is still held and the next tile is walkable:
            BeginStep(heldDirection)          // chains with zero idle frames
        else:
            ProgressMs = 0; break
```

Carrying the remainder rather than resetting to zero is what keeps held
movement at a constant speed instead of quantising to the frame rate. The
`while` loop is bounded by the elapsed time; a long frame may complete at most
a few steps, and each still runs the full collision and encounter check, so
logic is never skipped.

**Sprite animation is driven separately (D2).** Animation must not read
`ProgressMs` as its clock, or a paused/blocked player would freeze mid-stride
and animation timing would be welded to movement timing. It gets its own
accumulator advanced by the same `elapsedMs`, and selects a frame from
`Facing` + `IsMoving` + its own animation time. No walking frames exist today
(`FieldScreen.DrawToken` draws a static silhouette), so this stays unimplemented
— the seam is `Facing`/`IsMoving`, both of which the model already carries.

Collision is evaluated logically, before the step begins:

```
input -> destination tile -> Terrain(dest).Walkable ?
   yes -> Origin = Logical; Logical = dest; ProgressMs = 0; check encounter
   no  -> no move; update Facing only
```

Smooth interpolation alone works with the current art; sprite animation is
deferred and specified separately below.

---

## 9. Camera

None exists today. Minimal, classic:

```
cameraPixelX = floor(renderX * 16) - 160 + 8
```

Floored to integer pixels — at 320×240 with nearest filtering, a subpixel camera
causes visible shimmer on every tile edge. The renderer draws only tiles in
`[cameraTile - 1, cameraTile + 21] × [cameraTile - 1, cameraTile + 16]`, i.e.
~22×17 tiles per frame regardless of world size. No cinematic behaviour.

---

## 10. Menu and input architecture

### Menu is a top-level `GameMode`, not a Field overlay

This is decided from the code, not theory. `GameController.Mode` already treats
`Preparation` as a peer of `Field`, and three existing mechanisms are gated on
`Mode == GameMode.Field`:

- `GameController.StepField` early-returns unless the mode is Field
  (`GameController.cs:27`)
- `BattleScreen.InField` gates the `_Process` held-movement loop
  (`BattleScreen.cs:16, 41`)
- `Route()` calls `ClearMovement()` on every mode change
  (`BattleScreen.cs:127`)

Adding `GameMode.Menu` therefore gives "player cannot move while the menu is
open", "held keys are dropped on open", and "no encounter can start" **from
existing code paths**. An overlay flag would require three new guards and would
mix menu state into movement code, which §17 and §30 forbid.

Field must still render behind the menu: `RefreshScreens()` already controls
`fieldScreen.Visible`, so Field stays visible in Menu mode and `MenuScreen` draws
over it.

### Input ownership

One entry point remains (`BattleScreen._UnhandledInput`), routing by mode:

| Mode | WASD / arrows | Confirm | Back | TAB |
|---|---|---|---|---|
| Field | tile movement | interact (later) | — | **open menu** |
| Menu | menu navigation | confirm entry | back / close at root | close menu |
| Preparation | existing | existing | existing | — |
| Battle | existing battle input | existing | existing | **ignored** |
| GameOver | — | — | — | ignored |

TAB is consumed only in Field and Menu. It is explicitly ignored in Battle, which
is the preventative for "menu opening during battle".

### Menu structure — keep Field and Battle navigation separate

The approved and implemented Field-menu slice deliberately does not extract the
existing `BattleMenu`. Battle navigation is heavily regression-tested and owns
combat-specific actions and layout conventions. `FieldMenuTree` declaratively
defines the control-centre hierarchy, while `FieldMenuController` owns its own
frame stack, clamped cursor and immutable read model:

```
COMMAND (field, 3×2 root; vertical children)
├── Items        current placeholder; future inventory list/use/inspect
├── Magic        Spells / Adjustment / Information placeholders
├── Equip        current information panel; future menu-integrated equipment
├── Status       live persistent-player stats
├── Actions      current contextual-action information panel
└── System       Settings / For Testing placeholders
```

**Search / Interact is omitted (D3).** There is no field interactable in the
codebase to wire it to — the only current field Confirm action opens the
equipment screen, and that becomes the `Equipment` entry. A "Search" command
that reports nothing is an apparently functional command with no effect, which
is worse than its absence. It returns when there is something to search: chests,
NPCs, or examinable terrain. The menu shape is not frozen, so adding a fourth
root entry later costs nothing.

`BattleMenu` keeps its existing 3×3 root and two-column child conventions
unchanged. Future inventory/test-item work extends only the Field tree and
controller. A later independently gated refactor may share low-level stateless
pixel primitives, but it must not couple navigation state or layouts.

Visual language: black panels, 1 px white borders, white pixel text, nested
windows, `>` cursor on the selected row, information window along the bottom.
Built from the existing `Window()`/`Text()` primitives — original drawing code,
no copied assets.

---

## 11. Item and test-item architecture

P8 first: there is no inventory today, so one is required before the developer
menu can honestly use "the normal pipeline".

```csharp
enum ItemKind { Equipment, Consumable }

sealed record ItemDefinition(
    string Id, string DisplayName, ItemKind Kind,
    EquipmentDefinition? Equipment = null,
    bool IsDeveloper = false);

static class ItemRegistry {           // one registry, normal + test items together
    static ItemDefinition Get(string id);
    static IEnumerable<ItemDefinition> All { get; }
}

sealed class Inventory {              // lives on CharacterPreparation
    void Add(string itemId, int count = 1);
    bool TryRemove(string itemId, int count = 1);
    IReadOnlyList<(ItemDefinition Def, int Count)> Entries { get; }   // stable order
}
```

The developer menu grants items through exactly one path:

```
For Testing -> Give Test Item -> select
   -> Inventory.Add(id)          <- the same call any future shop/chest/drop uses
   -> item appears in Items and, if equipment, in the Equipment screen
   -> equips and affects battle through the existing StatResolver path
```

Nothing bypasses the item system; there is no direct stat mutation anywhere in
the developer path.

**Required change to existing code:** `HarnessController.EquipmentChoices`
currently reads the global `PrototypeEquipment.Items` array
(`HarnessController.cs:24-25`). It must instead read equipment held in the
player's inventory, with the four prototype items granted at new-game time
through the same `Inventory.Add` call the developer menu and any future chest,
drop or shop uses (D6) — one grant path, no separate cheat-item storage. This is
the one existing behaviour change proposed, and it has test coverage that will
need updating.

**Test items** — clearly marked, deliberately unbalanced, `IsDeveloper = true`:
`[TEST] God Sword` (STR +999), `[TEST] God Armor` (DEF +999), `[TEST] Absurd
Charm` (MaxHP +9999), `[TEST] Full Restore` (consumable).

**Isolation:** `IsDeveloper` filters them out of any future shop/drop enumeration
by default, and the `For Testing` submenu is *not constructed at all* unless
developer mode is on (`#if DEBUG` plus a `--dev` command-line override). Removing
the feature later is deleting one subtree, with no effect on inventory
architecture.

---

## 12. Field → Battle compatibility

Unchanged in substance. The existing flow is preserved end to end:

```
step commits -> EffectiveEncounter(dest) -> GameState.BeginEncounter(id)
   -> CharacterPreparation.BeginManagedBattle  (resolved stats + current HP/MP snapshot)
   -> existing BattleState / BattleSession / battle UI, untouched
   -> Ended -> GameState.CompleteEncounter -> CompleteBattle applies VitalsChanged once
   -> Victory: WorldState.Defeated.Add((x,y));  Flee: player returns to contact origin
   -> Mode = Field, same world position
```

Only two changes: the encounter **id** becomes a coordinate pair rather than a
string index into a map array, and the defeat flag is recorded in `WorldState`
rather than `FieldState.defeated[]`. `Scenario.Setup(seed)` remains the encounter
content for now — varied encounter tables are out of scope (§41), but the seam is
`GameState.BeginEncounter`, which can later select a table from terrain and a
coordinate hash without touching battle.

**Upgrade path to Option B**, for the record: procedural structures later add a
`StructureOverlay(seed,x,y)` consulted before terrain classification — still
pure, and seam-free **only if it obeys the feature placement rule in §7**
(world-coordinate origins, neighbourhood scan bounded by `maxFeatureRadius`).
Structures larger than that radius belong in an explicit finite-map region (D4),
not in this generator; only those would require Option B's stateful passes.

---

## 13. Save implications

Not implemented now. What a save would contain:

**Saved:** world seed · player world position + facing · player base stats, HP,
MP · equipment loadout · inventory contents · `WorldState` (sparse tile overrides
+ defeated encounter coordinates) · game mode entry point.

**Not saved:** generated terrain (reproducible from seed) · loaded chunks (pure
cache) · render position and movement progress (transient; a save resolves to the
logical tile) · menu cursor state · battle state (saving mid-battle is out of
scope) · item definitions (code).

The single rule that makes this work: *anything derivable from
`(seed, coordinates)` is never serialised.* A 200-hour save stays proportional to
how much the player has **changed**, not how far they have **walked**.

---

## 14. File-by-file migration plan

| File | Action | Detail |
|---|---|---|
| `Probe/Rules.cs`, `Stats.cs`, `Equipment.cs`, `BattleState.cs`, `Scenario.cs`, `Program.cs` | **KEEP** | Untouched. Golden log and its byte-exact tests stay green by construction |
| `Probe/Field.cs` | **SPLIT** | `FieldMapDefinition` + `PrototypeField` → `World/FiniteMap.cs`, **fixture-only (D4)**: still an `IWorldSource` implementation so the interface is proven to have two, but removed from the startup path — `GameState` constructs `ProceduralWorld`, and no runtime code path reaches `StartingMap`. Do not retain legacy branching to keep it playable. `FieldState` → slimmed coordinator. `TilePosition` → `World/WorldCoords.cs` |
| `Probe/World/WorldCoords.cs` | **NEW** | `TilePosition`, `ChunkPos`, `FloorDiv`, `Mod`, `ChunkOf`, `LocalOf`. The only place negative-coordinate math exists |
| `Probe/World/Terrain.cs` | **NEW** | `TerrainType` + the single `TerrainDef` table (`Walkable`, `EncounterCapable`, render key). Fixes P2 |
| `Probe/World/WorldGenerator.cs` | **NEW** | `Hash`, value noise, `Terrain(seed,x,y)`, `HasEncounter`, spawn search. Pure, static, no state |
| `Probe/World/ChunkManager.cs` | **NEW** | Sole owner of chunk lifecycle: 3×3 resident, LRU 32, `EnsureAround(chunkPos)` |
| `Probe/World/WorldState.cs` | **NEW** | Sparse overrides + `HashSet<(int,int)> Defeated`. Fixes P3, P4 |
| `Probe/World/IWorldSource.cs` | **NEW** | `TerrainAt(x,y)`, `TryGetBounds(out …)`. Fixes P1 without making interiors impossible (§10) |
| `Probe/World/FieldPlayer.cs` | **NEW** | Logical/Origin/Facing/ProgressMs, `Advance(elapsedMs)`. Fixes P5 |
| `Probe/World/FieldState.cs` | **MODIFY** | Keeps `TryMove` semantics and the flee-retreat `contactOrigin`; delegates terrain to `IWorldSource`, position to `FieldPlayer`, defeat to `WorldState` |
| `Probe/Items.cs` | **NEW** | `ItemDefinition`, `ItemRegistry`, `Inventory`, developer flag. Fixes P8 |
| `Probe/CharacterPreparation.cs` | **MODIFY** | Add `Inventory` property. No change to HP/MP/equip/battle-lock semantics |
| `Probe/GameState.cs` | **MODIFY** | Own `WorldState`; encounter id becomes a coordinate; grant starting items on new game |
| `Visual/Presentation/Menu.cs` | **KEEP** | `BattleMenu` remains unchanged; do not extract or re-instantiate it for Field work |
| `Visual/Presentation/FieldMenuTree.cs`, `FieldMenuController.cs` | **EXTEND** | Keep the implemented separate Field hierarchy/read model; future inventory and developer commands enter here. No Search entry (D3) |
| `Visual/Presentation/GameController.cs` | **MODIFY** | Add `GameMode.Menu`; add `Advance(elapsedMs)`; TAB open/close; Equipment now entered from the menu |
| `Visual/Presentation/HarnessController.cs` | **MODIFY** | Equipment options sourced from inventory; battle sub-modes unchanged |
| `Visual/FieldScreen.cs` | **MODIFY** | Camera, windowed chunk rendering, terrain table lookup, interpolated player position |
| `Visual/MenuScreen.cs` | **EXTEND** | Keep the implemented drawing-only panels and renderer-local window/layout helpers; add future item panels without changing Battle rendering |
| `Visual/BattleScreen.cs` | **MODIFY** | Route Menu mode; `_Process` drives `Advance(delta)` instead of stepping tiles directly. All battle drawing unchanged |
| `Tests/FieldTests.cs` | **MODIFY** | Finite-map assertions retarget to `FiniteMap`; encounter-index assertions retarget to coordinate identity |
| `Tests/WorldTests.cs` | **NEW** | Determinism, seams, negative coords, navigability audit, encounter identity |
| `VisualTests/FieldLoopTests.cs` | **MODIFY** | Add menu-mode and interpolation checks; existing loop assertions preserved |

---

## 15. Failure modes and preventative decisions

| Failure mode | Preventative design decision |
|---|---|
| Negative-coordinate off-by-one at the origin | All conversion in `WorldCoords.FloorDiv`/`Mod`; round-trip property test over `[-1000,1000]`; no other file may do `/ ChunkSize` |
| Base-terrain chunk seams | Terrain is a pure function of **world** coordinates; chunks are caches only. Structurally impossible for single-tile classification, and a test asserts border-column equality between independently generated neighbours |
| **Multi-tile feature seams (D5)** | Not covered by the above. Any future structure/path/lake/cluster must place and shape itself from world coordinates and scan every feature cell within `maxFeatureRadius`, including cells in neighbouring chunks (§7). None exist today; the rule is recorded so the first one added does not reintroduce seams. A future feature must ship with a border-continuity test |
| Terrain depends on load order | Generator is static and stateless; nothing is passed between chunks. Test generates a 3×3 block in forward and reverse order and compares |
| Defeated enemy respawns after chunk reload | Defeat lives in `WorldState`, not in the chunk cache. Explicit test: defeat → force-evict chunk → reload → still defeated |
| Encounter duplication | `PendingEncounterId` guard already exists and is preserved; contact is only evaluated at step commit, never during interpolation |
| Logical/visual divergence | Render position is **derived** from `Origin`/`Logical`/`ProgressMs`, never stored independently. Test asserts render position equals logical at `ProgressMs = 0` and at completion |
| Player stuck mid-load | Chunk generation is synchronous and cheap (256 tiles of pure hashing); the 3×3 set is ensured at step commit, before the step is accepted |
| Player enclosed at spawn / trapped regions | Corridor carving + deterministic spawn search; offline flood-fill audit asserting ≥85% single-component walkables across several seeds |
| Movement logic running 60×/second | `Advance` touches only `ProgressMs`; collision, encounter checks, and chunk updates fire once per step commit |
| Field input running while menu open | Menu is a top-level mode; `StepField` and `_Process` are already mode-gated; `ClearMovement()` already fires on mode change |
| Menu opening during battle | TAB is mapped only in Field and Menu; ignored in Battle by the routing table |
| Duplicate items from repeated confirm | Only movement is added to `heldMovement`; Confirm never auto-repeats. `Add` is one explicit call per press |
| Test items contaminating production | `IsDeveloper` flag filters enumeration; the `For Testing` subtree is not constructed outside developer builds |
| Renderer iterating an infinite map | Renderer is bounded to the camera window (~22×17 tiles), independent of world size |

---

## 16. Testing strategy

Extends the existing runner style — no new framework.

**Determinism:** same seed + coords ⇒ identical terrain across two fresh
processes; different seeds ⇒ meaningfully different terrain (assert < 90% tile
agreement over a sample region, guarding against a generator that ignores the seed).

**Chunk math:** round-trip identity over `[-1000,1000]`; boundary cases at
`-1, 0, 15, 16, -16, -17`.

**Seams:** chunk `(0,0)` east column equals chunk `(1,0)` west column, sampled
directly from the generator; repeated for a negative-coordinate pair.

**Load order:** 3×3 block generated forward vs reverse ⇒ identical.

**Navigability:** flood-fill audit over 128×128 for ≥3 seeds; spawn is walkable
and not a 1-tile pocket.

**Movement:** blocked destination ⇒ no logical move, facing still updates; valid
destination ⇒ logical commits immediately and render interpolates; `Advance`
never moves more than one tile per call.

**Chunk transition:** walking across a chunk edge preserves terrain continuity
and updates the resident set without a logical position discontinuity.

**World state:** defeat → evict → reload → still defeated.

**Menu:** TAB opens; movement rejected while open; test item enters inventory via
`Inventory.Add`; equipping it changes `EffectiveStats`; close restores movement.

**Battle regression:** the existing 48 core + 33 field-loop checks and the
byte-exact golden log must remain green throughout.

---

## 17. Implementation order for Codex

Each stage ends green before the next begins.

| Stage | Work | Gate |
|---|---|---|
| **1** | `WorldCoords`, `Terrain` table, `WorldGenerator` — pure domain, no integration | `Tests/WorldTests.cs`: determinism, seams, negative coords, load order, navigability audit |
| **2** | `IWorldSource`, `ProceduralWorld`, `FiniteMap`; `ChunkManager` | Chunk residency and LRU tests; `FiniteMap` proves two implementations |
| **3** | `WorldState`; `FieldState` retargeted to world source + world state; encounter identity by coordinate | Existing field tests retargeted and green; defeat-survives-reload test |
| **4** | `FieldPlayer` + `Advance`; `GameController.Advance`; `BattleScreen._Process` drives time | Movement/interpolation tests; existing loop tests green |
| **5** | `FieldScreen` camera + windowed rendering + terrain table | Visual QA: walk in all four directions across chunk borders and across zero |
| **6** | `Items.cs`; inventory on `CharacterPreparation`; equipment sourced from inventory | Equipment tests updated; battle equipment lock unchanged |
| **7** | Extend the separate `FieldMenuTree` / `FieldMenuController` for inventory-backed commands; keep `BattleMenu` unchanged | **Battle 3×3 grid and absent status panel verified unchanged** |
| **8** | Integrate the existing `GameMode.Menu`, `MenuScreen`, and TAB routing with the procedural Field | Menu tests; movement blocked while open; TAB ignored in battle |
| **9** | `System → For Testing → Give Test Item`; developer gating | Test item enters inventory, equips, affects battle; hidden in non-dev build |

Stages 1–4 are pure domain and independently testable without the engine.
Any future extraction of low-level drawing primitives is a separate regression-gated
refactor; it is not part of Stage 7 and never requires shared navigation state.

---

## 18. Previously open questions — now resolved

| Question | Resolution |
|---|---|
| Encounter density | **D1** — cell placement, ≤1 per 192 tiles, two configurable constants |
| Step duration | **D2** — 140 ms in one constant; retune to 160–180 ms after playtesting if wanted |
| `Search / Interact` | **D3** — omitted; no inert commands |
| Legacy `StartingMap` | **D4** — fixture-only, off the startup path |

No open questions remain. The tuning values expected to move after playtesting
are `EncounterCellSize`, `EncounterCellOccupancy`, and `StepDurationMs`.

---

**Architecture approved 2026-09-10. Nothing implemented. Codex may begin Stage 1.**
