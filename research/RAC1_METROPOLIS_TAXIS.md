# R&C1 Metropolis taxis (Kerwan, native LEVEL3)

Status: source-backed stateful taxi pass on isolated `chatgpt/worker4`.
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
distance after arrival, T/Triangle confirmation, 60 spline samples per
second (not the recovered native seek_1d recurrence), linear interpolation
between samples, deck offset and yaw smoothing, character process
suspension, collision-based exit and placeholder camera/audio. Retail
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
handoff and player-state gating, port the native speed/acceleration
recurrence from `lv3_2d3918_l3`, complete class-1012 two-ended
shuttle states, recover taxi sounds/animation, and connect taxi
availability to Kerwan mission progress. No PCSX2 required for
the native-code/level-data archaeology.
