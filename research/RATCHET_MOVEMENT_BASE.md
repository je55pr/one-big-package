# Ratchet movement base authority

## Decision

OBP selects **Ratchet & Clank (R&C1), NTSC-U original retail (`SCUS-97199`)** as the authority for the common Ratchet movement base.

This is a shortest-reliable-path decision, not a preference for R&C1 and not a reward for sunk work. Of the three trilogy candidates, R&C1 currently has the strongest controlled retail witnesses for the feel-critical translational controller and the smallest remaining archaeology surface before a complete deterministic implementation is defensible.

Going Commando (GC) has a very promising and well-bounded static controller spine, but it still needs live calibration for speed, jump, turning, landing and mode-dependent movement. Up Your Arsenal (UYA) is also recoverable, but its movement surface is materially broader because movement is embedded in a 178-state machine with multiple movement-basis modes.

The authority decision is therefore:

| Candidate | Evidence already available | Remaining path | Decision impact |
| --- | --- | --- | --- |
| R&C1 | Controlled live 60 Hz acceleration, stopping, jump/fall, air control, crouch, dense raw-stick magnitude/dead-zone shaping, stick/control-heading target composition, locomotion-state yaw and respawn witnesses; direct displacement output; portable recurrence tests | Keep the promoted conditioner boundary explicit and close selected camera/control-heading-source and host-contact questions | **Selected**: shortest path with the most empirically calibrated core |
| GC / R&C2 | Exact player-global/state spine, camera-relative input path, strafe branch, target-speed smoothing, jump/fall setup and candidate constants | Live-calibrate runtime cap/scalars, jump hold/release, turn easing, landing/slopes and mode differences across roughly 25-40 relevant routines | Strong sequel-specific authority, but replacing R&C1 now would trade measured behaviour for inferred behaviour |
| UYA / R&C3 | Outer hero tick, per-tick input path, centralized update/entry tables, shaped input, movement vectors and three-mode movement-basis selection | Label modes/states dynamically and recover precise recurrences across roughly 100 KB of closely related controller code | Recoverable, but not an archaeological shortcut to the common base |

## Concrete R&C1 evidence already available

The selected authority is already pinned by `research/RAC1_PLAYER_MOVEMENT.md` and `research/generated/rac1-ratchet-movement-controller.json`. The relevant recovered native behaviour is:

- player-state `0x0013f3d0 + 0x80/+0x84/+0x88` exposes actual per-update XYZ displacement matching live Ratchet Moby deltas;
- ordinary controller cadence is modelled as native displacement per 60 Hz update;
- loaded-overlay disassembly proves raw left-stick bytes are centered at 127, trimmed by 48 counts per axis and divided by 76 with a `1.0` clamp; Euclidean magnitude below exact `0.25` falls back to digital/zero input, making remapped radius 19 the exact analogue activation boundary; live displacement still brackets the walk/run change between cardinal 62/63 and diagonal radii 62.2254/63.6396, while an exact `0.82` magnitude comparison in code is currently proven only for the facing-controller branch; full planar ground input accelerates by approximately `1/480` native unit/tick to a measured cap of `0.09500919`;
- fixed-savestate moving-turn matrices show flat-ground run and walk displacement rotates with current native yaw each update rather than preserving prior world velocity or snapping to target yaw; clean 45/90/135/180-degree turns retain their speed plateaus despite large target-yaw error, ruling out a general facing-error projection multiplier, while release/reversal tests retain bounded state-specific exceptions;
- released ground input decelerates by approximately `1/300` native unit/tick to exact zero rather than hard-stopping;
- jump input has an eight-update anticipation phase, a measured launch-step table, a nine-update held-rise assist window, released-rise decrement near `0.00825`, and fall decrement near `0.009652` per update;
- short, 100 ms and long/max stationary-jump apexes are pinned, and a controlled running-jump witness matches the deterministic replay closely;
- airborne planar acceleration is approximately `1/180` unit/tick toward the same planar cap, while released air movement decays at approximately `1/1200` unit/tick;
- walking off a ledge enters the recovered falling recurrence immediately, while the final landing step may be collision-shortened by the host surface;
- crouch is stationary in native sequence 13, directional crouch uses turn-in-place sequences 14/15, and residual planar motion decays by approximately `0.001802944` unit/tick;
- live Ratchet Moby `+0x48` is proven yaw; fixed traces recover distinct startup-ground, run-ground, crouch-turn and air recurrences, plus ground/air caps and overshoot-to-target velocity reset;
- Veldin death/reset witnesses show motion state is cleared on respawn and Ratchet returns near the authored player start rather than inheriting pre-death motion.

