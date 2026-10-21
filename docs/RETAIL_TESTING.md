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
| `./tools/movement-smoke.ps1` | Movement, step/crest contacts, slope jumps, quiet/moving/flip landings, equipment, ledges, ice and Swingshot fixtures. Starts/geometry may be staged. |
| `./tools/rac1-veldin-play-smoke.ps1` | Ordinary input on authored Veldin: natural fall/restart, camera, opening inventory, enemy pursuit/damage and reachable wrench/crate contacts. No transform staging, development respawn or direct consequences. |
| `./tools/rac1-combat-contract-smoke.ps1` | Lower-level staged combat/session contracts; it is not the ordinary-play gate. |
| `./tools/rac1-enemy-smoke.ps1` | Opening toad/robot spawn, path entrances, reactions, attack contacts, rewards, occlusion and retry fixtures. |
| `./tools/rac1-wrench-smoke.ps1` | Input/combo/throw, body and hand attachment, staged damage contacts and recovery. |
| `./tools/rac1-bomb-glove-smoke.ps1` | Glove/bomb skins, socket/throw, arc, impact/splash/occlusion, ammo/cadence and cleanup. |
| `./tools/rac1-crate-smoke.ps1` | Crate/support, debris, bolts, ammo and health fixtures on Veldin/Novalis. |
| `./tools/rac1-placed-bolt-smoke.ps1` | Authored Veldin/Batalia/Pokitaru bolts, floating height, attraction, removal and exact one/five-bolt credit. |
| `./tools/rac1-campaign-smoke.ps1` | Source-game travel and campaign-state integration. |

The movement wrapper requires all three sources and runs `rac1:LEVEL0`,
`rac2:LEVEL1` and `rac3:TABLE1`. To isolate a destination, launch the built
Godot host with `--movement-smoke --destination GAME:LEVEL` and its source.
Each world's result is independent; the reused RAC1 controller is OBP policy.

The Bomb Glove gate accepts `-State PATH` for an immediate throw from a saved
setup. Its `-Rendered -CaptureDirectory PATH` mode stays offscreen/unfocusable
and preserves normal window preferences. Crate/placed-bolt captures use
`OBP_CRATE_CAPTURE_DIR`.
For a personal repro use [MOVEMENT_REPRO_STATES.md](MOVEMENT_REPRO_STATES.md).

Novalis host diagnostics are selected with `--rac1-novalis-swim-smoke`,
`--rac1-novalis-peckbot-smoke`, `--rac1-novalis-birdbot-smoke`, or
`--rac1-novalis-music-smoke` on `rac1:LEVEL1`. CLI dispatch and the actual
fixture bodies live under `game/diagnostics/`.

## Interactive R&C1 controls and checks

Relaunch through the desktop shortcut to build the current working tree.
Controls below are OBP host bindings; previews do not grant campaign ownership.

| Action | Keyboard / controller |
|---|---|
| Jump / aerial boost | Space / south face; release and press again in air |
| Crouch / directional flip | C / right shoulder; hold with side/back input and jump |
| Ledge | Jump toward an eligible edge; move sideways while hanging, jump to climb, crouch again to drop |
| Wall jump | Jump again beside an eligible tall wall; repeated jumps require opposing normals |
| Wrench / Multi-Strike | X / west face; time additional presses for the three-hit combo |
| Hyper-Strike | Wrench while airborne |
| Comet-Strike | Crouch plus wrench |
| Equipped item | E / east face; west face remains the wrench |
| Pack preview | F10 cycles Heli, Thruster, campaign equipment |
| Pack high/stretch jump | Crouch then jump, stationary / while running momentum remains |
| Glide | With a pack, release then hold jump again in air |
| Thruster Power Slam | Airborne, neutral stick, press crouch |
| Swingshot preview | F11; face an authored target and hold E/east, release to detach |

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
