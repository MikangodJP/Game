# Field Control Menu Architecture — retro command windows, hierarchical navigation, TAB entry

**Status:** **FIRST VISUAL SLICE AND CHANTLESS CONFIGURATION IMPLEMENTED 2026-09-11.** Owner decisions are
recorded in §19; the macOS preflight in §20 completed before implementation.
**Date:** 2026-09-11
**Scope:** a keyboard-driven Field command menu that becomes the game's main control
centre · two-row root grid · contextual pop-out windows · hierarchical back stack ·
`GameMode` and input ownership · retro black/light-border pixel presentation.
**Out of scope for the original slice:** Items, Skills, Settings, For Testing,
and every other gameplay system the menu will eventually host. The approved
Chantless Magic V1 extension is recorded in §21.

**Relationship to existing documents.** `ARCHITECTURE_PROPOSAL.md` (v0.1) and
`ARCHITECTURE_v0.2.md` govern the production architecture. `WORLD_ARCHITECTURE.md`
governs the procedural overworld and is APPROVED-but-unimplemented. This document
covers only the Field menu and **deliberately deviates from one approved item in
`WORLD_ARCHITECTURE.md` §17 Stage 7** — see §12.4 and §18.1.

---

## 1. Current relevant repository architecture

Reconstructed by reading the working tree at `6c5a6eb` (two uncommitted local
edits, see §18.6). No prior session context was used.

### 1.1 Assemblies and the engine boundary

```
Probe/  (net8.0 — zero Godot dependency)
  Phase1A.Rules         Rules.cs · Stats.cs · Equipment.cs   ops, RNG, effect evaluator
  Phase1A.Encounter     BattleState.cs                       battle-local state, EncounterResult
  Phase1A.Preparation   CharacterPreparation.cs              PERSISTENT PLAYER (stats, HP/MP, loadout)
  Phase1A.World         Field.cs                             TilePosition, FieldMapDefinition, FieldState
  Phase1A.Application   GameState.cs                         owns Player + Field; encounter begin/complete

Visual/  (Godot 4.6.3 host)
  Presentation/         GameController · HarnessController · BattleSession · Menu    ← NO Godot using
  BattleScreen.cs       Node2D root: sole input entry point, battle + preparation drawing
  FieldScreen.cs        child Node2D: field drawing
  PixelArt.cs           static 5×7 bitmap font + creature sprites

VisualTests/  (net8.0)  references Presentation only — runs headless, no Godot runtime
Tests/        (net8.0)  58 core behaviour tests
```

**The single most important structural fact for this feature:**
`Visual/Presentation/*` contains no `using Godot`. `VisualTests` references it
directly and drives the entire UI through `UiInput` values with no engine present.
**All new menu logic must live in `Visual/Presentation/` so it is testable the
same way.** Only drawing belongs in a `Node2D`.

The mechanism is a glob in `VisualTests/Phase1A.VisualTests.csproj`:

```xml
<Compile Include="../Visual/Presentation/*.cs" Link="Presentation/%(Filename)%(Extension)" />
```

Two consequences for this feature, both important:

1. **New files in `Visual/Presentation/` are picked up automatically.**
   `FieldMenuTree.cs` and `FieldMenuController.cs` need no `.csproj` edit at all.
2. **A single `using Godot` in either file breaks the entire headless test suite**,
   because `VisualTests` does not reference Godot. This is a good failure — it is
   the boundary enforcing itself at compile time — but Codex must expect it.

### 1.1a Build settings that constrain the implementation

`Directory.Build.props` applies to every project in the probe:

```xml
<LangVersion>12.0</LangVersion>          <!-- no C# 13+ syntax -->
<Nullable>enable</Nullable>              <!-- nullable-clean or it fails -->
<TreatWarningsAsErrors>true</TreatWarningsAsErrors>   <!-- zero warnings -->
```

`Probe`, `Tests` and `VisualTests` are plain `Microsoft.NET.Sdk` projects with
**no `PackageReference` at all**, and `probes/phase1a/NuGet.Config` declares
`<packageSources><clear /></packageSources>` — no sources whatsoever. Only
`Visual/Visual.csproj` has an external dependency: the MSBuild SDK
`Godot.NET.Sdk/4.6.3`. This matters for §20.

### 1.2 What currently exists as a feature

| Feature | Where | State |
|---|---|---|
| Field exploration, 19×11 fixed map, tile movement, collision | `Field.cs`, `FieldScreen.cs` | Working |
| Field → Battle → Field loop with HP/MP/gear persistence | `GameState.cs`, `GameController.cs` | Working |
| Battle: Attack / Defend / Run / Battle-adjusted Fireball; 149 WIP leaves | `Menu.cs`, `HarnessController.cs` | Working |
| Equipment preparation (4 slots, 4 items, live stat preview) | `HarnessController.cs`, `BattleScreen.DrawPreparation` | Working |
| 7-stat model + equipment bonus resolution | `Stats.cs`, `Equipment.cs` | Working |
| Event-log debug inspector (F2/F3) | `HarnessController`, `DrawMachineLog` | Working |
| **Inventory** | — | **Does not exist.** `EquipmentChoices` reads the global `PrototypeEquipment.Items` array directly (`HarnessController.cs:27`) |
| **Field menu** | `FieldMenuController.cs`, `MenuScreen.cs` | Working first visual slice |
| Party, summons, skills, settings | Battle/Field menu taxonomies | WIP presentation only |

### 1.3 `GameMode` today

`GameController.cs:7`

```csharp
public enum GameMode { Field, Preparation, Battle, GameOver }
```

`GameController` owns `GameState State`, `GameMode Mode`, and a single
`HarnessController Harness` that is **replaced** (not mutated) on each transition.
Transitions are centralised in exactly two methods:

- `StepField(dx, dy)` — **line 27: `if (Mode != GameMode.Field) return false;`**
  then `Field.TryMove`; an encounter id in the result constructs a battle and
  switches to `GameMode.Battle`.
- `Handle(UiInput)` — a `switch (Mode)` with one case per mode.

`GameMode.Preparation` is precedent that already answers the design question this
feature poses: **a full-screen, input-owning, movement-blocking UI state is
already modelled as a top-level `GameMode`, not as a Field overlay.**

### 1.4 Input routing today

`BattleScreen._UnhandledInput` is the **single input entry point** for the whole
game. It normalises keyboard and gamepad into one enum:

`HarnessController.cs:8` — `enum UiInput { Up, Down, Left, Right, Confirm, Back, Debug, Restart }`

Current bindings:

| Physical | `UiInput` | Notes |
|---|---|---|
| ↑↓←→ / WASD / D-pad | Up/Down/Left/Right | In Field these are *also* registered as held movement |
| Enter / KpEnter / Space / pad A | Confirm | |
| **E** | Confirm | **only `when InField`** (`BattleScreen.cs:65`) |
| Escape / Backspace / pad B | Back | |
| F2 | Debug | |
| R / pad Start | Restart | |
| F3 | *(not a `UiInput`)* | inline handler, MachineLog mode only |
| **Tab** | **unbound** | free for this feature |

Four mechanisms make routing safe today, and all four matter to this design:

1. **`InField`** (`BattleScreen.cs:16`) = `standalone is null && game.Mode == GameMode.Field`.
   It gates held-movement registration (line 100), the `_Process` repeat loop
   (line 41), and the `E` binding (line 65).
2. **`_Process` movement** runs only `if (InField && heldMovement.Count > 0)`,
   repeating every 0.14 s after a 0.18 s initial delay.
3. **`Route`** (line 111) sends directional input to `StepField` when `InField`,
   otherwise to `game.Handle`, then — **line 127** —
   `if (game.Mode != previousMode) ClearMovement();`
4. **`ClearMovement`** also fires on `GetWindow().FocusExited`.

Echo events return early (line 59), so held keys never auto-repeat *menu*
selection — only field movement repeats, and only through `_Process`.

### 1.5 Field rendering

`FieldScreen : Node2D` is a child of `BattleScreen`, so it draws **on top of** its
parent. Visibility is set in `RefreshScreens` (line 136):

```csharp
fieldScreen.Visible = standalone is null && game.Mode is GameMode.Field or GameMode.GameOver;
```

and `BattleScreen._Draw` (line 143) early-returns for those same two modes so the
parent does not paint over the field. Rendering is immediate-mode `DrawRect` at
integer coordinates in a 320×240 space; the map is drawn at a fixed origin `(8, 32)`
with `TileSize = 16`.

### 1.6 Battle command structure

`Menu.cs` defines `BattleMenu`: a `List<Frame>` stack where each `Frame` holds a
label, an entry list and its own `Selected` index.

```csharp
public sealed record MenuEntry(string Label, MenuEntry[]? Children = null,
                               MenuAction Action = MenuAction.None, string? WipLabel = null);
public int Columns => IsRoot ? 3 : 2;          // 3×3 root grid, 2-column submenus
public int SelectedRow    => SelectedIndex / Columns;
public int SelectedColumn => SelectedIndex % Columns;
```

`Move(dx, dy)` computes a candidate `row * Columns + column` and **returns without
moving** if it is negative, past the column count, or past the entry count — i.e.
**clamping, never wrapping**. `Confirm()` pushes a child frame (auto-appending a
synthetic `Back` entry), or returns a `MenuChoice` whose `ChoiceKind` is one of
`Attack / Defend / Run / Wip`. `Back()` pops, restoring the parent's cursor.

**This is a genuinely good navigation model and this design reuses its *shape*
without touching the class.** See §12.

### 1.7 Existing reusable UI code

