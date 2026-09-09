# First playable Field → Battle → Field loop

Verified 2026-09-09 against the actual Godot application. Normal launch now enters
the field. One encounter can be fought through the existing battle system, and
victory returns to the same map and position with actual remaining HP/MP, the
same gear and a defeated encounter flag. Exploration then continues.

## Existing architecture and connection point

Repository inspection found the .NET 8 probe, Godot host in `BattleScreen.tscn`,
`HarnessController` preparation/battle UI, `BattleSession` adapter, immutable
stat/equipment values and `CharacterPreparation`. There was no pre-existing
Field, persistent GameState or general scene-mode coordinator. The old host
started in preparation and discarded the character on R after battle.

The implementation reuses that character owner rather than adding a second
PlayerState with competing HP/equipment fields:

```text
GameController                     presentation mode/input coordination
  GameState                        application encounter/result coordination
    Player: CharacterPreparation   persistent HP/MP, base stats and equipment
    Field: FieldState              map, position and encounter completion flags
  HarnessController                existing preparation/battle menu behavior
    BattleSession                  existing adapter and enemy action policy
      BattleState                  copied resolved stats; owned battle-only state
```

`FieldScreen` reads state to draw. The existing Godot host routes inputs and
renders battle/preparation; it does not decide collision, defeat flags or result
application. Field data/movement has no engine or combat dependency. The new
`GameState` coordinator uses the `Phase1A.Application` namespace, while field
data uses `Phase1A.World`. No production assembly hierarchy or framework was added.

## Field and input

`PrototypeField.StartingMap` is an immutable literal map, ID
`prototype:field.starting_area`: **19×11 cells, 16 pixels per cell**, spawn **(2,5)**.
Rows define walkable ground (`.`) and solid terrain (`#`). Several interior rock
obstacles and all outer boundaries are solid. `FieldState.TryMove` accepts only
one cardinal tile step and consults this map data.

WASD, arrow keys and D-pad move immediately, then repeat after 0.18 seconds and
at 0.14-second intervals while held. Release removes that input source; the last
pressed direction wins if several are held. There are no diagonal/physics steps
or accumulated catch-up movement after a slow frame. Window focus loss and all
mode changes clear held movement. Only Field can run movement updates, and only
Battle routes input to battle commands. One Godot input entry point is used.

The minimal field HUD shows HP/MP, movement/equipment hints and whether the area
is clear. Original tiny pixel silhouettes identify the player and goblin token.
The existing 320×240, nearest-neighbor, integer-scaled presentation remains.

## Encounter entry, persistence and return

The stationary token at **(8,5)** is encounter
`prototype:encounter.goblin_wolf`, using the existing `Scenario.Setup` enemies
and `BattleSession` behavior: Goblin and Wolf, with Strike enemy responses.
Contact records the approach tile and a pending encounter ID before the battle
is created. Pending contact blocks repeated movement/contact triggers.

`GameState.BeginEncounter` requires that actual contact and a living player.
The existing player's current resolved stats and HP/MP are copied into an
`ActorSeed`. `BattleState` never references the field, GameState, equipment or
persistent character. Neither Attack nor the effect evaluator changed.

When terminal battle text is dismissed, the result screen accepts Enter/A/Back.
`GameState.CompleteEncounter` obtains its own battle's sealed result and asks
the player owner to apply the matching `VitalsChanged` delta once. The persistent
instance ID comes from that actor's entry snapshot, rather than assuming the
first result delta belongs to the player. Other player state, including base
stats and gear, stays on the same object throughout.

The normal game uses a private managed-completion policy: even after combat
becomes terminal, preparation remains locked until this coordinator completes
the return. Direct equip, another battle start or public result application
cannot bypass the pending result. There is no public unlock flag or caller-
supplied result to replay. Repeated completion is rejected. This closes a state
ownership gap that only became relevant when the preparation object persisted.

Outcomes:

- **Victory:** apply remaining HP/MP, mark the encounter defeated in FieldState,
  keep the contact position and return to Field. Rendering hides the token and
  collision no longer triggers that encounter. Nothing is healed or respawned.
- **Fled:** apply remaining HP/MP and return to the approach tile **(7,5)** in
  the demonstrated path. The token stays active. Cleared input and the safe
  position prevent immediate retriggering; deliberate contact can retry.
- **Defeat:** apply HP 0 and enter GameOver. Movement and ordinary confirmation
  do nothing. R/controller Start explicitly replaces the game with a fresh one.
- The existing **Aborted** result also returns to the approach tile if produced
  through the core API; no new abort command was added to normal play.

Field E/Enter/Space/A opens the same equipment preparation UI. Its second action
is now **Return to Field**, and Back at preparation also returns. Gear still
feeds EffectiveStats and is unavailable during Battle. The unchanged clamp rule
prevents automatic HP/MP restoration when equipment maxima increase. The battle
menu remains 3×3 with two-column WIP submenus, and no detailed battle-side stat
table was restored. All 149 WIP leaves and Attack/Defend/Run remain intact.

