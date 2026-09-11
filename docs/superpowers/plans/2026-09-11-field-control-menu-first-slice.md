# Field Control Menu First Visual Slice Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the approved retro Field Control Menu vertical slice from Field through nested menu windows and back to a normally playable Field.

**Architecture:** Keep battle and field menus separate. A Godot-free `FieldMenuTree` defines commands, a Godot-free `FieldMenuController` owns the frame stack and immutable read model, `GameController` owns menu mode, and a drawing-only `MenuScreen` overlays the still-visible `FieldScreen`. Skip the optional `RetroUi` extraction in this slice so existing battle rendering remains byte-for-byte untouched.

**Tech Stack:** C# 12, .NET 8, Godot 4.6.3 Mono, PowerShell 7, immediate-mode 320×240 integer pixel rendering.

**Spec:** `probes/phase1a/MENU_ARCHITECTURE.md`

## Global Constraints

- Root commands are exactly `ITEMS MAGIC EQUIP / STATUS ACTIONS SYSTEM` in a 3×2 logical grid.
- Navigation clamps at edges and never wraps; root uses three columns and child command windows use one.
- `Tab` and `JoyButton.Back` map to `UiInput.Menu`; `JoyButton.Start` remains mapped to `UiInput.Restart`.
- `GameMode.Menu` is the sole input-ownership gate; do not add a Field overlay boolean.
- Escape closes the active panel, then pops one child frame, then closes at root. Menu input closes from any depth.
- Menu windows use only opaque `#000000`, `#FFFFFF`, and disabled-only `#7F7F7F`; selection is the `>` cursor only.
- Field remains visible behind the menu and its movement hints are hidden while the menu is open.
- Status reads the authoritative current `CharacterPreparation`; do not copy or invent stats.
- Items, Magic, Equipment editing, Actions, Settings, and For Testing gameplay remain unimplemented and always produce visible truthful panels.
- Do not modify `Probe/**`, `Visual/Presentation/Menu.cs`, `Visual/Presentation/BattleSession.cs`, `Visual/PixelArt.cs`, `Visual/HarnessQa.cs`, `Tests/**`, or `golden/**`.
- Do not extract `RetroUi` in this slice. `MenuScreen` owns its small window/layout helpers; battle visuals remain unchanged.
- Preserve the known baselines: core 58/58 Debug and Release, presentation 20/20 before additions, battle QA 213, field QA 163, and both existing `PASS ALL` reports.

---

### Task 0: Reconfirm the Known-Good Baseline

**Files:**
- Inspect only: repository status and generated ignored QA artifacts.

**Interfaces:**
- Consumes: preflight commit `0af1ac6618599c98d0e1fb3a3c2aca34d039b6f1`.
- Produces: fresh baseline evidence before source changes.

- [x] **Step 1: Confirm repository state**

Run:

```bash
git status --short --branch
git log -1 --oneline
```

Expected: clean `main`, with `0af1ac6 build: make verification workflow cross-platform` at HEAD.

- [x] **Step 2: Run the current unified verification**

Run:

```powershell
pwsh ./probes/phase1a/launch-visual.ps1 -Verify
```

Expected: exit 0; core `58/58` twice; presentation `20/20`; `VISUAL QA PASS: 213 checks`; `FIELD QA PASS: 163 checks`.

---

### Task 1: Build the Pure Field Menu Tree and Controller

**Files:**
- Create: `probes/phase1a/Visual/Presentation/FieldMenuTree.cs`
- Create: `probes/phase1a/Visual/Presentation/FieldMenuController.cs`
- Create: `probes/phase1a/VisualTests/FieldMenuTests.cs`
- Modify: `probes/phase1a/VisualTests/Program.cs`

**Interfaces:**
- Consumes: `CharacterPreparation` for live status projection; no Godot types.
- Produces: `FieldMenuCatalog.Root`, `FieldMenuController`, `FieldMenuView`, `FieldMenuWindowView`, and `FieldMenuPanelView`.

- [x] **Step 1: Register failing pure menu tests**

Create `FieldMenuTests.All` and append it after existing field-loop tests:

```csharp
tests = tests.Concat(FieldLoopTests.All).Concat(FieldMenuTests.All).ToArray();
```

The tests must assert:

```csharp
var menu = new FieldMenuController();
Equal(0, menu.SelectedIndex);
Equal(3, menu.Columns);
Equal(new[] { "ITEMS", "MAGIC", "EQUIP", "STATUS", "ACTIONS", "SYSTEM" },
    menu.CurrentEntries.Select(entry => entry.Label).ToArray());

menu.Move(1, 0); Equal(1, menu.SelectedIndex);
menu.Move(1, 0); Equal(2, menu.SelectedIndex);
menu.Move(1, 0); Equal(2, menu.SelectedIndex);
menu.Move(0, 1); Equal(5, menu.SelectedIndex);
menu.Move(0, 1); Equal(5, menu.SelectedIndex);
```

Add separate cases for vertical same-column movement, Magic and System child frames, explicit Back, Escape-style panel dismissal, parent cursor restoration, every reachable leaf producing a panel, and exact live Status rows.

- [x] **Step 2: Run the presentation build and verify it fails**

Run:

```powershell
pwsh ./probes/phase1a/launch-visual.ps1 -Verify
```

Expected: failure while compiling `VisualTests`, because `FieldMenuController` and related types do not exist.

- [x] **Step 3: Add the declarative tree**

Create these public presentation-only types:

```csharp
public enum FieldMenuPanelKind { None, Info, Placeholder, Status }

public sealed record FieldMenuNode(
    string Id,
    string Label,
    IReadOnlyList<FieldMenuNode>? Children = null,
    FieldMenuPanelKind PanelKind = FieldMenuPanelKind.None,
    IReadOnlyList<string>? Lines = null,
    bool Enabled = true,
    bool IsBack = false);

public static class FieldMenuCatalog
{
    public static FieldMenuNode Root { get; }
}
```

`Root` contains the exact six commands. Magic contains `SPELLS`, `ADJUSTMENT`, `INFORMATION`, `BACK`; System contains `SETTINGS`, `FOR TESTING`, `BACK`. Leaf copy is:

```text
NO INVENTORY AVAILABLE.
SPELLS ARE NOT IMPLEMENTED YET.
MAGIC ADJUSTMENT IS NOT IMPLEMENTED YET.
MAGIC INFORMATION IS NOT IMPLEMENTED YET.
EQUIPMENT OPENS FROM THE FIELD WITH E.
NO CONTEXTUAL ACTIONS AVAILABLE.
SETTINGS ARE NOT IMPLEMENTED YET.
FOR TESTING IS NOT IMPLEMENTED YET.
```

- [x] **Step 4: Add the stack controller and immutable read model**

Create these value projections:

```csharp
public sealed record FieldMenuEntryView(string Label, bool Enabled);
public sealed record FieldMenuWindowView(
    string Title, IReadOnlyList<FieldMenuEntryView> Entries,
    int SelectedIndex, int Columns);
public sealed record FieldMenuStatusRow(string Label, string Value);
public sealed record FieldMenuPanelView(
    FieldMenuPanelKind Kind, string Title,
    IReadOnlyList<string> Lines,
    IReadOnlyList<FieldMenuStatusRow> Rows);
public sealed record FieldMenuView(
    IReadOnlyList<FieldMenuWindowView> Windows,
    FieldMenuPanelView? ActivePanel,
    string Breadcrumb);
```

`FieldMenuController` exposes:

```csharp
public IReadOnlyList<FieldMenuNode> CurrentEntries { get; }
public int SelectedIndex { get; }
public int SelectedRow { get; }
public int SelectedColumn { get; }
public int Columns { get; }
public int Depth { get; }
public void Open();
public void Close();
public void Move(int dx, int dy);
public void Confirm();
public bool Back();
public FieldMenuView BuildView(CharacterPreparation player);
```

`Back()` returns `true` only when the root requests a return to Field. It otherwise closes the active panel or pops one frame. `Open()` resets to root index 0. Status rows are `NAME`, `HP`, `MP`, `STR`, `DEF`, `MAG`, `RES`, `AGI`; HP/MP include current and effective maximum values, and the lines include `MAG/RES/AGI ARE WIP.`.

