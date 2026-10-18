# R&C1 class-803 Swingshot pendulum

This is the second ordinary Swingshot target family, distinct from class-758
pull targets and the unused-by-default class-758 swing-mode branch. Authority:
SCUS-97199, canonical original NTSC-U ISO SHA-256
`ab849fe7cc9cc81c487d61b0d3ea15b5849943481b6a6ebf4d9aa9cf7bc40d9d`.
Veldin overlay SHA-256 is
`eaa4b0c0065e4a72e72ea4c6746c2a53169340501996077023353f12bfdb3804`.
All addresses below refer to that serialized loaded overlay. Source coordinates
have Z up and the player structure is `P=0x13f350`.

## Target and admission

Producer `0x221ee0` selects class `0x323` (803), independently of the class-758
producer `0x222158`. The authored census has 63 class-803 placements. Aridia
level 2 contains six; instance 701 is at `(71,280.07758,53)`, with radius 7,
maximum request length `0.33333334`, default range 16 and region -1. In the
same level there are eleven class-758 targets, all authored in pull mode.

Class-803 PVar fields consumed by player admission/entry/motion:

| Field | Source meaning |
| --- | --- |
| `+0` | Nonzero enables candidate admission. |
| `+4` | Target rope radius, copied to `P+0x9a0`. |
| `+0xc` | Maximum XYZ request length, approached by the motion limiter. |
| `+0x10` | If nonzero, copied to target PVar `+0x14`; otherwise `+0xc` is copied. This is a target-owned field, not established as player initial speed. |
| `+0x1c` | Region/polygon admission; negative bypasses it. |
| `+0x20` | Maximum candidate range; zero uses 16. |
| `+0x24/+0x28/+0x2c` | Nonzero radius-seek gain/damping/maximum-step overrides. Defaults are `.025`, `.33`, `11*dt`. |
| `+0x34` | Enables live target-facing work. This requires a separate live object owner. |

Ordinary non-aiming candidates must have live Moby flag bit 0 clear, must not
lie more than 3 units below Ratchet, and must pass 45-degree yaw/pitch cones,
range, region and line-of-sight admission. The ray begins at player Z+0.5.
The generic scorer contributes `distance*(1+absolute yaw error)`, plus the
same native visibility/priority penalties as the other target family. Camera
aiming and fall-action variants have distinct cone values and are not silently
handled by the ordinary helper.

`Rac1TraversalEquipmentSwingTarget.ReadAuthored` parses source payloads and
restores native anchor coordinates. Host code receives typed facts rather than
interpreting PVar bytes. Region-bearing targets and targets with live-facing
behavior require their missing producer/owner; the current integration is bounded
to unrestricted static targets and ordinary gravity.

## Entry and projectile boundary

Action `0x2c` entry is `0x223bbc..0x223e0c`. It sets domain `0xe`, copies
the selected target to `P+0x994`, clears attachment flag `P+0x99c`, clears
radius velocity `P+0x9a8` and initial-forward velocity `P+0x9b4`, and sets
initial-boost flag `P+0x9be` to 1. Initial radius `P+0x9a4` is actual 3D
player-anchor distance. `P+0x9ac` is the initial anchor-facing yaw.

Entry retains the existing request `P+0xe0`. It does not initialize player
velocity from target PVar `+0x10`, nor copy contact vector `P+0x110` or final
translation `P+0x100` into that request. An OBP development attachment should
therefore seed the session from its current fixed-update request. Substituting
contact motion is a host approximation, not a recovered native entry.

Native held-gadget code sets attachment flag `P+0x99c` at `0x2c6c94` after
projectile arrival and its own visual transition. The source session's `Attach`
method begins at this attached boundary. Direct attachment from a development
input is explicitly an OBP preview policy and does not implement the Swingshot
projectile, equipped gadget model or arrival effects.

Entry selects sequence 52 with eleven blend ticks and multiplier `.7`. When
that sequence completes, `0x219e74..0x219ec8` selects sequence 53, next frame
10, twelve blend ticks, and multiplier `.3`. Subsequent source pitch/roll and
pendulum animation-rate adjustments are separate presentation work.

## Attached normal-gravity motion

