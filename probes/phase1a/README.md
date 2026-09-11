# Phase 1A — disposable battle probe and visual harness

This is an intentionally disposable experiment, not the production project skeleton.
Its deliverables are a working battle, a reviewed golden log, behavior tests, and
the [findings report](REPORT.md). Expect to replace most of this code in Phase 1B.
The Godot host now runs one playable **Field → Battle → Field** loop, using this
same combat core. It is not the production UI or the full Phase 1B architecture.

## First playable RPG loop

Normal launch starts in the **Starting Glade field**, with one persistent
player, empty gear and full base HP/MP. Move with **WASD / arrow keys / D-pad**.
Movement takes an immediate tile step, then repeats while held. Releasing the
key stops movement; mode transitions and loss of window focus clear held input.
The last pressed direction wins when several are held; there is no diagonal
movement. Boundaries and rock obstacles are solid, as defined by map data.

Press **E / Enter / Space / controller A** on the field to open the existing
equipment preparation screen. Equip gear there, then choose **Return to Field**
or press Back at preparation. This keeps the same player, resources, gear, map
and position. The equipment screen no longer starts encounters in normal play.

Walk into the visible goblin to start the existing **Goblin + Wolf** battle.
The field token represents that encounter group; it has no pathfinding or AI.
The field freezes during battle and repeated contact cannot start extra battles.
After the final battle text, press **Enter / A / Back** at the result screen:

- **Victory:** return at the encounter tile with the battle's remaining HP/MP.
  The encounter is marked defeated in field state and its token disappears.
  Walking through that location cannot trigger it again.
- **Run:** return to the tile immediately before contact, keeping HP/MP and gear.
  The enemy remains; another deliberate contact can start another encounter.
- **Defeat:** retain HP 0 and show Game Over. **R / controller Start** explicitly
  creates a new game. There is no automatic heal or mid-battle restart.

`GameState.Player` reuses `CharacterPreparation` as the single authoritative
player resource/equipment owner. Battle gets a resolved snapshot and its own
`EncounterResult` supplies the HP/MP delta back once. `FieldState` retains the map,
position and defeated encounter flags. The application coordinator owns the
transition; world data and the encounter core do not depend on presentation.

There is one fixed 19×11 map with 16-pixel tiles, one encounter group and no
save/load, map transitions, rewards, inventory or progression additions. After
victory you can continue exploring this map. See [loop implementation and
verification](RPG_LOOP_REPORT.md).

## Field control menu

Press **Tab** on the Field to open the retro control menu. On a controller,
Godot's **Back / View / Select** button is the equivalent menu toggle; controller
Start remains reserved for restarting at Game Over. The Field remains live and
visible behind opaque black windows, but `GameMode.Menu` owns input: movement,
encounters and equipment entry cannot run behind it. Opening or closing clears
held movement so a released key cannot produce a delayed step.

The root is a clamped 3×2 logical grid with cursor-only selection:

```text
ITEMS      MAGIC      EQUIP
STATUS     ACTIONS    SYSTEM
```

Magic opens **Spells / Adjustment / Information / Back**. System opens
**Settings / For Testing / Back**. Every unfinished leaf opens a visible,
truthful WIP panel rather than silently returning. Items reports that no
inventory exists, Actions reports that no contextual actions are available,
and **Equip is read-only information in this slice**; it does not enter
Preparation. The existing direct Field equipment binding remains **E / Enter**.

Status is functional and read-only. It shows the existing `ADVENTURER` prototype
identity and projects the current persistent player's live HP/MP and effective
STR/DEF/MAG/RES/AGI values; it does not copy or invent stats. Magic, Resistance
and Agility remain displayed WIP stats.

**Escape / Backspace / controller B** dismisses a panel first, then pops one
child window, then closes from the root. **Tab / controller View-Select** closes
immediately from any menu depth. Menu windows use literal black, white one-pixel
borders/text, and the existing bitmap font at native 320×240 resolution.

## Minimal character stats — temporary prototype

The seven integer stats are **MaxHP, MaxMP, Strength, Defense, Magic,
Resistance and Agility**. The implementation is intentionally small and may be
replaced by the owner's later stat design.

| Actor | MaxHP | MaxMP | STR | DEF | MAG | RES | AGI |
|---|---:|---:|---:|---:|---:|---:|---:|
| Adventurer | 80 | 12 | 12 | 8 | 6 | 6 | 10 |
| Goblin | 38 | 2 | 8 | 5 | 2 | 3 | 6 |
| Wolf | 46 | 0 | 10 | 4 | 1 | 3 | 12 |

