# Movement repro states

## Record a complete playtest

For bugs that depend on the route you took, record ordinary play from a fresh
R&C1 level entry. From PowerShell in the checkout you want to test:

```powershell
./tools/record-playtest.ps1 -Destination rac1:LEVEL1
```

This builds and launches that checkout. The default is Novalis; use
`rac1:LEVEL0` for Veldin. To begin with earned campaign progress, add
`-CampaignSave <campaign.json>`; the launcher copies it for this session.
Play normally, then:

- **F8** marks the bug without ending the recording.
- **F9** saves the run and stops recording. You can continue playing afterward.
- With a controller, **Start** pauses, then **Square** marks and **Select** saves.
- Keyboard shortcuts require the game window to have keyboard focus; controller
  input can still work in the background. The recorder shows a focus reminder.
- Closing the game also finishes the completed input steps. A crash or disk
  failure leaves an incomplete `.partial` file, which cannot count as a pass.

The on-screen recorder notice confirms saving. The launcher prints the exact
file under `captures/repros/`, named `*.run.jsonl.gz`. Give that single file to
the developer and say which marker/end point shows the bug. The file contains
the initial campaign, game-window controls, camera settings, simulation callback
order, window sizes and periodic observations of player/camera/objects/enemies,
inventory, projectiles, pickups and progression. Bug markers and F9 force an
observation. It contains no retail assets, disc paths or typed text.

Replay it in a fresh process without desktop control:

```powershell
./tools/replay-playtest.ps1 -Run <run.jsonl.gz>
```

The JSON report beside the run identifies the first differing observed field,
its step, expected/actual values, bug markers and the final observation. Replay
continues after a state difference so the end remains inspectable. Exit **0**
means the observed checkpoints matched, **3** means divergence, and **2** means
incompatible, invalid or incomplete input. Structural timing/input-count errors
abort. A match establishes repeatability of those observations, not exhaustive
world equality or retail parity. Checkpoints are never injected to correct drift.

The engine version, source build, platform and physics backend must match.
All referenced OBP assembly identities must match unless `-AllowBuildChange` is supplied explicitly
when investigating a fix; the report identifies this exception. `-Rendered`
replays in an unfocusable offscreen window. `-SkipBuild` reuses an existing build.
The wrappers use a fixed 60-Hz simulation. The visible human recorder explicitly
paces its callbacks because Godot's `--fixed-fps` bypasses real-time synchronization;
`--max-fps` alone is insufficient in that mode. A slow frame stretches wall time
rather than dropping simulation steps.
Gesture timestamps and post-calibration player controls are recorded separately,
so replay can run faster without changing quick-select/button timing.

This first version supports R&C1 gameplay, pauses and ordinary in-game travel.
It starts at level entry, with seeded random streams; it does not restore a live
mid-combat bookmark. Loading movement states is blocked while recording. Returning
to the developer Worlds browser/other games ends the run as unsupported. Native
file dialogs and device calibration are not replay fixtures. Replayed camera and
controller settings do not overwrite personal settings. Boundaries are limited
to one million callback steps, 128 MiB compressed and 512 MiB expanded input;
keep a diagnostic run focused. The background writer has a bounded queue and
fails explicitly if storage cannot keep up.

`./tools/playtest-smoke.ps1` checks fresh Veldin and Novalis record/replay, including
analogue input, jump, camera, weapon use, wrench, Quick Select, resize, controller
pause/menu navigation, marker/save, and deliberately invalid/divergent files.
These use ordinary input at authored starts; they do not solve either level.

## Save a stationary movement bookmark

In an R&C1 world, release the controls and stand at the starting point for the
bug. Point the camera so ordinary forward input takes Ratchet into it.

- **F12** saves the movement setup. An on-screen message confirms success.
- **Shift+F12** loads it. The pause menu also has Save/Load Movement State buttons.
- The default file is `captures/repros/quick-state.json`, relative to the checkout.
  Saving again replaces this quick slot. These local repro files are ignored by Git.

The save contains the destination and source-build identity, exact player
coordinates and facing, camera/control state including its recurrence, movement
equipment and development previews. It also records the host assembly ID so
different compiled builds can be identified. Loading preserves the saved origin
without the normal spawn-to-ground snap. Controls and movement start neutral.
Version2 also stores removed world objects, inventory, selected item, ammo and
the bolt wallet. Defeated enemies stay absent, including enemies still playing
their lethal reaction when saved. Broken crates retain removal and UID state;
loading does not emit their rewards again. Collected authored bolts stay absent.
Optional Version2 fields also retain Suck Cannon cargo identities and the weapon
preview flag. Loading a preview restores its separate inventory without exporting
those weapons to the campaign save. Live mines and Doom bots remain transient.
Player positions use Godot scene coordinates `(-native X, native Z, native Y)`.
The named source-camera recurrence fields keep their native coordinate convention.

These are development repro bookmarks. Loading rebuilds the level's controllers
and applies saved removals. It does not restore campaign progression, living
enemy positions/AI clocks, player health, airborne motion, attack clocks,
animation phase or transient projectiles/loot effects. Saving requires a
stationary, alive, grounded player with fly disabled. They are OBP files and are
not compatible with PCSX2 states or retail memory-card saves.

Older Version1 files remain loadable but contain only the player/camera setup:
their enemies and crates reset. After clearing enemies, save again with F12 to
record their absence. The loader identifies these older files in its notice.

## Replay a movement bookmark without computer use

From PowerShell in the checkout:

```powershell
./tools/replay-movement-state.ps1 -Frames 180
```

The wrapper builds the current Debug host, loads the quick slot, holds only
ordinary forward for the requested number of completed physics updates, writes
`captures/repros/forward-replay.json`, and quits. It uses the supported local
R&C1 source from `OBP_RAC1_ISO` or the remembered source library. It opens no
visible window. Use `-State` and `-Out` for named cases and `-SkipBuild` when the
host is already current. Exit zero and `[movement-replay] PASS` mean the trace
was captured successfully; they do not assert that the movement bug is fixed.

Equivalent Godot user arguments after `--`:

```text
--load-movement-state <state.json> --replay-forward 180 --replay-out <trace.json>
```

Omit `--replay-forward` to load the case for interactive play. Optional
`--movement-state-file <path>` changes the interactive quick slot.

Add `-Profile` to the wrapper for elapsed time, player controller/contact/animation
timings, initial slide versus step-query cost, engine FPS and viewport CPU/GPU
render timings. `-Rendered` runs the real renderer in an unfocusable offscreen
window; it bypasses saved window placement and does not overwrite preferences.
The default rendered backend is Forward+, with `-RenderingMethod gl_compatibility`
available for a backend comparison. For example:

```powershell
./tools/replay-movement-state.ps1 -Frames 600 -Profile -Rendered -Out captures/repros/profile.json
```

The equivalent profiling user argument is `--replay-profile`. Profiling adds
non-deterministic timing fields; ordinary traces omit them. Exclude startup
samples when comparing steady-state timings because engine monitors can still
refer to the level-loading frame.

The trace includes each physics tick's position, velocity, facing and control
heading, movement phase, support/floor flags, contact normals, and the small-step
adapter's last decision. It contains no geometry, textures, executable bytes or
source-disc paths. Save/load and camera continuation are tested separately from
any claim of retail movement parity.