`Rac1TraversalEquipmentSwing` models the static-target, ordinary-gravity branch
of `0x219e70..0x21a720`. Here `dt=1/60`, and fixed-update multiplier is 1
(boot small-data `0x15ed64` is f32 1, independently verified alongside the
existing timing globals). Its request is distinct from the later direct radial
position correction.

1. While Ratchet is below the target, action age is strictly over 40, and
   conditioned magnitude is strictly over `.25`, add a planar pump in the native
   control-relative input direction. Its magnitude is
   `min(.21*dt*inputMagnitude,5*dt)`. Helper `0x2118c8(1)` constructs that input
   target; it is not the current body-facing direction.
2. When planar target distance is below `targetRadius*1.57`, or source support
   distance exceeds `1.5`, replace request Z with `P+0x100.Z - 40*dt²`.
   Request XY remains intact. If the initial-boost flag is still set and this
   result is positive, subtract another `57*dt²`.
3. Apply whole-request drag `request *= (1-.015*dt)`.
4. If request length exceeds PVar `+0xc`, approach that authored limit by
   `15*dt²` and set the vector to that length. Zero is a real source limit in
   this player branch, not a default; the tested actual Aridia values are 1/3.
5. Offset the attachment point from player position by
   `(sin(facingYaw)*.15,-cos(facingYaw)*.15,0)`. Add the request and project that
   predicted attachment point to the sphere of the current radius about the
   target. Subtract the original attachment point to obtain the constrained
   request.
6. Advance radius toward the authored target radius with the source damped seek.
   If radius shrinks, directly correct player position along the projected radial
   vector by the radius change. This is a separate source output; it is not
   silently included in the primary request.
7. While initial boost remains set and Ratchet is below the anchor, a planar
   speed below `14*dt` receives a forward kick. The source damped seek operates
   on planar speed with gain `.047`, damping `.3` and no explicit maximum step;
   its velocity increment is applied in the entry target-facing direction.
8. On an unsupported update, initial boost clears when request Z changes from
   negative to positive, or the source pendulum presentation-transition flag is
   nonzero.

The generic damped seek at `0x25b798`/`0x25b8c0` is:

```text
error = target - value
velocity += gain*error - damping*velocity
if maximumStep > 0: clamp velocity to +/-maximumStep
clamp velocity to +/-abs(error)
value += velocity
if abs(target-value) < .01*maximumStep: value=target; velocity=0
```

The numerical API uses existing double native vectors and retains f32 authored
literals. It does not claim PS2 instruction-level floating-point equivalence.
Target-facing body yaw, pitch/roll, special-gravity frames, live target movement
and animation-dependent corrections remain distinct recovery/integration gaps.

## Release and host adapter

Action update `0x22c860..0x22c8e8` retains the swing while use is held. After
use release it waits for action age strictly above 10, then enters action `0x2d`.
Age below 40 sets the premature-release flag. Entry `0x223e0c` copies full XYZ
contact motion `P+0x110` into request `P+0xe0`, sets domain 2, and selects
sequence 11 with fifteen blend ticks. Fall fields `P+0x940/+0x944` become
`27*dt²` and `7*dt²`.

Keep the two native motion facts separate: swing gravity consumes the Z of
final translation `P+0x100`, while release preserves controlled contact motion
`P+0x110`. The latter must not be replaced with arbitrary final world delta.
The session evaluates release after `Step`, matching native motion/contact
before action update. An adapter checking before its next step must account for
that update boundary.

Godot may sweep the direct radial correction together with the request to
prevent the development adapter from moving through collision. This is a named
OBP collision policy; it is not a claim that the source performed the same sweep.
The native sphere/segment contact solver and dynamic support transport remain
separate owners.

## Verification

`Rac1TraversalEquipmentSwingTests` independently pins source entry, motion,
target admission, release, damped helper and attachment-flag code hashes. The
retail case reads the actual Aridia target PVars/positions and separates six
class-803 targets from eleven class-758 targets. Portable cases cover target
identity/geometry, native coordinate conversion, radius correction separation,
a full 150-update arc with initial-boost turnover, release timing and fall fields,
and rejection of dynamic-facing targets. All nine targeted tests passed with
canonical retail authority enabled.
