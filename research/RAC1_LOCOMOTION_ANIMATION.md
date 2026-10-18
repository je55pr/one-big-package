# R&C1 ordinary locomotion animation recovery

Authority: original NTSC-U `SCUS-97199`, Veldin overlay. Recovered 2026-10-18
directly from the verified ISO, without running PCSX2. This supersedes the
interpretation of sequence 3 as a one-shot in the earlier animation admission
map. **Ordinary low-speed locomotion loops sequence 3. Sequence 4 is selected
by speed, not by completing sequence 3.**

The [overlay probe](../tools/rac1-level-overlay-probe.py) reproduces the code
mapping, hashes the source ISO/overlay/boot executable, and emits data, addresses
and block hashes in
[`generated/rac1-level-overlay-probe.json`](generated/rac1-level-overlay-probe.json).
The source-game arithmetic contract is
[`Rac1OrdinaryLocomotionAnimation`](../src/OBP.RAC1/Player/Rac1OrdinaryLocomotionAnimation.cs).
It covers the regular Ratchet branch at the original 60 Hz timing context.
It is not an emulator or a claim of complete animation parity.

## Call chain and field meanings

`P = 0x0013f350`; all player offsets below are relative to P. The native state
entry jump table at `0x001e8030` sends action 2 to `0x002232d4`. The update jump
table at `0x001e82b0` sends it to `0x0022b434`. Following the ordinary eligible
path reaches a call at `0x0022be04` to `0x00228c18`.

| Field | Role in this recovered path |
| --- | --- |
| `P+0x2080` | Pointer to player Moby |
| `P+0x2084` | Native action state; ordinary locomotion is 2 |
| `P+0x2088` | Locomotion substate; 0/1 select sequences 3/4 |
| `P+0x194` | Scalar controller speed per update, before host collision adaptation |
| `P+0x1a0` | Updates since substate change |
| `P+0x3bc` | Signed halfword guard that can defer a substate change |
| `P+0xa90` | Player-owned animation-rate multiplier |
| `P+0xa94` | Current frame/blend transition rate |
| `P+0xab0`, `P+0xab4`, `P+0xab8` | Optional loop-range/resume fields |
| `Moby+0x50`, `Moby+0x51` | Current frame and next frame |
| `Moby+0x52`, `Moby+0x53` | Previous/current sequence for blending |
| `Moby+0x54` | Fractional frame/blend progress |

`P+0x1a0` increments at `0x00221630`. State entry resets it at `0x002275ec`,
and substate changes reset it through `0x00231ca4..0x00231cac` and the equivalent
end-of-update path at `0x002212a0`. It is not a clip-completion timer.

The caller handles support loss, input release, action changes and special
movement flags before reaching this helper. In particular, ordinary action 2
with cached input magnitude at least `0.17f` takes the branch at `0x0022b960`
to the helper. This is an active-locomotion maintenance check, not the separate
movement-admission threshold. Mode 1/3 use different rate branches; those and
other action callers are excluded from this contract.

### Update order

The regular path at `0x00207184..0x002071cc` calls these routines in order:

1. `0x00232670`: animation advance, using the previously assigned multiplier.
2. `0x00211670`: input, movement and counter update. Its call at `0x00211728`
   reaches movement executor `0x00217970`; its later call at `0x00211814`
   reaches `0x002215c8`, which increments substate age at `0x00221630`.
3. After intervening support/control helpers, `0x00229b70`: action update,
   including the ordinary animation selector and rate assignment.
4. `0x00216f10`: delayed-action timer processing. This is not the movement executor.

Therefore a new multiplier affects the next animation advance. The selector
reads the updated controller scalar after the movement executor, not the
resulting host collision displacement. The earlier pre-movement interpretation
misidentified `0x216f10` and missed the nested `0x211728` call; the host now
supplies the updated scalar, eliminating a one-update selector delay.
The probe verifies these call instructions
and hashes the surrounding update block. Intervening special-case producers
are not modeled by the bounded normal-movement adapter.

## Speed selector and hysteresis

At `0x00228ca0..0x00228ee0`, selection is eligible only when:

```
P.substateAge > scaledTickCount(4) && P.entryGuard == 0
```

