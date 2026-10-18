# R&C1 airborne actions and movement equipment

This recovery uses the canonical original NTSC-U gameplay overlay, not the
boot executable at the same virtual addresses. Veldin range 0 SHA-256 is
`eaa4b0c0065e4a72e72ea4c6746c2a53169340501996077023353f12bfdb3804`.
The seven serialized sections are independently mapped by
`Rac1AirborneNativeSourceTests`. No executable payload is retained here.

The dispatcher has separate motion (`0x1e7e20`), entry (`0x1e8030`) and
action-update (`0x1e82b0`) tables. Action 8 is glide. Action 0x0b is the
directional flip family. Action 0x0e is an aerial boost distinct from either.
A generic second-Cross-to-flip mapping is not recovered and must not be
introduced. The retail HelpDesk corroborates the names Glide, Stretch Jump,
Boost Jump, side flip and back flip; executable predicates remain authority.

## Recovered jump entry profiles

Numbers below are impulse-height parameters in native units, not guaranteed
world-space apexes. Native request is displacement per update. At the boot
timing context, `dt = float(1/60)` and `dtSquared = float(1/3600)`; computing
`dt*dt` produces a different final binary32 bit pattern.

| Action | Meaning | Height min / max | Launch age | Growth denominator | Gravity coefficient | Entry sequence / frame / blend |
| --- | --- | --- | --- | --- | --- | --- |
| 7 | stationary jump | 1.47 / 2.62 | 5 | 15 | 29.7 | 7 / 0 / 5 |
| 9 | moving-jump family | 1 / 2.7 | 5 | 15 | 29.7 | 8 family, native stride entry lookup / blend 5 |
| 0x0a | Heli-Pack stretch jump | 1.9 / 1.95 | 5 | 14 | 11 | 18 / 0 / 5 |
| 0x0b | directional flip | 3.2 / 3.25 | 5, or 1 after air domain 4 | 14 | 29.7 | 28/29/31, frame 0 / blend 5; fast entry frame 1 / blend 4 |
| 0x0d | Thruster-Pack boost jump | 0.1 / 0.2 | 9 | 14 | 29.7 | 17 / 0 / 5 |
| 0x0f | Heli-Pack boost jump | 1.9 / 1.95 | 9 | 5 | 29.7 | 21 / 0 / 5 |
| 0x10 | Thruster-Pack stretch jump | 0.4 / 0.9 | 1, or 4 after native air/recent-crouch history | 1 | 8.5 | 38 / 0 / 5; history override blend 8 |
| 0x11 | wall jump | 3.3 / 3.35 | 9 | 12 | 29 | 35 / 0 / 7 |
| 0x1c | jump from ledge | 2 / 2.05 | 10 | 1 | 25 | 34 / 0 / 4 |
| 0x69 | mud escape jump | mud depth + 1.7 / depth + 1.75 | 5 | 19 | 29.7 | 7 / 0 / 5 |

The common entry at `0x224590` copies contact-processed P+0x110 into request
P+0xe0. It clears pending impulse P+0x428 and already-applied impulse P+0x42c
at `0x2246c8/+0x6cc`; a new flip therefore does not preserve an old jump's
impulse accumulator. Contact displacement and the accumulator are different
channels. The separate P+0x3f8 post-apex gravity override and ordinary action
7's retained launch/update ordering still require careful integration; the
existing measured ordinary jump table is not silently replaced by this note.

The generic vertical helper `0x214108..0x214658` grows the height parameter
while Cross remains held, or when neither an impulse nor an applied impulse
exists, provided action age is at most 15. Each growth update computes:

```
height = min(height + (maximum - minimum) / growthTicks, maximum)
newImpulse = sqrt((height + height) * gravityStep)
pending += (newImpulse - applied) - pending
```

At or after launch age, a positive pending impulse adds to requested vertical
displacement and to `applied`, then clears. This is incremental impulse
addition, not repeatedly replacing vertical speed. The helper subtracts
P+0x4a0 gravity, clamps worsening displacement against
`contactProcessedVertical - 0.1f`, and clamps terminal descent at `-50f*dt`.
Before launch it requests `-48f*dtSquared` against support. Ledge action 0x1c
returns early before age 10 instead of adding that ground support request.

