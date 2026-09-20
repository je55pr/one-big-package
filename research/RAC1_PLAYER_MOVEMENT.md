# R&C1 Ratchet movement controller

Authority: NTSC-U original retail (`SCUS-97199`, build `rac1-ntscu-original`). The recovered values below come from controlled retail execution, live PINE sampling and the loaded authority executable. DebugPlayer calibration values are not evidence for native R&C1 motion.

## Controller boundary

Retail exposes Ratchet's actual per-update XYZ displacement in the player-state structure at `0x0013f3d0 + 0x80/+0x84/+0x88` (`0x0013f450` for X). Across controlled traces those floats match live Moby position deltas component-for-component on game updates.

OBP therefore models the R&C1 controller in **native world displacement per 60 Hz update**. `OBP.RAC1` owns acceleration, jump, crouch and the recovered yaw recurrences. `OBP.Runtime.Player` carries only neutral input/contact facts. Godot converts the native step to engine velocity and retains ownership of world collision, floor/ceiling contact and slope response.

This separation is important: retail slope traces acquire vertical displacement while ordinary locomotion remains active. That vertical component is collision/terrain response, not evidence for a second controller gravity equation.

## Planar input boundary

Dense fixed-savestate DualShock 2 byte sweeps are now reconciled with the active loaded overlay rather than standing on curve fit alone. The pad state is based at `I = 0x0013c940`; `0x00267270..0x002672e0` reads the four literal stick bytes, centers each at `127`, takes absolute displacement, rejects values below `48`, converts `(abs(raw-127)-48)` and `76` to floats, divides, clamps to `1.0`, then restores the raw sign. Thus the retail conditioner is exactly `sign(raw-127) * clamp((abs(raw-127)-48)/76, 0, 1)` for each axis.

The player-global base used by this path is `P = 0x0013f350`, with the existing player-state base `G = P+0x80 = 0x0013f3d0`. At `0x002116ac..0x00211724` retail copies conditioned left X/Y from `I+0x108/+0x10c`, computes their Euclidean magnitude and compares it strictly against `0.25f`; below that value the analogue pair is replaced by digital-pad input or zero. Because `0.25 * 76 = 19`, the live cardinal `18` inactive / `19` active boundary is now executable-backed. A capped magnitude sample is also retained at `P+0x229c`; `0x00211830..0x00211868` computes `min(length, 1.0)`. That sample is produced before the current conditioned pair is copied in this update path, so it is a cached prior-vector magnitude rather than a safe label for an instantaneous raw byte.

The loaded code also compares `P+0x229c` against exact `0.82f` at `0x0021c254`, equivalent to `62.319999...` remapped-count radius, and the first integer cardinal above it is `63`. Its proven consumer is the facing-controller coefficient path through `0x00211e30/0x00211f38`, however, not yet the translational-speed selector. The live displacement sweeps still bracket the walk/run change between cardinal `62` and `63` and between diagonal radii `62.2254` and `63.6396`; the numerical agreement is strong but is retained as a correspondence, not promoted into an unproven speed-code identity. Facing-aligned walk and run trials continue to show approximately `1/480` ground acceleration rather than a measured continuously magnitude-scaled acceleration.

Two fixed-savestate, eight-direction live matrices recover the full-scale stick-to-control-heading transform. The authority states are SHA-256 `07677959a3b7215a89b42745dffe55bb4d4709bce01d1aab31e6514c032a436d` and `067e5ca260233fcccc958f54e412e3fad26f38406a2a7d2f13d8f6477e92a0aa`; their independently sampled forward/control headings are `-2.073779821` and `+2.842167616 rad`. Loaded memory now identifies the source float used by the target routine at `0x00166dd8`: the same two states contain `-2.07377958298` and `+2.84216785431`, differing from the live inferred headings by only `2.38e-7 rad` in each case.