`scaledTickCount` is `0x001fef20`. Its `adda.s`/`madd.s` sequence forms
`0.5 + requestedTicks * tickScale`, followed by integer conversion. The boot
small-data context initializes tickScale at `0x0015ed68` to 1 and seconds per
update at `0x0015ed6c` to binary32 `1/60`. The boot ELF's `.reginfo` identifies
GP as `0x00166c00`. These are explicitly boot initial values, not fresh runtime
measurements. R5900 decoding was cross-checked against the PCSX2
[opcode tables](https://github.com/PCSX2/pcsx2/blob/master/pcsx2/R5900OpcodeTables.cpp)
and [FPU implementation](https://github.com/PCSX2/pcsx2/blob/master/pcsx2/FPU.cpp).

The two 16-byte table rows begin at `0x0017bdb8`. The helper uses the first two
floats of each row as lower/upper speed bounds, scaled by seconds per update.

| Current sequence | Lower bound | Upper bound | Ordinary change |
| --- | ---: | ---: | --- |
| 3 (substate 0) | 0 | `2.35f` | 3 → 4 when scalar step is strictly above `2.35f * tickSeconds` |
| 4 (substate 1) | `1.9f` | 10 | 4 → 3 when scalar step is strictly below `1.9f * tickSeconds` |

With the boot timing values, host binary32 products are approximately
`0.0391666666` and `0.0316666663` per update. Equality retains the sequence.
Between these bounds, history determines the sequence. A steady walk at
`0.015` never crosses the upper boundary, regardless of elapsed time.

The helper's table search can run beyond the two recovered rows at extreme
speeds before clamping. The C# contract rejects speeds above the final upper
bound rather than assigning behavior to those out-of-scope reads. The test
suite exercises threshold equality, adjacent floats, both guards and both
transition directions. Host binary32 arithmetic is not a full R5900 FPU model.

This threshold is also distinct from the host's current sampled run-yaw
boundary (`0.039582664`). The semantic `Run` signal is an existing approximation;
it must not be cited as the recovered native animation selector itself.

## Playback rate and frame alignment

The same helper always updates `P+0xa90`, including while selection is guarded:

| Sequence | Recovered multiplier |
| --- | --- |
| 3 | `clamp(P.scalarStep * 157f, 0.6f, 4f)` |
| 4 | `clamp(P.scalarStep * 14f, 0.6f, 2.2f)` |

The exact literal loads are retained by the probe. In the settled same-sequence
path, `0x002326e0..0x002326ec` multiplies this value by `P+0xa94` before advancing
fractional progress. The decoded asset rates of `0.25` (sequence 3) and `0.5`
(sequence 4) therefore are not fixed runtime rates. For example, step `0.015`
gives sequence 3 a multiplier near `2.355`. Fraction snapping and update ordering
still matter for exact frame cadence. During a sequence blend the advance path
uses separate logic, so multiplying the whole presentation clock is insufficient.

Ordinary entry clears substate to 0, then calls the selector with sequence
`substate + 3`. GP-relative entry-frame table `0x0015f6d8` contains `[8, 0]`:
the ordinary sequence-3 entry targets next frame 8, not frame 0. Entry blend
duration defaults to `scaledTickCount(8)`. The traced block
`0x002234c0..0x00223560` applies these overrides in order:

| Predicate | Requested blend ticks |
| --- | ---: |
| `P+0x22a8 == 1` | 12 |
| `P+0x209c == 0x23` | 15 |
| `P+0x209c == 6` or `0x2d` | 18 |
| `P+0x20a0 == 4` and `P+0x418 > scaledTickCount(5)` | 13 |

The host does not yet supply this complete history. Its ordinary idle-entry
adapter uses the default eight ticks; these overrides remain an explicit gap.

On a 3/4 substate change, `0x00228e48..0x00228ed4` maps the old **next-frame**
byte into the new clip using its decoded frame counts:

```
next = ((oldNext * newCount / oldCount) + offset[oldSubstate][newSubstate]) % newCount
offset = [[0, 1], [5, 0]]
```

The division is integer division. The phase table is at `0x0015f6c8`. R5900
`mult` at `0x00228e94` also writes its integer destination register; generic
MIPS decoders can display an incorrect accumulator alias here. The transition
passes `scaledTickCount(8)` as the blend argument. It does not restart both
clips at zero.

The selector at `0x002323b8` resets the optional loop range through `0x00232628`.
Neither the ordinary state-2 entry nor this locomotion helper installs a new
subrange. The ordinary animation advance path at `0x00232930..0x00232998`
wraps the next-frame byte to zero at the frame count and sets an end-of-cycle
flag while retaining the sequence. Together with the complete helper, this
establishes full-clip looping for the recovered ordinary path.

At `0x0023272c`, a cross-sequence blend increments by `P+0xa94` alone; it
ignores the playback multiplier. Ordinary eight-tick blends use rate `1/8`.
The fraction is snapped to 1 strictly inside `(0.99f, 1.01f)`, and to 0
strictly inside `(-0.01f, 0.01f)`. While fraction is at least 1, the next frame
becomes current, next increments/wraps, and remaining fraction becomes
`((fraction - 1) / oldRate) * newlyCurrentFrameRate`. This includes the first
frame after a blend. It is not equivalent to multiplying elapsed clip time.

## OBP disposition and remaining work

The previous presentation selector marked sequence 3 non-looping, freezing it
at frame 32. That flag is corrected. The movement smoke now requires a long
walk to retain sequence 3, advance frames and wrap; full input must still reach
sequence 4. This replaces the unsupported timed low-stick 3 → 4 assertion with
a stronger check of the behavior supported by the retail code.

`Rac1OrdinaryLocomotionAnimation` preserves the recovered speed, guard, rate and
phase rules independently of Godot. `Rac1LocomotionAnimationClock` now integrates
them with the recovered advance order, fraction snapping, default loops, entry
frame and blend timing. The source-aware presentation controller runs this
clock once per physics update for an ordinary idle entry and its uninterrupted
walk/run interval. `PlayerHost` supplies scalar speed after controller movement,
held-input eligibility and support; a transient ordinary edge interval advances
the animation but skips selector/rate reassignment. Rendering consumes a fixed
sampled pose and cannot advance the clock. Teleport, respawn and fly-mode resets
clear the session. The native clock is bound only at the original 60 Hz scale.

The controller snapshots its current interpolated pose when switching clips,
including a switch during another blend. **Interpolation of baked vertex
positions is an OBP approximation**, not recovered joint-pose blending/skinning.
The native frame indices, fraction and blend timing are separate from this
visual approximation. Special entry history, nonzero entry-guard writers,
release/stop, landing, attack and equipment transitions still need their own
admission. Other action entries retain the semantic fallback until a new idle
entry. Host support and scalar movement remain bounded reconstructions, and
binary32 arithmetic is not a full R5900 FPU implementation.

There was no new gameplay replay or rendered comparison. The dataflow, pinned
source blocks and deterministic contract tests establish these bounded rules;
they do not establish complete retail-versus-OBP movement parity.

Validation results, including the earlier loop-only checkpoint and the physics
integration follow-up, are recorded in [the parity audit](RAC1_MOVEMENT_PARITY.md).
New deterministic coverage checks old-rate ordering, unscaled blends, both
phase transitions, interrupted-blend snapshots, wrapping, variable-rate carry,
fraction snapping, unsupported edge updates, reset/other-action handoffs and
render-clock independence. The headless smoke also requires a sampled pose and
the expected steady-walk multiplier, so a legacy fixed-rate loop cannot pass.

## Jump-return clock handoff correction

The user reported slower-looking movement after jumping and continuing to move.
The adapter discarded its locomotion clock on airborne entry, then selected
sequence 4 through the semantic landing path. Only idle/standing could create a
new native-rate clock. Continued moving therefore stayed on unscaled asset
playback indefinitely: 30 FPS instead of approximately 39.9 FPS at full run
(`30 * 14 * .09500919`). It also bypassed ordinary 4-to-3 selection when slowing
to a walk. This was a concrete OBP handoff defect, not a new retail movement law.

The physics controller now reconnects supported, active locomotion after its
airborne semantic handoff. It preserves the selected sequence and full-loop
phase, assigns the recovered multiplier immediately, then advances through the
existing physics-clocked selector. It handles the explicit Land state and a
direct Fall-to-moving handoff. Render updates cannot advance the resumed pose.

`ResumePlayback` is explicitly an **OBP adapter policy**. It does not recover a
native landing blend or action-entry phase. The existing witnessed landing
sequence choice is retained; airborne animation, full native landing execution,
procedural joint work and reconstructed contact facts still have their prior
limits. Jump height, movement speed and crouch braking are unchanged.

Three regression cases (run, walk, and a host handoff without Land) failed with
no locomotion clock before the fix. They now repeat two jumps, require the
sampled pose/rate, and verify render-clock independence. A separate test pins
fractional phase preservation and rate on the first resumed update. The Godot
moving-jump smoke now retains input for five observed updates after landing
and checks the native clock, synchronized pose and, when support admits the
helper, the scalar-derived multiplier. An initial smoke failure was diagnosed
as an unsupported ordinary edge update (`phase=Falling`, floor false) retaining
1.1340233 while the new scalar would produce 1.1631899. The assertion now respects
that recovered helper gate; existing edge-clock tests require rate retention.
The prior smoke cleared movement immediately at touchdown and missed the defect.

Validation on 2026-10-18: portable C# 1,025 passed / 124 retail-dependent skipped;
full canonical retail-backed C# 1,149 passed / zero skipped. Godot Debug build
had zero warnings/errors; the expanded R&C1 smoke passed with held movement
across landing. Formatter verification and diff checks passed. Local logs use
ignored `captures/jump-return-*.log`; no new retail gameplay trace was captured.

## Landing ownership and standing interpolation correction

The later user report covered a different path from the held-input regression
above: releasing input in the air with a small landing velocity briefly selected
run before cutting to idle, and idle and flip landing poses appeared to update
slowly. The semantic fallback selected sequence 4 from the **launch** state even
when the landing input was neutral. Standing also lost its physics sampled pose
after initial idle or completed crouch stand-up, leaving discrete native
keyframes to the renderer. The sequence-0 rate of `.125` keyframes per original
60 Hz update is retained; the fix interpolates that fractional pose every physics
update instead of changing the asset's playback speed.

Grounded landing selection is now supplied as a one-update `GroundEntry` by the
movement session. The source helper `0x2293a8..0x22965c`, called from `0x22f224`
after the landing-contact counter exceeds one, distinguishes actual movement
input from residual contact motion. Its admitted ordinary entries transfer to
their own standing, locomotion, stop or crouch clocks:

| Native action | Sequence / next frame | Selector blend |
| --- | --- | --- |
| 2, delayed input above `.3` | 4 / native global entry frame | 8 updates, or 14 after contact counter 5 |
| 3, admitted forward request or Thruster long jump | 5 / 0 | preset `-1` |
| 3, Heli long jump landing | 5 / 0 | preset `-2` |
| 4, held crouch | 13 / 2 | preset `-2` |
| 0, native cycle-exit flag | 0 / 0 | preset `-2` |

The neutral counter timeout can enter action 0 **without selecting an animation**.
`GroundEntry` action 0 with blend 0 represents that precise ownership transfer;
its sequence must match the existing air clock. The complete phase, previous
rate, preset-curve position and sampled blend source survive. The action-0 owner
continues that clip until its native cycle flag, then selects sequence 0 with
preset `-2`. It can also restart locomotion or be interrupted by another action.
The session keeps its air selection while the native landing gate is still
pending, so supported contact alone does not select run or standing.

Preset curves `-1` and `-2` now retain the requested target sequence and next
frame. Applying `-2` to the grounded crouch entry must therefore blend toward
sequence 13 **frame 2**, rather than incorrectly replacing the next frame with 0.
All transitions snapshot the actual interpolated air/flip pose. Rendering cannot
advance either the preserved clock or standing playback. Vertex blending remains
the previously documented baked-pose approximation. The compatibility path for
hosts without `GroundEntry` now chooses a smooth neutral landing when supported
input is released; its entry timing remains explicitly an adapter fallback.

Deterministic regression cases cover neutral idle interpolation at the unchanged
native rate, low-velocity released landing, repeated flip landings, every admitted
ground-entry family, ownership transfer during both an unfinished blend and
ordinary playback, completion into standing, moving restart and reset. They also
retain the earlier held-run jump-return cases and crouch/stop clock tests.
