# Minimal equipment and command grid — bounded prototype update

Historical record of the equipment-only revision. The subsequent
[first RPG loop](RPG_LOOP_REPORT.md) starts in Field and preserves the same
player's resources and gear across battles; normal play no longer uses this
report's fresh-preparation reset flow.

Verified 2026-09-09. The disposable harness now starts in preparation, resolves
equipment into stats before battle, and uses a spatial battle-command grid.
No blocking architecture issue was discovered.

## Equipment model and exact values

`EquipmentDefinition` is an immutable record: stable ID, display name, compatible
`EquipmentSlot`, and seven flat integer `EquipmentBonuses`. `EquipmentLoadout`
is a separate immutable selection per slot. Changing a selection produces a new
loadout, replacing rather than stacking the previous item. Slots are enumerated
from the small slot model, so adding a slot does not require adding another
loadout field or rewriting the equip operation. Only these four slots exist:

| Slot | Stable ID | Display name | Bonus |
|---|---|---|---|
| Weapon | `prototype:equipment.wooden_sword` | Wooden Sword | STR +3 |
| Head | `prototype:equipment.cloth_cap` | Cloth Cap | DEF +1 |
| Body | `prototype:equipment.leather_armor` | Leather Armor | DEF +4 |
| Accessory | `prototype:equipment.copper_charm` | Copper Charm | MaxHP +5 |

Adventurer base stats remain **80 HP, 12 MP, STR 12, DEF 8, MAG 6, RES 6, AGI 10**.
Sword gives STR **15**; armor gives DEF **12**; cap and armor give DEF **13**.
Charm gives MaxHP **85**. The existing `StatResolver.Resolve` adds flat bonuses
in a stable slot order with checked integer arithmetic. No RNG is consumed by
preparation, previews, equipment changes or menu navigation.

`CharacterPreparation` owns base stats, current HP/MP and its current immutable
loadout. Changing gear validates the whole result before committing. Wrong-slot
items, invalid slots, invalid resulting stats and overflow are rejected without
partial mutation. On success, HP/MP become `min(current, new maximum)`. Increasing
a maximum never heals or restores MP: **80/80 → 80/85** with Copper Charm.
Replacement/unequip and both resource clamps have focused tests, including
fixture-only resource modifiers; no extra gameplay items were added for tests.

## Snapshot and state ownership

The direction is **Equipment → EffectiveStats → Combat**. `BeginBattle` copies
resolved `CharacterStats` and current HP/MP into the chosen actor seed. The seed
field is now named `InitialStats` because it contains the resolved entry value,
not the persistent character's unmodified base stats. The original encounter
template remains unchanged.

`BattleState` contains no equipment/loadout/preparation reference. It resolves
its own existing Weakened status against the entry stats. Attack and the damage
ops are unchanged and do not query items. `OpContext`, `EffectNode`, `Op` and
`EncounterResult` needed no equipment-specific fields or behavior.

The higher preparation layer retains the active encounter reference solely to
reject equip/unequip and duplicate starts while `IsFinished` is false. There is
no caller-settable unlock flag. Immutable loadout copies cannot change an existing
battle snapshot, including after the encounter ends. No equipment command was
added to combat; ITEMS and Equipment Quick Use remain WIP.

The existing snapshot-in/result-out boundary remains intact. This fixture does
not apply encounter results to a persistent character. R / controller Start after
the encounter creates a **new empty preparation with full base resources and the
same seed**. This explicit reset is not a field loop or persistence implementation.

## Visual and input changes

- Preparation → Equipment → slot → compatible item or None → confirm; Back then
  Start Battle. A compact full stat view remains visible outside battle.
- Item inspection shows only changed stats, such as `STR 12>15` or `DEF 8>12`.
  Previewing does not equip the item or mutate resources.
- Battle displays compact name, HP/MP and status. The permanent STR/DEF/MAG/RES/AGI
  panel has been removed from battle.
- Root commands occupy exactly `ATTACK / DEFEND / MAGIC`,
  `SUMMONING / SKILLS / SPECIAL`, `ITEMS / TACTICS / RUN` in a 3×3 grid.
- Arrows/WASD move by row/column and stop at edges or missing final-row cells.
  WIP submenus use two columns, four visible rows and row-based scrolling.
  Back restores the previous selection. Preparation lists use Up/Down;
  enemy targeting uses Left/Right. Controller D-pad follows the same mapping.
