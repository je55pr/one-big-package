# R&C1 locomotion, crouch and landing animation

Authority: original NTSC-U `SCUS-97199`, using the independently mapped Veldin
overlay and boot timing context in [RAC1_LEVEL_OVERLAY.md](RAC1_LEVEL_OVERLAY.md).
[The overlay probe](../tools/rac1-level-overlay-probe.py) reproduces addresses,
source-block hashes and constants in [its report](generated/rac1-level-overlay-probe.json).
These are bounded ordinary-mode contracts, not a complete retail action machine.

## Native ownership and update order

Here `P = 0x0013f350`. Action-2 entry/update dispatch to `0x2232d4/0x22b434`;
the ordinary animation helper is `0x228c18`, called at `0x22be04`.

| Field | Recovered meaning |
|---|---|
| `P+0x2080/+0x2084/+0x2088` | Player Moby, action, locomotion substate |
| `P+0x194` | Controller scalar step before collision adaptation |
| `P+0x1a0/+0x3bc` | Substate age and signed entry guard |
| `P+0xa90/+0xa94` | Playback multiplier and frame/blend transition rate |
| `P+0xab0/+0xab4/+0xab8` | Optional loop/resume fields |
| `Moby+0x50/+0x51` | Current/next frame |
| `Moby+0x52/+0x53/+0x54` | Previous/current sequence and fractional progress |

The regular `0x207184..0x2071cc` path advances animation (`0x232670`) first,
then input/movement (`0x211670`, nested movement call `0x211728 -> 0x217970`),
then action selection (`0x229b70`). `0x216f10` processes delayed-action timers.
Newly assigned rates affect the next advance; selection sees the updated
controller scalar, not final host displacement. Age increments at `0x221630`
and resets on entry/substate change; it is not a clip-completion timer.

Boot GP is `0x166c00`; tick scale at `0x15ed68` starts at 1, and the seconds
scalar at `0x15ed6c` starts at binary32 `1/60`. `scaledTickCount` (`0x1fef20`)
converts `0.5 + requestedTicks * tickScale` to an integer. Host binary32
arithmetic is not a full R5900 FPU model.

## Walk/run selection and playback

**Sequence 3 loops at low speed. Completing it does not select sequence 4.**
The ordinary action-2 caller reaches the helper with cached magnitude >= `.17`;
selection additionally needs age > `scaledTickCount(4)` and entry guard zero.
Modes 1/3 and special callers are outside this contract.

| Current substate/sequence | Change predicate | Rate multiplier |
|---|---|---|
| 0 / 3 | Scalar step strictly above `2.35 * dt` -> 4 | `clamp(step * 157, .6, 4)` |
| 1 / 4 | Scalar step strictly below `1.9 * dt` -> 3 | `clamp(step * 14, .6, 2.2)` |

The bounds are from `0x17bdb8`; equality retains the sequence. A steady `.015`
walk never crosses the upper bound. Rates still update while selection is
guarded. The base asset rates `.25/.5` are multiplied only on settled playback.
Unsupported speeds beyond the recovered final upper bound are rejected.

Ordinary entry targets sequence 3, next frame 8, blend 8; the entry-frame table
at `0x15f6d8` is `[8,0]`. Entry-history overrides are 12 ticks for
`P+0x22a8 == 1`, 15 for `P+0x209c == 0x23`, 18 for `0x209c == 6 or 0x2d`,
then 13 for `P+0x20a0 == 4` with contact counter `P+0x418 > scaledTickCount(5)`.
The host does not reconstruct every producer of that history.

On a 3/4 change, integer phase mapping uses the old **next-frame** byte:

```text
next = (oldNext * newCount / oldCount + offset[oldSubstate][newSubstate]) % newCount
offset = [[0,1],[5,0]]  // table 0x15f6c8; blend 8 scaled ticks
```

## Frame clock and standing curves

Default loops wrap next frame to zero at the decoded frame count and emit a
cycle flag while retaining the sequence. Cross-sequence blends advance by
transition rate alone, without the playback multiplier. Fractions snap to 1
strictly inside `(.99,1.01)` and to 0 strictly inside `(-.01,.01)`.
On a frame boundary, carry becomes `((fraction-1)/oldRate)*newFrameRate`;
this also applies to the first frame after a blend.

Evaluated animation time (`0x24edc8`) interpolates signed authored timestamps
in sixteenth units. A snapshot uses target timestamp + blend fraction; a wrap
uses current timestamp + fraction. It is neither elapsed seconds nor an
integer observer frame. Walk timestamps advance by 2, run by 1.

Negative blend arguments select preset curves, retaining the requested target
sequence and next frame:

| Preset | Source | Weights, one per original update |
|---|---|---|
| -1 | `0x17bdf0`, count `0x15f6e0` | `.15 .29 .42 .53 .63 .72 .80 .87 .93 .97 1` |
| -2 | `0x17be54`, count `0x15f6e4` | `.10 .20 .29 .38 .46 .54 .61 .68 .74 .80 .85 .90 .94 .97 .99 1` |

Exactly binary32 `.99` does not complete the strict near-one snap. Standing
retains its authored `.125` rate and interpolates the fractional pose every
physics update; rendering does not advance its clock.

## Crouch and yaw-selected turning

