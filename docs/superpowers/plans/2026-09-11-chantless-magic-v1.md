# Chantless Magic V1 Implementation Plan

> **Historical record:** This plan describes the original three commits. Its
> Field Adjustment steps were superseded by the approved correction recorded in
> `docs/superpowers/specs/2026-09-11-chantless-magic-v1-design.md`. It is not the
> current implementation guide.

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Historical implementation record for passive Field-panel dismissal and the first chantless Fireball slice.

**Current architecture:** `CharacterPreparation` owns learned Base Magic and per-spell last-successful configurations. `HarnessController` owns the Battle cast draft; `FieldMenuController` owns no adjustment state. `MAGIC > CHANTLESS > ELEMENTAL MAGIC > Fire` opens Fireball adjustment, while Transformation Magic stays unchanged.

**Tech Stack:** C# 12, .NET 8, Godot 4.6.3 Mono, PowerShell 7, console tests, Godot input/render QA.

**Spec:** `docs/superpowers/specs/2026-09-11-chantless-magic-v1-design.md`

## Global Constraints

- Fireball is chantless. `CHANT` is only an honest WIP message; do not add functional chanted casting, interruption, enemy-first order, AoE, known-form efficiency, learning, or more spells.
- UI leaf may remain `Fire`; all domain identity and stable IDs use Fireball.
- Size/Output are integer steps `1..16`, display `0.25..4.00`, default `4 = 1.00`.
- Size affects MP cost only; Output affects cost and single-target damage.
- Check MP before target selection. Failure displays immediately and consumes no turn.
- Preserve Transformation Magic, Attack, Defend, Run, Field, equipment, golden logs, and world behavior.
- Keep exactly the three approved commits.

---

### Task 0: Baseline

**Files:** Inspect only.

**Interfaces:** Consumes `main` at `dada4fd`; produces a known-green starting point.

- [x] **Step 1: Run `pwsh ./probes/phase1a/launch-visual.ps1 -Verify`**

Observed: Core `58/58` Debug and Release, Presentation `29/29`, Battle QA `213`, Field QA `163`, Menu QA `85`, zero warnings, reports `PASS ALL`.

- [x] **Step 2: Confirm clean starting source state**

Observed before design documents: `main...origin/main [ahead 2]`, no source changes.

---

### Task 1: Passive Panel Enter Dismissal

**Files:**
- Modify: `probes/phase1a/VisualTests/FieldMenuTests.cs`
- Modify: `probes/phase1a/Visual/Presentation/FieldMenuController.cs`

**Interfaces:** `Confirm()` will dismiss one active Info/Placeholder panel while retaining its parent frame and cursor. Status and future interactive panels remain unaffected.

- [x] **Step 1: Write the failing regression test**

Open `MAGIC > INFORMATION`, call `Confirm()`, and assert:

```csharp
Equal(2, menu.Depth);
Equal("INFORMATION", menu.CurrentEntries[menu.SelectedIndex].Label);
Check(menu.BuildView(player).ActivePanel is null, "Enter dismisses one passive panel");
```

Repeat through `GameController` to pin `GameMode.Menu`; verify Escape matches and Enter does not dismiss Status.

- [x] **Step 2: Run VisualTests and confirm the new assertion fails**

Expected: current `Confirm()` ignores every active panel.

- [x] **Step 3: Implement the minimum behavior**

```csharp
if (activePanel is { PanelKind: FieldMenuPanelKind.Info or FieldMenuPanelKind.Placeholder })
{
    activePanel = null;
    return;
}
if (activePanel is not null || CurrentEntries.Count == 0) return;
```

- [x] **Step 4: Run VisualTests green**

- [x] **Step 5: Commit only these two files**

```bash
git add probes/phase1a/Visual/Presentation/FieldMenuController.cs probes/phase1a/VisualTests/FieldMenuTests.cs
git commit -m "fix: allow enter to dismiss menu info panels"
```

---

### Task 2: Persistent Configuration and Field Adjustment (historical; superseded)

**Files:**
- Create: `probes/phase1a/Probe/Magic.cs`
- Create: `probes/phase1a/Tests/MagicTests.cs`
- Modify: `probes/phase1a/Probe/Rules.cs`, `Stats.cs`, `CharacterPreparation.cs`
- Modify: `probes/phase1a/Tests/Program.cs`
- Modify: `probes/phase1a/Visual/Presentation/FieldMenuTree.cs`, `FieldMenuController.cs`, `GameController.cs`
- Modify: `probes/phase1a/Visual/MenuScreen.cs`, `MenuQa.cs`
- Modify: `probes/phase1a/VisualTests/FieldMenuTests.cs`
- Modify: `probes/phase1a/README.md`, `MENU_ARCHITECTURE.md`
- Add: approved design and this plan.