| Primitive | Location | Reusable as-is? |
|---|---|---|
| 5×7 bitmap font, `GlyphAdvance = 6`, integer scaling | `PixelArt.Text` — **already `public static`** | **Yes, unchanged.** `FieldScreen` and `BattleScreen` both already call it |
| Window chrome (fill + 1px border + corner pips) | `BattleScreen.Window`, **private**, hardcoded colours | **Needs extraction** (§12.2) |
| Cursor glyph `">"` + highlight bar `#263338` | inline in `DrawCommandGrid` / `PrepChoice` | Convention worth copying; code is not shareable |
| Grid renderer with row scrolling | `BattleScreen.DrawCommandGrid`, **private** | **No — do not share** (§12.3) |
| Colour palette | duplicated `private static readonly` fields in both `BattleScreen` and `FieldScreen` | Should be consolidated as part of §12.2 |

Palette in use: `Ink #070a12` · `Panel #101923` · `Border #536b71` ·
`Paper #d6d2ae` · `Muted #84958d` · `Gold #dfb963`.

### 1.8 Pixel-perfect facts (already correct — do not re-engineer)

- Logical resolution **320×240**, window 960×720 (3×), `stretch/mode="viewport"`,
  `stretch/scale_mode="integer"`.
- `TextureFilter = Nearest` set in `_Ready`; `snap_2d_transforms_to_pixel` and
  `snap_2d_vertices_to_pixel` both `true`.
- Every draw call is an integer-rect `DrawRect`. There are no textures, no fonts,
  no gradients, and no animation anywhere in the project.

**Consequence: the menu introduces no graphics pipeline work whatsoever.** Writing
integer `DrawRect` calls in the 320×240 space is automatically pixel-perfect. The
only rule is: *no floats, no half-pixels, no scaling other than the integer
`scale` parameter `PixelArt.Text` already accepts.*

### 1.9 Tests that exist

| Suite | Count | Relevant content |
|---|---|---|
| `Tests/` (core) | 65 | includes 7 Magic tests; run in Debug **and** Release |
| `VisualTests/` (presentation, headless) | 36 | includes traversal of all 149 WIP leaves and 5 field-loop tests |
| `Visual/HarnessQa.cs` (in-engine) | 264 checks | real key/pad events, PNG capture, Battle adjustment, Fireball and limited-palette checks |
| `Visual/FieldQa.cs` (in-engine) | 183 checks | full field/configuration/battle loop through the normal entry point |
| `Visual/MenuQa.cs` (in-engine) | 87 checks | real Field-menu input, renderer, bounds and palette checks |
| `golden/battle-20260909.log` | 73 events | byte-exact, SHA256-pinned, `.gitattributes`-protected |

---

## 2. Existing input / GameMode behaviour, restated as invariants

These are the properties the menu must not break. Each is enforced by a specific
line of existing code, not by convention.

| # | Invariant | Enforced by |
|---|---|---|
| **I1** | Field movement executes only in Field mode | `GameController.cs:27` |
| **I2** | Encounters can trigger only from `FieldState.TryMove`, only via `StepField` | `Field.cs:74-83`, `GameController.cs:28-34` |
| **I3** | Held movement repeats only in Field mode | `BattleScreen.cs:41` |
| **I4** | Any mode change clears stale held input | `BattleScreen.cs:127` |
| **I5** | Losing window focus clears held input | `BattleScreen.cs:31` |
| **I6** | Battle owns all input while `Mode == Battle`; `GameController` intercepts only the terminal Confirm/Back | `GameController.cs:53-61` |
| **I7** | Equipment cannot change during battle | `CharacterPreparation.cs:31` |
| **I8** | The rules core never learns that presentation exists | namespaces + `Probe/` having no Godot reference |

---

## 3. Architecture options

### Option A — Dedicated `FieldMenuController` + declarative node tree + `MenuScreen` view *(recommended)*

A new, Godot-free `FieldMenuController` in `Visual/Presentation/`, driven by a
static declarative `FieldMenuTree`. A new `MenuScreen : Node2D` renders a
read-model. `GameMode.Menu` is added. `BattleMenu` is **not touched**.

- **Structure.** `FieldMenuNode` (identity, label, children, panel kind, enabled)
  → `FieldMenuController` (frame stack, selection, grid navigation, active panel)
  → `MenuScreen` (draws windows from a read-model).
- **GameMode integration.** One new enum value; one new `case` in
  `GameController.Handle`; one new open-transition in the `Field` case. I1–I5 are
  inherited with **zero new guards**.
- **Input.** One new `UiInput.Menu` value; one new key binding. Provably inert in
  all existing modes (§7.3).
- **Visual flexibility.** High. Each node declares its panel kind; the renderer
  switches on ~4 kinds. Nothing forces a submenu to be a list.
- **Complexity.** Low–medium. ~3 new presentation files, ~1 new view file, ~1
  extracted primitive file.
- **Regression risk.** Low. Additive everywhere except the shared-primitive
  extraction (§12.2), which is a pure colour-preserving refactor.
- **Scalability.** Adding a command = one entry in the tree plus, if it needs a
  new shape, one panel kind. The root controller is never rewritten.

### Option B — Generalise `BattleMenu` into a shared `MenuTree` used by both menus

This is what `WORLD_ARCHITECTURE.md` §17 **Stage 7** currently prescribes
("`MenuTree` extraction; `BattleMenu` re-instantiated").

- **Structure.** Extract the frame stack and grid maths into a generic
  `MenuTree<TChoice>` or a shared non-generic core; `BattleMenu` and the field
  menu both become thin wrappers.
- **GameMode integration.** Identical to Option A.
- **Input.** Identical to Option A.
- **Visual flexibility.** Lower unless additionally parameterised — `BattleMenu`'s
  `Columns => IsRoot ? 3 : 2` and its synthetic `Back` entry are battle
  conventions that the field menu does not want verbatim.
- **Complexity.** Medium. The shared type must carry battle's `MenuAction` /
  `ChoiceKind` or become generic; either way battle concepts and field concepts
  meet in one type.
- **Regression risk.** **Highest of the three.** `BattleMenu` is covered by the
  149-leaf traversal test, several presentation tests, and 213 in-engine QA checks
  including screenshot and palette verification. `WORLD_ARCHITECTURE.md` itself
  rates this "the highest-regression-risk step" and demands a standalone commit.
- **Scalability.** Good, but it buys a shared *stack + modulo arithmetic* — about
  40 lines — at the cost of coupling two menus with different responsibilities.

**Rejected for now.** The duplicated logic is small, the coupling is real, and the
risk lands on the most heavily verified code in the repository. The rule of three
applies: if a third menu appears, extract then, with both call sites already
stable. This is an explicit, owner-visible deviation from an approved plan (§18.1).

### Option C — Screen-per-menu with explicit transitions

Each submenu is its own controller + screen; navigation is a set of transitions.

- **Visual flexibility.** Highest — every screen is bespoke.
- **Complexity / scalability.** Poor, and the repository already contains the
  proof. `HarnessController` is exactly this pattern: **one 187-line class with a
  9-value `ScreenMode` enum and a `switch` that fuses preparation, equipment,
  targeting, messages, WIP, ended and debug-log states.** It works at its current
  size and is visibly at its limit. A control centre with Items, Magic, Skills,
  Equipment, Status, Summons, Tactics and System would reproduce that shape at
  five times the size — precisely the "giant conditional tree in one screen class"
  the brief forbids.
- **Regression risk.** Low initially, high cumulatively.

**Rejected.**

---

## 4. Recommended architecture

**Option A.** Concretely:

```
Visual/Presentation/           (no Godot — testable headless)
  FieldMenuTree.cs             FieldMenuNode, MenuPanelKind, FieldMenuCatalog.Root
  FieldMenuController.cs       frame stack, selection, grid nav, panel read-model
  GameController.cs  (edit)    GameMode.Menu + routing; owns one FieldMenuController

Visual/                        (Godot — drawing only)
  MenuScreen.cs      (new)     Node2D; draws the read-model with local window/layout helpers
  BattleScreen.cs    (edit)    Tab binding and Menu draw/visibility gating
  FieldScreen.cs     (edit)    suppress the movement hint line while the menu is open
```

Dependency direction: `MenuScreen → FieldMenuController → FieldMenuTree`.
Nothing in `Presentation/` references Godot; nothing in `Probe/` learns the menu
exists. The optional shared-primitive extraction was deliberately skipped in
this slice to leave Battle rendering untouched.

---

## 5. Menu state model

### 5.1 Definition layer — what the menu *is*

```
FieldMenuNode
  Id          stable string identity, e.g. "menu.magic.spells"
  Label       display text
  Children    FieldMenuNode[]?   — present ⇒ this node opens a child command window
  Panel       MenuPanelKind      — what a leaf shows when confirmed
  Enabled     bool               — false ⇒ drawn dim, never selectable-confirmable
```

`MenuPanelKind` for the first slice is deliberately tiny:

```
CommandList     a child window of further nodes (the default when Children exist)
Info            a titled window of read-only lines
StatusSheet     a two-column label/value window fed by the live player
Placeholder     "<SYSTEM> IS NOT IMPLEMENTED YET."
```

Adding a fifth kind later (settings values, inventory grid) is one enum value plus
one `case` in the renderer. **This is not a component framework and must not
become one.**

### 5.2 Runtime layer — where the player *is*

```
FieldMenuController
  Frames        stack of (Node, Selected)   — depth 1 = root
  IsRoot        Frames.Count == 1
  Columns       Frames.Count == 1 ? 3 : 1   — root is a grid; submenus are vertical lists
  CurrentEntries / SelectedIndex / SelectedRow / SelectedColumn
  ActivePanel   MenuPanelView?              — set when a leaf is confirmed
  Breadcrumb    "COMMAND" at root, else the parent chain

  Open()    reset to root, selection 0, no panel
  Move(dx, dy)
  Confirm() → push child frame | open panel | no-op if disabled
  Back()    → close panel | pop frame | request close at root
  Close()
```

