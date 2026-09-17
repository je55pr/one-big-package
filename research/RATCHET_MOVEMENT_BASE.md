# Ratchet movement base authority

## Decision

OBP selects **Ratchet & Clank (R&C1), NTSC-U original retail (`SCUS-97199`)** as the authority for the common Ratchet movement base.

This is a shortest-reliable-path decision, not a preference for R&C1 and not a reward for sunk work. Of the three trilogy candidates, R&C1 currently has the strongest controlled retail witnesses for the feel-critical translational controller and the smallest remaining archaeology surface before a complete deterministic implementation is defensible.

Going Commando (GC) has a very promising and well-bounded static controller spine, but it still needs live calibration for speed, jump, turning, landing and mode-dependent movement. Up Your Arsenal (UYA) is also recoverable, but its movement surface is materially broader because movement is embedded in a 178-state machine with multiple movement-basis modes.

The authority decision is therefore:

| Candidate | Evidence already available | Remaining path | Decision impact |
| --- | --- | --- | --- |
| R&C1 | Controlled live 60 Hz acceleration, stopping, jump/fall, air control, crouch and respawn witnesses; direct displacement output; portable recurrence tests | Close analogue/camera input, exact yaw recurrence and selected host-contact questions | **Selected**: shortest path with the most empirically calibrated core |
| GC / R&C2 | Exact player-global/state spine, camera-relative input path, strafe branch, target-speed smoothing, jump/fall setup and candidate constants | Live-calibrate runtime cap/scalars, jump hold/release, turn easing, landing/slopes and mode differences across roughly 25-40 relevant routines | Strong sequel-specific authority, but replacing R&C1 now would trade measured behaviour for inferred behaviour |
| UYA / R&C3 | Outer hero tick, per-tick input path, centralized update/entry tables, shaped input, movement vectors and three-mode movement-basis selection | Label modes/states dynamically and recover precise recurrences across roughly 100 KB of closely related controller code | Recoverable, but not an archaeological shortcut to the common base |

## Concrete R&C1 evidence already available

The selected authority is already pinned by `research/RAC1_PLAYER_MOVEMENT.md` and `research/generated/rac1-ratchet-movement-controller.json`. The relevant recovered native behaviour is:

- player-state `0x0013f3d0 + 0x80/+0x84/+0x88` exposes actual per-update XYZ displacement matching live Ratchet Moby deltas;
- ordinary controller cadence is modelled as native displacement per 60 Hz update;
- full planar ground input accelerates by approximately `1/480` native unit/tick to a measured cap of `0.09500919`;
- released ground input decelerates by approximately `1/300` native unit/tick to exact zero rather than hard-stopping;
- jump input has an eight-update anticipation phase, a measured launch-step table, a nine-update held-rise assist window, released-rise decrement near `0.00825`, and fall decrement near `0.009652` per update;
- short, 100 ms and long/max stationary-jump apexes are pinned, and a controlled running-jump witness matches the deterministic replay closely;
- airborne planar acceleration is approximately `1/180` unit/tick toward the same planar cap, while released air movement decays at approximately `1/1200` unit/tick;
- walking off a ledge enters the recovered falling recurrence immediately, while the final landing step may be collision-shortened by the host surface;
- crouch is stationary in native sequence 13, directional crouch uses turn-in-place sequences 14/15, and residual planar motion decays by approximately `0.001802944` unit/tick;
- live Ratchet Moby `+0x48` is proven yaw and normal facing converges toward planar travel, although the exact easing recurrence is not yet recovered;
- Veldin death/reset witnesses show motion state is cleared on respawn and Ratchet returns near the authored player start rather than inheriting pre-death motion.

This is enough to make the translational run/jump/fall/crouch core evidence-backed today. It is not enough to call the complete controller finished.

This follow-up archaeology narrowed two remaining gaps without filling them speculatively. Full-scale cardinal input/release remains the calibrated input envelope; arbitrary analogue magnitude, dead-zone shaping and the camera/control-heading transform are still unproven. Separately, exact-binary32 scans of the authority boot ELF and a loaded Veldin savestate do not establish the constants currently used by `Rac1RatchetYawController` as one retail yaw recurrence: the alleged ground error gain and both alleged max-step values are absent from the boot ELF, the ground max-step literal is absent from loaded EE RAM, and the common damping/gain literals occur broadly outside any proven player-yaw dataflow. The yaw implementation is therefore explicitly provisional.