This is enough to make the ordinary run/jump/fall/crouch, raw-stick magnitude shaping, moving-turn displacement direction, stick/control-heading target construction and facing recurrence evidence-backed today. The recovered conditioner is already promoted at the source-game boundary: the host carries unconditioned normalized signed axes through `PlayerControlIntent`, and `Rac1RatchetMovementController.Step` applies `Rac1AnalogueInput.ConditionUnitAxes`. Remaining ordinary-controller uncertainty is concentrated in the upstream camera/control-heading policy, selected host-contact behavior, and the bounded trigger behind the asymmetric sequence-4 run-reversal slowdown / opposite-run restart exception rather than analogue conditioning or the yaw easing law.

Full-scale cardinal and diagonal matrices prove `G+0x100 = WrapPi(controlHeading - stickAngle)` at two distinct saved control headings, while `G+0x0f0/+0x0f4/+0x0f8` carries the corresponding pitched 3D control-basis combination. Loaded-overlay dataflow independently verifies that ordinary target construction calls the angle helper at `0x001ff8b0`, loads the control-heading source float at `0x00166dd8`, and wraps their sum through `0x002000e8`; the two fixed savestates contain heading values matching the live inferred headings within `2.4e-7 rad`. A fixed-forward trace also shows `G+0x100` tracking that basis while it changes, including opposite target drift under left/right horizontal right-stick holds, without promoting an unrecovered camera turn-rate law. Dense partial-stick sweeps plus code recover the 48-count component trim, 76-count scale/clamp and exact radial activation threshold 19; their 62/63 live walk/run bracket remains distinct from the code-proven `0.82` high-magnitude facing branch. Fixed NTSC-U yaw traces resolve the earlier provenance conflict that static literal scans could not: startup ground is `0.9*v + 0.019*error`, run ground is `0.85*v + 0.008*error`, crouch turn is `0.93*v + 0.002*error`, and air is `0.8*v + 0.04*error`. The existing ground/air caps are verified, as is overshoot-to-target with velocity reset. The former standalone 1% early-snap threshold has no witness and is not part of the promoted native recurrence.

Directional R1 crouch witnesses remain zero-translation turn-in-place states rather than moving crouch/strafe. No separate ordinary moving-strafe law has been established as part of baseline R&C1 locomotion.

## Remaining required archaeology

The remaining work should stay surgical and close only the gaps that affect the common movement feel:

1. **Control-heading policy and input-boundary provenance.** Keep the promoted 48/76 trim/clamp and exact 0.25 activation law in `OBP.RAC1`, with no host-side dead zone, curve or interpolation; preserve the live 62/63 walk/run displacement bracket until its translational selector is traced. Literal DualShock 2 bytes remain archaeology/harness evidence, while the Godot host supplies normalized signed axes. The ordinary target consumer field is now identified as `0x00166dd8`; recover the retail camera/follow/recenter policy that produces it. DebugPlayer's presentation-camera heading remains host policy rather than a recovered retail chase-camera law.
2. **Contact and slope boundary.** Use a small set of controlled Veldin slopes, ledge departures and landings to determine the native facts the controller reacts to, especially uphill/downhill speed retention and landing/contact shortening. Godot still owns collision geometry, floor/ceiling detection and surface resolution.
3. **Reset and cadence hardening.** Keep controller reset semantics deterministic, verify any feel-relevant respawn heading/state needed by ordinary play, and ensure OBP executes the native recurrence once per 60 Hz controller update rather than depending accidentally on an unspecified host physics rate.