## Verification

Executed the complete documented command:

```powershell
.\probes\phase1a\launch-visual.ps1 -Verify
```

- **58/58 core tests in both Debug and Release**, with zero build warnings/errors.
  Ten new tests cover map collision, contact locking, same-player ownership,
  real combat results, victory/escape/defeat, exactly-once application, pending
  terminal locks, alternate actor instance IDs and movement/RNG independence.
- **20/20 presentation tests**: the 15 prior regressions plus five tests of
  Field/preparation/battle transitions, persistence, retry and explicit restart.
- **213/213 original Godot battle QA checks** still pass via the opt-in isolated
  fixture. This covers equipment, grid navigation, all original action routes,
  readable events, WIP and original pixel rendering.
- **163/163 new Godot field-loop QA checks** pass through the normal entry point.
  Real Godot key press/release events exercise WASD, held movement/release,
  boundaries and obstacles, contact while a movement key remains held, escape,
  defeat/GameOver/restart, equipment through E/Enter, WIP, Defend, Attack,
  victory/result application and continued walking over the defeated token tile.
- Runtime QA equipped Sword and Armor (STR **15**, DEF **12**), defended once,
  then won through ordinary attacks. Victory returned at **(8,5)** with exactly
  **HP 32/80, MP 12/12**, gear retained and the field encounter defeated. The
  player then walked to **(9,5)** and back through the old encounter position.
- Reviewed the startup, GameOver, equipment, battle, victory, returned-field and
  exploration screenshots: no clipping, restored stat table or smooth rendering.
  The field QA captures retain native 320×240 and the limited palette.

Artifacts: `artifacts/visual/field-qa.txt` ends in `PASS ALL`;
`field-engine-qa.log` reports 163 checks; `field-00-start.png` through
`field-08-continue.png` show the loop. Original `qa.txt`/`engine-qa.log` are
separate. These are generated, ignored artifacts. Input injection verifies
Godot routing, not a physical controller or physical keyboard device.

The original headless **73-event / 2,590-byte** golden remains byte-identical,
including two fresh processes with different cultures. SHA256:

```text
6d8bcd6f0f8eb977f05572e2b383fc3e997aa24548a5a8700a7ac0816c29b6dd
```

No RNG, ability, damage formula, enemy definition or golden baseline change
was needed. Independent ownership/mode/input reviews found no further issue.

## Files

| Added | Responsibility |
|---|---|
| `Probe/Field.cs` | Immutable map data, collision, field position and encounter flags |
| `Probe/GameState.cs` | Application coordination and persistent player/field ownership |
| `Tests/FieldTests.cs` | Ten core movement/persistence/transition regressions |
| `Visual/Presentation/GameController.cs` | Field/preparation/battle/game-over coordinator |
| `Visual/FieldScreen.cs` | Read-only field and GameOver drawing |
| `Visual/FieldQa.cs` | Actual Godot field-loop acceptance run |
| `VisualTests/FieldLoopTests.cs` | Five headless integration tests |
| `RPG_LOOP_REPORT.md` | Current implementation and evidence |

| Modified | Change |
|---|---|
| `Probe/CharacterPreparation.cs` | Owned result application, actor identity and managed-completion guard |
| `Tests/Program.cs` | Register field tests |
| `Visual/Presentation/HarnessController.cs` | Bind the existing player/session; return from preparation to Field |
| `Visual/BattleScreen.cs` | Normal Field startup, mode-scoped input/held movement and existing battle drawing integration |
| `VisualTests/Program.cs` | Register field-loop tests |
| `launch-visual.ps1` | Run field-loop QA after the prior battle QA during `-Verify` |
| `README.md`, `EQUIPMENT_REPORT.md` | Current instructions and historical-report pointer |

Godot generated script UID sidecars for the new visual C# files. Existing scene
entry, rendering settings, battle mechanics, stat/equipment definitions and
architecture documents remain intact.

## Launch and limits

```powershell
.\probes\phase1a\launch-visual.ps1
```

This opens Field. E/Enter opens equipment; contact with the token starts battle;
Enter at the victory result returns to exploration.

This is one bounded loop: one fixed map, one encounter group, no enemy AI,
no saving between process runs and no rewards, growth, quests or additional RPG
systems. The existing seed is reused for retries. The optional legacy
`--battle-probe` / `--qa` developer path remains isolated from normal startup.

No new Phase 1B architecture blocker was found. The required change was adding
an application-owned result boundary to the previously disposable preparation
object. `BattleState`, `OpContext`, `EffectNode`, `Op` and `EncounterResult`
required no new world-specific shape. The prior three NON-BLOCKER findings in
`REPORT.md` remain deferred; no further feature work was started.
