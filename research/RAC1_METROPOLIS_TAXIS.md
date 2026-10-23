# R&C1 Metropolis taxis (Kerwan, native LEVEL3)

Status: **UNFINISHED / WORK IN PROGRESS.** Merged as a playable, source-backed taxi foundation, not retail-parity complete.
Native class-816 inbound/outbound/departure sequencing is recovered and
implemented at the gameplay level; camera, dialogue, movement recurrence,
animation and mission gating are not yet retail-identical. No PCSX2 used.

## Original-game source recovery

Used the pinned NTSC-U retail ISO and the tools and Moby registrations from
https://github.com/daino-selvatico/rac1-native-game:

- Kerwan class 816 update: native address `0x002d3198`.
- Kerwan class 1012 update: native address `0x002dccc0`.
- The repository's `tools/codegen/gen_mkall.py gen kerwan` generated C++
  from the user's ISO under a separate temporary research directory,
  **outside this OBP repository**.
- Class 816 update dispatches up to eight native moby states (0..7);
  class 1012 dispatches four (0..3).
- Both functions use native `point_in_box` (boarding / activation-related
  spatial checks), waypoint lookups, `hero_cam_target_set`, and
  `attach_frame_set`. Trigger, camera and attachment parity is **not**
  implemented yet.

Retail native taxi placements (native XYZ, Z-up):

| Instance | Class | Position | PVar size | Outbound selector | Native path |
|---:|---:|---|---:|---|---:|
| 919 | 816 | (179.77748, 244.59235, 50) | 432 | +0xa8 | 66 |
| 920 | 816 | (264.25, 93.75, 34) | 432 | +0xa8 | 71 |
| 921 | 816 | (264, 85, 56) | 432 | disabled (-1) | none |
| 961 | 1012 | (280.74213, 149.90324, 48.165154) | 240 | +0x94 | 59 |
| 962 | 1012 | (207.35722, 133.20839, 61.993126) | 240 | +0x94 | 67 |

The original level's path block is decoded using `Rac1Paths.Read`,
rather than constructing synthetic Bezier curves.

| Taxi | Outbound path | Path points | Last point, native XYZ |
|---:|---:|---:|---|
| 919 | 66 | 150 | (263.95618, 84.99897, 55.99836) |
| 920 | 71 | 150 | (263.98114, 85.00012, 56.00007) |
| 961 | 59 | 75 | (263.27188, 237.42558, 34.271873) |
| 962 | 67 | 150 | (118.35907, 78.461815, 65.84171) |

Two class-1012 return paths are now included in playable round trips:
instance 961 takes outbound **59** (75 points), then return **65**
(150 points); instance 962 takes outbound **67** (150 points), then return
**58** (75 points). The joints agree to less than 0.15 world units.
Their outbound-only endpoints were tested in Godot and have **no**
walkable landing collision, so attempting to disembark after the first
leg would strand Ratchet in mid-air.

Class-816 incoming routes **62** and **70** (+0x94) and shared exit
route **61** (+0xbc) are now implemented as separate traffic phases.
Generated native class-816 code at 0x002d3198 establishes this cycle:
initial placement at inbound route start -> await the player in the
PVar +0x80 cuboid -> fly toward the taxi stop -> message/camera/boarding
states -> outbound +0xa8 route -> wait until Ratchet is **15 units**
from the destination -> take the +0xbc exit route -> reset. The
activation cuboids are source indexes **10** (i919) and **28** (i920),
not invented 5.5-unit boarding spheres. The current explicit
T/Triangle confirmation still replaces a native HUD message/actor
interaction and is **not** authentic.

The native PVar of taxi 921 has -1 for these path selectors, so OBP
retains its model but does not allow boarding it.

## Current Godot integration

- All five Kerwan taxi hulls have Godot collision from their actual retail
  Moby meshes (physics-verified at all five source Moby positions). No
  general dynamic-collider policy was changed.
- On R&C1 LEVEL3 import, the native XYZ paths are attached for all four
  outbound flights, both class-1012 returns, both class-816 incoming
  paths and the shared class-816 exit. Native cuboid 10/28 placement
  data also travels through source-tagged payloads.