`Open()` **resets to root every time.** Classic RPG behaviour, and it removes an
entire class of stale-state bugs. This mirrors `BattleMenu.Reset()`.

### 5.3 Read-model layer — what the renderer *sees*

`MenuScreen` receives value types only and never mutates anything:

```
MenuWindowView   Title, Entries[], SelectedIndex, Columns, Anchor
MenuPanelView    Kind, Title, Lines[]  (or Rows[] of (Label, Value) for StatusSheet)
```

This is the same discipline `BattleSession` already applies to battle state
(`ActorView` immutable projections). **No gameplay state enters pixel-drawing code.**

---

## 6. Root menu structure

### 6.1 Evaluating the proposed root against the actual repository

The brief proposes *Items · Magic · Equipment · Status · Actions · System*. Five of
those six are well supported by what the repository already contains or has
committed to. One is not.

| Command | Evidence of plausible future responsibility | Verdict |
|---|---|---|
| **Items** | `WORLD_ARCHITECTURE.md` **D6** approves a real inventory; §17 Stage 6 builds it; battle root already has an `ITEMS` category with 9 leaves | **Keep** |
| **Magic** | Battle has a complete Magic taxonomy; Field exposes only Spells and Information in this slice | **Keep** |
| **Equipment** | Fully implemented today (`EquipmentSlot`, `EquipmentLoadout`, preparation screen) | **Keep** |
| **Status** | 7-stat model live via `CharacterPreparation.EffectiveStats` | **Keep** |
| **System** | `WORLD_ARCHITECTURE.md` §17 Stage 9 — `System → For Testing → Give Test Item` | **Keep** |
| **Actions** | No *current* repository evidence — the only field actions ever specified were Search / Interact, and D3 deleted those. **Owner decision Q1 (2026-09-11): the menu is intended to become the game's general control centre and contextual/specific actions are explicitly wanted.** Future responsibility is therefore owner-declared intent, which outranks my repository inference (R9) | **Keep** |

> **Resolved — Q1.** I originally recommended substituting `SKILLS` because
> `ACTIONS` had no content the repository could justify. The owner has stated the
> intent directly, which is the authority R9 assigns them on semantics. `ACTIONS`
> is in. **D3 is still satisfied** because it forbids *silently inert* commands,
> not unimplemented ones: `ACTIONS` shows a visible information panel reading
> **"No contextual actions available."** — a truthful statement about the current
> game state, not a dead menu entry.
>
> `SKILLS` remains a legitimate category and moves to the expansion list in §6.3.

### 6.2 Approved root — 6 commands, 3 columns × 2 rows

```
┌──────────────────────────────────────┐
│ COMMAND                              │
├──────────────────────────────────────┤
│ > ITEMS      MAGIC       EQUIP       │
│   STATUS     ACTIONS     SYSTEM      │
└──────────────────────────────────────┘
```

Logical indices:

```
[0] ITEMS    [1] MAGIC    [2] EQUIP
[3] STATUS   [4] ACTIONS  [5] SYSTEM
```

Widest label is `ACTIONS` at 7 characters = 41 px, which still fits the 48 px
column pitch specified in §10.2 with 7 px to spare. No geometry change.

### 6.3 Pre-approved expansion path

`SKILLS`, `SUMMONS` and `TACTICS` all have strong evidence. `Menu.cs:110-118`
already defines `WEAPON SKILLS`, `MARTIAL SKILLS`, `CLASS SKILLS`,
`RACIAL / SPECIES SKILLS`, `MONSTER SKILLS`, `PASSIVE SKILLS` and `UNIQUE SKILLS`,
of which the passive, class and racial groups are character-sheet concepts that
belong in an out-of-battle control centre. **R8** makes summons a
first-class non-party resource, `Menu.cs:108` already has a `SUMMON MANAGEMENT`
category (Active Summons, Summon Capacity, Contracts, Formation, Dismiss), and
`Menu.cs:130` has a `TACTICS` category (Party Formation, Target Priority, Ally
Behavior, Auto Battle). None of the three has anything to manage until skills,
a party and a summon budget exist.

**Growing to 8 commands is a 4×2 grid and requires changing exactly two things:**
the entry list in `FieldMenuCatalog` and the `Columns` constant. The two-row
guarantee is preserved. Nothing else in the controller or renderer changes. This
is the scalability test the architecture must pass, and it passes.

At 4 columns the pitch drops from 48 px to 38 px in a 168 px window, which still
holds a 7-character label (41 px) only if the window widens to 184 px — a one-line
change in `MenuLayout.Root()`. Noted now so the expansion is not a surprise.

---

## 7. Input ownership model

### 7.1 The ownership table

| Mode | ↑↓←→ / WASD | Enter / Space / A | Esc / Backspace / B | **Tab** | E | R / Start | F2 / F3 |
|---|---|---|---|---|---|---|---|
| **Field** | move (immediate + held repeat) | open Preparation | — | **open Menu** | open Preparation | — | — |
| **Menu** | **move selection** | **confirm** | **back / close at root** | **close** | — | — | — |
| Preparation | list navigation | select | back / return to field | ignored | — | — | — |
| Battle | menu / target navigation | confirm / page text | back / cancel | ignored | — | Restart *(probe only)* | event log |
| GameOver | — | — | — | ignored | — | new game | — |

`E` is deliberately **not** a menu confirm: its binding is guarded by
`when InField` (`BattleScreen.cs:65`), so it becomes inert the instant the mode
changes to `Menu`. No work required.

### 7.2 The one input change

Add a value to the existing enum (`HarnessController.cs:8`):

```csharp
public enum UiInput { Up, Down, Left, Right, Confirm, Back, Debug, Restart, Menu }
```

Bind it in `BattleScreen._UnhandledInput`:

```
Key.Tab                → UiInput.Menu
JoyButton.Back         → UiInput.Menu      (Select; Start stays Restart)
```

`Tab` must not register as held movement — it is not a direction, so the existing
`if (InField && selected is Up or Down or Left or Right)` guard already excludes it.

### 7.3 Why `UiInput.Menu` is provably inert in every existing mode

This was verified by reading every consumer of `UiInput`:

- `GameController.Handle` — `Field` will gain an explicit `Menu` branch;
  `Preparation`, `Battle` and `GameOver` compare against specific values
  (`Confirm`, `Back`, `Restart`) and fall through.
- `HarnessController.Handle` — `IsPreparing` computes `delta` via
  `input switch { Up => -1, Down => 1, _ => 0 }` → `0`, then tests only `Back` and
  `Confirm` → returns unchanged.
- Every `ScreenMode` branch (`Wip`, `Messages`, `Ended`, `Targets`, `Menu`) is a
  chain of equality tests against specific values → no match → no-op.
- `MachineLog` uses `input switch { ... _ => 0 }` → offset unchanged.

**No defensive guard is needed anywhere.** A test will assert this rather than
trusting the reading (§15, T10).

### 7.4 Held-input hand-off — the mechanism that makes this safe

Because opening the menu changes `game.Mode`, `Route`'s existing line 127
(`if (game.Mode != previousMode) ClearMovement();`) fires **in both directions**:

```
Player holds →, presses Tab
  → Route(Menu) → game.Handle → Mode: Field → Menu
  → mode changed → ClearMovement() → heldMovement emptied
  → _Process now returns early because InField is false
  → physical → still held, but produces nothing; its release RemoveAll is a harmless no-op
  → arrow presses now route to game.Handle → FieldMenuController.Move
```

Closing reverses it identically. **This behaviour is free with `GameMode.Menu` and
must be hand-built with an overlay** — see §8.

---

## 8. `GameMode.Menu` vs Field overlay — decided on the code, not the prior plan

`WORLD_ARCHITECTURE.md` **D7** already approved `GameMode.Menu`. The brief
correctly asks me not to assume that. I re-derived it from the working tree.

### Option A — `GameMode.Menu` (top-level mode)

Every behavioural requirement is satisfied by code that already exists:

| Requirement | Satisfied by | New code |
|---|---|---|
| Player cannot move | `StepField` line 27 `if (Mode != GameMode.Field) return false;` | none |
| Held movement stops repeating | `_Process` line 41 `if (!InField ...) return;` | none |
| Encounters cannot trigger | I2 — the only trigger path is inside `TryMove`, reachable only from `StepField` | none |
| Field input does not continue behind the menu | `Route` line 117 sends directions to `game.Handle`, not `StepField`, once `InField` is false | none |
| Stale held input does not leak | `Route` line 127 `ClearMovement()` on mode change | none |
| `E` does not act as a menu confirm | line 65 `when InField` | none |

