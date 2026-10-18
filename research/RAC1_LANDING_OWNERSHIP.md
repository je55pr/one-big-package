# R&C1 landing and standing regression correction

Authority is the original NTSC-U LEVEL0 overlay and class-0 animation data.
This pass responds to quiet landings flashing the run animation, stepped idle
poses, flip/Thruster landing coast and a short Heli stretch jump. It uses offline
source recovery; no new matched PCSX2 trajectory was captured.

## Motion ownership

The previous host ended special flight on its first grounded bit. That bypassed
native landing counter P+0x418, the temporary request scaling, the ground action
selector and its contact-derived carry caps. It also treated controlled scalar,
flight request and residual carry as a single planar speed.

`Rac1AirborneLandingSession` now keeps the recovered landing phase and returns
an explicit ground handoff. Idle/stop carry uses the source-owned 12.6/12
coefficients rather than changing the ordinary run-release or crouch brakes.
Thruster stretch's neutral stop caps its contact carry at 5*dt; held crouch has
its separate priority and cap. The exact predicates, source block hashes and
remaining source channels are described in
[the airborne evidence](RAC1_AIRBORNE_AND_MOVEMENT_EQUIPMENT.md).

Flip flight request no longer overwrites controlled scalar P+0x194. That scalar
is zero for a back flip and inherited contact scalar P+0x454 for other flips;
the directional request remains independent. The source also uses sine of the
absolute wrapped input-angle difference for inherited request, and brakes the
directional channel only after apex with predicted landing time below 12
updates. These corrections prevent a stationary flip's flight request becoming
spurious landing momentum, including when crouch stays held through touchdown.

Heli stretch now uses the common 5.7*dt target. The earlier 2*dt attribution
belonged to a different action branch. Its missing `53*dtSquared*age` forward
request ramp is implemented, followed by retained flight request and separately
updated contact-derived scalar.

## Animation ownership

The earlier animation-clock reconnection covered landing with active movement.
Quiet landings still used semantic launch history to choose sequence4, followed
by an immediate idle switch. Idle without a source clock exposed the renderer's
discrete 7.5-keyframe-per-second playback.

The host now supplies continuous ordinary airborne selection and one-update
native ground entries. Ground actions 0/2/3/4 transfer to standing, locomotion,
stop or crouch clocks using the interrupted sampled pose. The native -1/-2
curves target the selected sequence/frame. Standing interpolates every physics
update without altering its authored .125-per-update transition rate.

The native animation-event idle handoff selects standing0 with preset-2. A
counter-timeout handoff without that event preserves its air clip until cycle
completion, then blends to standing. A clip already frozen at its terminal
pose counts as complete at ownership transfer; losing its earlier transient
completion event cannot freeze the player indefinitely.

## Integration boundaries

The Godot host supplies the preceding completed contact sample before its next
movement pass. Native movement, collision and action selection have different
within-update ordering, so that handoff remains a bounded timing adapter.
Landing vertical request uses the profile gravity on flat support; native
terrain-pitch terms and live scripts are not a complete reproduced contact
executor. The independent in-air P+0x150 producer is not reconstructed by copying
the whole flight request. Animation cycle events remain a host proxy for the
native animation flags, and baked vertex interpolation approximates joint-pose
blending.

The dedicated Godot regression fixture checks quiet ordinary/flip landings,
the separate held-crouch flip handoff and release,
absence of a neutral run flash, idle pose changes between authored keyframes,
Heli accelerated flight and Thruster capped touchdown carry. Those synthetic
observations validate integration, not an exact retail travel-distance oracle.
The separate [slope-jump note](RAC1_SLOPE_JUMP_SUPPORT.md) records the missing
prelaunch support request and incorrectly cancelled admitted jumps.