- Class-816 taxis now initialize at the **beginning of their incoming
  paths**, approach only when Ratchet is inside their actual trigger
  volume, stop for boarding, carry him along outbound splines and wait
  for 15-unit clearance before following the exit route.
- A confirmation (T / Triangle) is still host policy after they dock.
- Ratchet is **visible** during flight and carried with a preserved
  clamped relative deck offset, rather than hidden while a model flies.
  Taxi yaw now smoothly follows its actual movement tangent.
- Class-1012 shuttles remain a simpler two-leg ride and do not yet
  implement their complete source-native two-ended state machine.
- Landing only releases Ratchet near upward-facing collision; otherwise
  a safety fallback returns him to his source dock, not mid-air.
- X-axis mirroring, world teardown, cooldown and interrupted-flight
  cleanup are handled on the host.

**Host-created approximations, not retail facts:** 5.5-unit boarding
distance after arrival, T/Triangle confirmation, deck offset and yaw
smoothing, character process suspension, collision-based exit and
placeholder camera/audio. Source-enriched paths now use native waypoint
seeking; synthetic fixtures without retail PVars keep the old 60-samples/s
linear interpolation for deterministic fallback coverage. Retail
HUD message 0xbc9 and the source camera attachment/handoff are still
not implemented. Quest-dependent Moby retirement/gating is also missing.

## Validation

- Engine-independent tests verify model gating, boarded vs parked taxis,
  cuboid-gated inbound flights, docking, clearance-gated departures,
  native round trips, arrival/cooldown and invalid delta.
- With `OBP_RAC1_ISO` set, tests reconstruct Kerwan from retail disc and
  check the five taxi instances, source-native outbound selectors,
  class-816 incoming 62/70, shared exit 61 and cuboids 10/28,
  class-1012 return selectors 65/58, point counts and endpoint geometry.
- Godot headless Metropolis load, targeted live-ISO tests and .NET
  suite are validation gates; interactive player acceptance is still
  required. For a direct human playtest:
  `./tools/play-metropolis.ps1` from the isolated worktree.

## Next recovery targets

Next: replace the host key confirmation with source HUD/camera
handoff and player-state gating, validate the native yaw and audio
side-effects of `lv3_2d3918_l3`, complete class-1012 two-ended
shuttle states, recover taxi animations, and connect taxi
availability to Kerwan mission progress. No PCSX2 required for
the native-code/level-data archaeology.


## 22 October 2026 retail state-machine follow-up

The authorized NTSC-U (SCUS-97199) Kerwan level-3 overlay was traced
with the native overlay translator. This records semantics and instruction
addresses only, never original executable bytes or generated native payloads.

**Class 816 updater 0x002d3198:** Native states 0..7 cover: starting
route selection and mission-dependent actor retirement (0); scanning five
PVar cuboid selectors at +0x80..+0x90 via point_in_box (1); inbound
approach at +0x94 (2); yaw/focus/HUD interaction with text ID 0x0bc9,
player-state 8, button bit 0x10, and hero_cam_target_set (3);
cutscene handoff predicate (4); outbound +0xa8 (5); strictly greater than
15-unit 3D clearance or 5-unit 2D clearance depending on selector (6);
and exit +0xbc followed by admission reset (7). Position and heading
are updated with seek helpers and attach_frame_set.

**Class 1012 updater 0x002dccc0:** Native states 0..3 cover initial
path selection based on the authored +0x80 side (0), proximity/focus and
two-ended interaction (1), the player/camera handoff predicate (2), and
the selected spline traversal before a 15-frame reset delay (3).
Retail i961 initial side is 0 and i962 initial side is 1; the native
first path is +0x90 + 4 * side, so i961 first selects path 65, not
the path-59 source-dock approximation in OBP. HUD helper 0x002dcbc8
requests IDs 0x0bce, 0x0bcf, 0x0bcb, 0x0bcd depending on side and
interaction. The focus helper is 0x002dcb70.

**Motion helper 0x002d3918:** PVar +0x64 is a waypoint index, +0x60
a continuous speed. Distance to next waypoint governs advancement;
seek_1d updates speed, a normalized displacement advances the Moby,
and yaw_seek clamps orientation. The original movement is NOT
fixed-rate linear interpolation of waypoint samples.

