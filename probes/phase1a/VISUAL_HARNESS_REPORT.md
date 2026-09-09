# Disposable visual harness — integration findings

> Historical report before the minimal stat feature. For the current damage
> formula, stat display, test counts and revised golden, see
> [STAT_SYSTEM_REPORT.md](STAT_SYSTEM_REPORT.md).

Verified on 2026-09-09 with Godot 4.6.3 .NET, .NET SDK 8.0.425,
Windows x64 and the OpenGL Compatibility renderer.

## Delivered

- Original hand-authored Goblin and Wolf bitmap sprites, 5×7 bitmap lettering,
  hard pixel borders and a dark field. Native viewport 320×240; initial window
  960×720; integer nearest scaling. Captured screens use at most 25 colors.
- Attack with independent living-enemy targeting, Defend, guaranteed Run.
- Nine root commands, the complete requested provisional hierarchy, 149 named
  WIP leaves, scrolling, breadcrumbs, selection restoration and explicit Back.
- Readable event pages, authoritative HP/MP/status display, F2 event inspector,
  F3 original-log export, and a fresh encounter after a terminal result.

Launch and controls are in [README.md](README.md). The single verification
command is `./probes/phase1a/launch-visual.ps1 -Verify` from the repository root.

## Verification evidence

| Check | Result |
|---|---|
| Core Debug build and behavior/regression suite | 25/25, zero warnings or errors |
| Core Release build and behavior/regression suite | 25/25, zero warnings or errors |
| Presentation model suite | 8/8, including every WIP leaf and unchanged next-attack RNG behavior |
| Godot C# build | Zero warnings or errors |
| Actual Godot input/rendering QA | 122 checks passed; keyboard and synthetic controller inputs |
| Pixel captures | 11 native 320×240 PNGs; 5–25 colors per captured screen |
| Original golden log | Exact bytes unchanged, including independent processes with en-US and tr-TR cultures |

The QA reaches Fire and Dragon WIP dialogs, a scrolled Primordial submenu,
cancels a selected Wolf without taking a turn, attacks Wolf independently,
defends against both enemy responses, escapes without an enemy response, and
restarts. It then defeats Goblin with ordinary attacks, removes it from legal
targets, and continues until the hero loses to Wolf. Ended encounters reject
further combat commands. F2 inspection does not mutate the battle; F3 preserves
the canonical seven-field log format.

Evidence is generated in the ignored `artifacts/visual/` directory: `qa.txt`,
`engine-qa.log`, `machine-events.log` and PNGs. The F3 sample log is from the
moment the QA invoked export, not a replacement for the baseline encounter.
No physical gamepad, exported executable, other OS, or production asset pipeline
was tested or delivered.

Golden SHA256 remains:

```text
412e6d9a1a1a7bd1f27a51779b3512419681fe045e5283fb9975c2e47f5c98f2
```

## Boundary observations

**No BLOCKER was found while connecting the UI.** The core still has no Godot
reference. The only owner of its mutable `BattleState` in the harness is
`BattleSession`; menus and drawing receive immutable actor/event values.

| Shape | What the integration established |
|---|---|
| `BattleState` | Needs an authoritative command entry, read-only live actor/status views, an event stream that can be inspected before termination, and an immutable terminal result. A private guard boolean with core-owned expiration suffices for this experiment. |
| `OpContext` | Remains simulation-only. Input, UI nodes, fonts and displayed strings did not need to enter effect evaluation. |
| `EffectNode` | Existing Strike nodes could be reused unchanged. Menu hierarchy and WIP labels do not belong in effect definitions. |
| `Op` | Damage remains responsible for post-defense mitigation and actual applied amounts. Guard reduction must happen before HP clamping so prior-result effects still receive actual damage. Rendering does not calculate damage. |
| `EncounterResult` | A distinct `Fled` outcome is sufficient for escape. Its immutable values/events remain detached from the live display, and Run seals the encounter before the enemy controller can act. |

The old `Command` assumed every action contained an ability. The bounded change
was a `CommandKind` discriminator for Ability / Defend / Run with validation of
the corresponding payload. This is a disposable representation, not a general
action hierarchy. Existing ability definitions, evaluator ordering and golden
fixture decisions did not change.

Defend retains `floor(max(0, magnitude - defense) / 2)` damage before the HP cap,
through both enemy turns, and ends at the next accepted action. Guarded actual
damage, odd-number rounding, rejection behavior, existing status duration,
escape sealing and malformed commands have dedicated core tests.

## Limits retained deliberately

The adapter has fixed actor IDs for this three-actor fixture and uses Strike
for enemy turns. No healing/spell input exists, so MP remains 12 and the visual
fixture cannot apply Weakened. The headless fixture continues testing the
original three abilities and one status. Defending prevents some damage but
cannot create progress by itself; repeatedly defending can end in defeat.

One action and all enemy replies resolve before the first text page is shown.
HP therefore shows the latest core state while messages describe that completed
cycle. There is no timing model or incremental animation playback. The bitmap
font displays uppercase English, and control bindings are fixed.

The three prior **NON-BLOCKER** findings remain documented in
[REPORT.md](REPORT.md#three-deferred-findings-for-phase-1b): persistent status
payload/source identity, total modifier ordering, and concrete C# exhaustiveness
support. The harness did not expose a reason to reclassify them or freeze its
exploratory menu tree into the Phase 1B domain architecture.

## Source changes

- Added `Visual/`: Godot host/configuration, original pixel graphics, one drawing
  surface, opt-in engine QA, and the small presentation adapter/menu/controller.
  Godot's generated `.cs.uid` sidecars preserve script resource identities.
- Added `VisualTests/`, `launch-visual.ps1`, and this report.
- Extended `Probe/BattleState.cs` and `Probe/Rules.cs` with the bounded Defend/Run
  behavior and immutable status/guard views; extended `Tests/Program.cs` by seven
  behavior tests.
- Updated `README.md` and ignored Godot's generated `.godot/` cache.
- The architecture documents, `Scenario.cs`, and original golden baseline were
  preserved. No production UI, content schemas, registries or future mechanics
  were introduced.