The target dataflow is executable-backed at `0x002118c8..0x00211be4`. In ordinary mode it loads the conditioned left pair, normalizes a magnitude above `1.0`, calls the quadrant-aware angle helper at `0x001ff8b0` with negated Y/X, then loads `0x00166dd8` at `0x00211b5c`. The helper at `0x002000e8` adds those two angles and wraps across `+/-pi` by `2*pi` before the result is stored at `G+0x100`. With planar stick angle defined as forward `0`, right `+pi/2`, back `pi` and left `-pi/2`, this is the same `G+0x100 = WrapPi(controlHeading - stickAngle)` law recovered independently from the matrices. Cardinals and diagonals fit it with worst live error below `5.7e-7 rad`. Literal DualShock 2 bytes use X high for right, X low for left, Y low for forward and Y high for back.

The same first target-writing frame exposes a 3D control vector at `G+0x0f0/+0x0f4/+0x0f8`. In each authority state, full-scale forward and right are unit, mutually orthogonal basis vectors; back/left are their exact negatives and each diagonal is the normalized signed sum, with worst combination error below `1.5e-7`. The first state gives forward `(-0.48034167,-0.87305897,-0.08390385)` and right `(-0.87614834,+0.48204139,0)`; the second gives forward `(-0.95008272,+0.29329622,-0.10639659)` and right `(+0.29497051,+0.95550632,0)`.

That pitched 3D vector is not itself the facing-angle source. On diagonals, `atan2(controlVector.y, controlVector.x)` differs from `G+0x100` by up to `0.0017663 rad` in the first state and `0.0028466 rad` in the second, while `G+0x100` remains on the exact planar 45-degree lattice. Retail therefore composes facing in planar angle space and separately carries the pitched 3D control vector. `G+0x100` remains the target consumed by the already-recovered locomotion-state yaw recurrences.

A fixed-forward trace covers the changing-basis case directly. With left stick held at literal `[127,0]`, `G+0x100` and the projected `G+0x0f0/+0x0f4/+0x0f8` control vector stay aligned within `6.1e-7 rad` while the basis rotates by a net `0.2181052 rad`. During the right-stick-left segment the target advances `+0.2115783 rad`; it continues another `+0.0881104 rad` during the following neutral-right-stick hold, then reverses by `-0.1366279 rad` with right stick held right. This proves that a fixed movement stick continues to track a changing native control basis and that horizontal right-stick input can bias that basis, but the neutral drift shows chase follow/recenter is simultaneously active.

This identifies the control-heading source field consumed by ordinary target construction as `0x00166dd8`, but does **not** recover the retail chase-camera follow, recenter, obstruction law, exact right-stick turn rate or the state machine that produces that field. A stationary horizontal right-stick prelude also did not rotate the sampled basis in these states, so those upstream camera/control-heading semantics remain outside this recovery. The dense partial-stick sweeps and loaded code recover the left-stick byte conditioner and exact `0.25` activation threshold; the live walk/run displacement bracket remains retained separately from the `0.82` facing-controller branch. Emulator binding dead-zone/axis-scale settings remain harness policy, not retail controller evidence.

The partial-stick sweeps extend the same planar target construction below full scale. Let `dx = rawX - 127`, `dy = rawY - 127`, remap each signed component by subtracting 48 counts and clamping its magnitude to 76, and define forward-positive `fy = -remappedY`. For active input, `stickAngle = atan2(remappedX, fy)` and the full target remains `WrapPi(controlHeading - stickAngle)`. The fixed back-axis sweep is the same relation expressed around the back target: its measured offsets fit `atan2(remappedX, remappedBack)` with maximum residual about `6.6e-7 rad`, including mirrored negative-X witnesses.

### Deterministic analogue archaeology harness

`tools/rac1-movement-probe.py` is the static boot-ELF probe and now records the overlay boundary explicitly: the active movement signatures at `0x002116c0`, `0x00211b5c` and `0x0021c254` differ from same-address boot bytes, while the conditioner at `0x00267274` lies beyond the boot ELF's PT_LOAD range. `tools/rac1-savestate-movement-probe.py` verifies the active loaded-overlay instruction signatures, reduces the conditioned-axis/magnitude/heading dataflow and can compare control-heading values across fixed savestates. `tools/rac1-analogue-movement-harness.py` supplies the live experiment layer: it generates PCSX2 input-recording frames with literal DualShock 2 stick bytes, associates them with a fixed savestate, advances PCSX2 exactly one emulated frame at a time and takes one batched PINE sample after each advance.