The two high pack jumps also have source-authored impulse scripts. Thruster
boost uses table `0x17bf20`: coefficient 151 for 5 updates, zero for 27, then
coefficient 22 with +2 per update for 6, retaining the previous value with -3
per update for 6, then zero with +2 for 99. Script range for that action is
[9,44), so later records are retained provenance, not all exercised by this
bounded session. Heli boost uses coefficient 48 for 1000 updates from table
`0x15f6e8`, admitted over [35,71), or [34,70) under its native timing selector.
Coefficients multiply `dtSquared` and add to request before gravity. They are
not animation-driven root motion. Thruster stretch gravity approaches
`25f*dtSquared` by `7f*dtSquared` after native age 54.

## Airborne boost (action 0x0e)

The native second-Cross route at `0x22f684..0x22f764` admits an aerial boost
when the earlier action grants P+0x4af, native age is greater than 14, the
recent Cross history qualifies, and landing/animation/surface guards permit
it. P+0x12ea is kept as a special-surface flag, distinct from the recovered
water flag P+0x12e4. The history search window is `clamp(actionAge-4,2,30)`. Before apex it can
enter directly. After apex it additionally needs ground clearance above 0.7
and absolute vertical request below `5.8f*dt`. Earlier caller exclusions still
apply. Entry leaves P+0x4af clear, so replaying the new entry does not grant an
unbounded repeated boost. This branch has no explicit active-pack ownership
test; it is not presented as a Thruster-only move.

Sequence is 22, entry frame 3, positive blend 7 and initial playback multiplier
0.8. The special helper at `0x214108..0x2142d4` approaches an upward target
`7f*dt` by `150f*dtSquared` for the first eight action ages. The target scales
to 0.7 above 1.7 rise, and 0.45 above 2.1 rise. Active enabled Thruster-Pack
with Clank uses thresholds 2.1 and 2.7, with scales 0.7 and 0.47; enabled
Thruster-Pack additionally multiplies the target by 1.25. Existing faster
ascent is not clamped down to the target. Gravity and contact/terminal clamps
then apply normally.

Rise is measured from the **original jump launch position P+0x400**. The boost
entry uses launch age -1, so it does not replace that origin with the new boost
entry position. Substituting a boost-entry-relative height changes its law.

## Glide (action 8)

Entry `0x224520..0x224590` copies P+0x100 into P+0xe0, caps associated contact
motion at `4.5f*dt` when required, and selects sequence 19, frame 0, blend -2.
The native negative blend uses a preset; treating -2 as an arbitrary positive
blend duration is host policy and must remain identified as such.

Motion `0x21e8cc..0x21ead0` selects base planar step `3f*dt` for Heli-Pack and
`5f*dt` for Thruster-Pack. Conditioned input magnitude scales the planar target.
P+0x194 approaches it by `15f*dtSquared` when accelerating or
`7f*dtSquared` when slowing. The motion builder uses the action-owned facing,
clears vertical and requests descent `-(0.72f * baseStep)`. Thus neutral input
reduces horizontal movement but does not remove downward glide movement.

The final airborne admission requires Clank, an enabled aerial pack, held
Cross, age strictly greater than its action-specific tail gate and P+0x4a6,
and sufficient ground clearance. Ordinary and stretch jumps additionally need
the earlier caller's recent Cross **press** within `clamp(age-4,2,30)`; holding
the initial jump does not automatically become glide. Only pack high actions
0x0d/0x0f and aerial boost 0x0e continue to this tail without that history edge.
Ground actions 7/9 require clearance >1,
boost 0x0e >1.5, other admitted families >0.6. Shared minimum age is >20,
Thruster stretch uses >8, and aerial boost uses >17 when clearance exceeds 4.
The Heli boost P+0x4a6 gate is 85; its separate timer of 70 is not this gate.
Ledge/mud/flip and other explicit earlier caller exclusions are preserved.

## Ground crouch and directional input admission