MaxHP/MaxMP determine resource maxima; new actors default to full resources.
Explicit injured/depleted snapshots are accepted within bounds. Healing clamps
to MaxHP. There is no MP-restoration op; the existing MP mutation boundary
rejects an over-max change without changing state or events.

Strength and Defense affect the **Physical** paths of Strike and Crush:

```text
physical = max(1, BasePower + effective Strength - floor(effective Defense / 2) + variance)
if Defending: physical = max(1, floor(physical / 2))
applied damage = min(current target HP, physical)
```

Strike uses BasePower **2**, variance **0..2 inclusive**; Crush damage uses
BasePower **1**, variance **0..1 inclusive**. Variance comes from the existing
`battle.effect` stream, one draw per eligible target with nonzero variance.
Defense mitigation precedes Defend, then the remaining-HP cap. The physical
minimum also applies after Defend: a one-point physical hit remains one point.
This is the only guard edge-case adjustment from the pre-stat harness, required
by the new physical-damage minimum. Guard's lifetime remains unchanged.

Drain retains its prototype formula: rounded `4 + 0.7 * effective Strength +
variance(0..2)`, then subtract **full effective Defense**, minimum zero. It heals
half the actual applied damage, rounded away from zero at midpoints. Drain is
not classified as magic and does not consume Magic/Resistance. Its existing
prototype guard behavior may reduce one damage to zero.

**Magic, Resistance and Agility are stored/displayed only.** They do not affect
damage, mana costs, targeting or the existing round-robin turn order.

`CharacterStats` is an immutable value. `StatResolver.Resolve` is the one seam
between base and effective stats. It now adds flat equipment bonuses during
preparation. Battle receives those resolved values and applies the already-existing
frozen Weakened subtraction from Strength and Defense. No new status, class,
skill, stacking or generic modifier framework exists. Combat reads effective
stats through actor snapshots. Caster stats remain frozen per action; target
stats remain fresh per effect node. UI receives read-only values.

Detailed stats are displayed only on the out-of-battle preparation/equipment
screen. The battle party panel shows name, HP/MP and current status. Enemy
profiles remain available in the presentation read model for debugging.
See the [historical stat integration and golden revision](STAT_SYSTEM_REPORT.md).

## Minimal equipment — preparation only

From the field, press **E / Enter** to open **PREPARATION**. Choose
**Equipment**, choose a slot, then choose a compatible item or **None** to
unequip. Highlighting an item previews only the stats that would change;
confirming equips it. Back returns to preparation, then **Return to Field**
resumes exploration. All four prototype items are available as owned test fixtures.

| Slot | Prototype item | Flat bonus | Adventurer effective change |
|---|---|---|---|
| Weapon | Wooden Sword | Strength +3 | STR 12 → 15 |
| Head | Cloth Cap | Defense +1 | DEF 8 → 9 alone |
| Body | Leather Armor | Defense +4 | DEF 8 → 12 alone |
| Accessory | Copper Charm | MaxHP +5 | MaxHP 80 → 85 |

Cap and armor together give DEF **13**. The four current slots are **Weapon,
Head, Body, Accessory**; the loadout enumerates the slot model rather than
hardcoding separate slot fields. No other slots are implemented.

Immutable `EquipmentDefinition` values contain stable IDs, display names,
compatible slots and flat bonuses. `EquipmentLoadout` separately records the
selected definition per slot. Replacing a selection does not stack the former
item; incompatible slots are rejected. Definitions and loadouts are immutable.
`CharacterPreparation` owns the current loadout and resources. Base stats remain
unchanged: **BaseStats + EquipmentBonuses → EffectiveStats → Combat**.
Attack does not query equipment or recognize item IDs.

On any out-of-battle equipment change, current HP and MP are clamped to their
new maxima. Increasing a maximum does **not** heal or restore MP. For example,
equipping Copper Charm at 80/80 HP gives **80/85**, not 85/85.

**Equipment cannot be changed during battle.** `BeginBattle` copies resolved
stat values and current HP/MP into the encounter's `ActorSeed.InitialStats`
snapshot. `BattleState` contains no preparation or equipment references. The
higher preparation layer tracks the active encounter and rejects equip,
unequip and a second start until the game coordinator applies its result,
including while the terminal battle text is displayed. There is no public unlock flag
or equipment battle command. The existing ITEMS hierarchy, including its
Equipment Quick Use leaf, remains WIP and cannot equip anything.

The normal field loop preserves gear and applies battle HP/MP back to this same
character. The earlier isolated preparation/battle fixture remains available
only through the explicit Godot `--battle-probe` / `--qa` developer arguments;
its R reset behavior is preserved for regression checks. See the
[historical equipment implementation](EQUIPMENT_REPORT.md).