**Interfaces:** Produces `QuarterStepMultiplier`, `BaseMagicDefinition`, `PrototypeMagic.Fireball`, `ChantlessMagicConfiguration`, `ChantlessMagicCost.Calculate`, `FireballMagic.CreateAbility`, `MagicalDamage.Calculate`, player-owned configuration, and Adjustment draft/view/input.

- [x] **Step 1: Write failing core tests**

Register `MagicTests.All`. Test required Base Magic, default learned Fireball, all step conversions/formats, invalid bounds, monotonicity, Size-independent damage, Output-dependent damage, Magic/Resistance, unlearned Base rejection, battle lock, and exact costs:

```csharp
Equal(4, Cost(size: 4, output: 4));
Equal(6, Cost(size: 8, output: 4));
Equal(8, Cost(size: 4, output: 8));
Equal(12, Cost(size: 8, output: 8));
```

- [x] **Step 2: Run Debug core tests and observe missing-type compile failure**

- [x] **Step 3: Implement exact immutable domain data**

Fireball: ID `probe:magic.fireball`, display `Fireball`, Base MP `4`, Base Damage `8`. Central conversions use decimal only for display and integer ceilings for rules:

```csharp
public const int MinSteps = 1, MaxSteps = 16, DefaultSteps = 4;
private static int ValidateAndReturn(int steps)
{
    if (steps is < MinSteps or > MaxSteps) throw new ArgumentOutOfRangeException(nameof(steps));
    return steps;
}
public static decimal ToDecimal(int steps) => ValidateAndReturn(steps) / 4m;
public static string Format(int steps) => ToDecimal(steps).ToString("0.00", CultureInfo.InvariantCulture);
public static int ScaleCeiling(int value, int steps) => checked((int)(((long)value * steps + 3) / 4));
```

Cost uses one checked rational expression:

```csharp
var n = checked((long)c.BaseMagic.BaseMpCost * c.OutputSteps * (4 + c.SizeSteps));
return checked((int)Math.Max(1, (n + 31) / 32));
```

- [x] **Step 4: Add `DamageKind.Magical` and `MagicalDamage.Calculate`**

```csharp
return checked((int)Math.Max(1L,
    (long)basePower + caster.Magic - target.Resistance / 2));
```

`FireballMagic.CreateAbility` uses central cost and `ScaleCeiling(BaseDamage, OutputSteps)` in one selected-target Magical Damage node. Size never enters damage construction.

- [x] **Step 5: Make `CharacterPreparation` authoritative**

Add a read-only learned list containing Fireball, a default configuration, and `TryConfigureChantlessMagic`. Reject null, unknown Base Magic, and active-battle mutation without changing state.

- [x] **Step 6: Write failing Adjustment tests**

Test `1.00 → 1.25 → 1.50 → 1.25`, min/max clamps, row clamps, live cost, Escape cancellation, Apply persistence, reopen, Enter inert on Size/Output, and Field/Battle/Field ownership.

- [x] **Step 7: Implement Adjustment draft/view/input**

Add `FieldMenuPanelKind.Adjustment`. Adjustment opens a draft copied from the player; rows are Size, Output, Apply. Left/Right clamp steps. Enter applies only on Apply. Escape drops the draft. Change Information to a passive Info explanation. Update `GameController` to pass its player on confirmation.

- [x] **Step 8: Render the Adjustment panel**

Draw a bounded black/white modal with Base, Size, Output, MP Cost, Apply, cursor, and two 16-cell integer rectangle sliders. Add the modal to `LastWindowBounds`; add no palette values.

- [x] **Step 9: Extend Menu QA**

Drive real keys for step changes, slider change, cost preview, cancel, Apply, reopen, Info Enter dismissal, bounds, and palette.

- [x] **Step 10: Run Debug, Release, and full `-Verify`**

Expected: expanded Core/Presentation/Menu pass; Battle `213`, Field `163`, and golden remain unchanged.

- [x] **Step 11: Audit and commit configuration slice**

```bash
git diff --check
git diff --name-only HEAD -- probes/phase1a/golden probes/phase1a/Visual/Presentation/Menu.cs probes/phase1a/Visual/Presentation/BattleSession.cs probes/phase1a/Visual/HarnessQa.cs probes/phase1a/Visual/FieldQa.cs
git add docs/superpowers probes/phase1a
git commit -m "feat: add chantless magic configuration"
```

