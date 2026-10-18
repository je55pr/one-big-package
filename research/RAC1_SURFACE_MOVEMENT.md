# R&C1 ordinary ice, mud and class-zero transition movement

This recovery uses the canonical NTSC-U LEVEL0 loaded overlay, SHA-256
`eaa4b0c0065e4a72e72ea4c6746c2a53169340501996077023353f12bfdb3804`.
It identifies source controllers present in that overlay; it is not a new live
Hoven/Aridia/Novalis movie. Existing retail collision censuses independently
establish authored class-7 ice on Hoven, class-3 mud on Aridia, and class-zero
water surfaces on Novalis. `Rac1SurfaceMovementTests` pins independent source
blocks and the dispatch table, rather than treating a Godot feel test as native
authority.

## Ice action 0x2f

Action `0x2f` shares entry `0x2232d4` and update `0x22b434` with ordinary
locomotion, but dispatches movement to `0x21c438`, which first invokes
`0x2167d0`. Entry `0x2233cc..0x223454` clamps the entry scalar/request to
`7.7/60`, clears the previous slope cache, forces action `0x2f` and selects
sequence 55 with an eight-update blend.

The ordinary static ice helper constructs a target planar vector of length
`conditioned magnitude * 5.9/60` along desired native yaw. Its vector-approach
limit is

```
(2.45 * magnitude + 2 * max(0, -cos(previous request, target))) / 3600
```

The cosine term is zero when either planar vector has zero length. When
previous controlled 3D contact length exceeds `5.9/60`, a separate `13/3600`
limit overrides that input approach. The vector being approached is the
contact-produced `P+0x130`, not body facing and not a scalar ordinary-run
speed. Neutral input on flat ice supplies zero approach limit below overspeed;
there is no normal-floor braking coefficient hidden in this branch.

For support angle strictly above five degrees, a second approach pulls that
result toward downhill normal azimuth. Its target speed is
`(8/60) * angle/(pi/4)`, with step limit `(8.5/3600) * angle/(pi/4)`.
The source subsequently executes terrain/wall clipping. It also multiplies
the request by `cos(current terrain pitch)` when previous cached pitch is
below one degree and current pitch exceeds five degrees. The model accepts
these optional pitch facts separately from the support normal; one is not
silently substituted for the other.

The grounded action tail clears request Z and subtracts `150/3600`. An
unsupported tail instead subtracts `25/3600` from the previous final-motion
vertical component. Existing source jump admission remains available. Native
animation update can later choose sequence 109 or standing according to
input, speed, slope, animation transition state and source timers; the
surface model publishes initial sequence 55 and leaves those extra neutral
presentation variants as a remaining fidelity boundary.

`P+0x130` is independently constructed at `0x213b5c..0x213bbc`: zero Z in
both original request and primary actual displacement, normalize the latter,
project the former onto it with a nonnegative clamp, and return a vector along
the original planar request. The preceding `P+0x110` projection remains 3D.
The surface session's `RecordPrimaryContact` computes both; applications must
supply primary contact displacement before support/conveyor/external transfer.
If the caller supplies no feedback, propagation assumes unobstructed motion.

Crouch on ice is still native action 4, whose existing independent evidence
selects carry decay `1.7/3600` rather than ordinary `6.48/3600`. Its entry
copies controlled `P+0x110`, caps full length to `7/60`, then the carry helper
removes Z and preserves heading while reducing length. The surface session
implements that separate branch without arbitrary distance retuning.

The bounded implementation does not claim the helper's weapon-specific
overrides, special global mode, injected `P+0x920` impulse, exact native world
query membership, or collision execution. Godot still executes world contact.

## Mud action 0x68 and escape action 0x69

Entry `0x22668c` records mud escape history, sets movement domain `0x19`,
zeros input/scalar speed and selects sequence 113 with blend 8. Movement
`0x21f710..0x21f778` clears the whole request and supplies only negative Z:
normally `1/60`, reduced to `0.25/60` when
`mud surface height - playerZ > (escape count + 1) * 0.5`.

Update `0x22be90` admits action `0x69` when escape count is below two, action
age is strictly greater than `15 + 25 * escape count`, and a Cross press is
present within seven source input-history updates. The independently recovered
airborne profile uses the current mud depth: jump minimum/maximum heights are
depth + 1.7 / depth + 1.75, launch delay 5, growth window 19, sequence 7,
blend 5. It is not ordinary jump 7 with a renamed action number.

The mud update requests environmental restart if playerZ falls strictly below
surface height - 1.2, or after the source age limit 170. The session returns a
typed death request to the existing lifecycle; it does not respawn or alter
campaign state itself. Applications must retain authored mud-surface height,
not follow the sinking feet with a new inferred surface plane.

## Class zero / existing action-0x72 label

The retained routing label is `ShallowWaterWade`, but the newly inspected
action `0x72` does not provide freely controlled walking. Entry `0x227344`
sets domain 9 and selects standing; update dispatch is the common no-op;
movement shares `0x217a00` with idle/stop. That path zeros input acceleration
and decays an existing `P+0x150` carry by ordinary `6.48/3600`, then executes
the existing support/downward request tail.

The current adapter bridges entry carry from its prior controlled-motion seed
because the exact source `P+0x150` producer across this water transition is not
yet recovered. That entry bridge is an **OBP approximation**, not recovered
source initialization. This bounded state prevents a source-class-zero contact
from being silently treated as ordinary walking or throwing inside the host;
it is not complete water volumes, surface swimming, diving, Hydro-Pack
translation, oxygen depletion or drowning recovery. The separate recovered
water vocabulary distinguishes underwater domain `0x11`, surface-swim domain
`0x12`, Hydro-Pack action `0x35` and oxygen-mask ownership.

Magneboot floor orientation remains a separate source controller. The surface
session deliberately refuses that interaction rather than applying ordinary
up-axis physics to a wall or ceiling.

## Host integration validation boundary

The ice overspeed test reads `P+0x160`, which the contact tail stores as a
single at `0x213c38`. The runtime rounds the reconstructed contact length to
that source cell's precision before comparing with `5.9f * dt`. This avoids
double reconstruction noise turning an exactly-at-target request into an
overspeed state; the next larger source-float value still takes the separate
13-coefficient branch.

`OBPGame.Rac1TraversalContactSmoke` builds an explicitly synthetic raw-class-7
static floor. The actual input, production host, collision feedback and source
animation path accelerate to 5.9 units/second, retain neutral motion and apply
the separate ice crouch brake. The complete LEVEL0 movement smoke passed in
`captures/movement-broad-contact-smoke9.log`; that log is local evidence and is
not a distributable retail payload.

The later complete smoke7 still measured a flat-floor neutral transition from
5.9 to 5.4667 units/second across eight observed updates, while the pure native
law with exact unobstructed contact retains 5.9. The host retains most momentum,
but this trajectory difference is not resolved. Vertical jitter in Godot's
reconstructed primary contact could trigger the native overspeed branch; that
explanation remains a hypothesis. No coefficient was retuned to conceal it.

The first fixture placement at scene coordinates around 10,000 exposed a
large-coordinate Godot host boundary: very small accelerating requests could
return zero resolved planar displacement and repeatedly restart the ice
acceleration. The identical synthetic floor at coordinates around 2,000 passes.
The placement dependence is consistent with host floating-point precision,
but the exact engine-side cancellation has not been isolated. No native
coefficient was changed to mask that collision limitation.