The input plan is data, not keyboard emulation. `tools/rac1-analogue-plan.example.json` is the live-validated 68-frame witness; segments may also carry `"buttons": ["cross"]` for deterministic jump holds/releases while stick bytes remain literal. `build` writes both the `.p2m2` movie and PCSX2's required `<movie>_SaveState.p2s` companion under ignored `captures/`; `prepare-profile` makes an isolated portable PCSX2 profile there with PINE and the F7 frame-advance hotkey enabled. On Windows, `tools/rac1-pcsx2-input-recording-picker.ps1` targets the native replay picker's filename edit, clears stale text, types the already-resolved absolute movie path as keyboard input, accepts it with Enter, and retains a native Open-button fallback if Qt leaves the picker open. This avoids QFileDialog remembered-directory/off-screen-row virtualization while keeping gameplay input entirely in the movie. `capture` records displacement, live position, yaw, `G+0x0f0/+0x0f4/+0x0f8`, target yaw and animation sequence plus raw 32-bit words across player-state `G+0x000..G+0x17c`. `derive` reduces that private capture to payload-free evidence suitable for Git.

`tools/rac1-stick-heading-matrix.py` builds on that harness for the heading experiment. Its `capture` command restores the same savestate for each of eight literal full-scale cardinals/diagonals, and `derive` verifies the signed target-angle lattice plus the orthogonal 3D basis construction across one or more independently headed matrices. The retained reduction is `research/generated/rac1-stick-heading-probe.json`; raw movies, savestates and frame samples remain ignored under `captures/`.

`tools/rac1-ground-turn-response.py` builds fixed-savestate steering movies on the same harness. It captures steady run and walk 45/90/135/180-degree turns, mirrored 45/90/135 turns, both directions of left/right and forward/back reversals, and release-then-90/180 cases. Its reducer retains per-update planar speed, travel heading, native yaw, target yaw and animation sequence plus vertical-contact summaries while leaving movies, savestates and raw PINE payloads under ignored `captures/`. The retained reduction is `research/generated/rac1-ground-turn-response.json`.

A clean NTSC-U run used fixed Veldin state SHA-256 `07677959a3b7215a89b42745dffe55bb4d4709bce01d1aab31e6514c032a436d` and generated movie SHA-256 `cf666f5f6743b974477bd5291c18fe0a1f9da939fb0bce75938ddaac21a3a8b4`. The anchor begins with zero XYZ displacement at live position approximately `(154.77104, 120.58263, 29.484375)` and yaw/target `1.1110418`. In this initial controlled plan, twenty frames of literal left-stick `[127,64]` produced no displacement, while literal `[127,0]` produced movement and sequence transition `0 -> 3`; release returned `3 -> 0`. The later dense sweeps supersede that coarse bracket and recover the byte remap plus locomotion thresholds.

Raw movies, companion savestates and per-frame captures remain ignored under `captures/`. The retained reduction is `research/generated/rac1-analogue-movement-probe.json`. Re-run commands are:

```text
py -3.12 tools/rac1-movement-probe.py --iso path-to-authority.iso --out research/generated/rac1-movement-static-probe.json
py -3.12 tools/rac1-savestate-movement-probe.py --savestate state-a.p2s --compare-savestate state-b.p2s --zstd-dll path-to-zstd.dll --out research/generated/rac1-movement-savestate-probe.json
py -3.12 tools/rac1-analogue-movement-harness.py prepare-profile --source <pcsx2-profile> --out captures/rac1-analogue/pcsx2-profile --pine-port 28099
py -3.12 tools/rac1-analogue-movement-harness.py build --plan tools/rac1-analogue-plan.example.json --savestate <flat-veldin.p2s> --movie captures/rac1-analogue/trial.p2m2
py -3.12 tools/rac1-analogue-movement-harness.py capture --pid <pcsx2-pid> --pine-port 28099 --plan tools/rac1-analogue-plan.example.json --movie captures/rac1-analogue/trial.p2m2 --out captures/rac1-analogue/raw.json
py -3.12 tools/rac1-analogue-movement-harness.py derive --capture captures/rac1-analogue/raw.json --out research/generated/rac1-analogue-movement-probe.json
py -3.12 tools/rac1-stick-heading-matrix.py capture --pid <pcsx2-pid> --pine-port 28099 --savestate <state.p2s> --out-dir captures/rac1-heading/state-a
py -3.12 tools/rac1-stick-heading-matrix.py derive --matrix-dir captures/rac1-heading/state-a --matrix-dir captures/rac1-heading/state-b --fixed-stick-capture captures/rac1-heading/fixed-forward.raw.json --out research/generated/rac1-stick-heading-probe.json
py -3.12 tools/rac1-ground-turn-response.py capture --pid <pcsx2-pid> --pine-port 28099 --savestate <flat-veldin.p2s> --out-dir captures/rac1-ground-turns
py -3.12 tools/rac1-ground-turn-response.py derive --capture-dir captures/rac1-ground-turns --out research/generated/rac1-ground-turn-response.json
```

### Recovered analogue law

The dense sweep retains its payload-free reduction in `research/generated/rac1-analogue-input-law.json`. The raw byte law is now code-backed at its first two stages. Each signed axis component independently subtracts a 48-count deadband and saturates after 76 remapped counts. Retail then computes Euclidean magnitude. The exact activation comparison is normalized magnitude `< 0.25`, so remapped radius `19` is active while anything below `19` falls back to digital/zero input.

The translational walk/run split remains a live displacement bracket rather than an exact code threshold:

| Remapped magnitude witness | Retail displacement result |
| --- | --- |
| `< 19` | no analogue locomotion; exact code-backed activation boundary |
| cardinal `19..62`, diagonal radius `62.2254` | walk band, steady aligned planar step approximately `0.015` |
| cardinal `>=63`, diagonal radius `63.6396` | run witness, canonical planar cap `0.09500919` |

Cardinal remapped magnitude 18 is inactive while 19 moves; equal-axis `(13,13)` (radius `18.3848`) is inactive while `(14,14)` (radius `19.7990`) moves. Likewise cardinal 62 remains on the walk plateau while 63 reaches the run behavior; equal-axis `(44,44)` (radius `62.2254`) stays walk while `(45,45)` (radius `63.6396`) enters run. The loaded `0.82f` comparison corresponds to radius `62.32` and lies between those live walk/run witnesses, but its established dataflow selects facing-controller coefficients. Therefore `63` is retained as the first sampled integer-cardinal run witness, not asserted as an exact translational code threshold. Raw input is still not described correctly by either a simple radial raw-byte dead zone or an independent per-axis movement gate: component trim happens first, then radial magnitude participates in downstream control decisions.

Once active, the remapped components also extend the source commit's full-scale target lattice to partial input. Using forward-positive Y, `stickAngle = atan2(remappedX, remappedForward)`, and `G+0x100 = WrapPi(controlHeading - stickAngle)`. The retained partial-angle witnesses fit the equivalent back-relative form within about `6.6e-7 rad`, including mirrored negative-X samples. Remapped component 76 is already saturated by raw delta 124, so raw 124 and 128 produce the same 45-degree diagonal target. Full diagonal planar speed remains at the same run cap rather than receiving a square-stick speed boost.

Facing-aligned walk inputs from remapped magnitude 19 through 62 repeatedly settle near `0.015`. Run inputs from magnitude 63 upward accelerate toward the existing `0.09500919` cap. The clean full-scale cardinal sweep reached `0.094995147` and the retained run-band samples reach up to `0.095000645`, within about `1.4e-5` of the earlier canonical maximum witness rather than establishing a different cap. Measured acceleration in both bands remains approximately `1/480` unit/tick; there is no supported magnitude-dependent acceleration term. Run-band input still starts in sequence 3 and reaches sequence 4 only when actual planar displacement crosses the separately recovered startup/run speed boundary, so the input threshold and animation transition are distinct.