Expected: Task 3 Battle integration and golden files are absent.

---

### Task 3: Cast Configured Fireball in Battle (historical; superseded)

**Files:**
- Modify: `probes/phase1a/Visual/Presentation/Menu.cs`, `BattleSession.cs`, `HarnessController.cs`
- Modify: `probes/phase1a/VisualTests/Program.cs`, `FieldLoopTests.cs`
- Modify: `probes/phase1a/Visual/HarnessQa.cs`, `FieldQa.cs`
- Modify: `probes/phase1a/README.md`, `MENU_ARCHITECTURE.md`

**Interfaces:** Consumes player-owned configuration, central cost, and ability factory. Produces typed Fireball Battle choice, pre-target failure, normal targeting/submission, and deterministic readable messages.

- [x] **Step 1: Write failing Battle tests**

Affordable: target mode, exact one-time MP reduction, enemy damage, Output effect, Size non-effect, details, and enemy actions after player damage. Unaffordable:

```csharp
Equal(ScreenMode.Messages, depleted.Mode);
Check(depleted.BattleLines.Contains("Not enough MP."), "visible MP failure");
Equal(beforeLog, depleted.Session.MachineText);
```

Assert Transformation Magic still contains exactly Self Transformation, Beast Transformation, Material Transformation, Size Manipulation, and Polymorph as WIP.

- [x] **Step 2: Run VisualTests red**

- [x] **Step 3: Activate only the existing `Fire` leaf**

Add `MenuAction.Fireball` and `ChoiceKind.Fireball`; make only `new MenuEntry("Fire", Action: MenuAction.Fireball)` functional. Keep every sibling. Update WIP count `149 → 148` and test Fire separately.

- [x] **Step 4: Add BattleSession affordability/submission**

`CanSubmitFireball` uses current actor MP and central cost. Insufficient MP sets `LastMessages` to `Not enough MP.` without `TakeTurn`. `SubmitFireball` builds one ability, shares Attack's accepted-command/enemy-response loop, and adds Fireball/Size/Output/MP text. The ability cost performs the sole deduction.

- [x] **Step 5: Check affordability before target selection**

On Fire choice, fail immediately to Messages when unaffordable. Otherwise set a pending Fireball action and open the existing target mode. Target Confirm dispatches Attack or Fireball; Back cancels either. Do not create another target screen.

- [x] **Step 6: Extend Battle and Field QA**

Replace Fire-WIP checks with Fireball target/cast/cost/damage/message/order checks. Field QA commits a non-default configuration, casts it, completes the encounter, and confirms persistence. Exercise a Transformation leaf as WIP.

- [x] **Step 7: Update docs and test counts**

Remove obsolete statements that all Magic is WIP or MP always remains 12. Describe only implemented chantless behavior; future Chant remains an extension seam.

- [x] **Step 8: Run Core Debug, Core Release, Presentation, all engine QA, and full `-Verify`**

Expected: all expanded suites green, Battle QA above `213`, Field above `163`, Menu above `85`, zero warnings, all reports `PASS ALL`, golden unchanged.

- [x] **Step 9: Run the normal-game acceptance flow**

Verify cancel, Apply, preserved values, immediate MP failure where reachable, configured targeting/cost/damage, normal enemy response, victory return, configuration persistence, and Information Enter dismissal.

- [x] **Step 10: Audit and commit Battle slice**

```bash
git diff --check
git diff --name-only HEAD -- probes/phase1a/golden probes/phase1a/Probe/Field.cs probes/phase1a/Visual/PixelArt.cs
git ls-files | rg '(^|/)\.DS_Store$|(^|/)\.tools/|(^|/)(bin|obj|artifacts|\.godot)/' || true
git add probes/phase1a docs/superpowers/plans/2026-09-11-chantless-magic-v1.md
git commit -m "feat: cast configured fireball in battle"
```

---

### Task 4: Final Inspection

**Files:** Inspect only.

- [ ] **Step 1: Run full `-Verify` on the exact committed tree**

- [ ] **Step 2: Audit `git diff dada4fd..HEAD --check`, changed names, protected files, log, and clean status**

Expected: exactly three approved commits after `dada4fd`; no generated, golden, unrelated world, or chanted-casting changes.

- [ ] **Step 3: Report all 24 requested items and stop**
