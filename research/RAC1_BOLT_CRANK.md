# R&C1 bolt crank animation, turning and cameras

Authority is the supported original NTSC-U `SCUS-97199` disc. Novalis class
280 is updated at `0x002e0c68`; its 64-byte PVar supplies turns at `+0x30`
and the reusable flag at `+0x38`. Authored instances 296 and 297 both specify
one turn and are non-reusable. The same updater is present in Eudora at
`0x002bfd30` and Batalia at `0x002d8270` (3672 bytes each). A census of all
19 retail level entries finds 16 class-280 placements: Novalis 296/297;
Eudora 146 through 154; Batalia 296 through 300. These are placement counts,
not a claim that every placement is reachable before its surrounding machinery works.

| Level | Turn counts | Reusable placements |
|---|---|---|
| Novalis | 1 for both | none |
| Eudora | 3.5 except 152/153, which use 1.5 | 154 |
| Batalia | 3.5 except 300, which uses 1 | none |

Additional retail identities, reproduced using the bounded overlay reader:

| Level | Overlay SHA-256 | Crank updater SHA-256 |
|---|---|---|
| Eudora | `036dbc3ddad404de223be0d2fe68903d58c04aaf4687874f669d8d6cefbd1094` | `c43d8eba5a07764397d022db7b601a68d0e212ee5db6770980f632400b8c4ab8` |
| Batalia | `1f42f19b5484033224b4287fe67ae5b91adb5a94a75a4a46796216c30a3325da` | `8c2bc5863e593ecde8c864e2ff62830a8fa7c860c94f218a216a8ff6ec70b223` |

The local read used overlay SHA-256
`81673b8dcf5930c7313a7c83d0f76e5e1948691dc09af63dd8c8f6a04f9a72ae`.
The bounded loaded-code range `[0x002e0c68, 0x002e1ac0)` hashes to
`97bcda3ab6e4bce412cd527c79f8cd417d506d76b8d2dda24034c64f98d6acb0`.
These identities can be reproduced with `Rac1ExecutableOverlay.Read` and
SHA-256 over its `Read(start, length)` span. No extracted assets, executable
bytes, private source paths or captures are retained here.

## Decompilation crosswalk

