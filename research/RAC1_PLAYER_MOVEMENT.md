# R&C1 Ratchet movement controller

Authority: NTSC-U original retail (`SCUS-97199`, build `rac1-ntscu-original`). The recovered values below come from controlled retail execution, live PINE sampling and the loaded authority executable. DebugPlayer calibration values are not evidence for native R&C1 motion.

## Controller boundary

Retail exposes Ratchet's actual per-update XYZ displacement in the player-state structure at `0x0013f3d0 + 0x80/+0x84/+0x88` (`0x0013f450` for X). Across controlled traces those floats match live Moby position deltas component-for-component on game updates.

OBP therefore models the R&C1 controller in **native world displacement per 60 Hz update**. `OBP.RAC1` owns acceleration, jump, crouch and the recovered yaw recurrences. `OBP.Runtime.Player` carries only neutral input/contact facts. Godot converts the native step to engine velocity and retains ownership of world collision, floor/ceiling contact and slope response.

This separation is important: retail slope traces acquire vertical displacement while ordinary locomotion remains active. That vertical component is collision/terrain response, not evidence for a second controller gravity equation.

## Planar input boundary

The calibrated acceleration/speed witnesses use full-scale cardinal planar input and release. They prove the downstream displacement recurrence for those conditions, but they do **not** recover the retail stick dead-zone or post-dead-zone magnitude curve. `PlayerControlIntent.NormalizedPlanar()` is therefore only a deterministic full-scale/directional contract for the recovered controller today; it must not be cited as evidence that retail normalized every nonzero stick vector.

Two fixed-savestate, eight-direction live matrices now recover the full-scale stick-to-control-heading transform. The authority states are SHA-256 `07677959a3b7215a89b42745dffe55bb4d4709bce01d1aab31e6514c032a436d` and `067e5ca260233fcccc958f54e412e3fad26f38406a2a7d2f13d8f6477e92a0aa`; their independently sampled forward/control headings are `-2.073779821` and `+2.842167616 rad`. In both states, define planar stick angle with forward `0`, right `+pi/2`, back `pi` and left `-pi/2`. Retail writes `G+0x100 = WrapPi(controlHeading - stickAngle)`. Cardinals and diagonals fit that rule with worst error below `5.7e-7 rad` across the sixteen reset trials. Literal DualShock 2 bytes use X high for right, X low for left, Y low for forward and Y high for back.

The same first target-writing frame exposes a 3D control vector at `G+0x0f0/+0x0f4/+0x0f8`. In each authority state, full-scale forward and right are unit, mutually orthogonal basis vectors; back/left are their exact negatives and each diagonal is the normalized signed sum, with worst combination error below `1.5e-7`. The first state gives forward `(-0.48034167,-0.87305897,-0.08390385)` and right `(-0.87614834,+0.48204139,0)`; the second gives forward `(-0.95008272,+0.29329622,-0.10639659)` and right `(+0.29497051,+0.95550632,0)`.

That pitched 3D vector is not itself the facing-angle source. On diagonals, `atan2(controlVector.y, controlVector.x)` differs from `G+0x100` by up to `0.0017663 rad` in the first state and `0.0028466 rad` in the second, while `G+0x100` remains on the exact planar 45-degree lattice. Retail therefore composes facing in planar angle space and separately carries the pitched 3D control vector. `G+0x100` remains the target consumed by the already-recovered locomotion-state yaw recurrences.

A fixed-forward trace covers the changing-basis case directly. With left stick held at literal `[127,0]`, `G+0x100` and the projected `G+0x0f0/+0x0f4/+0x0f8` control vector stay aligned within `6.1e-7 rad` while the basis rotates by a net `0.2181052 rad`. During the right-stick-left segment the target advances `+0.2115783 rad`; it continues another `+0.0881104 rad` during the following neutral-right-stick hold, then reverses by `-0.1366279 rad` with right stick held right. This proves that a fixed movement stick continues to track a changing native control basis and that horizontal right-stick input can bias that basis, but the neutral drift shows chase follow/recenter is simultaneously active.

This does **not** recover the retail chase-camera follow, recenter, obstruction law, exact right-stick turn rate or the upstream source field that owns the control heading. A stationary horizontal right-stick prelude also did not rotate the sampled basis in these states, so the camera/control-heading state machine remains outside this recovery. The retail stick dead-zone and post-dead-zone magnitude curve likewise remain unresolved; emulator binding dead-zone/axis-scale settings are harness policy, not retail controller evidence.

### Deterministic analogue archaeology harness

`tools/rac1-movement-probe.py` remains the loaded-executable/static probe and `tools/rac1-savestate-movement-probe.py` remains the offline savestate probe. `tools/rac1-analogue-movement-harness.py` adds the missing live experiment layer: it generates PCSX2 input-recording frames with literal DualShock 2 stick bytes, associates them with a fixed savestate, advances PCSX2 exactly one emulated frame at a time and takes one batched PINE sample after each advance.

