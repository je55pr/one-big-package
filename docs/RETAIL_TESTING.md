# Testing OBP

## Native test gates

```powershell
./tools/test.ps1 -Configuration Release
dotnet format OneBigPackage.sln --verify-no-changes --no-restore
./tools/test-retail.ps1
```

The retail gate requires `OBP_RAC1_ISO`, `OBP_GC_ISO`, and `OBP_UYA_ISO` pointing
to authorized local images. Alternatively pass `-Rac1Iso`, `-GcIso`, `-UyaIso`.
It checks the paths and restores the caller's environment afterward. Portable
CI uses synthetic fixtures and payload-free evidence; retail payloads stay local.
Changes depending on retail bytes need the local retail gate before being called
authority-validated. A portable pass alone does not establish that.

## Godot integration checks

Run each gate to its explicit PASS and zero exit code. Closing a test window is
cancellation, not a pass. Scripts build the current host unless their documented
skip-build option is supplied. Headless/staged fixtures establish integration;
they do not establish a matched retail trajectory or ordinary playability.

| Command | What it checks |
|---|---|
| `./tools/controller-smoke.ps1` | Logical PlayStation button events through real Godot menus, file picker, dropdowns, sliders, repeat, pause, map and weapon selection. Retail sources add world/gameplay checks. |
| `./tools/playtest-smoke.ps1` | Fresh-process Veldin/Novalis input recording and replay, observations/markers, resize/menu timing, and rejection of invalid/divergent files. Repeatability checks, not retail-parity or level-solving claims. |
| `./tools/bug-report-smoke.ps1` | F8/paused-Square reporting, text isolation, pause/Quick Select restoration, shared completed tapes, inbox/ZIP and two fresh-process replays to selected markers. `-Rendered` checks the actual offscreen UI. Player workflow: [BUG_REPORTS.md](BUG_REPORTS.md). |
| `./tools/movement-smoke.ps1` | Movement, step/crest contacts, slope jumps, quiet/moving/flip landings, equipment, ledges, ice and Swingshot fixtures. Starts/geometry may be staged. |
| `./tools/rac1-veldin-play-smoke.ps1` | Ordinary input on authored Veldin: real source-projected opaque pause over the 3D world (capture at `captures/rac1-ui-veldin/root.png`), focus/pause/resume, natural fall/restart, camera, opening inventory, enemy pursuit/damage and reachable wrench/crate contacts. No transform staging, development respawn or direct consequences. |
| `./tools/rac1-combat-contract-smoke.ps1` | Lower-level staged combat/session contracts; it is not the ordinary-play gate. |
| `./tools/rac1-enemy-smoke.ps1` | Opening toad/robot spawn, path entrances, reactions, attack contacts, rewards, occlusion and retry fixtures. |
| `./tools/rac1-wrench-smoke.ps1` | Input/combo/throw, body and hand attachment, staged damage contacts and recovery. |
| `./tools/rac1-player-audio-smoke.ps1` | Retail footsteps/surfaces, jump/flip/combo cues, wrench contacts/flight/catch, pack loops/pause and accepted-hit/combat-death voices. `-Rendered` exercises playback offscreen. |
| `./tools/audio-shutdown-smoke.ps1` | Oozla startup/world-switch cleanup, plus three fresh-process Novalis route/water repetitions when R&C1 is supplied. Requires fixture completion and rejects post-exit ObjectDB/resource leaks even with exit code 0. Full logs: `captures/audio-shutdown-smoke/`; `-SelfTest` checks the log gate without retail data/Godot. |
| `./tools/rac1-bomb-glove-smoke.ps1` | Glove/bomb skins, socket/throw, arc, impact/splash/occlusion, ammo/cadence and cleanup. |
| `./tools/rac1-weapons-smoke.ps1` | Full working arsenal, idle/use poses, audio lifetime, contact, cargo and deployed-agent fixtures. |
| `./tools/rac1-crate-smoke.ps1` | Crate/support, debris, bolts, ammo and health fixtures on Veldin/Novalis. |
| `./tools/rac1-placed-bolt-smoke.ps1` | Authored Veldin/Batalia/Pokitaru bolts, floating height, attraction, removal and exact one/five-bolt credit. |
| `./tools/rac1-campaign-smoke.ps1` | Source-game travel and campaign-state integration. |
| `./tools/rac1-mission-play-smoke.ps1` | Interactive, human-played mission gate: Veldin encounter, Novalis Plumber purchase, death/retry, Aridia travel/revisit, then fresh-process save validation. No generated input or injected unlocks. |

The movement wrapper requires all three sources and runs `rac1:LEVEL0`,
`rac2:LEVEL1` and `rac3:TABLE1`. To isolate a destination, launch the built
Godot host with `--movement-smoke --destination GAME:LEVEL` and its source.
Each world's result is independent; the reused RAC1 controller is OBP policy.

The Bomb Glove gate accepts `-State PATH` for an immediate throw from a saved
setup. Its `-Rendered -CaptureDirectory PATH` mode stays offscreen/unfocusable
and preserves normal window preferences. The weapons gate has the same rendered
capture options. Crate/placed-bolt captures use `OBP_CRATE_CAPTURE_DIR`.
For a personal repro use [MOVEMENT_REPRO_STATES.md](MOVEMENT_REPRO_STATES.md).