`0x229660..0x229910` prioritizes the directional flip chord when the crouch
button family is held and input history supplies the native Cross/direction
chord. Direction classifier `0x221df8..0x221edc` compares input target yaw with
current body facing. It returns right 0, left 1, forward 2, back 3; strictly
inside a 45-degree sector is required, so exact boundaries retain forward.
Forward has no directional-chord grace window; other directions use 12.

Otherwise native crouch age must exceed 6 and Cross must occur in its 9-update
history search. Pack 2 with Clank and enabled equipment selects Heli high or
stretch; pack 3 selects Thruster high or stretch. Stretch requires conditioned
magnitude >0.7 and P+0x168 forward request >`0.9f*dt`. Heli additionally accepts
forward obstruction distance P+0x248 >2.5 **or** hit-normal angle P+0x250 <pi/4;
Thruster accepts distance >2.5 **or** crate-family flag P+0x254. Thruster
P+0x1b8 can consume the attempt without selecting an action. Missing equipment
falls back to action 7.

P+0x248 is a **forward obstruction distance**, not ground clearance. P+0x168
is the nonnegative forward projection of controlled requested movement, not
vertical displacement. Confusing these fields reverses the high/stretch rule.

## Planar ownership

- Heli stretch 0x0a builds its anticipation request from the prior scalar,
  then adds a forward term `53f*dtSquared*age` during ages 0..4
  (`0x21ed70..0x21ef18`). Its scalar approaches `5.7f*dt*inputMagnitude`
  using acceleration `20f*dtSquared` and deceleration `11f*dtSquared`.
  After launch it retains that authored request rather than accepting ordinary
  air steering (`0x214658..0x2146c4`). Its support carry remains separate.
  A previous implementation incorrectly attributed action 0x49's `2f*dt`
  P+0x424 override to Heli stretch; action 0x0a retains the common `5.7f*dt`
  cap. The nearby `3.5f*dt` override belongs to aerial boost 0x0e, while
  flip 0x0b retains the common cap and has its separate flight helper below.
- Thruster stretch 0x10 uses locked body heading. Until native age 55 its target
  is `11.5f*dt` when entry ground clearance <=1.4, otherwise `5.7f*dt`; later
  target is `3.5f*dt`. Acceleration is `44f*dtSquared`, deceleration
  `45f*dtSquared` (`0x2146cc..0x214790`).
- Ordinary air convergence `0x213e68..0x214108` scales its base increment by
  `clamp(1.5*(1-cos(current,target)),1,3)`. Either zero vector uses gain 1.5.
  The default active-input base is `20f*dtSquared`. Low/neutral target uses
  `2f*dtSquared` with no wall contact, giving the retained `1/1200` neutral
  decay. A native wall counter changes that low-input base to `6f*dtSquared`.
  Ice/contact overrides and moving support channels are not silently admitted.
- Heli/Thruster high-jump target caps are respectively `3.3f*dt` and `1.9f*dt`;
  aerial boost cap is `3.5f*dt`.
- Directional flip `0x214958..0x214b04` preserves current entry **input** yaw
  and a ten-update-old input yaw. A directional scalar approaches `4.2f*dt`
  by `8f*dtSquared`, or zero when apex was passed and predicted landing time
  P+0x44c is strictly less than 12. Entry retains contact scalar P+0x454
  only when it exceeds `1.5f*dt`. Motion writes persistent controlled scalar
  P+0x194 to zero for back flips and to P+0x454 otherwise; the directional
  request must not overwrite that scalar. Request inheritance separately uses
  `sin(absoluteWrappedAngle(oldInput,newInput))*P454` along the older input
  heading, including back flips' still-retained P+0x454. Replacing either
  input heading with body facing, using cosine, or reversing the predicted
  landing comparison changes the recovered trajectory and landing carry.

## Thruster-Pack Power Slam

Action `0x22` (decimal 34), rather than wall-jump action `0x11`, is the
Thruster-Pack Power Slam. Entry `0x223e5c..0x223ebc` preserves the current
request and scalar, initializes descent acceleration to `100f*dtSquared`,
and selects sequence 42 at frame 0 with blend 6. Its action domain is 11.
Admission `0x22eae4..0x22ebc8` accepts only actions 7/9/0x0d/0x0e/0x0f,
age strictly greater than launch age + 8, conditioned input below 0.3, a
crouch-family press within 9 updates, and enabled Thruster-Pack 3. If apex
was passed, predicted landing time must exceed 10 updates.