- [x] **Step 5: Run the focused presentation suite**

Run:

```powershell
pwsh ./probes/phase1a/launch-visual.ps1 -Verify
```

Expected: all existing 20 presentation tests and all new pure-controller tests pass; existing engine QA counts remain 213 and 163.

---

### Task 2: Add GameMode and Input Ownership

**Files:**
- Modify: `probes/phase1a/VisualTests/FieldMenuTests.cs`
- Modify: `probes/phase1a/Visual/Presentation/GameController.cs`
- Modify: `probes/phase1a/Visual/Presentation/HarnessController.cs`
- Include in first commit: `docs/superpowers/plans/2026-09-11-field-control-menu-first-slice.md`

**Interfaces:**
- Consumes: `FieldMenuController` from Task 1 and existing `GameController.StepField` mode gating.
- Produces: `GameMode.Menu`, `UiInput.Menu`, and `GameController.FieldMenu`.

- [x] **Step 1: Add failing mode-routing tests**

Add cases equivalent to:

```csharp
var game = new GameController();
var player = game.State.Player;
var position = game.State.Field.PlayerPosition;
game.Handle(UiInput.Menu);
Equal(GameMode.Menu, game.Mode);
Equal(0, game.FieldMenu.SelectedIndex);
Check(!game.StepField(1, 0), "menu blocks field movement");
Equal(position, game.State.Field.PlayerPosition);

game.Handle(UiInput.Right);
game.Handle(UiInput.Confirm); // Magic.
Equal(2, game.FieldMenu.Depth);
game.Handle(UiInput.Menu);
Equal(GameMode.Field, game.Mode);
Check(ReferenceEquals(player, game.State.Player), "menu preserves player owner");
```

Also place the player at the safe tile before contact, open the menu, attempt to step onto the encounter, and assert that `InEncounter`, position, and encounter flags do not change. Verify `UiInput.Menu` is inert in Preparation, Battle, and GameOver.

- [x] **Step 2: Run tests and verify the enum/routing tests fail**

Run the unified command. Expected: compile failure because `UiInput.Menu`, `GameMode.Menu`, and `GameController.FieldMenu` do not exist.

- [x] **Step 3: Implement minimal mode routing**

Make the enum changes:

```csharp
public enum GameMode { Field, Menu, Preparation, Battle, GameOver }
public enum UiInput { Up, Down, Left, Right, Confirm, Back, Debug, Restart, Menu }
```

Add `public FieldMenuController FieldMenu { get; } = new();`. In Field, Menu input calls `FieldMenu.Open()` then changes the mode. In Menu, Menu input closes immediately; directional and Confirm input delegate to the controller; Back closes a panel, pops a frame, or returns to Field when `Back()` returns true. Do not change the Battle, Preparation, or GameOver branches beyond their natural no-op handling of the new enum value.

- [x] **Step 4: Run the unified verification**

Expected: all headless tests pass and existing engine QA remains green before rendering exists, because no physical Tab binding exists yet.

- [x] **Step 5: Commit the framework**

```bash
git add docs/superpowers/plans/2026-09-11-field-control-menu-first-slice.md \
  probes/phase1a/Visual/Presentation/FieldMenuTree.cs \
  probes/phase1a/Visual/Presentation/FieldMenuController.cs \
  probes/phase1a/Visual/Presentation/GameController.cs \
  probes/phase1a/Visual/Presentation/HarnessController.cs \
  probes/phase1a/VisualTests/FieldMenuTests.cs \
  probes/phase1a/VisualTests/Program.cs
git commit -m "feat: add field control menu framework"
```

---

### Task 3: Render the Menu Over the Live Field

**Files:**
- Create: `probes/phase1a/Visual/MenuScreen.cs`
- Create: `probes/phase1a/Visual/MenuQa.cs`
- Modify: `probes/phase1a/Visual/BattleScreen.cs`
- Modify: `probes/phase1a/Visual/FieldScreen.cs`