Cost: one enum value, one `case`, and two display lines (keep `fieldScreen`
visible in `Menu` mode; extend `_Draw`'s early-return to include `Menu`).

### Option B — Field overlay / substate (e.g. a `MenuState?` on `GameController`)

`game.Mode` stays `Field`, therefore `InField` stays `true`, therefore **every
mechanism above silently continues to run.** Required new guards:

1. `StepField` — block when the overlay is open.
2. `_Process` — block held repeat when the overlay is open.
3. `_UnhandledInput` line 100 — stop registering arrows as held movement, otherwise
   an arrow key both moves the cursor *and* queues a field step.
4. `Route` line 117 — stop sending directions to `StepField`.
5. `Route` line 127 — add an explicit `ClearMovement()` on open *and* close, since
   no mode change occurs.
6. Line 65 — suppress the `E`-as-Confirm binding.

Six guards, each one a place where a future contributor can forget the overlay
exists. This is precisely what `WORLD_ARCHITECTURE.md` **D7** ruled out:
*"No redundant Field-specific overlay guards — mode switching already provides the
gating."*

### Recommendation

**`GameMode.Menu`, confirmed.** The overlay is not merely less tidy; it requires
re-implementing five existing invariants by hand. The `Preparation` mode is
existing precedent for the exact same shape of UI state.

The one genuine trade-off: `Menu` must keep the field **visible** underneath,
which `Preparation` does not. That is two lines (`RefreshScreens` visibility and
the `_Draw` early-return), not six guards.

---

## 9. Two-row grid navigation

### 9.1 Model

Logical positions only — `index = row * Columns + column`. **No navigation
decision ever reads a pixel coordinate.** Identical in shape to
`BattleMenu.Move`, which is the established convention.

```
Left  : column - 1
Right : column + 1
Up    : row - 1        (column preserved)
Down  : row + 1        (column preserved)

reject if column < 0 || column >= Columns
reject if row < 0
reject if index >= Entries.Count        ← handles a short final row
otherwise commit
```

### 9.2 Edges clamp; they do not wrap — recommended

`README.md` states the existing convention outright: *"Movement stops at the edge
and at missing cells in an incomplete final row… there is no wrap or configurable
navigation policy."* `BattleMenu.Move` implements it by returning early.

Two menus with opposite edge behaviour would be a genuine usability defect.
**Recommendation: clamp, matching battle.** A wrap policy, if ever wanted, is a
project-wide decision to be made once — not introduced by this feature.

### 9.3 Worked examples (3 columns, 6 entries)

| From | Input | To | Why |
|---|---|---|---|
| `[0] ITEMS` | Down | `[3] STATUS` | row 0→1, column 0 preserved |
| `[0] ITEMS` | Up | `[0]` | row −1 rejected |
| `[0] ITEMS` | Left | `[0]` | column −1 rejected |
| `[2] EQUIP` | Right | `[2]` | column 3 ≥ Columns rejected |
| `[5] SYSTEM` | Down | `[5]` | index 8 ≥ 6 rejected |
| `[4] SKILLS` | Up | `[1] MAGIC` | row 1→0, column 1 preserved |

With a future 4×2 root of 7 entries, `[3]` + Down → index 7 ≥ 7 → rejected. The
short-final-row rule needs no special case.

### 9.4 Submenus are vertical lists, not grids

`Columns = 1` below the root. Rationale: the two-row grid is a *root* requirement
driven by having a small fixed command set. A spell list or a settings list is
naturally vertical, and a 1-column grid makes Up/Down the whole navigation model
with no dead cells. `Columns` is per-frame, so a future submenu that genuinely
wants two columns sets it without touching the navigation code.

---

## 10. Contextual / pop-out window system

### 10.1 Anchoring policy — one function, not per-submenu coordinates

All placement goes through a single static helper. No submenu ever hardcodes a
rect.

```
MenuLayout.Root()                                  → fixed rect
MenuLayout.Child(parentRect, selectedColumnX, contentSize) → rect
```

`Child` policy, in order:

1. **Preferred:** left edge aligned to the selected column's x minus 4 px;
   top edge at `parent.Bottom + 4`. This produces the "the window opens *from the
   command you chose*" feel rather than a fixed dropdown.
2. **Horizontal clamp:** into `[8, 312 − width]`.
3. **Vertical flip:** if `y + height > 232`, place above the parent instead
   (`parent.Top − height − 4`), then clamp to `≥ 8`.
4. **Depth ≥ 2:** same rule against the immediate parent window, with a +8 px
   horizontal cascade so the parent's border stays visible.

That is the entire layout engine: one function, four rules, no constraint solver.

### 10.2 Concrete geometry (320×240)

Font metrics are fixed: glyph 5×7, advance 6 px, so an *n*-character label is
`6n − 1` px wide.

**Root window — rect (8, 8, 160, 54)**

| Element | Coordinates |
|---|---|
| Title `COMMAND` | (14, 14), Gold |
| Separator rule | `Fill(10, 26, 156, 1, border)` |
| Column origins | x = 14, 62, 110 (48 px pitch) |
| Row baselines | y = 32 (row 0), y = 44 (row 1) |
| Cursor `>` | at the column origin |
| Label | column origin + 6 (root); +8 in vertical child windows |

The widest label, `ACTIONS` (7 chars = 41 px), ends at x = 108 in column 1,
leaving a black pixel at x = 109 before column 2's cursor at x = 110. `SYSTEM`
ends at x = 150, inside the 168 px right edge.

**Child windows** are content-sized:
`width = max(96, 16 + 8 + longestLabel)`, `height = 15 + 12 × entries + 6`,
both rounded up to even numbers so the 1-px border stays symmetric.

**Example — MAGIC selected (column 1, x = 62):**

```
┌──────────────────────────────────────┐
│ COMMAND                              │
├──────────────────────────────────────┤
│   ITEMS    > MAGIC       EQUIP       │
│   STATUS     SKILLS      SYSTEM      │
└──────────────────────────────────────┘
        ┌──────────────────┐
        │ MAGIC            │
        ├──────────────────┤
        │ > SPELLS         │
        │   ADJUSTMENT     │
        │   INFORMATION    │
        │   BACK           │
        └──────────────────┘
```

### 10.3 Panel kinds are not forced into one component

The renderer is a switch over `MenuPanelKind`:

- **CommandList** — cursor + labels, dim when `Enabled == false`.
- **Info** — title + wrapped lines using `MenuScreen`'s renderer-local helper.
- **StatusSheet** — two columns: label left-aligned, value right-aligned at a fixed
  inset. Fed from `CharacterPreparation.EffectiveStats`, `Hp`, `Mp`.
- **Placeholder** — a centred `<NAME>` + `IS NOT IMPLEMENTED YET.` + `[ BACK ]`,
  modelled on the existing `DrawWip` treatment so the game reads as one product.

Parent windows stay on screen behind their children. That is the whole visual
point of the style.

---

## 11. Retro visual rules

### 11.1 Palette — RESOLVED: literal retro black and white

**Owner decision Q2 (2026-09-11): literal black / white.** The command windows
must read unmistakably as an early-Dragon-Quest-style command box, not as a modern
dark UI. The existing game palette continues to govern everything *outside* the
menu windows — the field, the battle screen, sprites — unchanged.

`MenuStyle`, the `WindowStyle` instance used by `MenuScreen`:

| Role | Value | Note |
|---|---|---|
| Window fill | `#000000` | pure black, fully opaque — the field behind is covered, not tinted |
| Border | `#FFFFFF` | pure white, exactly 1 px on all four edges |
| Primary text | `#FFFFFF` | |
| Title text | `#FFFFFF` | no separate accent colour inside menu windows |
| Cursor | `#FFFFFF` | the `>` glyph |
| Disabled / unavailable text | `#7F7F7F` | **the one documented exception — see 11.1a** |

The battle screen and preparation screen keep `BattleStyle` — `Panel #101923`
fill, `Border #536b71`, `Paper #d6d2ae` text — **completely unchanged**. Two
styles, one `Window` primitive. This is exactly why §12.2 parameterises style
instead of hardcoding colours.

#### 11.1a Two consequences of going strictly black and white

**1. The selection highlight bar is dropped.** Both existing grids draw a
`#263338` fill behind the selected cell. In a two-colour window that value does
not exist. Options were: an inverted cell (white fill, black text) or
cursor-only selection.

> **Decision: cursor-only.** The `>` glyph alone marks the selection, which is
> what the classic command windows this is modelled on actually did. It is also
> the least code and the most legible at 320×240. If selection proves hard to
> see in the vertical submenu lists during review, inverted-cell is the fallback
> and is a one-`case` change in `MenuScreen`.

**2. Disabled entries need a third value.** A strictly two-colour window has no
way to show "present but unavailable" except dithering (a 50% checkerboard of
white pixels), which is period-authentic but is real extra work and reads badly
at 1× on modern displays.

> **Decision: permit exactly one grey, `#7F7F7F`, for disabled text only.** This
> is the only non-black/white value allowed inside a menu window, it is
> documented here so it cannot spread, and it is the pragmatic equivalent of what
> the dithering would communicate. If you would rather have true dithering, it is
> a contained change inside the text-drawing call and can be done later.

Note that the first slice has **no disabled entries** — every leaf is reachable
and every leaf shows a panel (§13.2) — so `#7F7F7F` is defined by the style but
unused until a command genuinely becomes conditionally unavailable.

### 11.2 Hard rules for the implementation

- Integer `DrawRect` only. No floats, no `Vector2` fractions, no rotation, no alpha
  blending, no easing, no tweens, no transitions.
- Border thickness exactly 1 px on every edge at every window size.
- Text only through `PixelArt.Text`. No `Font`, `Label`, `Control`, or Godot theme
  resources anywhere.
- No rounded corners, no drop shadows, no gradients, no glassmorphism.
- The cursor is the existing `">"` glyph. No selection bar inside menu windows
  (§11.1a), and no new art assets in the first version.
- Menu window fill is **opaque** `#000000`. The field is covered where a window
  sits, never tinted or blended — there is no alpha anywhere in this project.
- Nothing in the menu may consume a random draw, touch `BattleState`, or write to
  `FieldState`.

---

## 12. Relationship to the Battle menu

### 12.1 The line

> **Do not share layout, selection rendering, or navigation state. Share
> stateless pixel primitives only in a later independently gated refactor.**

### 12.2 Implemented choice — renderer-local helpers

The first slice took the zero-regression branch: `MenuScreen` owns its small
window, wrapping, sizing and anchoring helpers. `BattleScreen.Window`,
`BattleScreen.Wrap`, its palette, and `FieldScreen`'s palette remain unchanged.
Both renderers call the already-public `PixelArt.Text` directly.

No `RetroUi` class was added. A later extraction may share stateless primitives
only if it has its own battle-image regression gate; it must not be a prerequisite
for extending the Field menu.

### 12.3 Not shared — `DrawCommandGrid`

`BattleScreen.DrawCommandGrid` hardcodes the battle window's origin (x = 12,
y = 146), a 296 px width, `visibleRows = columns == 3 ? 3 : 4`, a 18/14 px row
height and **row scrolling with `^` / `+` indicators**. The field root menu is a
fixed 2×3 grid at a different origin with no scrolling and different cell widths.