Motion `0x21a720..0x21a92c` brakes planar scalar by `30f*dtSquared`.
Before age 12, vertical request approaches `5.7f*dt` by `70f*dtSquared` in
either direction. Ages 12 through 33 inclusive preserve vertical request;
after 33 it decreases by `100f*dtSquared`. This is separate from the
ordinary jump gravity and terminal fall clamp. The first negative dive
request sets P+0x4b8. While unsupported, native damage/lock probes run;
while supported, the impact update counter increments and the first impact
starts camera effects. Combat and camera effects are outside the bounded
movement session. Grounded impact does not itself end the action: update
`0x22c8e8..0x22ca24` ends it on animation flag P+0xa98 bit 2. The current
host's animation-cycle event is an explicitly identified approximation of
that native event, and native landing-prediction animation retiming remains
outside this session.

## Wall-jump candidate and motion

Candidate producer `0x20b5b8..0x20b914` requires ground clearance at least
0.4 and requested vertical displacement at least `-20f*dt`. An existing
ledge candidate clears the wall lease. It predicts position 20 updates
using contact-motion XY and requested Z minus `0.5*g*20*20`; a successful
ledge continuation at that position also clears the lease and requests
a 22-update ledge timer.

The low mask-2 segment goes from player-local `(radius-0.02,0,0.3)` to
`(1,0,0.3)` and must hit. The high mask-2 segment goes from `(0,0,h)` to
`(radius+1.4,0,h)`: h is 1.7 for action 0x11 or forward flag P+0x255 with
no crate-family P+0x254, otherwise 3.0. The high hit must exclude surface
classes 10 and 12, have normal angle at least 75 degrees from up, face
within 50 degrees of the player, and lie within 0.72 of the segment origin
(0.95 when already in action 0x11). Current, previous, or second-previous
movement domain must be air domain 4. After an earlier wall jump, the next
normal must differ by at least 170 degrees from the previous launch normal;
this prevents repeated jumps from the same wall. A successful probe refreshes
P+0x1b4 to 6 updates, or 7 while already wall-jumping. Most rejected probes
leave the prior lease unchanged, so callers must distinguish rejection from
the explicit ledge veto.

Selector `0x22f414..0x22f4c8` accepts actions 7/9/0x11 with a live lease,
no landing, and Cross within 7 updates whose press age is younger than the
current action. Repeated action 0x11 additionally requires age >25. No
special authored wall-jump surface flag was found in this path; the actual
surface, opposing-wall history, and geometric predicates remain mandatory.

Entry `0x225278..0x225338` caches the admitted normal and selects sequence
35. Regular-axis planar motion `0x214b04..0x214c40` preserves inherited XY
before age 9, then requests `4.9f*dt` along the cached outward normal. Body
yaw smoothing starts only after age 9 and is separate from this direction.
The host static-ray executor and use of the existing ledge candidate probe
for the future ledge-continuation callback remain OBP adapter boundaries;
the controller's current-domain-only admission is conservative relative to
native three-domain history. Unknown world surface classes are rejected.

## Implementation and limits

### Corrective landing recovery

First support does not immediately become ordinary ground movement. Shared
update `0x22ee30..0x22ef40` initializes landing counter P+0x418 to 1 only
after launch, after apex P+0x41e, and with support counter P+0x30e equal to
zero. Aerial boost suppresses this while authored animation time is below 35.
An initialized landing counter increments on later updates even before the
airborne action hands off. The source animation time/bit2 event are supplied
facts, not guessed host deadlines.

Regular-axis landing motion `0x21edb0..0x21ee68` builds controlled request
from the prior scalar; starting at landing counter 2 it temporarily multiplies
that request scalar by 0.4, then restores the persistent scalar. Except at
counter 1, P+0x194 independently approaches the conditioned target using
P+0x480 acceleration and P+0x484 deceleration. The ordinary profile rates
are 20 and 11 times squared duration; Thruster stretch uses 44 and 50.
`0x216710` independently brakes P+0x150 carry by P+0x484 and adds the
remaining planar carry. Treating all three quantities as one ground velocity
preserves too much flight speed on landing.

