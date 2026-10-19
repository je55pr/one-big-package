# R&C1 Metropolis taxis (Kerwan, native LEVEL3)

Status: source-backed first playable transport pass on the isolated
`chatgpt/worker4` worktree. No PCSX2 used.

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

Class-816 incoming routes **62** and **70** (+0x94) and shared
continuation **61** (+0xbc) are recovered but still need their native
state transitions mapped; they are not yet executed.

The native PVar of taxi 921 has -1 for these path selectors, so OBP
retains its model but does not allow boarding it.

## Current Godot integration

- Existing imported native taxi models, positions and identities remain intact.
- All five Kerwan taxi hulls have Godot collision derived from their actual
  retail Moby render meshes. **Verified by direct Godot physics rays: 5/5
  collider hits at the original taxi positions.** Other Mobies are unchanged.
- At R&C1 LEVEL3 import, attach native XYZ splines, including the two
  class-1012 source-backed return legs. No generic cross-game schema change.
- Boarding near a routable taxi with T or Triangle starts flight.
- Taxi models follow imported native waypoints in order. The player controller
  is suspended while riding; class-1012 passengers return on their native
  reverse path before disembarking at the original taxi.
- Landing is permitted only on nearby upward-facing collision. A missing
  destination surface logs an explicit warning and returns Ratchet safely
  to the departure dock instead of dropping him into empty space.
- The world/scene X-axis mirror is handled explicitly on entry, flight and exit.
- One-time arrival/cooldown and cleanup on world teardown are implemented.

**Host-created approximations, not retail facts:** 5.5-unit boarding radius,
explicit T/Triangle interaction, 60 spline samples per second, interpolation
between samples, immediate model reset on arrival, local player suspension,
mesh-derived static collision, safe exit rays, and camera/audio presentation.
The retail update functions have not been ported into C#.

## Validation

- Engine-independent tests verify model gating, boarded vs parked taxis,
  progress along source path samples, native round trips, arrival, cooldown,
  and invalid delta.
- With `OBP_RAC1_ISO` set, tests reconstruct Kerwan from retail disc and
  check all five models plus all four exact PVar outbound path IDs, both
  class-1012 native return selectors, sample counts, and endpoint geometry.
- Full .NET suite, C# build and Godot headless world-load smoke are required
  before merge. Human boarding/playfeel acceptance is still outstanding.

## Next recovery targets

Recover the retail spatial trigger volumes, full native state transitions,
class-816 incoming/continuation routes, automatic taxi movement, retail
waypoint timing, camera and Ratchet attach points, opening/closing animations,
taxi-related audio, and checkpoint continuity. No PCSX2 needed
for static-code/level-data archaeology.
