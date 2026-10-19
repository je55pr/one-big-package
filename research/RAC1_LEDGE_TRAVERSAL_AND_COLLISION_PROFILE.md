# R&C1 ordinary Ratchet ledges and collision query profile

The authority is the canonical NTSC-U LEVEL0 loaded overlay, SHA-256
`eaa4b0c0065e4a72e72ea4c6746c2a53169340501996077023353f12bfdb3804`.
Addresses below belong to that overlay, not the boot executable. No new live
PCSX2 witness was captured during this recovery.

## Contact shape inputs

Initializer `0x204f80..0x204fdc` writes ordinary Ratchet horizontal radius
`P+0x234 = 0.45f`, active/target upper contact heights `P+0x220/228 = 0.8f`
and lower heights `P+0x224/22c = 0.7f`. `0x2115cc..0x211610` approaches each
active height toward its target by `0.02 * native update scalar`. The action-4
target override at `0x2114dc..0x2114f4` writes upper target `0.35000002f`.

The primary correction executor at `0x2133a8` temporarily raises the query
origin by the lower contact height, then passes horizontal radius and
`max(upper - lower, 0.05f)` to `0x1f19a0`; it also executes `0x1f3170` with
the same radius. `0x2135ec..0x2135f0` bounds accepted corrections to eight
passes. The origin offset is removed afterward, and support ray correction
is separate. These facts do **not** prove that native collision is a Godot
capsule, nor do the two heights alone establish a full physical capsule
envelope. `Rac1CollisionProfile` therefore exposes the source query inputs
without assigning an unproved primitive type. Godot's shape, margins and
solver remain explicit host approximation.

### Small-step host adapter

`PlayerHost.Rac1SmallSteps.cs` supplies bounded traversal for shallow static
terrain lips which the foot-aligned enclosing capsule otherwise treats as a
wall. This is **OBP host policy**, not a reproduction of `0x1f19a0` and not a
recovered retail step-height field. Its maximum rise is a rounded-clearance
bound chosen as `lower contact height - radius * cos(support angle)` (about
0.411 units). This uses the recovered query inputs and 50-degree support angle
to bound the host correction; the formula itself is not recovered native code.
Both the current foot position and the terrain beneath it bound the rise,
preventing the capsule from incrementally climbing a taller wall corner.

Only already-supported ordinary grounded movement can use the adapter. An
opposed real slide contact must find a nearby ordinary-material top within the
rise limit and the recovered 50-degree support gate. Full-body upward, forward
and downward sweeps then require clear headroom, unobstructed travel and static
support. The resulting planar progress must improve on the original slide;
the support witness adds no planar distance. Real slide contacts are necessary:
a horizontal-only predictive sweep can miss corners encountered by the separate
downward request, particularly during diagonal approaches.

The capsule's radial corner normal is distinct from the queried top normal.
During the final downward settling pass only, the adapter can classify a corner
as floor down to normal-Up `(radius - maximum rise) / radius`; it immediately
restores the ordinary floor angle afterward. The vertical settling reach
accounts for the contact skin projected along that corner normal. These are
capsule-adapter choices, allowing a slow walk over a small lip while the top
itself still has to pass the source 50-degree gate. They are not retail radial
contact or step-response laws.

Airborne actions, ledge grabs, swingshots, known special-surface actions and moving supports
retain their separate paths. The original source request still owns contact
feedback and animation; placement corrections do not seed extra crouch carry.

A separate crest correction can undo only the current grounded uphill request
plus the existing contact skin when that tangent would overshoot a flat top.
It requires a walkable static shape contact, so crossing a shallow rim does not
briefly turn into a falling action. Global Godot floor snap remains disabled.

The imported LEVEL0 collision near native `(132, 113.5)` has ordinary floor
height `31.234375`; the adjacent ship-rock top at `(131.3, 113.5)` is
`31.4375`, a rise of `0.203125`. Before the adapter, the headless ordinary-input
trial stopped at scene X about `-131.803`. Both this eastern approach and the
southern rim now cross while retaining grounded action and floor contact.
These are Godot integration observations against retail geometry, not a live
retail movement replay. `Rac1GroundProjectionRetailTests` verifies the source
geometry, while `--movement-smoke` additionally checks walking, diagonal
approaches, 0.12/0.22/0.30/0.38-unit steps, a rejected 0.43-unit lip, a taller
wall and low headroom.