**Interfaces:**
- Consumes: `GameController.FieldMenu.BuildView(Game.State.Player)`.
- Produces: a drawing-only `MenuScreen : Node2D`, physical Menu input, and `--menu-qa`.

- [x] **Step 1: Add the failing engine QA surface**

Create `MenuQa.cs` as another `partial class BattleScreen`. It must reference the future `menuScreen` field and assert normal startup, real Tab entry, held-movement clearing, root navigation, child/panel hierarchy, Status data, System entries, gamepad Back closure, resumed movement, and Tab inertness during Battle.

Add only the command-line hook in `_Ready`:

```csharp
if (args.Length >= 2 && args[0] == "--menu-qa")
    CallDeferred(MethodName.StartMenuQa, args[1]);
```

- [x] **Step 2: Build and verify the new QA surface fails**

Run:

```powershell
pwsh ./probes/phase1a/launch-visual.ps1 -Verify
```

Expected: Visual compilation failure because `MenuScreen` and `menuScreen` do not exist.

- [x] **Step 3: Implement `MenuScreen`**

`MenuScreen` contains no navigation or gameplay logic. It draws `FieldMenuView` using:

```csharp
private static readonly Color MenuInk = new("000000");
private static readonly Color MenuPaper = new("ffffff");
private static readonly Color MenuDisabled = new("7f7f7f");
private static readonly Rect2I RootRect = new(8, 8, 160, 54);
```

Root title is `COMMAND`; column origins are 14, 62, and 110; row baselines are 32 and 44. Each selected entry draws only `>` and white text. Window drawing fills opaque black and draws four one-pixel white edges. Text always goes through `PixelArt.Text` at integer coordinates.

Use one `ChildRect` function for all command and panel windows. It aligns to the selected parent column, places four pixels below, clamps inside 8..312/8..232, flips above on overflow, and adds an eight-pixel cascade for depth two or greater. Child widths are content-sized with a 96-pixel minimum and even dimensions.

Expose an internal read-only `LastWindowBounds` projection strictly for engine QA. It must contain root, child frames, and active panel rectangles from the most recent draw.

- [x] **Step 4: Wire physical input and draw order**

In `BattleScreen`:

```csharp
Key.Tab => UiInput.Menu
JoyButton.Back => UiInput.Menu
JoyButton.Start => UiInput.Restart
```

Create `menuScreen = new MenuScreen { Game = game, Visible = false }` and add it after `fieldScreen`. `RefreshScreens` keeps Field visible for Field, Menu, and GameOver; MenuScreen is visible only for Menu. The parent `_Draw` early return includes Menu so it cannot cover the field.

In `FieldScreen`, omit only the two bottom control-hint lines while `Game.Mode == GameMode.Menu`; do not change map, token, or HUD rendering.

- [x] **Step 5: Run the new Menu QA directly**

After building Visual, run Godot with:

```text
--path probes/phase1a/Visual -- --menu-qa probes/phase1a/artifacts/visual
```

Expected: `MENU QA PASS: <count> checks`, `menu-qa.txt` ending in `PASS ALL`, and captures for root, Magic child, Adjustment panel, Status, System, and returned Field.

The QA must verify every `LastWindowBounds` rectangle lies inside 320×240, root-window pixels contain only black and white, a cursor pixel is white, a sampled Field pixel outside the windows is unchanged, held movement stops across Tab, and a new movement press works after close.

- [x] **Step 6: Run existing battle and field QA unchanged**

Run the unified verification. Expected: battle stays at 213, field stays at 163, and both existing reports still end `PASS ALL`.

---

### Task 4: Integrate Menu QA and Documentation

**Files:**
- Modify: `probes/phase1a/launch-visual.ps1`
- Modify: `probes/phase1a/README.md`
- Modify: `probes/phase1a/MENU_ARCHITECTURE.md`
- Modify: `probes/phase1a/WORLD_ARCHITECTURE.md`

**Interfaces:**
- Consumes: `--menu-qa` from Task 3.
- Produces: one full verification entry point covering all three engine suites and documentation matching the implemented architecture.

