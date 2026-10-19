# Movement repro states

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

## Replay without computer use

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