Sharing it would mean parameterising six values to serve two call sites with
different scroll semantics — abstraction for its own sake. `MenuScreen` gets its
own ~20-line grid renderer. **This duplication is deliberate and should be left
alone until a third grid exists.**

### 12.4 Not shared — `BattleMenu` itself

`BattleMenu` stays exactly as it is. Its `MenuAction` / `ChoiceKind` are combat
concepts; its `Columns => IsRoot ? 3 : 2` and synthetic `Back` entry are battle
conventions; and it is the single most heavily verified class in the presentation
layer. The field menu borrows its *shape* — frame stack, `row * Columns + column`,
clamped edges, cursor restored on `Back` — by writing ~40 similar lines, not by
coupling the two.

> **Deviation — ACCEPTED by the owner, 2026-09-11 (Q3).**
> `WORLD_ARCHITECTURE.md` §17 Stage 7 prescribes extracting a shared `MenuTree`
> and re-instantiating `BattleMenu`. **That recommendation is superseded for this
> feature by this document.** `BattleMenu` and `FieldMenuController` /
> `FieldMenuTree` remain separate logical systems. Only low-level visual
> primitives may be shared later, and only where doing so is genuinely low-risk
> (§12.2).
>
> Owner's stated rationale, recorded verbatim in effect: the ~40 lines of
> duplicated menu-navigation logic are preferable to coupling the already-working
> battle command system to a much more complex control-centre menu.
>
> **`BattleMenu` is not to be touched unless a later requirement independently
> justifies it.** Stage 4 (§14) must amend `WORLD_ARCHITECTURE.md` §17 so the two
> documents do not disagree.

---

## 13. First vertical slice — exact scope

### 13.1 Behaviour

```
FIELD ──Tab──▶ MENU (root, 3×2, cursor on ITEMS)
                 │
                 ├─ ITEMS   → Placeholder: "INVENTORY IS NOT IMPLEMENTED YET."
                 ├─ MAGIC   → child command window: SPELLS · INFORMATION · BACK
                 │              each leaf → Placeholder
                 ├─ EQUIP   → Info panel: "EQUIPMENT OPENS FROM THE FIELD WITH E."  (§13.3)
                 ├─ STATUS  → StatusSheet: REAL live values
                 ├─ ACTIONS → Info panel: "NO CONTEXTUAL ACTIONS AVAILABLE."
                 └─ SYSTEM  → child command window: SETTINGS · FOR TESTING · BACK
                                each leaf → Placeholder
               │
               └─ Esc at root, or Tab anywhere ──▶ FIELD (unchanged position, HP, MP, gear)
```

`ACTIONS` uses the **Info** panel kind rather than **Placeholder**, because
"no contextual actions available" is a true statement about the present game
state rather than an admission that a system is missing. The distinction is
small but it is the difference between a menu that reports and a menu that
apologises, and it costs nothing — both kinds already exist.

`STATUS` is real and read-only: `NAME`, `HP cur/max`, `MP cur/max`, `STR`, `DEF`,
`MAG`, `RES`, `AGI` from `CharacterPreparation`, plus the existing
`MAG/RES/AGI ARE WIP` note. It is trivial, safe, and proves the `StatusSheet`
panel kind is genuinely a different shape from a command list.

### 13.2 Placeholder discipline (D3-compliant)

Every non-functional leaf shows a visible panel naming the missing system. **No
leaf silently returns to the parent.** Entries with no plausible content at all are
not created in the first place.

### 13.3 Why EQUIP does not open the Preparation screen yet

Tempting — the screen already exists and works. But `GameController.cs:51` is:

```csharp
case GameMode.Preparation:
    Harness.Handle(input);
    if (Harness.ReturnToFieldRequested) Mode = GameMode.Field;   // ← hardcoded
```

`Preparation` can only return to `Field`. Menu → Preparation → Back would drop the
player into the field instead of back into the menu. Wiring it correctly needs a
`returnMode` concept on `GameController` and re-verification of the equipment
tests and the 163 field-loop QA checks.

That is a real change to verified code and does not belong in a UI-framework
slice. **Stage 5 (§14) does it as its own commit.** For now `EQUIP` is an honest
Info panel pointing at the existing `E` binding.

### 13.4 Explicit non-goals

Not in this slice, and not to be "partially started": inventory or item data;
any spell, skill or summon data; settings storage or any persisted preference;
`Give Test Item` (depends on `WORLD_ARCHITECTURE.md` Stage 6 inventory);
mouse input; menu animation or transitions; localisation or lowercase glyphs;
save/load; any change to `BattleMenu`, `BattleState`, `Rules`, `Stats`,
`Equipment`, `Field` or the golden log; any world-engine work.

---

## 14. Implementation stages

Each stage ends green before the next begins. Stages 1–3 are the approved slice.

| Stage | Work | Gate |
|---|---|---|
| **0** | **COMPLETE:** macOS verification preflight (§20) | All six owner-stated conditions satisfied in commit `0af1ac6` |
| **1** | **COMPLETE:** `FieldMenuTree.cs` + `FieldMenuController.cs` — pure presentation logic | Headless grid, stack, panel, live Status and mode tests green |
| **2** | **SKIPPED BY DESIGN:** optional `RetroUi.cs` extraction | Renderer-local helpers leave Battle visuals untouched (§12.2) |
| **3** | **COMPLETE:** `GameMode.Menu`, `UiInput.Menu`, Tab/pad binding, `MenuScreen.cs`, visibility gating | 29/29 presentation; battle 213; field 163; menu QA 85 |
| **4** | **COMPLETE:** `README.md` + architecture cross-reference update | Docs match behaviour |
| **5** *(separate approval)* | `returnMode` on `GameController`; `EQUIP` opens Preparation and returns to the menu | Equipment tests + field-loop QA green |

The optional Stage 2 extraction was not needed. Avoiding it kept the existing
Battle drawing helpers and palette out of the feature diff.

---

## 15. Testing strategy

Headless tests go in `VisualTests/FieldMenuTests.cs`, registered from
`VisualTests/Program.cs` exactly like `FieldLoopTests.All`. In-engine tests go in
`Visual/MenuQa.cs` as another `partial class BattleScreen`, following `FieldQa.cs`.

| # | Test | Suite |
|---|---|---|
| T1 | Tab in Field → `Mode == GameMode.Menu`, controller at root, selection 0 | headless |
| T2 | Menu open → `StepField(dx, dy)` returns false in all four directions; `PlayerPosition` unchanged | headless |
| T3 | Menu open → walking into the encounter tile is impossible; `InEncounter` stays false; `Field.Encounters` unchanged | headless |
| T4 | Arrow input moves selection per the §9.3 table, all six cases | headless |
| T5 | Down from row 0 lands in row 1, same column; Down from row 1 is rejected (two-row layout respected) | headless |
| T6 | Enter on MAGIC pushes a frame; `Breadcrumb` updates; `CurrentEntries` is the child list | headless |
| T7 | Esc pops to parent **and restores the parent's cursor position** | headless |
| T8 | Esc at root closes → `Mode == GameMode.Field`; Tab at any depth closes | headless |
| T9 | After closing, `StepField` moves again; position, HP, MP and loadout are all unchanged from before opening | headless |
| T10 | `UiInput.Menu` in `Battle`, `Preparation` and `GameOver` changes neither `GameMode`, nor `ScreenMode`, nor `Session.MachineText` | headless |
| T11 | Every leaf in the tree is reachable and every leaf yields a panel — no silent no-op (mirrors the existing 149-leaf traversal test) | headless |
| T12 | `StatusSheet` values equal `Player.EffectiveStats` / `Hp` / `Mp` exactly | headless |
| T13 | Real Tab key press opens the menu; held `→` across the transition produces no field step; release after closing produces none either | in-engine |
| T14 | Menu windows render inside 320×240, borders exactly 1 px, colours within the limited palette | in-engine |
| T15 | Field remains visible behind the menu; closing restores the field HUD exactly | in-engine |

**Verified after implementation:** all 58 core tests (Debug *and* Release), all
20 pre-existing presentation tests plus 9 Field-menu tests (29 total), all 213
battle QA checks, all 163 field-loop QA checks, all 85 menu QA checks, and
`golden/battle-20260909.log` byte-for-byte. The menu touches no rule, no RNG
stream and no event — **a golden diff means something is wrong with the change,
not with the baseline.**

---

## 16. File-by-file plan for Codex

### New files

| File | Contents |
|---|---|
| `Visual/Presentation/FieldMenuTree.cs` | `FieldMenuNode`, `MenuPanelKind`, `FieldMenuCatalog.Root` (the 6 root commands + MAGIC/SYSTEM children) |
| `Visual/Presentation/FieldMenuController.cs` | frame stack, `Open`/`Move`/`Confirm`/`Back`/`Close`, `Columns`, `Breadcrumb`, `ActivePanel`, read-model projection |
| `Visual/MenuScreen.cs` | `Node2D`; draws root grid + child windows + panels from the read-model with renderer-local helpers; no navigation or game logic |
| `VisualTests/FieldMenuTests.cs` | T1–T12 as a `static (string, Action)[] All` |
| `Visual/MenuQa.cs` | `partial class BattleScreen`, `--menu-qa` entry, T13–T15 |