- [x] **Step 1: Add Menu QA to `-Verify`**

After the existing field run, invoke:

```powershell
& $Godot --path $visualRoot --log-file (Join-Path $artifacts 'menu-engine-qa.log') -- --menu-qa $artifacts
if ($LASTEXITCODE -ne 0) { throw 'Godot field menu QA failed.' }
if ((Get-Content (Join-Path $artifacts 'menu-qa.txt') -Tail 1) -ne 'PASS ALL') {
    throw 'Godot field menu QA report did not finish with PASS ALL.'
}
```

Keep the prior runs and checks intact.

- [x] **Step 2: Update user documentation**

Document Tab / controller View-Select entry, the exact 3×2 grid, cursor-only black/white windows, Magic/System hierarchy, real Status sheet, placeholders, Field visibility, Back semantics, and the new presentation/menu QA counts. Explicitly state that EQUIP is read-only information and does not enter Preparation in this slice.

- [x] **Step 3: Resolve the architecture-document conflict**

In `MENU_ARCHITECTURE.md`, mark the visual slice as implemented, record that the multiplayer concern is resolved by local-only menu mode, and replace obsolete Stage 0-blocked wording. In `WORLD_ARCHITECTURE.md`, replace Stage 7's `MenuTree`/BattleMenu extraction prescription with the approved separation: `BattleMenu` remains unchanged; `FieldMenuTree` and `FieldMenuController` are separate; only low-level visual primitives may be shared in a later independently gated refactor. Do not change procedural-world requirements.

- [x] **Step 4: Run the complete regression gate**

Run:

```powershell
pwsh ./probes/phase1a/launch-visual.ps1 -Verify
```

Expected: exit 0; core Debug and Release `58/58`; all presentation tests pass; battle `213`; field `163`; menu QA passes; `qa.txt`, `field-qa.txt`, and `menu-qa.txt` each end `PASS ALL`.

- [x] **Step 5: Commit rendering, QA, and docs**

```bash
git add probes/phase1a/Visual/MenuScreen.cs \
  probes/phase1a/Visual/MenuQa.cs \
  probes/phase1a/Visual/BattleScreen.cs \
  probes/phase1a/Visual/FieldScreen.cs \
  probes/phase1a/launch-visual.ps1 \
  probes/phase1a/README.md \
  probes/phase1a/MENU_ARCHITECTURE.md \
  probes/phase1a/WORLD_ARCHITECTURE.md
git commit -m "feat: render retro field control menu"
```

---

### Task 5: Manual Acceptance and Final Inspection

**Files:**
- Inspect only: generated screenshots/logs and Git state.

**Interfaces:**
- Consumes: the completed two-commit vertical slice.
- Produces: acceptance evidence and a clean handoff.

- [ ] **Step 1: Launch the normal game**

Run:

```powershell
pwsh ./probes/phase1a/launch-visual.ps1
```

Move on Field, open with Tab, navigate all six root cells, open Magic → Adjustment, unwind with Escape, view Status, inspect System, close with Tab, resume movement, enter Battle, finish or flee, return to Field, and reopen the menu. Confirm Battle visuals are unchanged.

- [x] **Step 2: Inspect generated menu captures**

Verify native 320×240 dimensions, crisp one-pixel borders, black/white-only menu windows, cursor-only selection, child/panel bounds, live field outside windows, live Status values, and restored Field HUD after close.

- [x] **Step 3: Audit the final diff and repository contents**

Run:

```bash
git diff 0af1ac6..HEAD --check
git diff 0af1ac6..HEAD --name-only
git status --short --branch
git ls-files | rg '(^|/)\.DS_Store$|(^|/)\.tools/|(^|/)(bin|obj|artifacts|\.godot)/' || true
git diff 0af1ac6..HEAD -- probes/phase1a/golden probes/phase1a/Visual/Presentation/Menu.cs probes/phase1a/Visual/PixelArt.cs probes/phase1a/Visual/HarnessQa.cs
```

Expected: no whitespace errors; no forbidden artifacts; no protected-file diff; clean working tree; exactly the two feature commits ahead of `0af1ac6`.