**Feature-branch changes:** Native initial-side values are preserved;
class-1012 re-admission uses the source-backed 15-frame timer;
class-816 clearance uses a strict greater-than-15 test; native HUD
message IDs resolve through the attached disc's text cache when present.
Interrupted flights safely restore player physics, hull collision and
authored placement, and do not rewind unrelated taxis. Taxi collision
children are released on teardown/partial configuration failure, and
the exit ray does not use the moving taxi as walkable ground.

**Godot route gate:** tools/rac1-metropolis-taxi-smoke.ps1 passed with
exit code 0 using retail Level 3. It staged Ratchet in cuboid 10, saw
i919 dock, used the real taxi boarding handler, completed the authored
outbound route and a grounded dismount, then boarded i962, aborted its
mid-flight ride, reboarded, and completed its safe fallback round trip.
This is staged player placement, NOT ordinary walk-up traversal.

**Still provisional:** native player focus and button admission,
camera/attachment handoff, mission gates, native yaw and precise native
sound or animation timing, and class-1012 two-ended arrival/grounding.
The source-safe host keeps T/Triangle boarding and deck offset, plus
an automatic class-1012 return where authentic endpoints lack
supported landing collision.


## 22 October 2026 native seek follow-up

The source-pinned NTSC-U Kerwan data section provides speed constant
**18.0** at 0x161b08 and acceleration constant **4.5** at 0x161b0c.
The helper lv3_2d3918_l3 (0x002d3918) reads the 18-unit maximum speed
and multiplies it by the native frame-duration scale; acceleration and
turn acceleration are 4.5 times the per-frame squared time scale.
The underlying retail seek_1d_v at 0x0025b970 accelerates toward
sqrt(2 * acceleration * distance) clipped by max speed, braking with
the authored deceleration branches. Waypoint advancement occurs when
the taxi enters a radius of two per-frame maximum steps (0.6 units
at 60 Hz). Importantly, the helper starts with a zeroed local scalar
position each tick while preserving velocity across ticks in PVar+0x60;
a persistent spline-time interpolation is **not** native behaviour.

Rac1TaxiNativeMotion now ports that translation as an engine-neutral
60-Hz fixed-step path follower. PVar-backed retail class-816 approach,
outbound and exit paths and class-1012 outward/return host-safety paths
use the native stepper; test-only fixtures without retail PVars retain
the explicitly provisional legacy sampler. Endpoints are snapped
within the native 0.6-unit acceptance radius to support safe Godot
landing. The 1012 return remains a *host safety policy*, not retail
state-machine parity. Yaw and sound side effects remain unported.
Tests cover 30/60/120-Hz host equivalence, velocity/braking and actual
retail Kerwan arrivals. Long authored routes take substantially longer
with native acceleration than the old one-waypoint-per-frame preview.
For practicality, the staged Godot route smoke uses a temporary
12x Engine.TimeScale and restores it at exit; gameplay does not.

## 23 October 2026 native input and camera target research

Source overlay class-816 updater at 0x002d3604 checks an active HUD
Moby focus, a zero HUD lock flag, pad-down bit 0x10 and player state 8
before calling hero_cam_target_set. The class-1012 updater at
0x002dcf24 uses its focus helper (0x002dcb70), HUD helper
(0x002dcbc8), the same pad bit/state gate and calls the same native
camera-target routine. These facts do **not** establish that OBP's T key
is a retail keyboard equivalent, or that the existing fixed 5.5-unit
host boarding radius is authentic.

The translated hero_cam_target_set source, 0x00233ee8, writes the
target's position, yaw and flag to P+0x1640, +0x164c, +0x1650, and
requests hero state 0x65 unless already in 0x65..0x67. Native taxi
attachment remains handled by attach_frame_set. OBP's PlayerHost
recovered type-0 camera uses a world-space (TopLevel) pose. Suspending
player physics on taxi boarding previously left that camera immobile
at the dock. The Kerwan taxi host now transports the existing
camera-to-Ratchet world offset during flight and resets the recovered
camera on abort/dismount. This is an explicitly **provisional visual
fallback**, not a port of native hero state 0x65, camera target/rotation
or native attachment animation.