### Modified files

| File | Change | Risk |
|---|---|---|
| `Visual/Presentation/GameController.cs` | `GameMode.Menu`; own a `FieldMenuController`; `Field` case handles `UiInput.Menu`; new `Menu` case delegating and closing | **Medium** — the transition hub |
| `Visual/Presentation/HarnessController.cs` | add `Menu` to the `UiInput` enum. **Nothing else in this file changes** | Low |
| `Visual/BattleScreen.cs` | `Key.Tab` / `JoyButton.Back` → `UiInput.Menu`; `_Draw` early-return includes `Menu`; `RefreshScreens` keeps `fieldScreen` visible in `Menu` and adds `menuScreen`; `AddChild(menuScreen)` **after** `fieldScreen`; battle drawing helpers remain unchanged | **High** — see §18 |
| `Visual/FieldScreen.cs` | suppress only the `WASD/ARROWS MOVE…` hint lines when `Game.Mode == GameMode.Menu` | Low |
| `VisualTests/Program.cs` | register `FieldMenuTests.All` | Low |
| `launch-visual.ps1` | add the `--menu-qa` run to `-Verify`; require `menu-qa.txt` to end `PASS ALL` | Low |
| `README.md` | document Tab, the root grid, the menu input table, and the new test counts | Low |
| `WORLD_ARCHITECTURE.md` | amend §17 so Stage 7 no longer prescribes the `MenuTree` extraction (Q3) | Low |

**No `.csproj` edits are required.** `VisualTests` globs
`../Visual/Presentation/*.cs` (§1.1), so the two new presentation files compile
into the headless suite automatically. `MenuScreen.cs` sits under `Visual/` and
is picked up by the Godot SDK's default glob.

### Files that must NOT be touched

`Probe/**` (all of it), `Visual/Presentation/Menu.cs`,
`Visual/Presentation/BattleSession.cs`, `Visual/PixelArt.cs`,
`Visual/HarnessQa.cs`, `Tests/**`, `golden/**`.

---

## 17. Scalability check

Adding `SUMMONS` later:

1. Add one `FieldMenuNode` to `FieldMenuCatalog.Root`.
2. Change the root `Columns` constant from 3 to 4.
3. If it needs a shape the four panel kinds do not cover, add one enum value and
   one `case` in `MenuScreen`.

`FieldMenuController` is not edited. `MenuLayout` is not edited. `GameController`
is not edited. That is the bar, and the design meets it.

The deliberate ceiling: there is no data-driven menu file, no runtime menu
registry, no dynamic panel plugin system, no theming layer. When content moves to
JSON in Phase 1B, `FieldMenuCatalog` becomes a loader for the same node type —
one file changes.

---

## 18. Known risks and regression points

**18.1 — Resolved document deviation.** §12.4 declines the former
`WORLD_ARCHITECTURE.md` §17 Stage 7. That document now preserves the separate
`BattleMenu` / `FieldMenuController` decision, so the plans agree.

**18.2 — `BattleScreen.cs` is the highest-risk file.** It holds the only input
entry point and draws both battle and preparation. The implementation therefore
limited its changes to physical menu mapping, child creation and mode visibility;
all existing battle drawing helpers and palettes stayed unchanged. The 213-check
battle QA gate remains the regression proof.

**18.3 — Draw order.** `MenuScreen` must be added as a child **after**
`fieldScreen`, and `BattleScreen._Draw` must early-return for `Menu`; otherwise the
parent's `Fill(0, 0, 320, 240, Ink)` paints over the field. Covered by T15.

**18.4 — Held-input leakage.** The riskiest runtime behaviour, and the one the
brief calls out. It is free with `GameMode.Menu` (§7.4) but only because of
`Route`'s line 127 — a line a future refactor could remove without realising what
depends on it. T13 exists specifically to fail if it does.

**18.5 — Root-menu semantics are the owner's call.** `ACTIONS` → `SKILLS` (§6.1)
is a game-design substitution, not a technical one, and it is reversible at zero
cost before Stage 1.

**18.6 — Resolved macOS harness risk.** The cross-platform preflight commit
`0af1ac6` made the PowerShell workflow discover .NET 8 and Godot 4.6.3 Mono on
macOS while retaining the Windows/Linux paths and every existing verification
gate. §20 is retained as the historical preflight record.

**18.7 — Correction to an earlier claim about `project.godot`.** I previously
stated that the uncommitted editor rewrite "likely breaks letterboxing" by
dropping `window/stretch/aspect="keep"`. **That was overstated and I could not
substantiate it.** Godot's project-settings writer omits any key whose value
equals the engine default, which is the most likely reason both dropped keys
disappeared without the owner changing anything. `msaa_2d=0` is unambiguously the
MSAA default. For `stretch/aspect`, godot-proposals#2701 ("Use the `keep` stretch
aspect by default (instead of `ignore`)") was closed against a merged PR, which
indicates `keep` did become the Godot 4 default — but I could not confirm the
final value from a primary source, so I am not asserting it.

The practical position is unchanged and does not depend on resolving this:
**restore both keys explicitly.** It is free, it pins the intent against engine
default drift across future Godot versions, and it removes the ambiguity
permanently. §20.5 gives the exact method and a 10-second way to settle the
question definitively on the owner's own engine build.

---

## 19. Decision register

### 19.1 Resolved 2026-09-11

| # | Question | Decision |
|---|---|---|
| **Q1** | `ACTIONS` or `SKILLS` as the sixth root command? | **`ACTIONS`.** Owner intent: the menu is the general control centre and contextual actions are explicitly wanted. Shows an Info panel, never a silent no-op. `SKILLS` deferred to §6.3 |
| **Q2** | Menu palette | **Literal black `#000000` / white `#FFFFFF`.** Existing palette still governs everything outside menu windows. One documented grey for disabled text (§11.1a) |
| **Q3** | Accept the §12.4 deviation from `WORLD_ARCHITECTURE.md` Stage 7? | **ACCEPTED.** `BattleMenu` is not refactored and is not to be touched. This document supersedes Stage 7 for this feature |
| **Q4** | Stage 5 (`EQUIP` → Preparation → back to menu): approve now or after the slice? | **After.** `EQUIP` stays an informational panel in slice 1. Stage 5 is documented but **not approved** and needs its own approval later |
| **Q5** | Gamepad binding for opening/closing the menu | **`JoyButton.Back` (Select / View) → `UiInput.Menu`.** `JoyButton.Start` remains the verified Game Over restart binding |
| — | `GameMode.Menu` | **APPROVED** |
| — | 3×2 root grid | **APPROVED** |
| — | Clamp rather than wrap | **APPROVED** |
| — | Explicit menu stack / back navigation | **APPROVED** |
| — | Root submenus vertical | **APPROVED** |
| — | Contextual window anchoring strategy (§10.1) | **APPROVED** |
| — | `STATUS` shows real existing stats in slice 1 | **APPROVED** |
| — | `MAGIC` exposes Spells / Information as demo entries | **APPROVED; Adjustment removed by later correction** |
| — | `SYSTEM` exposes Settings / For Testing as demo entries | **APPROVED** |
| — | No real Magic, Settings, Inventory or For Testing functionality | **APPROVED** |
| — | Every non-functional leaf produces a visible placeholder or disabled state | **APPROVED** |

### 19.2 Still open

| # | Question | My recommendation |
|---|---|---|
| **Q6** | **Disabled-entry rendering.** §11.1a permits one grey (`#7F7F7F`) as the only non-black/white value inside a menu window, rather than true 50% dithering | Grey. No disabled entries exist in slice 1, so this can be revisited at zero cost when the first conditional command appears |

Q6 needs an answer before the first conditionally-unavailable command, which is
not in this slice.

---

## 20. macOS verification preflight (Stage 0 — COMPLETE)

This section records the gate completed in commit `0af1ac6` before menu
implementation began. The owner's stated requirement was a *known-good Mac
development state*, and explicitly:
**do not weaken or delete QA because the platform changed, and prefer making the
existing workflow cross-platform over maintaining two divergent test systems.**
This section is written to that instruction.

### 20.1 Definition of done

| # | Condition | Evidence required |
|---|---|---|
| 1 | Core tests run | 58/58 in **Debug and Release**, both reporting zero warnings and zero errors |
| 2 | Presentation tests run | 20/20 headless |
| 3 | BattleScreen visual/QA checks run | `qa.txt` and `field-qa.txt` both end `PASS ALL` (213 + 163 checks) |
| 4 | `project.godot` preserves intended pixel scaling / aspect behaviour | Keys explicitly present; §20.5 |
| 5 | Baseline game launches and plays | Manual: walk, open preparation, fight the goblin, win, return |
| 6 | Working tree state understood | §20.6 |

### 20.2 The good news, established by inspection

Three findings that make this much smaller than it looked:

**The core and presentation suites have zero external dependencies.**
`probes/phase1a/NuGet.Config` is `<packageSources><clear /></packageSources>` and
`Probe` / `Tests` / `VisualTests` are plain `Microsoft.NET.Sdk` projects with no
`PackageReference`. Conditions 1 and 2 need nothing but a .NET 8 SDK — no network,
no Godot, no packages.

**The golden log is OS-newline-independent by construction.** This was the single
largest cross-platform risk and it is already handled:

- `CanonicalLog.Format` (`Scenario.cs:54`) appends an explicit `"\n"` via
  `FormattableString.Invariant` — never `Environment.NewLine`, never `AppendLine`.
  A repository-wide grep for `Environment.NewLine`, `AppendLine` and `\r\n` across
  `Probe/`, `Tests/` and `VisualTests/` returns **nothing**.
