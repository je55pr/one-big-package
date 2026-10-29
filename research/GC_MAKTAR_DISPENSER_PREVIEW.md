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

## Live-tester facing and smoothness correction

The first interactive test revealed two specific errors in the initial
visual diagnostic. The extension played in visible 15-Hz steps, and the
barrel pointed into the outer arena wall.

All six retail placement Z rotations point the model's local **+Y**
extension axis radially **away** from arena centre (500,601); their
forward-axis dot product with the inward radial vector is approximately
-1.0. The source does not tell us which runtime articulated-state
rotation would be applied by the dispenser gameplay actor. The **preview**
now adds 180° around the native Z/up axis at the instance root, facing
the extending model toward the arena. This does not rewrite the authored
transforms or assert the real game's initial state.

The decoded regular sequence-0 has 10 keys at 0.25 × 60 = **15 keys/sec**.
The diagnostic previous-frame-only renderer would jump between those keys
and snap immediately back to the start. To make motion inspectable, the
preview now interpolates **six positional samples per key interval** and
plays an extend-then-retract ping-pong cycle, 108 display samples at
90 samples/sec. This is **vertex-space interpolation of source poses**,
not recovered native quaternion blending or event timing, and is limited
to the opt-in diagnostic. Ordinary source model animation is unchanged.
The authored six placements and arena collision remain untouched.

Godot 4.7.2 Mono renders the corrected side-on angle and live-player
view with exit code 0; regression tests check six inward radial angles,
all ten original keys, intermediate samples, continuity at the loop
seam and preserved native collision.

## Second live-tester correction: actual forward axis and all six placements

The test player confirmed that the interpolation was smooth, but the
previous 180-degree rotation still aimed the dispenser incorrectly. The
error was in our *direction test*, not in the authored model: GC's
`MatrixFromPosRotScale` maps native local +Y to
`(sin(yaw), cos(yaw))`, while our first regression check used
`(-sin(yaw), cos(yaw))`. We now compute an explicit preview-facing yaw
with `atan2(500 - instanceX, 601 - instanceY)`, and test the actual
importer matrix against all six native placements, not a hand-derived
yaw sign. This is an OBP preview calibration, not an asserted native
actor-root animation.

The debug preview also supports `--maktar-dispenser-preview -1`, which
displays **all six** source-authored class-2581 placements. Only
instance 394's full mesh is animated; the other five show the original
housing faces bound to joints 3 and 4 (660 original triangles per
housing). The moving boom (joint groups 0 and 1) is suppressed on
inactive units to avoid six simultaneous arms blocking the arena.
That filtered housing is a diagnostic stand-in for an unknown native
idle state, not a replacement model or reconstructed gameplay logic.

To test all six at once:

```powershell
./tools/play.ps1 -Planet 2 -MaktarArena -MaktarDispenser -MaktarDispenserAnimate
```

A specific native instance 394..399 can still be selected with
`-MaktarDispenserInstance`, rendering its complete moving mesh alone.
The original Maktar collision, authored transforms, level meshes and
normal-play mode are unchanged.

## Third live-tester correction: active arm only

The player confirmed that the five inactive housings were already at the
correct locations. However, the one animated arm was still **180° out**.
This is a useful distinction: the visible geometry extending from class
2581 is not necessarily the model axis assumed by the radial facing
test. Rather than rotating the entire set of six and breaking the accepted
housings, the opt-in full/animated dispenser now receives exactly a
half-turn relative to its previous preview facing. Instances 395–399
keep their existing housing-only rotations and all six retain their
source positions. The display interpolation and source clip are unchanged.

A new test checks that the complete native instance matrices for each
housing and corresponding active model point in opposite directions
while retaining their common origin and vertical axis. This is a
tester-guided **visual preview correction**, not evidence for retail
actor orientation or original arena wave timing.

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
