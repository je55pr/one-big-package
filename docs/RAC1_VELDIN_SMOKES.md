# R&C1 Veldin smoke boundaries

Ordinary-play and staged Godot-host smokes cover different questions. Keep their results separate.

Require each gate's explicit `PASS` line as well as a zero process exit code.
Closing an automated test window is a cancellation and now exits with failure;
older runs with `desktop-window-close` are not passed smoke evidence.

## Ordinary-play Veldin gate

`tools/rac1-veldin-play-smoke.ps1` launches `rac1:LEVEL0` through the normal
source/destination provider path with no campaign persistence enabled. The host
therefore begins from the same ephemeral opening state used by ordinary local
development: current level 0, no admitted travel destinations, Wrench equipped,
Bomb Glove owned with six rounds, four Nanotech, and no prior crate/projectile
session mutations.

The smoke then drives only the public player input boundary. It verifies:

- natural full-forward Veldin fall, recovered state `0x77` / sequence 11 /
  Nanotech 0 boundary, automatic gameplay restart to the authored class-0 start,
  restored Nanotech/motion/yaw/camera presentation, and resumed ordinary control
  without development respawn input;
- recovered type-0 horizontal camera orbit and recovered vertical manual orbit
  through the ordinary camera InputMap actions while Ratchet remains stationary;
- ordinary keyboard selection of the Bomb Glove and one primary-action fire,
  proving the opening six-round grant is usable by observing the normal 6 -> 5
  inventory transition;
- all 16 authored Veldin class-749 placements registered into the recovered
  runtime population while remaining idle at the authored Ratchet start, preventing
  the old privileged one-enemy pursuit regression;
- polygon-driven class-749 activation during ordinary movement, with multiple
  placements in the same authored group able to respond;
- natural pursuit/attack marker damage, including a real 4 -> 3 Nanotech hit;
- a Wrench selection/action against the hostile after gameplay itself establishes
  contact range;
- practical class-500 contact reached by ordinary movement. A test-side
  collision-grid route chooses reachable terrain and may press the normal jump
  action after sustained lack of planar progress; it never writes Ratchet's
  transform. The final Wrench action must increase the live destroyed-crate count.

The gate intentionally does **not** teleport Ratchet or enemies, write checkpoint,
death or attack state, mutate ammo, press the development R-respawn control, or
invoke consequence handlers in place of input. The route planner and low-stick
settling used to make the final crate contact deterministic are test policy only,
not recovered navigation AI.

The directional input driver inverts the recovered per-axis stick deadzone so
its requested full run stays a run at diagonal headings. Previously a raw unit
circle could condition into the walk band, causing a six-second route timeout
despite steady forward progress. This changes test input only; gamepad input
conditioning and the route's movement/consequence assertions remain unchanged.

Earlier revisions of this smoke pressed R after observing the recovered death
boundary. That proved only the development manual-respawn seam and did **not**
prove natural gameplay restart. Those R-assisted results must not be cited as
automatic-respawn evidence.

A separate normal player-mode verification, launched without the smoke flag,
held only ordinary forward input. Ratchet repeatedly crossed the natural Veldin
gate, entered state `0x77` / sequence 11 / Nanotech 0, automatically restarted
at authored class 0 with Nanotech 4, and resumed the still-held forward input.
No development respawn input was sent. This is host-integration evidence for the
automatic path, not evidence for an unrecovered retail death-animation duration.

CLI flag: `--rac1-veldin-play-smoke`. The historical `--rac1-combat-smoke` flag
is retained as an alias for this honest gate so old invocations no longer select
the staged harness.

## Human playability signoff

On 2026-09-25, after the ordinary-play and synthetic retail-backed gates passed,
the current `rac1:LEVEL0` build was launched in normal interactive player mode
against the supported NTSC-U source. Jess was asked to verify only the claimed
runtime behavior: ordinary movement/camera/jump, natural class-749 activation
and damage, Wrench contact against an enemy and crate, Bomb Glove firing/ammo
consumption, and automatic Veldin fall restart without the development R key.

Jess's reported result was: **"no failures."**