- `Probe/Program.cs` uses `Console.Write`, not `WriteLine`, and sets
  `Console.OutputEncoding = new UTF8Encoding(false)`.
- The child-process comparison reads `StandardOutput.BaseStream` — raw bytes, so
  no `TextReader` newline translation.
- `.gitattributes` pins the golden files to LF, which is the native macOS ending.

**The only external dependency in the entire project** is the MSBuild SDK
`Godot.NET.Sdk/4.6.3` in `Visual/Visual.csproj`, needed for condition 3 only.

### 20.3 Recommended approach: one cross-platform script set under PowerShell 7

`verify.ps1` and `launch-visual.ps1` are already 90% portable — they use
`Join-Path`, `Get-Command` and `$env:` throughout. PowerShell 7 runs natively on
macOS. **Keeping one script set and fixing its path handling is strictly less work
than writing bash equivalents, and it is the only option that does not create the
divergent test systems the owner ruled out.**

Rejected alternative: parallel `.sh` scripts. Two systems, two sets of drift, and
the Windows machine this project came from would immediately be second-class.

#### The six defects to fix — all path handling, no logic change

| # | Location | Problem | Fix |
|---|---|---|---|
| 1 | both, line 6 | `Join-Path $PSScriptRoot '..\..'` — on Unix `\` is a **literal filename character**, not a separator, so `$projectRoot` resolves to garbage and `Resolve-Path` throws | `Join-Path $PSScriptRoot '..' '..'` |
| 2 | both, line 8 | `.tools\dotnet\dotnet.exe` — Windows separators **and** the `.exe` suffix | build with `Join-Path`; choose the executable name via `$IsWindows` |
| 3 | both | `.tools\cli-home`, `.tools\nuget-packages`, `.tools\nuget-cache`, `Tests\...`, `VisualTests\...`, `artifacts\visual` | all to multi-argument `Join-Path` |
| 4 | `launch-visual.ps1:12` | Godot path hardcoded to `Godot_v4.6.3-stable_mono_win64_console.exe` | resolve per platform; macOS is `Godot_mono.app/Contents/MacOS/Godot` inside the bundle |
| 5 | `launch-visual.ps1:24` | `"$env:DOTNET_ROOT;$env:PATH"` — `;` is the **Windows** PATH separator; macOS uses `:` | `[IO.Path]::PathSeparator` |
| 6 | `launch-visual.ps1` | no way to point at a Godot outside `.tools/` | add a `-Godot <path>` parameter, mirroring the existing `-Dotnet` |

Everything else — `Get-Command`, `$LASTEXITCODE`, `throw`, `New-Item`,
`ProcessStartInfo` in the tests — is already portable. `$IsWindows` /`$IsMacOS`
are built-in PowerShell 7 automatic variables, so no platform-detection code needs
inventing.

### 20.4 The preflight, in order

Stop at the first failure; each step's output is the evidence for §20.1.

| Step | Action | Expected |
|---|---|---|
| **P1** | Install .NET 8 SDK (8.0.4xx to match the verified 8.0.425), PowerShell 7, and Godot **4.6.3 .NET for macOS (universal)**. Extract Godot under `.tools/godot/` (gitignored). De-quarantine it: `xattr -dr com.apple.quarantine <path to the .app>` — otherwise Gatekeeper silently blocks the CLI invocation | `dotnet --version`, `pwsh --version`, Godot opens |
| **P2** | Apply the six fixes in §20.3. **Path handling only — no logic, no test, no check removed** | Scripts parse under `pwsh` |
| **P3** | `pwsh ./probes/phase1a/verify.ps1 -Configuration Debug` then `-Configuration Release` | **58/58 twice**, zero warnings (`TreatWarningsAsErrors` is on), golden bytes match |
| **P4** | Run the `VisualTests` portion of `launch-visual.ps1 -Verify` | **20/20** |
| **P5** | Resolve `Visual/NuGet.Config` (§20.6) and restore `project.godot` (§20.5) | — |
| **P6** | `pwsh ./probes/phase1a/launch-visual.ps1 -Verify` | Godot import succeeds; `qa.txt` and `field-qa.txt` both end **`PASS ALL`** |
| **P7** | `pwsh ./probes/phase1a/launch-visual.ps1` and actually play it | Walk, equip, fight, win, return to field |
| **P8** | Commit as **one toolchain commit**, separate from all menu work | Clean tree |

P3 and P4 depended on nothing but P1 and P2. The documented fallback was not
needed: P6 passed with the full 213-check battle and 163-check Field suites, and
the optional shared-renderer extraction was later skipped by design.

#### Most likely macOS-specific failures, ranked

1. **P6 rendering QA.** `gl_compatibility` on macOS goes through a different
   graphics path than on Windows. The QA checks a limited palette and captures
   PNGs rather than diffing them byte-for-byte, so flat `DrawRect` fills should
   match exactly — but this is where a genuine platform difference would surface.
   If a colour check fails, **investigate the colour, do not relax the check.**
2. **Gatekeeper quarantine** on the Godot binary, which fails in a confusing way
   (silent refusal rather than a clear error). Handled in P1.
3. **The `tr-TR` culture child process.** macOS .NET uses ICU by default so the
   culture exists; this would only fail if the SDK were running in globalization
   invariant mode. Low risk, easy to diagnose from the test name.
4. Not a risk: golden newlines (§20.2) and NuGet for the core suites (§20.2).

### 20.5 Restoring `project.godot`

The uncommitted rewrite is **purely Godot's own canonical re-serialisation**: it
added the standard header comment block and blank lines after section headers, and
reordered two keys. It introduced **no new setting**. The only substantive
difference is the two absent keys.

> **Therefore: do not revert the file wholesale.** Reverting would discard Godot's
> canonical formatting, and the editor would simply rewrite it again on next open,
> producing a confusing repeat diff. **Re-add the two keys into the current file,
> in their correct sections, preserving everything else:**
>
> - `window/stretch/aspect="keep"` → `[display]`
> - `anti_aliasing/quality/msaa_2d=0` → `[rendering]`

**To settle the default-value question definitively in 10 seconds**, on the actual
engine build that matters: open the project in Godot 4.6.3 on the Mac, go to
*Project → Project Settings → Display → Window → Stretch*, and look at **Aspect**.
If it reads `keep` with no revert arrow beside it, `keep` is this build's default
and the rewrite changed no behaviour. Either way the explicit key is what we keep,
because it pins the intent for every future engine version.

### 20.6 Working-tree state, and the `NuGet.Config` decision

`git status` at `6c5a6eb` (`docs: architecture v0.2 and approved world engine
design`), on `main`, in sync with `origin/main`:

```
 M probes/phase1a/Visual/NuGet.Config     ← intentional macOS port work
 M probes/phase1a/Visual/project.godot    ← Godot editor rewrite (§20.5)