Release is stateful. A steady run does not apply `1/300` immediately: the retained fixed aligned `forward_release_zero` trace gives the sequence-4/5 handoff planar steps `0.095009189508`, `0.092646040782`, `0.090283567728`, `0.085735093521`. From the following sequence-5 update onward the decay is the existing approximately `1/300` unit/tick until exact zero; the sustained median decrement is `0.003332469` with sampled range `0.003325785..0.003339829`. Releasing the approximately `0.015` walk plateau instead transitions `3 -> 0` through four neutral outputs `0.012627291`, `0.010271503`, `0.00230833`, `0.0`. These are bounded empirical transitions, not fitted recurrences, and are not extrapolated to partially accelerated startup releases.

### Grounded steering while moving

A fixed-savestate steering matrix now separates translation direction from facing-target error. On flat ground, steady forward-origin run turns at 45/90/135/180 degrees and mirrored 45/90-degree turns retain their pre-turn planar speed to within a ratio of `0.999807..1.000110`, even while `|targetYaw-yaw|` reaches about `3.1164 rad`. Their displacement heading follows the current native yaw with worst retained error `0.0001531 rad`. Flat walk-band turns, including both sides and the 180-degree reversal, retain `0.999055..1.000512` of the plateau and follow current yaw within `0.0009843 rad`. Therefore ordinary held-input ground translation rotates the displacement vector with **current facing/yaw** each update. It does not preserve the prior world-space velocity direction, snap translation to the new target yaw, or apply a general cosine/projection speed penalty based only on facing error.

The reversal controls rule out a single facing-error scalar law. Full-run forward-to-back and left-to-right reversals stay at the run cap while heading continues to follow yaw, but the flat right-to-left reversal drops from about `0.0950` to `0.069631942` before recovering with a median positive step of `0.002083784`, again matching the existing `1/480` acceleration. The opposite back-to-forward run trial is terrain-contaminated (`disp_z` reaches about `0.06144`) and is not used to infer a steering scalar. The right-to-left run slowdown occurs at constant Z with sequence 4 throughout, so it is retained as a real direction/history-dependent scalar transient whose exact trigger is not yet recovered. Walk-band reversals in both directions remain on the plateau while tracking yaw.

Release-then-turn trials expose state-specific restart behavior rather than changing the steady-turn rule. After four neutral updates from a steady run, a 90-degree command follows sequence `4 -> 5 -> 3 -> 4`; speed falls through `0.082402625`, `0.079062797`, `0.075730328` and then resumes an approximately `1/480` rise (median `0.002082801`) while displacement continues to follow yaw. The 180-degree run restart shares the `4 -> 5 -> 3 -> 4` state path but resets the remaining `0.075730328` step to `0.002084892`; subsequent steps rise by median `0.002083046`. After that reset, the next moving outputs align to target yaw to within roughly `0.002 rad` while body yaw catches up, making this opposite-direction restart an explicit exception to the steady moving facing-aligned path. Walk release-then-90/180 instead follows `3 -> 0 -> 3`, reaches exact zero, restarts by approximately `1/480`, and its displacement remains aligned with current yaw.

These trials preserve the existing straight-line `1/480` acceleration, walk plateau, run cap and release witnesses. They add a steering-direction contract plus bounded exceptions; they do not justify replacing those recurrences with a new facing-error speed formula. Per-update payload-free witnesses are retained in `research/generated/rac1-ground-turn-response.json`.

## Ground locomotion

From the fixed Veldin savestate, full planar input ramps from rest by approximately `1/480` native unit per tick (`0.002083333...`) until a sustained displacement magnitude of `0.09500919` unit/tick.

