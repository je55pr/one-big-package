# R&C1 Ratchet movement controller

Authority: NTSC-U original retail (`SCUS-97199`, build `rac1-ntscu-original`). The recovered values below come from controlled retail execution, live PINE sampling and the loaded authority executable. DebugPlayer calibration values are not evidence for native R&C1 motion.

## Controller boundary

Retail exposes Ratchet's actual per-update XYZ displacement in the player-state structure at `0x0013f3d0 + 0x80/+0x84/+0x88` (`0x0013f450` for X). Across controlled traces those floats match live Moby position deltas component-for-component on game updates.

OBP therefore models the R&C1 controller in **native world displacement per 60 Hz update**. `OBP.RAC1` owns acceleration, jump and crouch recurrences. `OBP.Runtime.Player` carries only neutral input/contact facts. Godot converts the native step to engine velocity and retains ownership of world collision, floor/ceiling contact and slope response.

This separation is important: retail slope traces acquire vertical displacement while ordinary locomotion remains active. That vertical component is collision/terrain response, not evidence for a second controller gravity equation.

## Ground locomotion

From the fixed Veldin savestate, full planar input ramps from rest by approximately `1/480` native unit per tick (`0.002083333...`) until a sustained displacement magnitude of `0.09500919` unit/tick.

Releasing planar input enters the native locomotion-stop path rather than zeroing motion. Flat-ground displacement decays by approximately `1/300` unit/tick (`0.003333333...`) until exactly zero.

The production controller preserves those native-tick values. It does not relabel the former DebugPlayer `MoveSpeed=10` value as retail Ratchet speed.
## Jump and airborne motion

A normal jump has an eight-update input/anticipation phase before vertical translation begins. Controlled Cross holds during that window produce the observed launch-step table used by `Rac1RatchetMovementController`: minimum launch is about `0.13816452`, rising through `0.14207458`, `0.14589500`, `0.14962959`, `0.15328598`, and capping at about `0.15686607` native unit/tick.

After launch, holding Cross temporarily softens the rising recurrence for nine observed updates. Releasing Cross uses a steeper rising decrement of about `0.00825` unit/tick. After the apex, the falling branch uses about `0.009652` unit/tick per update. Holding Cross beyond the recovered assistance window does not increase height indefinitely: the 300 ms and 500 ms witnesses reach the same maximum apex.

Representative stationary-jump retail apexes from the same start are:

- short tap: `+1.22678375` native units;
- 100 ms hold: `+1.50166893`;
- long/max hold: `+2.05437660`.

The portable recurrence reproduces each within `0.00003` native unit.

A controlled full-speed running jump with a 100 ms Cross hold remains airborne for about `0.599474 s`, travels `3.5152865` native units horizontally and reaches `+1.50166893`. The deterministic controller replay resolves 37 displacement updates, `0.600 s` sampled air time and `3.51534` units at the recovered speed cap.

Airborne planar control is real. Starting from a stationary jump, planar displacement accelerates at about `1/180` unit/tick toward the same `0.09500919` cap. Releasing input in air decays much more gently, about `1/1200` unit/tick.

Walking off a ledge enters the same recovered falling recurrence immediately. Landing is host collision response: the final retail downward step can be shortened by the surface and vertical controller motion is then cleared.
## Crouch and turning

With the isolated controller binding repaired, R1 produces a stationary crouch in native sequence 13 with zero translation. Supplying planar direction while crouched selects native sequences 14/15 and still produces zero translation: these are turn-in-place states, not crouch walking.

Entering crouch while already moving does not hard-stop Ratchet. Existing planar displacement decays by approximately `0.001802944` native unit/tick while sequence 13 is active. The R&C1 controller therefore suppresses new planar acceleration and jump while grounded/crouched but preserves this measured residual slide-down.

Normal facing is measured independently of animation. Live Ratchet Moby `+0x48` is a yaw angle, corroborated by the corresponding 2x2 rotation terms at `+0xc0/+0xc4/+0xd0/+0xd4`. Controlled A/D trials begin at yaw `1.11104 rad`, rotate toward the current planar travel vector, and settle with facing aligned to sustained travel.

The largest observed single yaw update in these witnesses is about `0.142 rad/tick` (roughly `8.1 degrees/tick`). Retail visibly eases the turn; **the exact yaw-easing recurrence is not yet recovered**. OBP does not promote that maximum sample as a constant turn-rate law.

## Respawn/reset

Three independent Veldin fall-off trials enter native death sequences 10/11 and then restore Ratchet to approximately `(132.09, 115.48, 31.4266)` in standing state. That location agrees with the separately recovered authored class-0 player-start region.

The post-respawn controller does not inherit the pre-death motion. `Rac1RatchetMovementController.Reset()` therefore clears planar displacement, vertical displacement, jump anticipation/hold state and returns the controller to grounded state. Host placement remains responsible for choosing the authored player start; this work does not invent a broader checkpoint system.
## Runtime promotion

`Rac1RatchetMovementController` is engine-independent and owns only the recovered native-tick recurrence. `PlayerControlIntent` carries desired planar direction, jump held/pressed and optional crouch held; `PlayerContactFacts` carries grounded/ceiling contact supplied by the host.

`DebugPlayer` selects this controller only when `RuntimeWorld.Game == "rac1"`. It converts per-tick displacement to Godot units/second at 60 Hz and then calls `MoveAndSlide()`. GC/UYA keep the previous provisional debug movement. Interactive R&C1 uses **C** for crouch; R remains the development respawn key.

A real Godot `rac1:LEVEL0` authority run completed `Idle -> Walk -> Run -> JumpRise -> Fall -> Land -> Run` against reconstructed collision. The engine trace is an integration witness; exact timing/velocity assertions live in portable recurrence tests so terrain does not contaminate the numeric comparison.

Payload-free evidence is frozen in `research/generated/rac1-ratchet-movement-controller.json`. Local raw PINE traces and screenshots remain outside Git.

## Deliberately unresolved

- the exact normal-turn yaw easing recurrence, beyond its target alignment and observed envelope;
- native camera-relative stick processing/dead-zone details for arbitrary analogue magnitudes;
- broader death/checkpoint selection beyond the witnessed Veldin reset-to-player-start behavior;
- collision details such as ledge grabs, wall interactions and special traversal abilities;
- combat movement, wrench lunges and hit volumes, which belong to the separate combat milestone.

These are not filled with DebugPlayer constants or animation guesses.