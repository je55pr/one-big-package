# R&C1 ordinary stop and restart presentation

Authority: original NTSC-U Veldin overlay and class-0 Ratchet sequence data.
The overlay probe verifies the canonical ISO and mapped overlay before producing
payload-free block hashes and constants. This follow-up uses offline recovery
and OBP tests; it does not include a fresh PCSX2 gameplay comparison.

Scope is the supported ordinary, unaimed mode-0 caller. Equipment, targeting,
combat, native action locks and other overrides remain outside this admission.
Crouch-slide tuning is deferred at the user's request.

## Delayed input and history are different facts

`0x211690` calls `0x211830`, whose capped vector magnitude is written to
P+0x229c. Only afterward, at `0x2116ac..0x2116bc`, are the current conditioned
left-stick axes copied into P+0x1d20/0x1d24. Therefore the action update sees
the **previous update's magnitude**, capped at 1. Replacing it with current
input would change release/restart admission by one update.

The separate input-history producer sets its magnitude-ring base to input
state +0x2d0 at `0x26724c`. `0x267650..0x26769c` computes the current conditioned
left-stick vector's length and stores it without a radial clamp. A diagonal
can therefore retain a value above 1. `0x2677bc..0x267808` advances the 30-entry
ring head after storing and caps its available count at 30.

`0x266e80(4)` reads the magnitude at
`(head - min(available, 4) + 30) % 30`. The early-history clamp and write-before-
advance order matter; this is not a four-sample average or animation phase.

## Release from ordinary action 2

The ordinary branch `0x22b934..0x22bae8` uses delayed magnitude < .17 and
contact-derived P+0x160. The latter is the **3D** length of primary controlled
motion P+0x110, recovered by the [contact audit](RAC1_CONTACT_MOTION_CARRY.md).
It is distinct from scalar P+0x194 and final displacement P+0x100.

For the ordinary caller with P+0x20aa clear:

| Contact length / condition | Result |
| --- | --- |
| Above `2.7f * secondsPerUpdate`, and below `4.4f * secondsPerUpdate` | Action 3, sequence 20, target frame 7, blend 9 |
| At/above the high threshold, with history(4) < .8 | Same sequence-20 path |
| At/above the high threshold, with history(4) >= .8 | Default action-3 entry, selecting 5/6, target frame 4, blend 9 |
| At/below the low threshold | Keep action 2 until action age > 25 and ordinary neutral guard is clear; then standing 0 with preset -1 |

The default 5/6 selector remains: previous pose sequence 4 and
`3 < evaluatedTime < 16` selects 5; otherwise 6. A snapshotted blend has previous
sequence 255. Both bounds are strict and use authored animation time.

Sequence 20 is now admitted as an actual posed avatar clip: 14 frames, constant
transition rate .25 (15 FPS at nominal 60 Hz), authored timestamps 0,32,...,416
in native sixteenth units. Sequences 5/6 each have 13 frames with the same rate
and timestamp stride. The source-backed avatar checks pin these data facts.

## Animation time and standing blend

`0x24edc8..0x24eebc` evaluates signed 16-bit authored timestamps, scaled by 1/16.
Ordinary interpolation uses the current and next frame timestamps. A snapshot
blend uses target timestamp + blend fraction; a wrap uses current timestamp +
fraction rather than interpolating backward to frame zero.

These coordinates differ from elapsed seconds. The admitted walk timestamps
advance by 2 per frame, run timestamps by 1. The native avatar decoder now
retains them, and the neutral clip carries optional authored timeline positions
for interpretation by its source controller. Godot does not interpret them.

Preset -1 uses the 11-value curve at `0x17bdf0`, count at `0x15f6e0`:

```text
.15 .29 .42 .53 .63 .72 .80 .87 .93 .97 1
```

Action 3 checks the frame clock's end-cycle bit before restart input and enters
standing when it is set. Stop presentation therefore does not wait on a generic
seconds-based clip timer or loop indefinitely after release.

## Restart

The ordinary action-3 update at `0x22c2e4..0x22c45c` checks delayed input > .2.
If scalar P+0x194 is exactly zero, it enters action 0 with animation selection
suppressed. If scalar is nonzero, input > .5 admits locomotion; weaker input
retains the stop action. The ordinary idle branch at `0x22a720` admits
locomotion above .22. Ordinary action-2 animation entry returns to sequence 3,
target frame 8 with its default eight-update blend, using the interrupted pose.

Entry `0x2232d4..0x2233a4` also initializes native movement scalar from contact
motion, caps it, and can clear it for an opposed-facing stop history. This
presentation integration does **not** implement that complete motion-ownership
transition. Reversed-direction translation still follows OBP's existing bounded
movement controller. The new reverse-restart smoke validates presentation only.

## OBP boundary and checks

`Rac1OrdinaryStopAnimation` owns the predicates, evaluated timeline and standing
curve. `Rac1InputMagnitudeHistory` owns the native ring semantics. The R&C1
presentation controller advances its stop clock once per physics update and
snapshots an interrupted blend before restarting. Render updates consume the
sampled pose. Attack, airborne motion, crouch and reset clear stop ownership.

The host supplies completed primary-contact length only under the existing
static/no-external-transport admission. Missing contact facts retain the prior
semantic fallback; they are not replaced with target speed. Equipment/aiming
guards and full action histories are not reconstructed by this adapter, so this
does not establish complete action-machine parity. Baked-vertex blending remains
an approximation of native joint blending. Existing translational release tables,
braking values and GC/UYA provider behavior are unchanged.

Source checks pin the delayed-input order, history writer/read/advance, release
and restart blocks, literals, authored timestamps and standing curve. Portable
tests cover strict boundaries, history startup/wrap, blend/wrap time, interrupted
stops, zero-scalar no-selection restart and unrelated-action cleanup. The Godot
smoke adds walk release, run stop-family selection, quick reverse restart and
settling back to standing using real host contact feedback.

Manual check after rebuilding through the desktop launcher, R&C1/Veldin:

1. Walk and release the stick; check the transition to standing.
2. Run and release at several points in the stride; check the stop animation.
3. Release briefly, then resume or reverse direction before stopping completes.
4. Jump or crouch during a stop; check that the previous stop pose does not stick.

These checks seek visual/transition regressions, not approval of slide distance.

Validation on 2026-10-18: full canonical retail-backed C# suite 1,145 passed,
zero skipped; final portable suite 1,021 passed / 124 retail-dependent skipped;
Python tooling 81 passed; authority-verified report regenerated.
Godot Debug build had zero warnings/errors, and the expanded R&C1 movement smoke
passed both ordinary stop/restart trials and its existing crouch/jump/contact
checks. Formatter verification and diff checks passed. Local logs use ignored
`captures/stop-restart-*.log`; no new retail trajectory was captured.