The two image-derived candidate routes missed the reported snag. A subsequent
user movement bookmark identified scene `(-131.647232, 31.240614, 108.697662)`
and effective native forward heading `1.9442879091`. Its adjacent looped-rock
top is about 0.30 units above the underlying `31.234375` floor, exceeding the
adapter's former `lower height - radius` limit of 0.25. That short ray therefore
returned the underlying floor instead of the obstructing top. The unmodified
180-update forward replay advanced only 0.00418 planar units before stalling.

With the revised clearance bound, replaying the same persisted pose and forward
input crosses the lip by update 31 and continues beyond it. The crossing itself
retains grounded action and floor contact; later movement off the rock can enter
an ordinary fall. The exact starting pose and heading are now an engine-smoke
regression in addition to the earlier candidate approaches. This validates the
host correction against the saved case, not the retail physical solver or human
signoff. `docs/MOVEMENT_REPRO_STATES.md` describes the local capture/replay workflow.

### Host collision shape consolidation

A later movement bookmark at scene `(-139.876053, 31.972511, 111.315048)`,
effective forward heading `-0.8210970474`, reproduced a severe frame-rate drop
while pushing into a dense static contact corner. Profiling a 600-update,
960x540 Forward+ replay separated the initial `MoveAndSlide` from step probes,
animation, camera and GPU rendering. Excluding startup samples, the original
contact cost was about 8.5 ms per native update and the rendered replay reported
about 7 FPS; the step probe itself cost about 0.07 ms and GPU rendering about
3.3 ms. A correct final position alone had hidden this render-rate failure.

The world blob retains 86,184 native octree triangle references, many repeating
the same surface. The old Godot shape also emitted a reverse copy of each
reference, yielding 172,368 overlapping shape faces. `BuildCollisionFacePlan`
now consolidates only exact scene-space geometry with identical material IDs,
keeping 31,423 faces for LEVEL0. `BackfaceCollision` supplies two-sided contact
without reverse copies. An explicit face-to-source table preserves the first
source triangle's identity and material byte; the source blob is not modified.
Different materials and merely nearby geometry are not merged.

The same local replay then held 60 FPS, with median contact cost about 1.3 ms.
These are host performance observations on one machine, not native solver or
retail frame-rate evidence. Unit tests pin the structural reduction and verify
all emitted LEVEL0 face identities without using hardware-dependent timing
thresholds. The dense corner remains a blocking contact; the original small-lip
movement bookmark still crosses. Movement, enemy-grounding and save-state
integration checks cover the shared shape boundary separately.

The additional ascending steep-contact response at `0x2121c0..0x212300`
requires a nonzero unsupported counter, support metric below `0.15f`, positive
previous final vertical displacement, a prior radial contact, and positive
radial-normal Z. For normal angle at least 50 degrees, it removes only the
opposed planar component and subtracts a further `2 * 54 / 3600` downward.
This radial normal is distinct from the support normal.

## Ledge detection

`0x20bed0..0x20c6fc` produces candidate flag `P+0x4e8`, top height `P+0x4e0`,
wall normal yaw `P+0x4e4`, hang target `P+0x4d0`, dynamic owner `P+0x4f8`
and body-clearance bit `P+0x4f4 & 1`.

The ordinary actor admission gate is separate from geometric detection. It
excludes collision mode 2 and a nonzero `P+0x1b0` cooldown, then admits
movement domain 2, action 8, shimmy actions `0x1a/1b`, or airborne domain 4
when raw flag `P+0x41e` is set or requested vertical motion is below
`3.5/60`. Action `0x0e` is excluded before authored animation time 37.

The source predicts two updates from `P+0x110`, subtracting three increments
of the selected gravity via `0x259508`. Four downward solid rays use forward
distances `0.45 + 0.075 * attempt`, with attempt 1 through 4, between
predicted Z + 1.65 and Z + 1.15. Candidate tops exclude source classes
0, 9 and 12, require a normal angle strictly below 20 degrees, and must not
exceed current player Z + 1.5. Dynamic surfaces additionally require an
authored traversal flag. A horizontal ray just 0.05 above the top rejects an
obstructed route to it. A separate radial/axial query annotates body clearance.
The top must lie at least 1.8 above the last retained support height.