- Existing 149 WIP leaves, deep Back navigation, Attack, Defend, Run, event text
  and debug logging remain available. No WIP action changes combat state or RNG.
- The 320×240 viewport, integer scaling, original sprites, bitmap typography,
  hard pixel borders and limited palette remain. No new image assets or smooth
  UI treatment were introduced.

## Verification and evidence

From the repository root:

```powershell
.\probes\phase1a\launch-visual.ps1 -Verify
```

- **48/48 core tests in Debug and 48/48 in Release**, including 11 equipment
  tests and the existing deterministic/status/stat regressions.
- **15/15 headless presentation tests**, including all nine root positions and
  directional boundaries, ragged submenu grids, all 149 WIP leaves, deep Back,
  preparation, snapshot values, locking and unchanged action routes.
- **213 Godot rendering/input checks**. Real keyboard/controller events are
  injected through Godot's input path; a physical controller was not tested.
- Visual QA equipped Wooden Sword and Leather Armor, verified previews and
  STR 15 / DEF 12 in the battle snapshot, then compared actual first-turn damage
  with an unequipped session at the identical seed. Sword dealt **3 more damage**;
  armor prevented **2 damage per enemy**, for **4 less damage** across both replies.
- QA exercised MaxHP preview/equip/unequip without healing, rejected equipment
  changes during battle, navigated the grid spatially, tested WIP, Attack, Defend
  and Fled, then completed an ordinary unequipped battle in **Victory, HP 7/80,
  MP 12/12**. This visual input sequence differs from the headless golden script.
- Captures are native **320×240**, with at most **25 colors** in these scenes.
  Preparation, previews, equipped slots, battle grid, submenus, targets and final
  result were visually inspected for clipping and readability.

Evidence is in `artifacts/visual/qa.txt` (`PASS ALL`), `engine-qa.log`, and the
native PNG screenshots. Build/QA outputs are ignored generated artifacts.

The existing **73-event, 2,590-byte** golden scenario is unequipped and remains
byte-for-byte unchanged, including fresh-process comparisons in en-US and tr-TR.
Its SHA256 is:

```text
6d8bcd6f0f8eb977f05572e2b383fc3e997aa24548a5a8700a7ac0816c29b6dd
```

The earlier pre-stat baseline also remains preserved. No baseline regeneration
was required. The human-assigned headless fixture labels remain `phase1a-stats-1`
and `literals-stats-1`; these are not hashes of the whole current source tree.

## Files changed

| Files | Change |
|---|---|
| `Probe/Equipment.cs` (new) | Slots, flat bonuses, immutable definition/loadout and prototype items |
| `Probe/CharacterPreparation.cs` (new) | Preparation ownership, equipment validation/resource clamp, active encounter guard and snapshot creation |
| `Probe/Stats.cs` | Equipment bonuses enter the existing effective-stat resolver |
| `Probe/BattleState.cs` | Entry stat field becomes `InitialStats`; no equipment coupling |
| `Tests/EquipmentTests.cs` (new), `Tests/Program.cs`, `Tests/StatTests.cs` | Equipment regression coverage, test registration and entry-field migration |
| `Visual/Presentation/BattleSession.cs` | Accepts a battle created from preparation |
| `Visual/Presentation/HarnessController.cs` | Preparation flow and spatial navigation integration |
| `Visual/Presentation/Menu.cs` | Actual grid coordinates and edge handling |
| `Visual/BattleScreen.cs` | Equipment screens, changed-stat preview, compact battle vitals and grid rendering |
| `Visual/HarnessQa.cs`, `VisualTests/Program.cs` | Real-engine and headless integration checks |
| `README.md`, `STAT_SYSTEM_REPORT.md`, `EQUIPMENT_REPORT.md` (new) | Current instructions/report and historical-report pointer |

Launch interactively:

```powershell
.\probes\phase1a\launch-visual.ps1
```

## Limits and architecture findings

This remains a disposable probe: four predefined owned items, one compatible
prototype item per slot, no quantities or inventory management, no persistent
field flow, and no in-battle equipment changes. The bitmap UI displays English
uppercase. MAG/RES/AGI still have no combat behavior; balance is provisional.

No new architecture blocker was found. Preparation can resolve gear into values
without teaching the encounter, effect evaluator or result structure about items.
The three deferred Phase 1B NON-BLOCKER findings remain in `REPORT.md` unchanged.