The retail admission PVar selector census was rechecked:
class-816 i919 slots +0x80..+0x90 = (10, -1, -1, -1, -1);
class-816 i920 = (28, -1, -1, -1, -1). Both class-1012
i961 and i962 have optional cuboid selector +0xa0 = -1.
Accordingly, the host must not interpret -1 as a real cuboid or
invent five simultaneously active class-816 triggers. These exact
selectors now have a retail-authority regression test.

## 23 October 2026: original Kerwan taxi hover and tilt

The previously unexplored visual helper calls turned out to be source-backed
taxi dynamics, not path movement or camera handoff. Class-816's native
`lv3_2d3c40_l3` at 0x002d3c40 and class-1012's
`lv3_2dd0f0_l3` at 0x002dd0f0 both invoke retail
`sub_00262b00` (0x00262b00) and `sub_00262b80` (0x00262b80).
The first adds a sine offset to the Moby's native vertical coordinate
using the phase remembered at a PVar offset. The second writes the two
sway angles from independent sine/cosine phases. They run on every
admitted native updater tick, not once per ride.

Constants recovered from the NTSC-U overlay data section (same for both
families): vertical amplitude 0.2 units, vertical phase 120 degrees/second,
sway amplitude 0.078539819 radians (4.5 degrees), angular phase rates
70 and 100 degrees/second. Original source PVar phases are at
+0xec/+0xf0/+0xf4 for class 816 and +0xac/+0xb0/+0xb4 for class
1012, respectively. The first sine phase advances before writing the
new vertical offset. The angles are written from the old two angular
phases, which are then advanced.

`Rac1TaxiNativeHover` ports these steps as a deterministic 60 Hz,
engine-neutral presentation oscillator. The retail importer preserves
each taxi's three actual authored phases. The Godot taxi presentation
adds vertical bob and tilts to the hull (and therefore to the carried
player), while route geometry, class-1012 host return-to-source fallback
and collision-supported dismount probes remain unchanged. Stationary
retail taxi 921 is also presented with its native hover. The Godot
pitch/roll axis remapping is host rendering policy pending exact matrix
verification, not a claim of full native orientation parity.
Synthetic test fixtures without PVars retain their original unanimated
presentation.

## 23 October 2026: class-1012 native HUD selector correction

The original class-1012 focus helper at Kerwan 0x002dcb70
recognizes several eligible Ratchet state/focus-pointer combinations;
the corresponding HUD helper at 0x002dcbc8 is passed a PVar-backed
message selector, **not** the initial side flag. After focus admission
the updater at 0x002dcf34 toggles its active side and passes
`PVar + 0x98 + 4*(side ^ 1)` to the HUD helper, with this exact mapping:
selector 1 -> 0x0bce; selector 2 -> 0x0bcf; selector 3 -> 0x0bcb;
selector 4 -> 0x0bcd. The helper also requests retail camera kind
0x0c when its PVar+0xa4 camera handle is unallocated. OBP's camera
request/lifecycle and hero native state 0x65 are not ported.

The four retail PVar selectors were verified directly:
i961 has side-zero/side-one selectors (1,2), i962 has (3,4).
The OBP host currently boards at the authored **start of path
PVar+0x94** (native side one) for both taxis, even though i961's
initial-side PVar+0x80 is zero. The correct current host source-dock
prompts are therefore i961 -> 0x0bce and i962 -> 0x0bcb.
The earlier approximation chose i962's prompt as 0x0bcf,
incorrectly conflating a route-side selector with the authored
message selector.

`Rac1TaxiNativeHud` now holds both source PVar message selectors,
resolves the requested prompt for the next side and returns no retail
message for unsupported selector/side values. The Godot HUD uses
these PVars instead of initial-side parity. This recovers the prompt
mapping, **not** native HUD focus allocation, gameplay state gates,
two-ended class-1012 boarding or the camera transition.

## 23 October 2026: Ratchet's scripted taxi walk-to contract

The shared `hero_cam_target_set` routine (0x00233ee8) does **not**
immediately trigger a taxi camera cut or attach the passenger. It copies a
target world position into hero +0x1640, target yaw into +0x164c and an
optional flag into +0x1650. Unless already in 0x65/0x66/0x67 it enters
hero state **0x65**. The class-816 and class-1012 updaters then use
`sub_00233f38` (0x00233f38) to wait for those states to finish.