Up to five horizontal support rays at top Z minus
`0.7 * (1 - sample / 5)` find the edge wall. Inward wall heading must differ
from player facing by less than 65 degrees. One-unit vertical solid rays
search outward along the wall normal in 0.03 increments below 2.4. The first
clear result gives hang target planar position at the clear offset minus
0.015 plus radius 0.45, and height `topZ - 1.43f` (literal
`0xbfb70a3d` at `0x20c0bc`, added directly at `0x20c4b8`).

`Rac1LedgeProbe` owns these query coordinates and gates. The Godot callback
maps native `(X,Y,Z)` to scene `(-X,Z,Y)` and returns native face classes.
Native query masks 2 and 4 currently share Godot's static mesh layer. The
clearance body uses an enclosing Godot capsule; resolved host displacement
substitutes for `P+0x110` in prediction. Those three seams remain host
approximations. Unidentified dynamic tops fail closed.

## Grab, hang, shimmy and ledge jump

| Action | Entry / update | Motion | Source animation |
| --- | --- | --- | --- |
| `0x18` | `0x224158` / `0x22d884` | toward cached hang target, capped `4/60` in 3D | 32, entry frame 0 when contact planar length exceeds `3.5/60`, otherwise frame 2; blend 9 |
| `0x19` | `0x224218` / `0x22d8ac` | same hold cap | 33, frame 0, blend 6 |
| `0x1a/1b` | `0x22425c/298` / `0x22dbb0` | query adjacent target at yaw +/-90 degrees, distance 0.3; average admitted targets; require corner difference <=30 degrees | 36/37, frame 0, blend 6 |
| `0x1c` | shared `0x224590` / `0x22e7f4` | recovered airborne ledge-jump profile | 34, frame 0, blend 4 |

Grab enters hang only after strict authored time `>13.5`. A lost continuation
query releases to action 6. Recent Cross within 17 updates requests `0x1c`;
an away heading over 135 degrees, magnitude above 0.5 and Cross within 9
updates instead releases downward. Button mask `0x0a` also releases. Lateral
input above 0.5 and within 45 degrees of either tangent enters shimmy only if
the neighboring ledge query succeeds. On release, shimmy waits for animation
cycle completion before returning to hang.

Shimmy planar motion reads the signed authored-time table at `0x1c3cb0` and
multiplies by 0.5; it is not a constant lateral speed. The 15 values are
`0, 0, .025, .050, .075, .162, .162, .107, .037, .037, .037, -.017, -.017,
-.035, -.035`. Native negative values briefly reverse the request. The
vertical component remains the average-target correction. The separate
continuation query `0x20c758` is retained as an evidence boundary; Godot's
current adapter reuses the stricter full candidate geometry rather than
claiming its membership masks and continuation clearance are byte-equivalent.

The body-clearance bit is not read by the recovered hang action selector.
It must not be invented as a new gate preventing the source action-`0x1c`
jump. Normal ceiling collision still constrains that jump. There is no
teleport-to-top rule. The separate wall-jump candidate producer at `0x20b5b8`
owns wall-jump admission; it is not inferred from this ledge query or from
HelpDesk terminology about jump slots.

`Rac1LedgeTraversalTests` checks the geometry against a deterministic authored
edge fixture, input/cooldown gates, grab timing, signed shimmy motion, exits,
profile height approach and steep contact response. Its retail-gated test
independently maps all seven serialized overlay sections and pins the exact
source block hashes and shimmy table. The combined ledge/surface targeted run
passed 27 tests with canonical ISO access, including all later guard/steep
contact cases. The later literal regression independently confirmed the exact
`-1.43f` hang offset. The complete production-host LEVEL0 movement smoke in
`captures/movement-broad-contact-smoke9.log` then passed real input-driven jump,
ledge grab, hang, signed shimmy, Cross ledge jump and R1 drop on an explicitly
synthetic static fixture. A separate pair of eight-unit vertical fixture walls
also admitted the recovered wall-jump query and launched action `0x11` with
sequence 35 upward and away from the facing wall. These fixtures validate
integration, not retail placement, trajectory or native primitive equivalence.