?? .DS_Store, probes/.DS_Store, probes/phase1a/.DS_Store, probes/phase1a/Visual/.DS_Store
?? probes/phase1a/MENU_ARCHITECTURE.md    ← this document
```

**`.DS_Store`:** four of them are untracked. Add `.DS_Store` to `.gitignore` as
part of the P8 toolchain commit. They are macOS Finder metadata and must never be
committed.

**`Visual/NuGet.Config`** was repointed from the Windows Godot bundle's local
`nupkgs` folder to `nuget.org`. This is a **real trade-off, not a mistake**:

| | Bundled local path | `nuget.org` (current edit) |
|---|---|---|
| Offline build | Yes | No |
| Guaranteed engine-matched SDK | Yes — the SDK ships with the engine | Version-pinned to `4.6.3` in the csproj, so still deterministic |
| Cross-platform | **No** — the path contains `Godot_v4.6.3-stable_mono_win64`, and on macOS `GodotSharp` lives *inside* the `.app` bundle at a completely different path | Yes, identical on both platforms |
| Divergence | Would need a second config file per OS — the thing the owner ruled out | One file |

> **Recommendation: keep the `nuget.org` source.** The README's "no external
> package feed" claim was a *nice-to-have offline property*, not a correctness
> property: `Godot.NET.Sdk/4.6.3` is a pinned, immutable package, so builds remain
> deterministic. A static XML file cannot branch on OS, so preserving the bundled
> path would force exactly the two-config divergence the owner rejected.
> **Update `README.md` in the same commit** so the documented claim matches
> reality — an inaccurate README is worse than the dependency.
>
> If offline builds matter to you, say so: the alternative is having
> `launch-visual.ps1` generate `Visual/NuGet.Config` from the resolved Godot path
> at build time. That works and stays single-source, but it makes a checked-in
> file machine-generated, which I would avoid unless offline builds are a real
> requirement rather than a preference.

### 20.7 What Stage 0 must not do

- Delete, skip, loosen or `-Skip`-flag any existing check.
- Reduce the 58 / 20 / 213 / 163 counts.
- Regenerate `golden/battle-20260909.log`. A golden failure on macOS means the
  port is wrong, **not** that the baseline needs refreshing. `README.md` already
  states this and it is doubly true across a platform change.
- Change any `.cs` file. Stage 0 is scripts, tooling and config only. If a source
  change appears necessary, stop and raise it — it means a genuine
  platform-dependence exists in the code and that is a finding worth its own
  discussion.
- Touch `BattleMenu` (Q3).

---

**Status: first visual slice implemented. Stage 0 is complete, Q5 is resolved,
and the unified gate covers core, presentation, battle, field, and menu QA.**

---

## 21. Chantless Magic V1 extension

The Field `MAGIC` branch contains only Spells, Information and Back. It owns no
cast editor, draft, or committed Magic configuration. Passive Info/Placeholder
panels accept Enter or Escape for one-layer dismissal.

Battle `MAGIC` first contains Chantless and Chant. Chant is an honest passive
WIP message. Chantless wraps the complete existing ten-category Magic taxonomy,
including the unchanged five-leaf Transformation Magic branch.

Selecting `CHANTLESS > ELEMENTAL MAGIC > Fire` opens the transactional Battle
cast editor. Size and Output are exact integer quarter steps `1..16`, displayed
as `0.25..4.00`, with default `4 = 1.00`. The Battle screen draws two 16-cell
sliders in a fixed black/white modal. Escape discards the draft and returns to
Fire; selecting Cast validates MP before opening the existing target picker.

One cost calculator implements
`ceil(BaseMpCost × Output × (0.5 + 0.5 × Size))` with Fireball Base MP 4.
Fireball Base Damage 8 is scaled by Output, then existing Magic adds offense and
half Resistance mitigates it. Size deliberately does not multiply single-target
damage. Agility remains unused.

The visible Fire leaf is the typed entry point for the domain spell **Fireball**.
Insufficient MP produces a readable message without calling `TakeTurn`, then
returns to the intact Battle draft; canceling a target also returns to that
draft. A legal cast builds one ability from the draft, deducts MP once in the
existing ability path, applies magical damage, and then uses the unchanged enemy
response loop. Its messages name Fireball and show Size, Output and MP cost.

`CharacterPreparation` stores last-successful configurations by stable spell ID.
Only a successful submission records the Fireball draft. Adjustment cancellation,
target cancellation, insufficient MP, invalid/dead targets and aborted casts do
not update it; the saved value seeds the same spell in the same or a later battle.

Only the Fire leaf was activated. Water and every other sibling remain WIP.
`TRANSFORMATION MAGIC` still contains exactly Self Transformation, Beast
Transformation, Material Transformation, Size Manipulation and Polymorph as WIP
leaves; Fireball does not collapse or rename that taxonomy. Chanted casting,
multi-target Size behavior and additional Base Magics remain future seams.

---

## 22. Combat Styles V1 extension

### 22.1 Scope and ownership

Combat Styles extend only the existing player physical-action path. One
immutable Style definition owns a stable ID, Japanese and English names, an
ASCII Battle label, stance modifiers and exactly two immutable Techniques. A
Technique owns identity and multipliers but references the authoritative BASIC
ATTACK / `Strike` ability; it does not copy the physical formula or effect tree.

`CharacterPreparation` knows all three Styles and fixes Sword God as Primary.
It copies a value-only profile into each player `ActorSeed`. `BattleState`, not
the UI or preparation layer, owns mutable Active Style and turn-start Style IDs,
resolves submitted IDs against that snapshot, and rejects forged or foreign
Techniques. Active Style is not part of `EncounterResult` and a new Battle starts
in Sword God again. Generic actors may remain styleless; their multiplier is
1.00, preserving the reviewed headless golden replay.

### 22.2 Fixed prototype definitions

| Stable Style ID | Name | Battle label | STR | DEF | RES |
|---|---|---|---:|---:|---:|
| `probe:style.sword-god` | 剣神流 / Sword God Style | `SWORD GOD` | +20% | -20% | — |
| `probe:style.water-god` | 水神流 / Water God Style | `WATER GOD` | -15% | +20% | +15% |
| `probe:style.north-god` | 北神流 / North God Style | `NORTH GOD` | +10% | -10% | +10% |

| Owner | Stable Technique ID | Display | Damage | Accuracy |
|---|---|---|---:|---:|
| Sword God | `probe:technique.sword-god.straight-slash` | `STRAIGHT SLASH` | ×1.10 | ×1.00 |
| Sword God | `probe:technique.sword-god.heavy-slash` | `HEAVY SLASH` | ×1.25 | ×0.85 |
| Water God | `probe:technique.water-god.steady-cut` | `STEADY CUT` | ×0.90 | ×1.10 |
| Water God | `probe:technique.water-god.precise-cut` | `PRECISE CUT` | ×1.00 | ×1.05 |
| North God | `probe:technique.north-god.adaptive-cut` | `ADAPTIVE CUT` | ×1.00 | ×1.10 |
| North God | `probe:technique.north-god.risky-cut` | `RISKY CUT` | ×1.15 | ×0.90 |

Stance evaluation is
`equipment-resolved stats → frozen Weakened subtraction → Active Style stance`.
Only Strength, Defense and Resistance can change. The arithmetic is fixed-point
with nearest rounding and midpoint values away from zero. MaxHP, MaxMP, Magic
and Agility remain unchanged.

### 22.3 Battle interaction and lifecycle

The existing 3×3 root is unchanged. `ATTACK` now opens one one-column screen:

```text
CURRENT STYLE TECHNIQUE 1
CURRENT STYLE TECHNIQUE 2
BASIC ATTACK
```

Left/Right moves through Sword → Water → North, clamps at both ends, and applies
the Style immediately for free. Up/Down changes the selected action without
changing Style. Confirm opens the existing living-enemy target picker. Target
cancel returns to the same Style and row; Back from there returns to the root.
Neither Back operation reverts a Style already selected or consumes a turn.

A legal Style change is available only to the actor that currently owns the
turn. It updates Active Style and emits `StyleChanged`, while leaving the
turn-start Style untouched. Re-selecting the active Style is a successful no-op
without a duplicate event. Unknown, off-turn and post-Battle changes are
rejected without mutation.

Shift is exactly `ActiveStyleId != TurnStartStyleId`. Switching back before
acting removes it. The fixed round-robin scheduler refreshes TurnStart Style to
the current Active Style only when that actor's next turn begins, so Shift stays
active throughout intervening enemy responses. No scheduler policy changed.

### 22.4 Hit, damage and event contracts

Only Technique commands use accuracy. The base chance is 90%, represented in
integer millionths:

```text
chance = 0.90 × Technique Accuracy × (0.85 when Shifted, otherwise 1.00)
```

Battle draws `0..999999` from the independent `battle.technique-hit` stream.
A lower roll hits. Rejected commands draw from neither RNG stream. A missed
Technique is still a committed action: it emits `Missed`, runs status cleanup,
advances the turn and permits normal enemy responses, without drawing from
`battle.effect`.

| Modifier while Shifted | Factor |
|---|---:|
| Technique accuracy | ×0.85 |
| Technique and BASIC physical damage | ×0.85 |
| Positive stance modifier magnitude | ×0.85 |
| Negative stance modifier magnitude | ×1.00 |

Technique Damage and Shift Damage combine once. The resulting scale applies to
physical nodes after base power, Strength, Defense and variance, but before
Guard and current-HP clamping. Magical and legacy Prototype nodes are not
scaled. BASIC ATTACK remains guaranteed-hit and performs no Technique roll, but
its damage and positive stance modifiers still receive the Shift penalties.

`ActionStarted.Detail` is the stable Technique ID for a Technique and remains
`probe:ability.strike` for BASIC ATTACK. `StyleChanged` and `Missed` are stable
typed core events. Presentation maps definitions to readable names; no rule or
persistent decision compares display text.

### 22.5 Explicit exclusions

V1 adds no general hit system, stat, Agility use, initiative or action-speed
scheduling, progression, Style ranks, schools, teachers, Mastery, Favorites,
counter/reaction rules, combos, feints, items or terrain interaction. Fireball,
Defend, Run, equipment locking, Field persistence and result application keep
their existing ownership and behavior.

**Status: Combat Styles V1 is implemented through core, presentation, Godot
input/rendering and deterministic verification.**

---

## 23. Expanded Stats Foundation V1 extension

The rules layer now owns one closed fifteen-entry `StatId` catalog and one
immutable value block. Stable `core:stat.*` IDs are persistence identities;
presentation never discovers stats by reflection or display text. Current HP/MP
remain separate mutable resources. Defense and Resistance are compatibility
aliases for PhysicalDefense and MagicalDefense; old Agility is isolated as
LegacyAgility and is not mapped to DEX, SPD, or RFL.

Equipment emits typed FlatAdd modifiers during persistent preparation. Battle
starts from that copied result, emits frozen Weakened FlatAdd modifiers, then
the active Style emits typed PercentAdd basis points. The resolver applies flats
before percentages and sums independent percentages per stat before applying
them once, preserving `equipment → Weakened → Style` without compounding by
insertion order. Persistent totals reject invalid minima; Battle-local totals
clamp at catalog minima.

The current Field Status contract is deliberately unchanged:

```text
NAME, HP, MP, STR, DEF, MAG, RES, AGI
```

The equipment comparison likewise remains MAXHP, MAXMP, STR, DEF, MAG, RES,
AGI. These are explicit presentation projections onto canonical IDs; DEX, SPD,
END, CON, INT, RFL, BAL, and MDEX are retained in immutable Battle views but are
not automatically displayed. `StatCatalog` category metadata is the seam for a
later approved grouped Status design; no tabs, groups, scrolling, or new rows
were added here.

Physical/Drain and Fireball calculations read canonical effective values but
retain all existing formulas. BASIC remains guaranteed-hit, Technique accuracy
keeps the independent `battle.technique-hit` stream, Style Shift rules are
unchanged, and the scheduler remains fixed round-robin. Flow is not implemented:
future inputs may read effective RFL/DEX/BAL, while Technique Mastery remains a
separate Technique-keyed concern. Conditions, variable parameters, derived
results, inventory, initiative, and action delay remain outside this slice.

**Status: Expanded Stats Foundation V1 is implemented without changing the
current menu surface.**

The 2026-09-13 complete gate passed 94/94 core tests in Debug and Release,
40/40 presentation-model tests, and the unchanged 282/193/87 Battle, Field, and
Menu engine checks. All three QA reports ended in `PASS ALL`; the golden replay
remained byte-exact.