This human signoff is intentionally bounded to those claimed behaviors. It does
not promote unresolved host contact dimensions, presentation tuning, surface
motion laws, mission semantics, or unknown native selectors into recovered
retail behavior.

## Movement terrain regression gate

`--movement-smoke` includes staged starts on the imported eastern and southern
ship-rock rims and the exact user-saved snag pose beside the looped rocks. It
also retains blocking at the subsequent saved dense-contact performance case,
and crosses synthetic 0.12/0.22/0.30/0.38-unit steps, with additional walking
and diagonal approaches. The trials drive normal stick input and require
crossing without entering an airborne action. Negative fixtures retain blocking
at a 0.43-unit lip, a taller wall and a step beneath insufficient headroom. These
checks precede the existing slope-jump, landing, equipment and ledge trials.

The starts and synthetic geometry are test policy. The shallow-step/crest
adapter remains a bounded Godot approximation, described in
`../research/RAC1_LEDGE_TRAVERSAL_AND_COLLISION_PROFILE.md`; a pass does not
establish the exact retail step limit or reproduce its radial contact solver.

The collision-shape consolidation also has a deterministic structural gate:
LEVEL0 keeps 31,423 exact geometry/material faces from 86,184 source references,
with every emitted face mapped back to its unchanged source. Rendered replay
profiling is available through `tools/replay-movement-state.ps1 -Profile -Rendered`.
Keep those timing observations separate from functional smoke assertions.

The cross-game movement harness still has a pre-existing GC LEVEL1 startup
failure: its ordinary spawn snap selects terrain around height 124 instead of
the authored height 115, then walking leaves support. Earlier
`movement-clock-gc-smoke` and `movement-offline-smoke-gc` logs already show the
same failed walk-plateau assertion before collision consolidation. A Veldin
pass does not establish sequel playability.

## Synthetic combat host-contract gate

`tools/rac1-combat-contract-smoke.ps1` selects
`--rac1-combat-contract-smoke`. Its implementation lives in
`game/diagnostics/OBPGame.Rac1CombatContractSmoke.cs`. This is explicitly a lower-level synthetic
integration harness: it may stage transforms and call narrow internal seams to
isolate wrench/contact, projectile, presentation, recovered Veldin population
gating and lifecycle contracts.

A pass from the synthetic contract gate is useful for regression isolation, but
must not be cited as proof that a player can reach the same setup from an
ordinary local-dev start. End-to-end playability claims belong only to the
ordinary-play Veldin gate above.

## Retail boundary

`tools/rac1-enemy-smoke.ps1` adds a separate staged opening-enemy gate, with a
fixed host random seed. It checks all 25 animated/contact bodies, health 1/2,
nonlethal and lethal reactions, dedicated robot fragments, exact emitted-bolt
arrival accounting, stationary toad rest clips, all eight authored robot
entrances from their script-initialized path starts, active socket/player contact,
ordinary wrench/Bomb Glove input against robots, wall occlusion and the explicit
OBP combat-death retry button. It deliberately stages transforms and supplies
admitted descriptors for the final retry-UI fixture. Its result must not be
presented as an ordinary-play route or live retail replay. Current recovery and
presentation limits are in `../research/RAC1_VELDIN_ENEMIES.md`.
The entrance fixture bounds both distance from the authored route and endpoint
error of the live Godot contact bodies. Entrances use recovered direct XYZ path
updates, including height, rather than host floor projection. This guards
against editor-placement backtracking, overshoot and unreachable endpoints.
The opening-pair fixture also checks both toads' original spawn clearance and
180 approach frames on the imported terrain. It stages only Ratchet for that
check, tracks below-floor clearance and minimum height, and requires both
creatures to pursue. This catches a buried spawn and subsequent tunnelling that
an attack check accepting either creature could miss.

These smokes exercise production code backed by the repository's retained R&C1
evidence, but neither converts OBP host policy into retail evidence. In
particular, the current Wrench spatial admission dimensions and the smoke route
planner remain host/test policy. Native consequences stay separated from those
host-side contact choices as documented in `../research/RAC1_WRENCH_COMBAT.md`.