[Lombyte at be4134e4](https://github.com/lombyte-project/Lombyte/tree/be4134e4c9f559f6f0a022c59a443c5f6cc64d70)
provided the implementation leads: `bolt_crank_update` in
`src/overlays/shared/gameplay/entities/novalis_elevator.c` and
`HERO_STATE_CRANK` in `src/overlays/shared/gameplay/hero/hero_set_state.c`.
Its GPL-3.0 licence was inspected. No external C or assembly source is imported
into OBP. The C# controller is an independent implementation; overlay-specific
addresses must be resolved from the retail overlay rather than assumed from
shared decompilation extern labels.

Direct retail instruction inspection also identifies the crank's calls to
Novalis animation selector `0x00247a90` (including the idle selection call at
`0x002e1554`). The four Ratchet clips below were decoded from the provider's verified
LEVEL0 carrier. Wrench 11 comes from class 71 in the LEVEL0 global-class area,
through `Rac1WrenchAsset`. The selections were not inferred from neighbouring sequence numbers.

| Owner | Sequence | Purpose | Frames | Timing |
|---|---:|---|---:|---|
| Ratchet | 68 / `0x44` | Wrench insertion | 13 | constant rate .25 |
| Ratchet | 63 / `0x3f` | Holding the crank | 13 | constant rate .25 |
| Ratchet | 64 / `0x40` | Slow turn | 60 | per-frame rates |
| Ratchet | 65 / `0x41` | Fast turn | 46 | per-frame rates |
| Wrench class 71 | 11 / `0x0b` | Inserted wrench | 12 | decoded native timing |

## Implemented rules

`Rac1BoltCrankSession` owns source action `0x3b`, the seven-update Square
buffer, idle cooldown, distance/facing/height gates, forward-stick projection,
speed acceleration and thresholds, radial spring, tangential facing, handle
rotation, angular progress, idle unwind and completed lowering. It uses the
authored turn count rather than a fixed progress increment.

Entry selects Ratchet 68 with eight blend ticks and wrench 11 with thirteen.
Insertion completion selects idle 63 with seven ticks. Idle-to-slow,
slow-to-fast and slow-to-idle use fourteen ticks; fast-to-slow uses sixteen.
The fast/slow thresholds are .6/.4 of the native maximum `3.7 / 60` movement
step; acceleration is `5.5 / 3600`. Square after the first sixty action ticks
or completed progress releases the interaction. Releasing the stick stops
turning while retaining the grip. Completed non-reusable cranks lower .5 units
at .5 units per second. Eudora's reusable crank also releases and lowers at
full progress, but returns to native idle rather than setting the permanent
completion state. Its connected mechanism owns subsequent progress reset:
there is no invented automatic reset timer. The animation/camera slice admits
this variant without implementing those external reset/save callbacks.

The existing player animation clock samples the clips and their hand socket.
The wrench retains its insertion terminal pose. Existing linked-door and
music mechanisms consume the same progress/completion; unwind updates door
positions and restores host door collision too. Cranking blocks equipment use
and Quick Select, and reset/world retirement releases scene-local ownership.
`Rac1BoltCrankMechanism` owns crank nodes and interaction on every RAC1 world
containing class 280. The separate Novalis adapter consumes progress for its
already implemented doors and music. Eudora and Batalia publish no new machinery
movement, collision removal or completion rewards.

## Native crank camera

The native updater calls mode-3 camera mounting at `0x00316ef8`, with 180
NTSC ticks. PVar `+0x20` selects an authored 0x80-byte point in gameplay's
`+0x60` directory: position is point `+0x30`, Euler rotation is `+0x70`.
PVar `+0x28/+0x2c` supplies the radius interpolation references.

| Crank | Point | Native eye XYZ | Native rotation XYZ | Radius references |
|---|---:|---|---|---|
| 296 | 0 | 168.74, 98.88, 63.83 | 0, .070005156, -1.129996 | 1, 0 |
| 297 | 1 | 104.54, 85.56, 65.07 | 0, .53998744, 1.030006 | 1, 1 |

`Rac1BoltCrankCameraController` reproduces type-5 mode 3: retained spherical
offset and origin, iterative cosine angle/origin interpolation, the native
long-azimuth branch and authored cubic radius interpolation. It publishes the
same camera's heading to movement. Right-stick input and camera centering do
not alter the mounted pose. The native optional collision flag is zero for
this mount, so ordinary follow-camera obstruction cannot retract it.

Release calls the mode-4 handoff at `0x00317070`. Type-0 setup starts from the
outgoing eye, and the global position/quaternion blend uses cosine easing.
Novalis's `switch_active_camera_record` default is `.01` for both blend rates,
overriding the general `.018` setting used by Eudora and Batalia. The rate is
carried by the source-authored camera definition, not inferred by the player
from world identity. Every crank reads its own point and radius references,
including Eudora 153's `-1,0` references and Batalia's end references up to 3.
The renderer accepts an optional neutral
camera-up vector so quaternion interpolation retains its orientation, including
transient roll. The player owns the camera controller; the world lends only
the source-qualified authored definition. Placement/reset clears that context.

The local original-MIPS comparison executed type-5 updater `0x00317278` for
185 updates per case, for each authored definition with stationary and moving
player inputs (740 samples). Maximum C# eye error was .000158 world units and
heading error .00000334 radians. A separate 104-update original-MIPS return
blend comparison had maximum eye error .000024 units and forward/up vector
error .000000681. Entry/return drivers are local inspection aids, not committed
test infrastructure. Code identities from the same overlay above:

| Loaded code range | SHA-256 |
|---|---|
| `0x00317278` + 1004 bytes | `a186432596f2ba94fa4334baff180df677059cd9cda5820920b35fb9238a381b` |
| `0x0020dcf8` + 480 bytes (publication blend) | `350149e85992d103be45cadc80e36d0a5d525fa8b0b2478cb8f697afda9fce52` |

Decompilation leads also include `follow_camera_update.c`, `follow_camera_setup.c`,
`switch_active_camera_record.c`, `step_camera_blend.c` and the level-address
crosswalk in `config/overlays/us/functions.tsv` at the revision above.

Comparison input plan: initial player 296 `(173.9,90.58811,62.39)` or player
297 `(110.86252,93.46376,57.39)`; initial eye is player plus `(6,0,2)` and
rotation is `(0,.0831412,pi)`. The moving cases add
`(.5*sin(.03*t), .5*(cos(.03*t)-1), 0)` to that player for updates `t=0..184`.
The return comparison starts at eye `(179.9,90.58811,64.39)` and identity
orientation, targeting point 0's fixed transform, with zero platform carry
and both rates `.01`. These are controlled function observations, not a live
retail camera playthrough or a claim of moving-support equivalence.

The expanded sweep executes the original camera updater at `0x002eeb88`
(Eudora) and `0x00313c80` (Batalia), in addition to Novalis, for every authored
definition with stationary and moving inputs: 5920 mount samples. Maximum
eye error is .000454 units and heading error .00000406 radians. The 1664
return samples across all definitions have maximum eye error .009761 units
and forward/up error .000493. These are numerical observations, not EE
bit-equivalence. The Eudora/Batalia publication blend entry points are
`0x001dea60` / `0x001feaf8`.

Expanded comparison inputs use each authored crank position plus
`(1.05,0,.39)` as the player; eye is player plus `(6,0,2)`. Initial rotation
is `(0,.0831412,atan2(sin(float(pi)),cos(float(pi))))` in both implementations,
so the exactly opposite heading chooses the same side of the wrap boundary.
Moving inputs use the same .5-amplitude trajectory above. The return comparison
uses the initial eye with identity orientation and each fixed authored target,
zero platform carry and the level's .01/.018 rates. Return completes after
102/57 updates, respectively, including the initial zero-weight publication.

## Boundaries and manual checks

Godot supplies held-wrench availability, support, camera-relative input and
scene transforms. Planar crank motion follows the source controller while
vertical support remains the existing Godot contact adapter. The mirror-animation
option, nearby-hostile clearance scan and native save-bit/callback machinery are
outside this change. All 16 authored crank interactions and cameras are
admitted; reusable machinery callbacks remain outside this slice. Native EE floating-point
bit equivalence and complete ordinary-camera collision parity are not claimed.

The source height predicate remains `abs(heroZ - .3 - crankZ) < .2`.
OBP's existing foot capsule settles at different heights on imported supports:
the Godot engagement adapter checks that predicate at both ends of the .3
foot-to-origin interval while grounded. This is an explicit OBP contact
approximation, not a recovered second native height rule. Source distance,
facing, buffer and cooldown predicates are retained.

Manual retail-loaded inspection exercised both authored cranks through
68 -> 63 -> 64 -> 65 -> 64 -> 63, resumed turning, completion, standing
handoff and lowering. A rendered Novalis run used the existing movement
bookmark boundary and ordinary named input actions to inspect insertion,
grip, turning, linked-door opening and subsequent ordinary movement. A
rendered cancellation/re-entry also released the grip, unwound the door and
restarted insertion before a subsequent completed turn. The direct temporary
SceneTree exit printed out-of-tree transform warnings from the existing
`Rac1NovalisRouteMechanism.Dispose`; no crank exception occurred.
Rendered camera checks also covered both authored views and restored ordinary
control. The second view used a local version-2 bookmark with hostile placements
already cleared so combat would not interrupt the camera observation.
The expanded retail-loaded Godot inspection checks insertion, turning,
completion/release and authored camera targets for all 16 placements, including
Eudora 154's reusable flag. Rendered checks include Eudora 153's negative radius
reference and Batalia 299. Cancellation/re-entry was also exercised on Eudora
154; toggling diagnostic fly during a Batalia grip invalidates the old follow
camera before ordinary control resumes. The follow handoff seeds obstruction
correction from the full offset length, consistent with subsequent updates.
Eudora placements 147 and 150 have no usable support
at their authored initial positions in the current imported world; those two
interaction checks use explicitly temporary local support. No substitute floor,
crank relocation or new connected mechanism is added to production. Normal
access to unsupported placements remains a world/mechanism dependency.
Local capture/inspection drivers stay outside the repository; no test infrastructure
or retail evidence payload is added.
