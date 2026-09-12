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

Magic opens **Spells / Information / Back**. It has no casting adjustment state;
Fireball is adjusted only when it is selected during Battle. Information explains
the chantless configuration model.
System opens **Settings / For Testing / Back**. Every unfinished leaf opens a
visible, truthful WIP panel rather than silently returning. Items reports that no
inventory exists, Actions reports that no contextual actions are available,
and **Equip is read-only information in this slice**; it does not enter
Preparation. The existing direct Field equipment binding remains **E / Enter**.

Passive Info and Placeholder panels dismiss with **Enter or Escape**, one layer
only, without closing the Field control menu.

Status is functional and read-only. It shows the existing `ADVENTURER` prototype
identity and projects the current persistent player's live HP/MP and effective
STR/DEF/MAG/RES/AGI values; it does not copy or invent stats. Magic and Resistance
feed Fireball's implemented damage seam; Agility remains displayed-only.

`CharacterPreparation` owns the learned Base Magic collection, which contains
Fireball by default, and spell-specific last-successful configurations. Size and
Output are stored as integer quarter steps, never accumulated floating point.
Battle's visible `MAGIC > CHANTLESS > ELEMENTAL MAGIC > Fire` leaf opens a cast
draft for the domain spell **Fireball**. `Fire` remains only the current taxonomy
label; it is not the permanent identity of the spell.

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

**Magic and Resistance now form the dedicated Fireball damage seam.** Fireball's
Output scales its base power, then Magic adds offense and half Resistance
(rounded down) mitigates it. Size does not multiply a single target; it affects
the shared MP-cost calculation. Agility remains stored/displayed only. None of
these stats changes targeting or the existing round-robin turn order.

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

## Combat Styles V1 — physical Battle actions

Every player Battle knows all three prototype Styles and starts fresh in the
immutable Primary Style, Sword God. Active Style is Battle-local and does not
persist into a later encounter.

| Stable Style ID | Japanese / English name | Battle label | STR | DEF | RES |
|---|---|---|---:|---:|---:|
| `probe:style.sword-god` | 剣神流 / Sword God Style | `SWORD GOD` | +20% | -20% | — |
| `probe:style.water-god` | 水神流 / Water God Style | `WATER GOD` | -15% | +20% | +15% |
| `probe:style.north-god` | 北神流 / North God Style | `NORTH GOD` | +10% | -10% | +10% |

The stance is applied after equipment resolution and the existing frozen
Weakened subtraction. It modifies only Strength, Defense and Resistance with
integer fixed-point arithmetic and nearest rounding, with midpoint values away
from zero. MaxHP, MaxMP, Magic and Agility are unchanged.

Each Style owns exactly two prototype Techniques:

| Stable Technique ID | Display name | Damage | Accuracy |
|---|---|---:|---:|
| `probe:technique.sword-god.straight-slash` | `STRAIGHT SLASH` | ×1.10 | ×1.00 |
| `probe:technique.sword-god.heavy-slash` | `HEAVY SLASH` | ×1.25 | ×0.85 |
| `probe:technique.water-god.steady-cut` | `STEADY CUT` | ×0.90 | ×1.10 |
| `probe:technique.water-god.precise-cut` | `PRECISE CUT` | ×1.00 | ×1.05 |
| `probe:technique.north-god.adaptive-cut` | `ADAPTIVE CUT` | ×1.00 | ×1.10 |
| `probe:technique.north-god.risky-cut` | `RISKY CUT` | ×1.15 | ×0.90 |

All six definitions reference the same authoritative BASIC ATTACK / `Strike`
ability and effect nodes. They add stable identity and multipliers; there is no
duplicate physical implementation.

Only Techniques use the V1 hit seam. Their fixed base chance is **90%**:

```text
final Technique chance = 0.90 × Technique Accuracy × Style Shift Accuracy
```

| Technique | Established | Style Shifted |
|---|---:|---:|
| Straight Slash | 90.000% | 76.500% |
| Heavy Slash | 76.500% | 65.025% |
| Steady Cut | 99.000% | 84.150% |
| Precise Cut | 94.500% | 80.325% |
| Adaptive Cut | 99.000% | 84.150% |
| Risky Cut | 81.000% | 68.850% |

Chances are integer millionths. Battle draws `0..999999` from the independent
`battle.technique-hit` stream; a roll below the chance hits. A miss commits the
action, emits `Missed`, ticks status and proceeds through the existing enemy
response order, but draws nothing from `battle.effect`. BASIC ATTACK remains
guaranteed and never performs this Technique roll.

Changing Style is immediate and free while that actor owns the turn. Battle
keeps both Active Style and the Style captured at the start of the turn. Shift
is active exactly while those IDs differ:

| Modifier while Style Shifted | Applied factor |
|---|---:|
| Technique accuracy | ×0.85 |
| Every physical action's damage, including BASIC ATTACK | ×0.85 |
| Positive stance modifier magnitude | ×0.85 |
| Negative stance modifier magnitude | ×1.00 |

The combined physical multiplier is applied once after the existing physical
formula and before Guard and remaining-HP clamping. It never scales magical or
legacy Prototype damage nodes. Switching away and back to the turn-start Style
before acting removes Shift; the real Style-change events remain. The turn-start
Style refreshes only when the unchanged fixed round-robin scheduler returns to
the player for the next turn. Agility remains unused by stance, accuracy,
damage and scheduling.

Selecting root **ATTACK** opens one vertical screen: the current Style's two
Techniques followed by **BASIC ATTACK**. Left/Right immediately switches among
Sword → Water → North and clamps at the ends; Up/Down selects an action; Confirm
opens the existing living-enemy target picker. Canceling a target returns to the
same Style and row. A second Back returns to the Battle root. Neither Back path
reverts an already applied Style change or spends a turn.

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
| Arrow keys / WASD in Battle root and WIP menus | Move spatially through rows and columns; stop at edges |
| Left / Right or A / D in Physical Style | Switch Active Style immediately; clamp Sword ↔ Water ↔ North |
| Up / Down or W / S in Physical Style | Select Technique 1, Technique 2 or BASIC ATTACK |
| Arrow keys / WASD / D-pad in Field menu | Move through the clamped 3×2 root or vertical child commands |
| Up / Down or W / S in preparation | Choose preparation action, equipment slot or item |
| Left / Right or A / D while targeting | Choose living enemy; stop at the ends |
| Enter / Space | Confirm; advance battle text; close WIP message |
| Escape / Backspace | Back one menu level; target cancel returns to its action screen; close WIP or remaining battle text |
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

The four functional commands are:

- **ATTACK:** open the Physical Style screen. Choose either current-Style
  Technique or BASIC ATTACK, then choose Goblin or Wolf through the shared
  target flow. Style switches are immediate/free and Back does not revert them.
  BASIC reuses the existing guaranteed-hit `Strike`; living enemies answer in
  the core's existing stable turn order.
- **DEFEND:** spend the action guarding. Physical damage is halved after
  Defense mitigation, rounded down with minimum 1, then capped to remaining HP.
  Guard ends at the start of the defender's next **accepted** action; rejected
  commands do not remove it. This is a temporary boolean, not a new status system.
- **MAGIC:** first choose `CHANTLESS` or `CHANT`. Chant is a passive WIP message.
  Chantless contains the complete existing Magic taxonomy.
- **MAGIC > CHANTLESS > ELEMENTAL MAGIC > Fire:** open the Battle cast adjustment
  for the domain spell **Fireball**, then choose Cast and one living enemy.
  Affordability is checked on Cast before target selection. A failed check shows
  `Not enough MP.` and returns to the intact draft without spending a turn;
  target cancellation is also free and returns to the adjustment. A confirmed
  cast deducts the centralized cost once, applies magical damage, runs the normal
  enemy response loop, and records that spell's last-successful configuration.
- **RUN:** guaranteed escape, with a distinct `Fled` result and no enemy response.

Every other Magic leaf, plus **SUMMONING, SKILLS, SPECIAL, ITEMS and TACTICS,**
remains UI-only. The complete requested hierarchy contains **149 WIP leaves**,
including the passive Chant entry.
Every WIP leaf opens a named dialog and returns to the same selection. Submenus
support a stack of any depth, explicit Back, breadcrumbs and scrolling. WIP
browsing consumes no turns or random draws. Transformation Magic remains its
separate five-leaf WIP taxonomy and is not folded into Fireball.

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
is **unchanged by the equipment/grid, field-loop, Fireball and Combat Style
updates** because its generic actors have no Style profiles.
Fireball is the visual fixture's only MP-consuming player command; there is no
healing or status-producing player command. These starting profiles are prototype
data, not final balance. Victory and defeat are both valid results. No inventory,
summons, rewards, save flow or production UI infrastructure was added.

## Verify the visual harness and original probe

```powershell
pwsh ./probes/phase1a/launch-visual.ps1 -Verify
```

