# Maktar class-2581 dispenser: opt-in native skeletal visual study

Source: Going Commando NTSC-U v1.01, `/G/LEVEL2.WAD`, level-core
class table and gameplay Moby instances. Does not ship retail asset bytes.

## What the source actually proves

Six authored class-2581 placements (native indices 394–399) surround the
Maktar Galactic Gladiators arena. All have scale 1.0 and a 0x30-byte,
zero-initialised authored PVar. Each has mode bits zero. The class asset
contains **1,403 vertices, 1,188 triangles, five joints and four regular
animation sequences** with 10, 8, 8 and 10 frames. The first sequence has
ten fully decoded native frames and complete *rigid* joint assignments,
though the general imported mesh does not qualify for the blended-skin
native initial-pose renderer.

The old translation-only/static approximation gives a largest triangle
edge of about 29.64 world units; a native regular-key pose has ~15.06.
Those are **different poses**, not permission to truncate vertices or
rescale the authored object.

A native first-key AABB for placement 394 is approximately native
X 499.7–520.9, Y 547.4–575.2, Z 499.4–510.4.
All six are placed on/behind the arena perimeter, not at six arbitrary
positions in the centre. Their assets do not carry an authored PVar
selector identifying which sequence should be used for the initial
presentation. Original gameplay ownership and timeline are not yet
recovered; simply displaying or looping all six native first keys
leaves large mechanical barrels obscuring the arena.

## Bounded reproduction, without enabling broken normal gameplay

`Rac2WorldImport.WithMaktarDispenserPreview` is a
**source-qualified, opt-in diagnostic**. It reopens the authorised source
and places **one** source class-2581 Moby as independent
`RuntimeAnimatedMesh` objects, split by the native material groups.
It preserves the original native triangle/UV identities, six source
instance positions and per-instance transforms. First-sequence frames
are evaluated by `Ps2MobyNativePose` from original skin-local positions
and retail joint matrices, not by re-skinning the old bind approximation.
Diffuse vertex colours combine the authored per-instance ambient and
its assigned directional light; the preview locks lighting at the
first frame. All collision, progression, enemies and normal runtime
world meshes remain unchanged.

Single-key paused/inspection preview:

```powershell
./tools/play.ps1 -Planet 2 -MaktarArena -MaktarDispenser -MaktarDispenserInstance 394
```

An independent sequence-0 visual loop:

```powershell
./tools/play.ps1 -Planet 2 -MaktarArena -MaktarDispenser -MaktarDispenserInstance 394 -MaktarDispenserAnimate
```

Other native instances are numbered 395–399. The dedicated shot list
`tools/shots/maktar-dispenser-394.json` allows close camera views via
Godot's existing `--shots` service, adding
`--maktar-dispenser-preview 394`. Frame captures and images are
local QA evidence, not committed retail textures.

## Status and next work

The static class-2581 meshes **remain hidden in normal gameplay**.
Activating all six in sequence-0 first frame still blocks the fighting
platform, so restoring all of them without native actor lifecycle is
**not** a defensible fix. In-engine per-instance capture demonstrates
the actual mesh against the authored architecture, and keeps every
triangle source-backed. Future work must recover class-2581's native
activation/visibility state and sequence selection, then tie it to arena
wave release. Do not equate this visual preview with implemented enemy
spawning or verified original-game parity.