## Launch the visual harness

From the repository root with PowerShell 7:

```powershell
pwsh ./probes/phase1a/launch-visual.ps1
```

This restores and builds the C# project, imports Godot resources, and opens the
field screen. Close the window to exit. The source lives in `Visual/`.

The required tools are **PowerShell 7**, a **.NET 8 SDK**, and **Godot 4.6.3
.NET/Mono**. The workflow is shared by macOS, Windows and Linux. It prefers a
repository-local `.tools/dotnet/` installation, then checks `dotnet` on PATH and
standard platform locations for an installed 8.0 SDK. A newer SDK on PATH does
not silently replace the required .NET 8 toolchain. `-Dotnet <path>` selects an
explicit .NET host.

Install the matching build from the official
[Godot 4.6.3 archive](https://godotengine.org/download/archive/4.6.3-stable/).
The launcher discovers the repository-local Windows layout, a `Godot_mono.app`
in the macOS system Applications folder, or `godot-mono`, `godot4` and
`godot` on PATH. Use `-Godot <path>` for any other location. The launcher checks
that the selected engine reports version 4.6.3 and Mono support; the regular
non-.NET Godot build cannot run this harness.

`Visual/NuGet.Config` restores the pinned `Godot.NET.Sdk/4.6.3` package from
nuget.org into the ignored repository cache. The headless core intentionally
keeps its separate no-feed configuration because it has no external packages.

| Input | Action |
|---|---|
| WASD / arrows / D-pad in Field | Move one tile immediately; repeat while held; obey map collision |
| E / Enter / Space / A in Field | Open equipment preparation |
| Tab in Field or Field menu | Open from Field; close immediately from any menu depth |
| Controller Back / View / Select | Same conceptual Field-menu toggle as Tab |
| Arrow keys / WASD in battle menus | Move spatially through rows and columns; stop at edges |
| Arrow keys / WASD / D-pad in Field menu | Move through the clamped 3×2 root or vertical child commands |
| Up / Down or W / S in preparation | Choose preparation action, equipment slot or item |
| Left / Right or A / D while targeting | Choose living enemy; stop at the ends |
| Enter / Space | Confirm; advance battle text; close WIP message |
| Escape / Backspace | Back one Field-menu level; cancel targeting; close WIP or remaining battle text |
| D-pad, A / B equivalents | Move, confirm / back |
| F2 during battle | Open or close original event inspector; arrows scroll |
| F3 inside the inspector | Save canonical events to `artifacts/visual/machine-events.log` |
| Enter / A / Back at the battle result | Apply result and return to Field, or Game Over on defeat |
| R / controller Start at Game Over | Explicitly start a fresh game |

The battle root command menu remains a real **3×3 grid**:

```text
ATTACK      DEFEND      MAGIC
SUMMONING   SKILLS      SPECIAL
ITEMS       TACTICS     RUN
```

Left/right change column; up/down change row. Movement stops at the edge and
at missing cells in an incomplete final row. WIP submenus use **two columns**,
up to four visible rows and row-based scrolling. Back restores the previous
menu's cursor; there is no wrap or configurable navigation policy.

The three functional commands are:

- **ATTACK:** choose Goblin or Wolf and submit the existing `Strike` ability.
  Living enemies answer in the core's existing stable turn order.
- **DEFEND:** spend the action guarding. Physical damage is halved after
  Defense mitigation, rounded down with minimum 1, then capped to remaining HP.
  Guard ends at the start of the defender's next **accepted** action; rejected
  commands do not remove it. This is a temporary boolean, not a new status system.
- **RUN:** guaranteed escape, with a distinct `Fled` result and no enemy response.

**MAGIC, SUMMONING, SKILLS, SPECIAL, ITEMS and TACTICS are UI only.** The complete
requested hierarchy contains 149 WIP leaves. Every leaf opens a named WIP
dialog and returns to the same selection. Submenus support a stack of any depth,
explicit Back, breadcrumbs and scrolling. They consume no turns or random draws.

The screen uses a **320×240** viewport, initially **960×720 (3×)**, with integer
scaling and letterboxing when resized. Sprites, borders and a hand-authored 5×7
bitmap alphabet are drawn as solid integer pixels. There are no sampled font
assets, gradients, smoothing or animation. Goblin and Wolf are original temporary
sprites. The font currently displays English uppercase only.

Battle HP/MP/status are read-only projections of core state, and readable text
comes from core events. Each confirmed action resolves its enemy responses
immediately; the resulting state is displayed while text is paged three lines
at a time. This is not frame-by-frame event playback.

Only Strike is used by enemies in this visual fixture. The original headless
scenario still exercises Crush, Drain and Weakened. Its golden log was
intentionally updated for the earlier stat formula revision (see below), and
is **unchanged by the equipment/grid and field-loop updates**.
The visual fixture has no spell costs, healing or status-producing command;
MP therefore stays at 12. These starting profiles are prototype data, not final
balance. Victory and defeat are both valid results. No inventory, summons, rewards,
save flow or production UI infrastructure was added.

## Verify the visual harness and original probe

```powershell
pwsh ./probes/phase1a/launch-visual.ps1 -Verify
```

This runs the **58 core tests in both Debug and Release**, the **29 presentation
tests** (including traversal of all 149 battle WIP leaves and the Field menu
tree), then opens Godot briefly
for automated rendering/input checks. A graphical desktop is required for the
last step. The QA injects keyboard and controller events through Godot's normal
input path, captures native-resolution PNGs, checks the limited palette, and
exercises equipment preview/equip/unequip, resource maxima, the battle snapshot
and equipment lock, spatial navigation, Attack, Defend, Run, ordinary battle
completion and logging.
This does not claim a physical controller was tested.

Results, engine logs and screenshots are written under `artifacts/visual/`;
`qa.txt`, `field-qa.txt`, and `menu-qa.txt` must all end with `PASS ALL`. The
second Godot run
starts through the normal Field entry and exercises movement, release/collision,
equipment, encounter contact, escape, defeat/restart, victory, persisted HP/gear,
defeated encounter removal and continued movement. The third run exercises the
Field control menu's real key/controller routing, held-movement clearing,
nested Back behavior, window bounds and palette, live Status sheet,
placeholders, Field-under-menu rendering, and Battle input isolation. Existing
golden comparisons launch separate processes and compare exact bytes. The
baseline is never regenerated by these commands. The current revision passes
**213 isolated battle QA checks**, **163 field-loop QA checks**, and **85
Field-menu QA checks** in the real engine.
See [current RPG loop findings](RPG_LOOP_REPORT.md), the
[historical equipment findings](EQUIPMENT_REPORT.md), the
[historical stat findings](STAT_SYSTEM_REPORT.md) and the
[historical visual harness findings](VISUAL_HARNESS_REPORT.md).

## Run only the headless probe

From the repository root with PowerShell 7:

```powershell
pwsh ./probes/phase1a/verify.ps1 -Configuration Debug
pwsh ./probes/phase1a/verify.ps1 -Configuration Release
```

This requires a **.NET 8 SDK**, not just the runtime. The current macOS arm64
preflight uses SDK **8.0.425**, runtime **8.0.31**, C# 12. The SDK is machine
tooling and is not part of the source deliverable. The headless probe has **no
external package dependencies**; the tests are a small console runner using real
battle objects. `verify.ps1` restores, builds, and runs it; any failure returns a
failing script invocation.

Print the golden scenario after building:

```powershell
dotnet ./probes/phase1a/Probe/bin/Debug/net8.0/Phase1A.Probe.dll 20260909
```

Use the .NET 8 host selected by the verification script if `dotnet` on PATH is a
different major version. The CLI prints only the
canonical event log. Tests launch two fresh OS processes, with `en-US` and `tr-TR`
cultures, and compare their **raw stdout bytes** with each other and with the
fixed baseline. They do not regenerate or normalize the baseline.

## Scope

- One adventurer against **Goblin** and **Wolf**.
- Exactly three ability definitions: **Strike**, **Crush**, **Drain**.
- Exactly one status definition: **Weakened**.
- Four small ops: damage, heal, apply status, spend mana. Spend mana is also
  exercised directly by a rejection test; ability costs are committed before effects.
- A single core executable with Rules, Encounter, Preparation, World and Application
  namespaces, plus the test executable.
  No production assembly hierarchy or dependency-checking framework.
- The headless core has no dependency on Godot or the visual harness. There is
  no JSON content, schema, pack, registry, mod, save system, full modifier pipeline
  or world simulation. The original architecture documents remain untouched.

## Files

| File | Purpose |
|---|---|
| `Probe/Rules.cs` | Data literals, minimal context interface, ops, ordered evaluator, explicit RNG |
| `Probe/Stats.cs` | Seven-stat value, base-to-effective resolver, centralized physical damage |
| `Probe/Equipment.cs` | Four slots, immutable item definitions/loadout, flat bonuses and four literals |
| `Probe/CharacterPreparation.cs` | Authoritative player equipment/resources, resolved snapshot entry, owned-result application and equipment guard |
| `Probe/Field.cs` | Immutable map/collision data and retained field position/encounter state |
| `Probe/GameState.cs` | Application-owned persistent player, encounter entry and result application |
| `Probe/BattleState.cs` | Owned mutable battle state, turn progression, event emission, result boundary |
| `Probe/Scenario.cs` | Three abilities, two monsters, one status, scripted encounter fixture, log formatting |
| `Probe/Program.cs` | Seed-in / canonical-log-out CLI |
| `Tests/Program.cs`, `Tests/StatTests.cs`, `Tests/EquipmentTests.cs`, `Tests/FieldTests.cs` | 58 behavior/regression tests, including 12 stat, 11 equipment and 10 field/persistence tests |
| `golden/battle-20260909.log` | Current reviewed stat baseline; 73 events, 2,590 bytes |
| `golden/archive/battle-20260909.pre-stats.log` | Preserved pre-stat baseline; 88 events, 3,080 bytes |
| `REPORT.md` | Phase 1B handoff and the three deferred NON-BLOCKER findings |
| `Visual/Presentation/BattleSession.cs` | Core-owning adapter, immutable views, three command routes, readable events |
| `Visual/Presentation/Menu.cs` | Requested UI-only command tree, spatial grid and navigation stack |
| `Visual/Presentation/HarnessController.cs` | Preparation/equipment flow, menu, target, WIP, text and debug state |
| `Visual/Presentation/GameController.cs` | Field/preparation/battle/game-over mode transitions and input routing |
| `Visual/FieldScreen.cs` | Read-only field/player/enemy rendering using the existing pixel presentation |
| `Visual/BattleScreen.cs`, `Visual/PixelArt.cs` | Input, low-resolution drawing, original bitmap glyphs and sprites |
| `Visual/HarnessQa.cs` | Opt-in real-engine rendering and input checks |
| `Visual/FieldQa.cs` | Real-engine acceptance checks for the full field/battle loop |
| `Visual/project.godot`, `Visual/BattleScreen.tscn`, `Visual/Visual.csproj`, `Visual/NuGet.Config` | Small Godot C# host and local SDK configuration |
| `VisualTests/` | 20 headless presentation tests, including five field-loop tests, without a Godot runtime dependency |
| `launch-visual.ps1` | Build/launch or complete verification command |
| `toolchain.ps1` | Shared cross-platform .NET 8 discovery and isolated CLI/cache environment |
| `VISUAL_HARNESS_REPORT.md` | Integration findings, boundaries and verification evidence |
| `STAT_SYSTEM_REPORT.md` | Historical stat behavior, exact profiles and intentional golden revision |
| `EQUIPMENT_REPORT.md` | Historical equipment/grid changes, boundaries, verification and limitations |
| `RPG_LOOP_REPORT.md` | Current gameplay loop, persistence/transition boundaries and verification |

## Golden contract

The baseline is UTF-8 without BOM, LF line endings, including the final LF.
`.gitattributes` protects its line endings on Windows. Each event is:

```text
sequence|kind|sourceActorId|targetActorId|detail|amount|value
```

`-1` means no actor; `-` means no detail. Fixed probe identifiers contain no
delimiters. Numbers are formatted with the invariant culture. `amount` is actual
HP change magnitude, signed MP delta, or a frozen status magnitude; `value` is
remaining HP/MP/status turns, or actor count for `BattleStarted`.

Golden seed: `20260909`. Scenario definitions: `literals-stats-1`. Probe code label:
`phase1a-stats-1`. These labels are metadata on `EncounterResult`, outside the canonical
event body, so extracting the same scenario to data in 1B need not rewrite the
golden body merely because the code version changes. They are human-assigned
probe labels, not executable hashes or production release identifiers.

SHA256 of the reviewed log:

```text
6d8bcd6f0f8eb977f05572e2b383fc3e997aa24548a5a8700a7ac0816c29b6dd
```

The stat revision deliberately changes the former 88-event, 20-action victory
(HP 10 / MP 0) to a **73-event, 15-action victory (HP 46 / MP 0)**. Actor values,
physical Defense mitigation and the physical minimum changed; event format,
RNG algorithm and controller policy did not. The pre-stat file is preserved in
`golden/archive/` with SHA256
`412e6d9a1a1a7bd1f27a51779b3512419681fe045e5283fb9975c2e47f5c98f2`.
It is historical evidence, not an active expected output of the changed rules.

Keep the scenario's initial snapshots, controller decisions, RNG arithmetic and
draw order, rounding rule, event order and format fixed for the 1B extraction
comparison. A failed golden comparison requires investigation, not an automatic
baseline refresh. The per-seed controller is in `Scenario.Run`; action events
also record the actual actor, target and ability sequence.