The original hero-state routines are explicit: `walk_to_update`
(0x00217970), `walk_to_input` (0x00229b70), and the state transition
handlers in `hero_transitions.cpp`. In 0x65 Ratchet steers toward the
planar target; inside 0.2 native units, he tries to face the saved yaw.
If his planar speed is below 2.7/60 units per frame and yaw differs by
less than two degrees, he enters 0x67. With a nonzero movement mode and
higher speed, he instead enters 0x66 and brakes until the current
animation finishes, then resumes 0x65. State 0x67 waits for animation
completion (or a nonzero target flag) and returns to normal, unless
Ratchet has moved farther than 0.2 from the target. Only when the
native 0x65/0x66/0x67 group is left does the taxi updater advance
toward passenger attachment and flight.

`Rac1TaxiHeroTargetSession` now implements the **source-backed state
transition contract**, independent of Godot: arbitrary target and
heading changes, planar radius, speed/facing admission, braking and
retargeting, 0x67 recovery, 0x00233f38 status and departure eligibility.
Regression tests exercise target acquisition, wrong orientation, the
nonzero-movement-mode 0x66 branch, leaving the spot while settled,
forced flag release, and angle wraparound.

**Integration limitation:** the native hero walking motion itself
depends on collision, Ratchet's animation completion, movement mode,
and per-frame velocity from the shared player controller. Until these
are fed into the source state session by the Godot host, the existing
bounded source-dock direct flight remains in place. Do not claim the
walk-in, camera request, or native attach-frame presentation is
implemented solely because the state-gating session now exists. We
must not manufacture Ratchet's final approach by teleporting through
unproven walkable geometry.

## 23 October 2026: Godot controller-driven taxi walk-to

The Godot taxi host now queues a pending boarding request rather than
immediately calling `TryBoard` and disabling Ratchet. It starts the
source-backed `Rac1TaxiHeroTargetSession` at the *first authored outbound
waypoint*, which is the retail taxi's dock placement; the facing heading
comes from the next authored waypoint. While pending, the new
`PlayerHost.Rac1TaxiWalk` host adapter supplies a camera-relative
movement intention to the **existing RAC1 movement controller**. Ordinary
`MoveRac1Contact` continues to resolve geometry, including floor and
hull collision. Ratchet's actual pose, yaw, velocity and the host
`PlayerAnimationState.Idle` semantic are fed into the target session
each tick. Only when its native status is zero may the host attach the
passenger, disable moving hull collision and start the recovered flight.

The source routine `hero_cam_target_set` actually writes the world
target and heading and enters native hero state 0x65. The source's
animation-done signal and 0x65 yaw recurrence do not yet have a
matching Godot-native clip/time signal, so this host uses the **Idle
semantic as an animation-complete approximation** and a bounded angular
correction (0.12 radians per physics update) for target facing. The
near-target raw stick strength reduction is also host policy. Neither
should be described as recovered native constants.

Abort, death and level reset release a pending target immediately,
without toggling the taxi hull or changing Ratchet's physics process.
An 8-second game-clock limit cancels blocked/unwalkable approaches
rather than attaching through walls or leaving input captured. Direct
flight and collision-safe dismount retain the previous safe fallback.
The staged Godot smoke now checks 0x65 admission, separation of
walk-to from in-flight mode, interruption during the walk, and
reboarding, in addition to its existing flight/dismount checks.

### Source-dock collision qualification

A staged Kerwan class-1012 attempt on i962 proved that its currently
imported source waypoint sits above unsupported ground: during the
attempted `0x65` walk Ratchet fell more than 13 vertical units and the
native boarding radius gate could not complete safely. The retained
class-1012 host therefore **does not enable physical walk-to yet**:
i961/i962 continue using bounded direct boarding with safe camera,
flight interruption, and return-to-source fallback. This is explicitly
provisional, not authentic class-1012 passenger attachment. The original
class-816 arrival dock is supported, so its pending `0x65` walk-to,
interruption/retry and native release-to-flight can be exercised
without giving Ratchet a target in open air.

The earlier generic Godot integration description is conditional on
the source dock having supported collision; no complete class-1012
walk-to parity is claimed. The staged smoke now exercises an off-centre
class-816 start and asserts actual horizontal movement through the
ordinary controller before flight, as well as preserving class-1012's
safe original fallback.

