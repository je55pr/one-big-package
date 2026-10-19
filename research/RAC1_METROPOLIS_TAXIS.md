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

The inbound and additional path selectors recovered but **not yet
executed** include class 816 indexes 62 and 70 (+0x94), class 816
shared continuation path 61 (+0xbc), and class 1012 indexes 65 and 58
(+0x90). These are critical for eventual continuous round-trip movement.

The native PVar of taxi 921 has -1 for these path selectors, so OBP
retains its model but does not allow boarding it.

## Current Godot integration

- Existing imported native taxi models, positions and identities remain intact.
- At R&C1 LEVEL3 import, add a source-tagged payload with the authentic
  outbound native XYZ spline points. No generic cross-game world change.
- Boarding near a routable taxi with T or Triangle starts flight.
- The taxi model moves exactly along the original authored coordinates, with
  interpolated steps, hiding the Ratchet mesh and pausing ordinary movement.
- The endpoint uses a downward native-collision ray for disembarking.
- The world/scene X-axis mirror is handled explicitly on entry, flight and exit.
- One-time arrival/cooldown and cleanup on world teardown are implemented.

**Host-created approximations, not retail facts:** 5.5-unit boarding radius,
explicit T/Triangle interaction, 60 spline samples per second, interpolation
between samples, immediate model reset on arrival, local player suspension,
simple exit ray, and camera/audio presentation. The retail update functions
have not been ported into C#.

## Validation

- New engine-independent tests verify model gating, boarded vs parked taxis,
  progress along source path samples, arrival, cooldown, and invalid delta.
- With `OBP_RAC1_ISO` set, tests reconstruct Kerwan from retail disc and
  check all five models plus all four exact PVar outbound path IDs, path
  sample counts, and endpoint coordinates.
- Full .NET suite, C# build and Godot headless world-load smoke are required
  before merge. Human boarding/playfeel acceptance is still outstanding.

## Next recovery targets

Recover the retail spatial trigger volumes, full native state transitions,
incoming/reverse routes, waypoint stepping and direction (not just the
outbound spline), camera and Ratchet attach points, opening/closing
animations, taxi-related audio, and checkpoint continuity. No PCSX2 needed
for static-code/level-data archaeology.