This runs the **80 core tests in both Debug and Release**, the **39 presentation
tests** (including traversal of all 149 battle WIP leaves and the Field menu
tree), then opens Godot briefly
for automated rendering/input checks. A graphical desktop is required for the
last step. The QA injects keyboard and controller events through Godot's normal
input path, captures native-resolution PNGs, checks the limited palette, and
exercises equipment preview/equip/unequip, resource maxima, the battle snapshot
and equipment lock, spatial navigation, all three physical-action rows, immediate and
back-persistent Style switching, BASIC ATTACK, a real Technique, Defend,
Battle-adjusted Fireball, pre-target MP failure, Run, ordinary battle completion
and logging.
This does not claim a physical controller was tested.

Results, engine logs and screenshots are written under `artifacts/visual/`;
`qa.txt`, `field-qa.txt`, and `menu-qa.txt` must all end with `PASS ALL`. The
second Godot run
starts through the normal Field entry and exercises movement, release/collision,
equipment, Battle Magic Adjustment, configured Fireball, encounter contact,
escape, defeat/restart, victory, persisted HP/MP/gear/last-used configuration, defeated encounter
removal and continued movement. The third run exercises the
Field control menu's real key/controller routing, held-movement clearing,
nested Back behavior, window bounds and palette, live Status sheet,
placeholders, Field-under-menu rendering, and Battle input isolation. Existing
golden comparisons launch separate processes and compare exact bytes. The
baseline is never regenerated by these commands. The current revision passes
**282 isolated battle QA checks**, **193 field-loop QA checks**, and **87
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
- Three fixed fixture abilities—**Strike**, **Crush**, **Drain**—plus a Fireball
  ability built from the authoritative chantless configuration and six
  Style-owned Technique identities that reuse Strike's physical implementation.
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
| `Probe/PhysicalActions.cs` | Authoritative BASIC ATTACK identity and exact physical-action scaling helpers |
| `Probe/CombatStyles.cs` | Three immutable Style definitions, six Technique definitions, stance and Shift math |
| `Probe/Magic.cs` | Required Base Magic data, exact quarter steps, shared MP cost and Fireball ability factory |
| `Probe/Equipment.cs` | Four slots, immutable item definitions/loadout, flat bonuses and four literals |
| `Probe/CharacterPreparation.cs` | Authoritative player equipment/resources, known/Primary Styles, per-spell last-used Magic, resolved snapshot entry, owned-result application and equipment guard |
| `Probe/Field.cs` | Immutable map/collision data and retained field position/encounter state |
| `Probe/GameState.cs` | Application-owned persistent player, encounter entry and result application |
| `Probe/BattleState.cs` | Owned mutable battle state, Style lifecycle, Technique hit seam, turn progression, event emission and result boundary |
| `Probe/Scenario.cs` | Three abilities, two monsters, one status, scripted encounter fixture, log formatting |
| `Probe/Program.cs` | Seed-in / canonical-log-out CLI |
| `Tests/Program.cs`, `Tests/StatTests.cs`, `Tests/EquipmentTests.cs`, `Tests/FieldTests.cs`, `Tests/MagicTests.cs` | Existing core/stat/equipment/field/Magic regression coverage |
| `Tests/CombatStyleTests.cs` | 15 focused Combat Style tests; 80 core tests total |
| `golden/battle-20260909.log` | Current reviewed stat baseline; 73 events, 2,590 bytes |
| `golden/archive/battle-20260909.pre-stats.log` | Preserved pre-stat baseline; 88 events, 3,080 bytes |
| `REPORT.md` | Phase 1B handoff and the three deferred NON-BLOCKER findings |
| `Visual/Presentation/BattleSession.cs` | Core-owning adapter, immutable Style views, typed BASIC/Technique/Defend/Fireball/Run routes, affordability and readable events |
| `Visual/Presentation/Menu.cs` | Requested UI-only command tree, spatial grid and navigation stack |
| `Visual/Presentation/HarnessController.cs` | Preparation/equipment flow, Physical Style actions, Battle cast draft, menu, target, WIP, text and debug state |
| `Visual/Presentation/GameController.cs` | Field/preparation/battle/game-over mode transitions and input routing |
| `Visual/FieldScreen.cs` | Read-only field/player/enemy rendering using the existing pixel presentation |
| `Visual/BattleScreen.cs`, `Visual/PixelArt.cs` | Input, low-resolution drawing, original bitmap glyphs and sprites |
| `Visual/HarnessQa.cs` | Opt-in real-engine rendering and input checks |
| `Visual/FieldQa.cs` | Real-engine acceptance checks for the full field/battle loop |
| `Visual/project.godot`, `Visual/BattleScreen.tscn`, `Visual/Visual.csproj`, `Visual/NuGet.Config` | Small Godot C# host and local SDK configuration |
| `VisualTests/` | 39 headless presentation tests, including Combat Style and field-loop coverage, without a Godot runtime dependency |
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