Novalis host diagnostics are selected with `--rac1-novalis-swim-smoke`,
`--rac1-novalis-peckbot-smoke`, `--rac1-novalis-birdbot-smoke`, or
`--rac1-novalis-music-smoke` on `rac1:LEVEL1`. CLI dispatch and the actual
fixture bodies live under `game/diagnostics/`.

## Interactive R&C1 controls and checks

The mission play gate launches its own clean campaign and retains the save/logs
under `captures/mission-play-*`. Play Veldin to the Clank encounter and acknowledge
the conversation cards with Cross/Enter. On Novalis, collect 500 bolts, approach
the Plumber and press Triangle/Q to purchase the Infobot. After Aridia unlocks,
die and retry normally, use Select/R3 (keyboard M) to travel to Aridia, then
return to Novalis. The observer finishes and the wrapper opens a second process
to verify the saved admissions and retired purchase actor. Avoid testing equipment,
movement-state loading, developer flight and Worlds browser shortcuts during this
check. Closing early is cancellation. Scene cards are an OBP substitute for
unimplemented cinematic playback; original scripts still own the sequence and
unlock. This interactive gate is pending a complete human playthrough.

Relaunch through the desktop shortcut to build the current working tree.
Controls below are OBP host bindings; previews do not grant campaign ownership.

For Ratchet audio, compare low-stick walking/full running on different floors,
jump/double-jump/flips, the three Square swings, airborne Square and R1+Square.
Try both pack glides and pause/resume while a loop plays. Cues use the retail
animation keyframes and level sound banks. Player self-audio is listener-relative
in Godot; native spatial mixing, SPU pitch quantization and every grain opcode
are not reproduced exactly. Native sound probability programs may choose silence.
Standard enemy-hit and normal combat-death audio use the original reaction
timelines independently while OBP's complete damage motion/pose owner is absent.

Menus use **D-pad / left stick** to navigate, **Cross** to select and
**Triangle** to return (**Circle** is also accepted). **L1 / R1** moves between
controls. Lists follow focus and held directions repeat. Start leaves the title.
Start pauses/resumes gameplay; Select or R3 opens/closes the current R&C1 map
presentation. The map, pause and quick-select overlays suspend world simulation.

| Action | Keyboard / controller |
|---|---|
| Jump / aerial boost | Space / Cross; release and press again in air |
| Crouch / directional flip | C / R1 (R2 also in R&C1); hold with side/back input and jump |
| Ledge | Jump toward an eligible edge; move sideways while hanging, jump to climb, crouch again to drop |
| Wall jump | Jump again beside an eligible tall wall; repeated jumps require opposing normals |
| Wrench / Multi-Strike | X / Square; time additional presses for the three-hit combo |
| Hyper-Strike | Wrench while airborne |
| Comet-Strike | Crouch plus wrench |
| Equipped item | E / Circle; Square remains the wrench |
| Weapon selection | Hold Triangle / Q, point with stick or D-pad, release to equip; L1 / R1 changes page; double-tap Triangle swaps the last two equipped items |
| Camera centre | L1 / Home |
| Pack preview | Pause → Testing Equipment, or F10; cycles Heli, Thruster, campaign equipment |
| Pack high/stretch jump | Crouch then jump, stationary / while running momentum remains |
| Glide | With a pack, release then hold jump again in air |
| Thruster Power Slam | Airborne, neutral stick, press crouch |
| Swingshot preview | Pause → Testing Equipment, or F11; face an authored target and hold Circle / E, release to detach |

Save/load test states, arsenal preview/refills and returning to Worlds are also
available from Pause. No keyboard shortcut is required for these testing flows.
The eight-position paged weapon presentation, quick-tap timings, camera reset
and galactic travel screen are OBP adapters. This pass does not recover native
first-person aiming, GC/UYA strafing modes or the retail local-map renderer.
The ordinary gameplay analogue deadzones/response remain source-owned; the menu
repeat/deadzone does not change them. Device mappings use SDL logical buttons.

The full weapon selection/preview controls are in
[RAC1_WEAPONS_TESTING.md](RAC1_WEAPONS_TESTING.md).

Useful manual checks:

- Walk/run and release, resume or reverse during a stop, then jump or crouch.
  Quiet and flip landings should return to smoothly sampled idle; running jumps
  should launch on shallow descending seams. Compare both packs' stretch flight
  and touchdown carry with crouch released and held.
- Check the wrench's neutral jaw shape and hand attachment after both running
  and jumping. Combo, Comet catch and Hyper recovery should return control.
- Break a stack's bottom support; check falling crates, fragments and homing bolt
  arrival. Compare metal crates under wrench/Bomb damage. Ammo/health pickups
  should wait when full; one Nanotech cloud restores one point despite particles.
- Test authored loose/floating bolts and Comet attraction at a distance. Credit
  belongs to arrival, not initial contact. Campaign wallets and per-level removal
  records have separate lifetimes; use repro states to retain a test setup.

Recovered dimensions still feed host contact queries and a Godot capsule.
Source-backed timing/model facts, host geometry/presentation policy and live
retail comparisons are separate evidence. Remaining native limits belong in
the subsystem references indexed by [research/README.md](../research/README.md),
rather than dated test totals or completed-task reports.