### Bounded walk-to admission after collision smoke

Off-centre i919 staging (0.65 world units from the source marker)
demonstrated the imported taxi surface cannot presently support that
walk: after 30 normal physics frames Ratchet had moved just 0.002
units horizontally, fallen 1.46 units vertically, and had no live
planar step. This is real evidence that an uninterrupted 0x65
approach from the generic 5.5-unit boarding radius is unsafe.

The Godot host therefore only enters 0x65 for **close source-dock
alignment** (planar distance at most 0.2 units). Wider admissions
retain the previously working provisional direct-attach host policy.
A native target with more than 2 units vertical deviation is cancelled
without claiming a boarding, with ordinary controls retained. The
smoke retains native 0x65 entry/abort/retry at the authored dock and
full ride/dismount checks but **does not prove a walk from off-centre
ground to taxi**. That requires recovered boarding geometry and
proper collision support before the 0x65 steering can be admitted
beyond the immediate dock.

The close-dock host gate additionally requires the player to be
within 1.5 vertical units of the authored taxi target; Ratchet on
the lower ordinary floor must use direct boarding instead of a
false 0x65 admission above his head. These radius and height
restrictions are conservative OBP collision-safety policy, not
recovered native focus constants.

## 23 October 2026: state-6 authored taxi exit selectors

The native Kerwan class-816 state-6 updater at **L3:0x002d3754**
reads the slot selected by the taxi's original Moby byte +0xbc
from the five original PVar selectors at **+0xbc + slot*4**.
It distinguishes two post-ride clearance transitions:

- With a departure path selector other than -1, Ratchet must be
  **strictly farther than 15 units in three dimensions** (native
  `vec3_distance` at 0x002d3804). The taxi then enters state 7,
  travels its authored departure path, and returns to idle.
- With selector **-1**, it checks only horizontal X/Y native world
  distance (**strictly greater than 5 units**, `vec2_distance` at
  0x002d3854), then returns directly to idle state 1. Vertical
  separation by itself must not release this special/pathless case.

Previously the OBP traffic host unconditionally required 15 units
of full 3D separation before creating departure motion, conflating
both native states. `Rac1TaxiNativeDepartureClearance` now owns the
finite-input-safe source threshold decision; the session returns a
pathless variant to idle instead of manufacturing an outbound spline.
The original **five** integer path selectors are retained from
authored PVars on each class-816 Moby instead of discarding the dormant
variants. The existing Godot host still admits only slot zero, so
storing other selectors does not awaken unused/special modes.

Retail i919 and i920 both use **exit path 61** in slot zero. This
ordinary admitted path keeps its proven 15-unit native gate. For
synthetic worlds without retail PVar selectors, the provisional
15-unit path-mode default is retained. Tests exercise exact 15/5
boundaries, vertical-only clearance, NaN/Infinity rejection and
all five retail exit selector slots. Native camera/focus/player
attachment and other slot selection remain separate work.

### Staged Godot admission fix, 23 October 2026

A follow-up staged smoke unexpectedly chose the **safe direct boarding**
fallback for retail i919 instead of beginning state 0x65. This did not
indicate a regression in the native class-816 state-6 exit logic.
Instrumentation showed the staged player position was precisely
**3.0 OBP units above** the authored dock, with zero planar error.
The culprit was the unrelated host
`RuntimeSpawnSceneAdapter.GroundingLift = 3` used by
`ApplyRac1TaxiArrival`, which the smoke had used for staging.
The live taxi host was correctly enforcing its 1.5-unit vertical
collision-safety admission limit.

The smoke now subtracts that *host-only* clearance lift when staging
the exact retail source waypoint and **asserts** its position matches
the authored taxi point before invoking the ordinary taxi input
handler. This stages an isolated state-0x65 check without relaxing
normal gameplay's safety policy or claiming an off-centre walkable
taxi surface. Class-816 host fallback diagnostics record the actual
player/dock 3D offsets when admission is deliberately kept on the
direct-boarding path.

After the correction, the complete Kerwan staged Godot smoke passed:
i919 native walk-to -> abort -> retry -> flight -> grounded dismount,
then i962 bounded direct boarding -> flight interruption -> reboarding
-> safe return-to-source landing. Audio retired cleanly.