Heli stretch enters ground stop action 3 immediately when P+0x418 is nonzero,
selects sequence 5 with blend -2, then multiplies contact-derived P+0x150
by 0.8 (`0x22f224..0x22f280`). Other profiles call ground selector
`0x2293a8..0x22965c` after the landing counter exceeds 1. Crouch has priority
and enters action 4, sequence 13/frame 2/blend -2, copying current contact
carry with the native 7*dt cap. Conditioned magnitude strictly greater than
0.3 with no control lock enters action 2 and requests sequence 4, with blend
8 (14 after landing counter 5). Otherwise Thruster stretch, or forward
request strictly greater than 3*dt after counter 3, enters ground stop
action 3/sequence 5/blend -1 and caps contact-derived carry at 5*dt. Neutral
landings outside those stop cases retain their current airborne action until
counter >12 or animation P+0xa98 bit2, then enter action 0.
The event and counter paths have different animation requests: bit2 selects
ordinary standing sequence0/frame0 with blend-2 (`0x229620..0x229644`), while
counter>12 without that event suppresses a new selection and retains the old
clip through the idle handoff.

Both ground stop 3 (`0x22371c`) and idle 0 (`0x222610`) copy the most recent
contact P+0x110 into carry P+0x150 at entry. Idle handoff therefore does not
zero contact movement or reuse an older flight scalar. Their motion clears
the controlled request and adds braked carry: idle selects
`12.6f*dtSquared`, while stop selects `12f*dtSquared*min(1+P+0x16c,1)`
(`0x217e0c..0x217fcc`). On ordinary flat support the latter factor is 1.
`BrakeGroundCarry` exposes that separate post-landing recurrence without
retuning ordinary run-release or crouch coefficients.

`Rac1AirborneLandingSession` keeps those channels, counter boundaries and
ground handoff metadata explicit. Its `ContactMotion` must describe the
latest completed primary contact pass, not the prior flight scalar. Ordinary
surface axes are the bounded scope; ice, alternate contact frames, and the
buffered landing-jump branch remain separate. The host still supplies action
age/order and admitted support/apex/animation facts.

### Contracts and checks

New engine-independent contracts are `Rac1AirborneJumpProfile`,
`Rac1AirborneMotionSession`, `Rac1AirborneAdmission`,
`Rac1AirborneBoostSession`, `Rac1AirbornePlanarMotion`,
`Rac1AirborneFlipPlanarSession`, `Rac1AirbornePowerSlamSession`,
`Rac1AirborneWallProbe`, `Rac1AirborneWallJump`, `Rac1AirborneHeliLongSession`,
`Rac1AirborneLandingSession` and `Rac1MovementEquipmentGlide`.
Callers supply native action age, conditioned input, admitted contact and
inventory facts. Their unit tests cover strict boundaries, impulse accumulation,
script transitions, source-authored vertical targets, angle-dependent planar
control, and explicit interruption.

Ceiling cancellation and the meaning of a host `Landed` fact are OBP collision
adapter policies. This work does not reproduce R5900 FPU/VU rounding, the full
retail contact solver, action sound/effects or
every animation-rate correction. `MathF` trigonometry is an approximation of
the native trigonometric implementation. The measured ordinary jump path stays
separate until native age/update ordering agrees with retained live traces.

Reproducible source checks run with the authorized canonical `OBP_RAC1_ISO`:

```powershell
dotnet test tests/OBP.Tests/OBP.Tests.csproj -c Release --filter "FullyQualifiedName~Rac1Airborne|FullyQualifiedName~Rac1MovementEquipmentGlide"
```

`Rac1AirborneNativeSourceTests` pins dispatcher targets, complete relevant code
block hashes, immediate float literals, impulse resets, VU square-root dispatch,
and scripted impulse tables. This provides offline authority checks; it does
not replace a new controlled retail gameplay comparison.