Action 4 entry/update are `0x2238e0/0x22c45c`. Entry selects sequence 13,
frame 2, blending for 9 ticks from previous standing sequence 0 or 14 otherwise
(including previous-sequence 255 snapshots). Requested loop `[3,18]` is rejected
by `0x2325e0` because sequence 13 has only 15 frames, so the full loop remains.
Release requires neither crouch button held and action age > 11 scaled ticks;
the ordinary return selects standing 0/frame 0 with preset -2.

The turn block (`0x217bf0..0x217e08`) is eligible at conditioned magnitude > `.2`
or while sequence 14/15 is selected. It reads updated native yaw velocity:

- Above `abs(velocity) > .3490658402442932 * dt`, positive selects 14 and
  negative 15; changing sequence targets frame 0 over 5 ticks.
- Rate is `clamp(abs(velocity)/(3.3161256313323975*dt), .55, 2.5)`.
- Exactly zero returns to sequence 13/frame 0 over 7 ticks; small nonzero
  velocity preserves sequence and multiplier.

Raw stick left/right is not the selector. The presentation minimum release age
does not by itself reconstruct full native movement/action-lock ownership.

## Release, history and restart

Action selection sees the previous update's capped input magnitude at
`P+0x229c`: `0x211690 -> 0x211830` precedes writes to current axes
`P+0x1d20/+0x1d24`. Separate input history is an **unclamped** 30-entry magnitude
ring at input-state `+0x2d0`, written before advancing the head. Reader
`0x266e80(4)` indexes `(head-min(available,4)+30)%30`; it is not an average.

Ordinary action-2 release (`0x22b934..0x22bae8`) needs delayed magnitude < `.17`
and uses contact length `P+0x160`, the 3D length of primary motion `P+0x110`.
With `P+0x20aa` clear:

| Contact/input condition | Ground selection |
|---|---|
| `2.7*dt < length < 4.4*dt` | Action 3, sequence 20/frame 7, blend 9 |
| `length >= 4.4*dt`, history(4) < `.8` | Same sequence-20 path |
| `length >= 4.4*dt`, history(4) >= `.8` | Default stop below |
| `length <= 2.7*dt` | Remain in action 2 until age > 25 and neutral guard clear; then standing with preset -1 |

Default action-3 entry (`0x2237c8..0x223840`) selects 5 only when previous pose
sequence is 4 and `3 < evaluatedTime < 16`; otherwise 6. It targets frame 4,
blend 9. Sequence 20 has 14 frames, 5/6 have 13, all at base rate `.25` with
timestamp stride 32 sixteenth units. Asynchronous observer-frame release
trials do not replace these native predicates; their raw-trace identities
remain in [the stop witness](generated/rac1-ratchet-locomotion-stop-phase.json).
The rejected `0x22478c` generic-Moby call remains gated by class `0x25f`.

Action 3 checks cycle completion before restart input. Update
`0x22c2e4..0x22c45c` requires delayed input > `.2`: zero scalar enters action 0
without animation selection; nonzero scalar needs input > `.5` to resume.
Ordinary idle admits locomotion above `.22`. Entry reuses the interrupted pose.
Complete opposed-facing motion-history overrides remain a separate boundary.

## Jump-return clock handoff correction

Held-input landing must reconnect the native-rate clock and preserve phase.
`ResumePlayback` is an OBP adapter policy, not a recovered native landing phase.
Otherwise full run can fall back to unscaled 30-FPS assets instead of the
scalar-derived rate, and slowing cannot perform the native 4-to-3 selection.
Unsupported ordinary-edge updates advance the existing clock without assigning
a new selector/rate. Render updates cannot advance the resumed pose.

## Landing ownership and standing interpolation correction

Helper `0x2293a8..0x22965c`, called from `0x22f224` after landing counter > 1,
distinguishes current input from residual contact motion. The admitted handoffs
transfer the sampled air pose into the selected owner:

| Native action | Target | Blend |
|---|---|---|
| 2, delayed input > `.3` | 4 / native global entry frame | 8, or 14 after contact counter 5 |
| 3, forward request or Thruster stretch | 5 / 0 | -1 |
| 3, Heli stretch | 5 / 0 | -2 |
| 4, held crouch | 13 / 2 | -2 |
| 0, cycle-exit flag | 0 / 0 | -2 |

A counter timeout can enter action 0 **without selecting animation**. Its
`GroundEntry` blend 0 preserves the air clip, clock, curve and blend source
until cycle completion, then selects standing with -2. A clip already frozen
at its terminal pose counts as complete. Floor contact alone must not select
run from launch history. Physics-sampled standing prevents stepped idle poses.

## Implementation and validation boundary

`Rac1OrdinaryLocomotionAnimation`, `Rac1LocomotionAnimationClock`,
`Rac1OrdinaryStopAnimation`, `Rac1InputMagnitudeHistory` and the source-game
presentation controller own the rules. Reset, attack, crouch and air transitions
clear or transfer their clock ownership explicitly. Tests cover strict bounds,
history wrap, interrupted blends, phase carry, repeated quiet/moving/flip
landings and render-clock independence; the movement smoke exercises host input.

Ordinary baked-vertex interpolation approximates native joint blending.
Native terrain/contact execution, procedural joints, all entry-guard/history
producers and equipment/aiming overrides are not established by this contract.
Deterministic host fixtures are not matched retail trajectory measurements.