Do not expand this recovery into combat, wrench lunges, gadgets, ledge grabs, special traversal, full native camera reconstruction, arbitrary checkpoint archaeology or sequel-only movement states. Those may have their own authorities and milestones.

GC and UYA evidence remains useful as corroboration and for future game-specific behaviour, but it must not be used to fill an R&C1 unknown unless R&C1 evidence independently supports the same rule.

## Intended controller boundary

The common movement implementation should preserve the existing architecture boundary rather than turning Godot into the source of truth:

- `OBP.RAC1` owns all recovered R&C1-native movement rules: the recovered analogue shaping contract, ground/air acceleration and stopping, jump anticipation/hold/release, gravity/fall recurrence, crouch behaviour, facing/turn recurrence and deterministic movement state/reset.
- `OBP.Runtime.Player` carries only game-neutral facts needed by a source-game controller: unmodified planar control input, button edges/holds, host contact facts and the planar control heading required by the recovered native target transform. Do not pre-apply an OBP-created movement-angle transform.
- `OBP.Godot` / `game/` owns device sampling, engine units, scene/camera presentation, collision geometry, `MoveAndSlide()` or equivalent resolution, floor/ceiling classification and feeding resulting contact facts back to the controller.
- The controller output remains native-style deterministic movement for one controller update. Host conversion to engine velocity must not change the recurrence or become the place where source-game speed/gravity constants live.

The existing `Rac1RatchetMovementController` is the implementation home for the recovered translational rules. `RawGamepadInput` reads named-action strengths without applying an action dead zone, normalization or curve; `PlayerControlIntent` preserves those normalized signed host axes unconditioned; and `Rac1RatchetMovementController.Step` applies `Rac1AnalogueInput.ConditionUnitAxes` before movement rules or planar-basis mapping. The literal DualShock 2 byte form remains the archaeology/harness representation used to establish and cross-check that conditioner, not the format of the Godot/runtime boundary. The separate yaw controller consumes planar stick direction, the neutral control heading and a source-game `GroundStartup` / `GroundRun` / `CrouchTurn` / `Air` mode, keeping both the recovered target construction and recurrence out of Godot.

The production Godot host now adopts this selected controller for ordinary Ratchet play in R&C1, GC and UYA worlds. Camera-relative planar intent and facing both pass through the existing bounded control-yaw seam: Godot supplies the camera/control basis, while `Rac1RatchetYawController` owns target construction and the recovered yaw recurrence. The former DebugPlayer speed/jump/gravity path is no longer an ordinary-play fallback; only development fly/noclip and manual respawn remain intentionally host-specific.

## Provenance rule

**Native behaviour in this controller means native R&C1 behaviour only.** Every recovered constant, recurrence, state transition or input rule promoted into the common controller must be traceable to the selected R&C1 authority or to payload-free evidence derived from it.

Using that R&C1 controller as the default movement base in R&C1, GC and UYA reconstructed worlds is an **OBP-created design choice**. It must be labelled that way in code and documentation. It is not evidence, and must never be presented as a claim, that the three retail games used identical movement controllers.

GC remains authoritative for GC-specific movement once separately recovered; UYA remains authoritative for UYA-specific movement once separately recovered. Future game-specific overrides may therefore diverge from the common R&C1 base without invalidating this decision.

## Decision boundary

This document selects the authority and defines the recovery/implementation boundary. The ordinary translational recurrence, raw-stick shaping, stick/control-heading target construction and locomotion-state yaw recurrences now have R&C1 retail witnesses, and the recovered shaping is promoted through a host-neutral normalized-axis seam. Retail camera-heading policy, contact policy and special traversal remain outside that claim. Literal DualShock 2 byte traces establish provenance for the conditioner but are not themselves a Godot/runtime input format. Nothing here asserts trilogy-wide native equivalence.