The input plan is data, not keyboard emulation. `tools/rac1-analogue-plan.example.json` is the live-validated 68-frame witness. `build` writes both the `.p2m2` movie and PCSX2's required `<movie>_SaveState.p2s` companion under ignored `captures/`; `prepare-profile` makes an isolated portable PCSX2 profile there with PINE and the F7 frame-advance hotkey enabled. On Windows, `tools/rac1-pcsx2-input-recording-picker.ps1` clears stale filename state, tolerates QFileDialog's remembered directory and explicitly accepts a selected replay when Qt does not close the picker itself. `capture` records displacement, live position, yaw, `G+0x0f0/+0x0f4/+0x0f8`, target yaw and animation sequence plus raw 32-bit words across player-state `G+0x000..G+0x17c`. `derive` reduces that private capture to payload-free evidence suitable for Git.

`tools/rac1-stick-heading-matrix.py` builds on that harness for the heading experiment. Its `capture` command restores the same savestate for each of eight literal full-scale cardinals/diagonals, and `derive` verifies the signed target-angle lattice plus the orthogonal 3D basis construction across one or more independently headed matrices. The retained reduction is `research/generated/rac1-stick-heading-probe.json`; raw movies, savestates and frame samples remain ignored under `captures/`.

A clean NTSC-U run used fixed Veldin state SHA-256 `07677959a3b7215a89b42745dffe55bb4d4709bce01d1aab31e6514c032a436d` and generated movie SHA-256 `cf666f5f6743b974477bd5291c18fe0a1f9da939fb0bce75938ddaac21a3a8b4`. The anchor begins with zero XYZ displacement at live position approximately `(154.77104, 120.58263, 29.484375)` and yaw/target `1.1110418`. In this one controlled plan, twenty frames of literal left-stick `[127,64]` produced no displacement, while literal `[127,0]` produced movement and sequence transition `0 -> 3`; release returned `3 -> 0`. That is a reproducible byte-level observation, **not** yet proof of the native dead-zone boundary or post-dead-zone response curve.

Raw movies, companion savestates and per-frame captures remain ignored under `captures/`. The retained reduction is `research/generated/rac1-analogue-movement-probe.json`. Re-run commands are:

```text
py -3.12 tools/rac1-analogue-movement-harness.py prepare-profile --source <pcsx2-profile> --out captures/rac1-analogue/pcsx2-profile --pine-port 28099
py -3.12 tools/rac1-analogue-movement-harness.py build --plan tools/rac1-analogue-plan.example.json --savestate <flat-veldin.p2s> --movie captures/rac1-analogue/trial.p2m2
py -3.12 tools/rac1-analogue-movement-harness.py capture --pid <pcsx2-pid> --pine-port 28099 --plan tools/rac1-analogue-plan.example.json --movie captures/rac1-analogue/trial.p2m2 --out captures/rac1-analogue/raw.json
py -3.12 tools/rac1-analogue-movement-harness.py derive --capture captures/rac1-analogue/raw.json --out research/generated/rac1-analogue-movement-probe.json
py -3.12 tools/rac1-stick-heading-matrix.py capture --pid <pcsx2-pid> --pine-port 28099 --savestate <state.p2s> --out-dir captures/rac1-heading/state-a
py -3.12 tools/rac1-stick-heading-matrix.py derive --matrix-dir captures/rac1-heading/state-a --matrix-dir captures/rac1-heading/state-b --fixed-stick-capture captures/rac1-heading/fixed-forward.raw.json --out research/generated/rac1-stick-heading-probe.json
```

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

Normal facing is measured independently of animation. Live Ratchet Moby `+0x48` is yaw, corroborated by the corresponding 2x2 rotation terms at `+0xc0/+0xc4/+0xd0/+0xd4`. In the fixed controller traces, player-state `G+0x18` (`g[6]`) is bit-identical to that live yaw and `G+0x100` (`g[64]`) supplies the turn target used by the recovered recurrence.

Fixed NTSC-U traces resolve four ordinary yaw modes. With `v_n = WrapPi(yaw_n - yaw_{n-1})` and `error_n = WrapPi(target_n - yaw_{n-1})`:

| Native state | Recovered recurrence | Fixed-trace fit |
| --- | --- | --- |
| ground startup / sequence 3 | `v_n = 0.9*v_{n-1} + 0.019*error_n` | gain `0.01899999`, retention `0.89999985`, RMS `7.96e-8 rad` |
| ground run / sequence 4 | `v_n = 0.85*v_{n-1} + 0.008*error_n` | gain `0.008000051`, retention `0.84999915`, RMS `8.88e-8 rad` |
| crouch turn / sequence 14 witness | `v_n = 0.93*v_{n-1} + 0.002*error_n` | RMS `5.10e-8 rad` |
| jump/air / sequence 7 | `v_n = 0.8*v_{n-1} + 0.04*error_n` | unsaturated rows RMS about `9.3e-8 rad` |

The startup-to-run transition is observed between planar magnitudes `0.037499697` and `0.039582664` native unit/tick. Sequence 7 begins during the recovered jump-anticipation frames, before vertical displacement starts, so yaw switches to the jump/air recurrence at anticipation entry rather than waiting for host airborne contact. The production controller therefore keeps yaw mode as source-game state rather than reducing it to a `grounded` boolean; its deterministic translation recurrence crosses the observed run boundary on the nineteenth full-input acceleration tick.

The existing hard caps are retail-backed: ground witnesses repeatedly reach approximately `0.165806293 rad/tick` versus the implementation constant `0.165806278`, while air witnesses reach approximately `0.250163794 rad/tick` versus `0.250163853`. Ground and air traces also prove overshoot protection: when the recurrence requests a step larger than the remaining target error, retail lands exactly on the target and clears turn velocity.

The former standalone `SnapStepFraction = 0.01` early-snap term has **no independent retail witness**. Exact-target events in the controlled traces are already explained by overshoot clamping and zero-on-target behavior, so the production recurrence removes that extra threshold rather than presenting it as native.

The strongest local fixed traces are `right_release_zero.json`, `forward_release_zero.json`, `jump_air_forward.json` and `turn_to_crate.json`. They remain outside Git; portable tests retain only selected derived float witnesses. Static exact-literal probes in `research/generated/rac1-movement-static-probe.json` and `research/generated/rac1-movement-savestate-probe.json` remain useful negative evidence: direct literal occurrence is not what establishes the recurrence. The controlled live yaw dataflow is the authority.

## Respawn/reset

Three independent Veldin fall-off trials enter native death sequences 10/11 and then restore Ratchet to approximately `(132.09, 115.48, 31.4266)` in standing state. That location agrees with the separately recovered authored class-0 player-start region.

The post-respawn controller does not inherit the pre-death motion. `Rac1RatchetMovementController.Reset()` therefore clears planar displacement, vertical displacement, jump anticipation/hold state and returns the controller to grounded startup state. Host placement remains responsible for choosing the authored player start; this work does not invent a broader checkpoint system.

## Runtime promotion

`Rac1RatchetMovementController` is engine-independent and owns only recovered native-tick movement state. `PlayerControlIntent` carries desired planar direction, jump held/pressed and optional crouch held; `PlayerContactFacts` carries grounded/ceiling contact supplied by the host. The movement result also exposes `GroundStartup`, `GroundRun`, `CrouchTurn` or `Air` yaw mode so `Rac1RatchetYawController` can apply the retail state-specific recurrence without Godot inventing gameplay state.

`DebugPlayer` now uses this controller for ordinary grounded/airborne play in R&C1, GC and UYA reconstructed worlds. That trilogy-wide reuse is an **OBP-created design choice** and is not evidence that GC or UYA used the R&C1 native controller. The host supplies its presentation camera's planar forward heading; `Rac1RatchetYawController` combines that neutral fact with raw stick direction using the recovered `WrapPi(controlHeading - stickAngle)` rule, then applies the recovered yaw recurrence. The host separately converts native per-tick displacement to Godot units/second at 60 Hz and calls `MoveAndSlide()`, so Godot remains collision/contact authority. **C** uses the recovered crouch state across the common base, while **F** fly/noclip and **R** manual respawn remain separate development features. Capture metadata records the common-controller label, recovered locomotion/yaw state and avatar animation state for deterministic inspection.

A real Godot `rac1:LEVEL0` authority run completed `Idle -> Walk -> Run -> JumpRise -> Fall -> Land -> Run` against reconstructed collision. The engine trace is an integration witness; exact timing/velocity assertions live in portable recurrence tests so terrain does not contaminate the numeric comparison.

Payload-free evidence is frozen in `research/generated/rac1-ratchet-movement-controller.json`. Local raw PINE traces and screenshots remain outside Git.

## Deliberately unresolved

- native dead-zone and post-dead-zone magnitude shaping for partial-stick input, plus the retail camera/control-heading source and follow policy;
- broader death/checkpoint selection beyond the witnessed Veldin reset-to-player-start behavior;
- collision details such as ledge grabs, wall interactions and special traversal abilities;
- combat movement, wrench lunges and hit volumes, which belong to the separate combat milestone.

These are not filled with DebugPlayer constants, sequel constants or animation guesses.