Releasing planar input enters the native locomotion-stop path rather than zeroing motion. The steady walk and steady run boundaries first use the retained transition samples above; only sustained sequence-5 run release is modeled by the canonical `1/300` unit/tick (`0.003333333...`) decay to exact zero. Releases during partially accelerated startup remain on the previous conservative fallback until a retail transition law is recovered.

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

Airborne planar control is real and, unlike grounded locomotion, it is continuous in conditioned stick magnitude rather than selecting the ground walk/run plateaus. Clean stationary-jump trials applying stick only after launch settle at `0.02375915` for raw `[127,60]`, `0.02951669` for `[189,60]`, `0.07777548` for `[219,219]`, `0.07874057` for `[127,238]`, and `0.07954972` for `[220,220]`. Those witnesses agree within `0.00004` native unit/tick with `0.09500919 * min(conditionedMagnitude, 1)`. Across the same cardinal/diagonal set, planar acceleration remains about `1/180` unit/tick and neutral release decays about `1/1200` unit/tick.

Sequence-7 jump anticipation already uses that airborne planar recurrence before vertical launch. Independent `[127,60]` and `[220,220]` anticipation movies show approximately `1/180` planar increments after the native state transition while vertical displacement remains zero. Walking off a ledge likewise enters the recovered falling recurrence immediately. Landing and ledge-edge surface response remain host collision facts: the final retail downward step can be shortened, and uneven-ground planar/vertical spikes from local ledge routes are deliberately excluded from controller-equation fitting.

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

`Rac1RatchetMovementController` is engine-independent and owns only recovered native-tick movement state. `RawGamepadInput` reads named Godot action strengths without applying an action dead zone, normalization or curve, and `PlayerControlIntent` carries those unconditioned normalized signed right/forward host axes plus an optional engine-neutral control basis, an optional native-world-to-host planar basis, jump held/pressed and optional crouch held; `PlayerContactFacts` carries grounded/ceiling contact supplied by the host. `Rac1RatchetMovementController.Step` applies the recovered 48/76 component conditioner and radial activation through `Rac1AnalogueInput.ConditionUnitAxes`. Grounded active locomotion then advances only the recovered scalar speed law and resolves displacement direction from the same-update native yaw supplied by `Rac1RatchetYawController`, transformed through the native planar basis; it no longer interpolates a world-space velocity vector toward the new stick target. Literal DualShock 2 bytes remain the retail archaeology/harness representation used to recover and densely cross-check that law, not the live Godot/runtime boundary format. Ground inputs in the retained walk witnesses target the approximately `0.015` plateau, while run witnesses retain the canonical `0.09500919` cap. Native air state, including jump anticipation, continues to target `0.09500919 * conditionedMagnitude` through the control basis with the recovered vector acceleration/release recurrence; unsupported contact enters that same air law without importing terrain projection into the controller. The movement result also exposes `GroundStartup`, `GroundRun`, `CrouchTurn` or `Air` yaw mode so `Rac1RatchetYawController` can apply the retail state-specific recurrence without Godot inventing gameplay state.

`DebugPlayer` now uses this controller for ordinary grounded/airborne play in R&C1, GC and UYA reconstructed worlds. That trilogy-wide reuse is an **OBP-created design choice** and is not evidence that GC or UYA used the R&C1 native controller. The host supplies its presentation camera's planar forward heading; `Rac1RatchetYawController` combines that neutral fact with raw stick direction using the recovered `WrapPi(controlHeading - stickAngle)` rule, then applies the recovered yaw recurrence. The host separately converts native per-tick displacement to Godot units/second at 60 Hz and calls `MoveAndSlide()`, so Godot remains collision/contact authority. **C** uses the recovered crouch state across the common base, while **F** fly/noclip and **R** manual respawn remain separate development features. Capture metadata records the common-controller label, recovered locomotion/yaw state and avatar animation state for deterministic inspection.

A real Godot `rac1:LEVEL0` authority run completed `Idle -> Walk -> Run -> JumpRise -> Fall -> Land -> Run` against reconstructed collision. The engine trace is an integration witness; exact timing/velocity assertions live in portable recurrence tests so terrain does not contaminate the numeric comparison.

