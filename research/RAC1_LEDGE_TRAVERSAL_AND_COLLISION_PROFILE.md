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