Directional R1 crouch witnesses remain zero-translation turn-in-place states rather than moving crouch/strafe. No separate ordinary moving-strafe law has been established as part of baseline R&C1 locomotion.

## Remaining required archaeology

The remaining work should stay surgical and close only the gaps that affect the common movement feel:

1. **Analogue input envelope and camera-relative intent.** Recover the native dead-zone, post-dead-zone magnitude scaling, camera/control-yaw transform, arbitrary-direction behaviour and reversal response. The current `PlayerControlIntent.NormalizedPlanar()` discards input magnitude, and DebugPlayer's camera rotation is host policy rather than retail evidence.
2. **Facing and turn recurrence.** Recover ground and air yaw easing, snap/max-step behaviour and crouch-turn yaw from controlled Moby `+0x48` witnesses. The audit found that the present `Rac1RatchetYawController` constants outrun the admitted evidence, so those constants must not become the common-base authority merely because tests reproduce the implementation.
3. **Contact and slope boundary.** Use a small set of controlled Veldin slopes, ledge departures and landings to determine the native facts the controller reacts to, especially uphill/downhill speed retention and landing/contact shortening. Godot still owns collision geometry, floor/ceiling detection and surface resolution.
4. **Reset and cadence hardening.** Keep controller reset semantics deterministic, verify any feel-relevant respawn heading/state needed by ordinary play, and ensure OBP executes the native recurrence once per 60 Hz controller update rather than depending accidentally on an unspecified host physics rate.

Do not expand this recovery into combat, wrench lunges, gadgets, ledge grabs, special traversal, full native camera reconstruction, arbitrary checkpoint archaeology or sequel-only movement states. Those may have their own authorities and milestones.

GC and UYA evidence remains useful as corroboration and for future game-specific behaviour, but it must not be used to fill an R&C1 unknown unless R&C1 evidence independently supports the same rule.

## Intended controller boundary

The common movement implementation should preserve the existing architecture boundary rather than turning Godot into the source of truth:

- `OBP.RAC1` owns all recovered R&C1-native movement rules: analogue shaping once recovered, ground/air acceleration and stopping, jump anticipation/hold/release, gravity/fall recurrence, crouch behaviour, facing/turn recurrence and deterministic movement state/reset.
- `OBP.Runtime.Player` carries only game-neutral facts needed by a source-game controller: unmodified planar control input, button edges/holds, and host contact facts. If camera/control heading is required for the recovered native input transform, expose it as a neutral fact instead of pre-applying an OBP-created movement transform.
- `OBP.Godot` / `game/` owns device sampling, engine units, scene/camera presentation, collision geometry, `MoveAndSlide()` or equivalent resolution, floor/ceiling classification and feeding resulting contact facts back to the controller.
- The controller output remains native-style deterministic movement for one controller update. Host conversion to engine velocity must not change the recurrence or become the place where source-game speed/gravity constants live.

The existing `Rac1RatchetMovementController` is the natural implementation home for the recovered translational rules, but its contract may need to stop normalizing every nonzero planar vector once analogue magnitude is recovered. The separate yaw implementation must be reconciled against retail evidence before it is promoted as part of the authority base.

## Provenance rule

**Native behaviour in this controller means native R&C1 behaviour only.** Every recovered constant, recurrence, state transition or input rule promoted into the common controller must be traceable to the selected R&C1 authority or to payload-free evidence derived from it.

Using that R&C1 controller as the default movement base in R&C1, GC and UYA reconstructed worlds is an **OBP-created design choice**. It must be labelled that way in code and documentation. It is not evidence, and must never be presented as a claim, that the three retail games used identical movement controllers.

GC remains authoritative for GC-specific movement once separately recovered; UYA remains authoritative for UYA-specific movement once separately recovered. Future game-specific overrides may therefore diverge from the common R&C1 base without invalidating this decision.

## Decision boundary

This document selects the authority and defines the recovery/implementation boundary only. It does not implement movement, bless unresolved yaw constants, replace collision policy, or assert trilogy-wide native equivalence. The next movement worker should finish the remaining R&C1 archaeology first, then implement only what that evidence supports.