### Manual paired Joy-Con feel check (2026-09-19, non-authoritative)

A brief human check of the live `rac1:LEVEL0` build with a Godot-recognized paired Joy-Con reported that lateral translation went in the intended direction while Ratchet's presented body faced the opposite direction, producing a visible moonwalk effect. The same runtime session exercised ordinary walk, run, jump and attack states without exposing a corresponding displacement failure.

This is dispositioned as a **host presentation defect**, not as evidence against the recovered native displacement, stick-conditioning or yaw-recurrence laws. The suspect seam is the native-yaw-to-`VisualRoot` scene rotation conversion in `DebugPlayer`; no movement constants or retail-derived controller behavior are changed on the strength of this qualitative review. Follow-up work must correct and regression-test the presentation sign/basis conversion without modifying `Rac1RatchetMovementController` or `Rac1RatchetYawController`.

### Manual lateral run-band follow-up (2026-09-20)

A second brief live `rac1:LEVEL0` check reported a concrete directional discrepancy: pushing fully left or right can remain in walking, while full forward/back enters running much more reliably. The recovered conditioner itself is axis-symmetric, and grounded steering rotates the already-selected scalar speed only after `Rac1AnalogueInput.SpeedBand` is chosen; animation presentation runs later still. The first sampled run witness therefore exposes the host seam directly: with the current normalized-axis boundary, a cardinal host component must reach `(48 + 63) / 128 = 0.8671875` before it can select the retained run witness. A physically gated lateral value below that remains deterministically in the walk band even if the player regards the stick as fully deflected.

A follow-up Godot trace instrumented direct SDL `Input.GetJoyAxis` values and composed `InputMap.GetActionRawStrength` axes in the same sample. On the paired Joy-Con, the sampled left-stick values matched exactly across the two host paths (`direct-left=(0.166674,0.112963)`, `action-left=(0.166674,0.112963)`); after device re-enumeration, the single right Joy-Con likewise matched at near-neutral (`0.000015,0.000015`). The trace did not capture controlled cardinal extrema, so it does not establish the device's maximum physical range, but it provides no evidence of a software-side axis discrepancy between SDL and InputMap. The same session repeatedly transitioned through `Walk -> Run` during ordinary controller use.

The controller owner then identified a concrete hardware explanation: these Joy-Con sticks had been replaced, and the lateral axis requires a noticeably firmer push to reach its true outer range. Combined with the equal SDL/InputMap samples and absence of any reproduced software asymmetry, the earlier left/right report is retained as a **physical-device range false alarm**, not an OBP calibration defect. No host outer-range normalization was added, and the recovered 48-count trim, 76-count scale, activation radius, retained 62/63 run bracket, movement plateaus, acceleration, steering and yaw laws remain unchanged. Portable coverage now asserts that the existing host action-composition path sends all four logical full-scale cardinals into the same RAC1 run contract while preserving partial cardinal walk magnitudes.

Payload-free movement evidence is frozen in `research/generated/rac1-ratchet-movement-controller.json`, with the dense raw-stick reduction in `research/generated/rac1-analogue-input-law.json`, the independent control-heading matrices in `research/generated/rac1-stick-heading-probe.json`, and the moving-turn/reversal reduction in `research/generated/rac1-ground-turn-response.json`. Local raw PINE traces and screenshots remain outside Git.

## Deliberately unresolved

- the exact translational walk/run selector inside the retained 62/63 live bracket, the trigger behind the flat sequence-4 right-to-left run slowdown and the special run-release 180-degree target-aligned restart, plus the retail camera/control-heading source, chase follow/recenter, obstruction law and exact right-stick response;
- broader death/checkpoint selection beyond the witnessed Veldin reset-to-player-start behavior;
- collision details such as ledge grabs, wall interactions and special traversal abilities;
- combat movement, wrench lunges and hit volumes, which belong to the separate combat milestone.

These are not filled with DebugPlayer constants, sequel constants or animation guesses.
