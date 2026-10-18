# R&C1 ordinary crouch and stop animation

Static recovery from the original NTSC-U Veldin overlay, using the authority
and section mapping in [RAC1_LEVEL_OVERLAY.md](RAC1_LEVEL_OVERLAY.md).
`tools/rac1-level-overlay-probe.py` verifies the full ISO and overlay hashes,
then emits instruction-block hashes, addresses and recovered constants in
`research/generated/rac1-level-overlay-probe.json`. No emulator was used for
this follow-up. Existing class-0 selector witnesses and decoded clip tests
provide the independent actor/asset context.

## Crouch entry and release

Action 4 dispatches to entry `0x2238e0` and update `0x22c45c`. In the ordinary
neutral-pose context, entry compares the previous pose sequence (`Moby+0x52`)
with standing 0. It selects sequence 13, target frame 2, with a 9-update blend
from standing or 14 updates otherwise (`0x2239b0..0x223a04`). A blend snapshot
has previous sequence 255 and therefore takes the longer path.

The caller requests loop range [3,18]. This is **not an effective loop range**:
`0x2325e0` rejects an end outside the selected clip's decoded frame count.
The admitted sequence 13 has 15 frames, so the full-clip loop remains active.

With neither crouch button held, release is admitted only when action age
`P+0x198 > scaledTickCount(11)`. The ordinary branch selects standing 0,
frame 0, with blend argument -2 (`0x22c49c..0x22c4bc`, shared call at
`0x231790`, whose delay slot sets frame argument zero).

Negative blend arguments select a preset curve. For -2 the selector sets
curve index 1, counter 0 and transition rate 1. Frame advance reads 16 values
at `0x17be54`, with count at `0x15f6e4`:

```text
.10 .20 .29 .38 .46 .54 .61 .68 .74 .80 .85 .90 .94 .97 .99 1
```

The strict near-one snap in frame advance does not finish at exactly the
binary32 representation of .99; the sixteenth update completes the blend.

## Crouch turning

The movement executor runs after animation advance, inside input update and
before action selection (`0x211728` calls `0x217970`).
`0x217bf0..0x217e08` admits the action-4 turn block when conditioned magnitude
is strictly greater than .2, or sequence 14/15 is already selected. After the
yaw recurrence, it reads signed angular velocity `P+0x184`:

- If absolute velocity exceeds `0.3490658402442932f * secondsPerUpdate`,
  positive selects 14 and negative selects 15. A changed sequence blends to
  frame 0 over 5 scaled updates.
- The turn rate multiplier is
  `clamp(abs(velocity)/(3.3161256313323975f * secondsPerUpdate), .55f, 2.5f)`.
- Exactly zero velocity returns to sequence 13, frame 0, over 7 updates.
- Small nonzero velocity retains both the sequence and previous multiplier.

Historical right/left clip names reflect the retained live trials; they must
not replace the signed-yaw predicate with raw stick direction. The host passes
its updated native yaw velocity, not a screen-relative left/right label.

## Stop selection correction

The old release witnesses established 5/20/6 but did not recover the predicate.
Default action-3 entry `0x2237c8..0x223840` now establishes a bounded rule:

```text
previous pose sequence Moby+0x52 == 4 AND 3 < P+0xaa8 < 16 -> sequence 5
otherwise -> sequence 6
target frame 4; blend 9 scaled updates
```

`P+0xaa8` is evaluated animation time, produced via `0x24edc8` after advance.
That helper uses signed 16-bit frame timestamps, scaled by 1/16 and interpolated
according to blend/wrap state. It is not safely interchangeable with an integer
observer frame or even frame index plus fraction without checking the asset.
`SelectDefaultStopSequence` exposes only this guarded selector, with boundary
tests; it is not yet connected to the host.

Sequence 20 comes through a separate ordinary-release caller
`0x22b934..0x22bae8`: low input (< .17), `P+0x160` thresholds scaled by the
update duration, a guard at `P+0x20aa`, and a recent-input sample from
`0x266e80(4)` determine its admission. The sample reader indexes an input
history ring; it is not another animation phase. This caller suppresses the
default action-3 selector and chooses 20, target frame 7, blend 9. Lower-speed
release has an age-gated neutral path using another preset blend curve.

The producer/meaning of `P+0x160`, history sampling, complete caller guards and
sequence-20 playback still need recovery/admission before stop integration.
Do not approximate that field with the host's locomotion scalar merely because
both look speed-like. Restart also depends on prior action and facing history.

The subsequent [ordinary stop/restart follow-up](RAC1_STOP_AND_RESTART_ANIMATION.md)
recovers the delayed-input and history producers, admits sequence 20 and authored
timestamps, and connects the bounded ordinary stop/standing/restart presentation.
It supersedes the pending producer/playback statements above. Full native motion
ownership and equipment/action-history overrides remain outside that integration.

## OBP integration and limits

### Full-run-to-crouch momentum audit

The manual OBP check reported substantial carry into crouch, stopping within
about one second. A new Veldin host smoke reproduces supported full-speed entry
both with directional input held and with it released: 53 updates to stop
(0.883 seconds), travelling approximately 2.45 native world units. Neither case
reaccelerates, loses the crouch state, or redirects residual motion with yaw.

The static ordinary action-4 path supports gradual braking. Entry copies the
current motion vector `P+0x110` into carry `P+0x150` and caps its length at
`7 * secondsPerUpdate`; that cap exceeds ordinary full-run speed. The action-4
movement branch at `0x217f78` loads coefficient `6.48f`, multiplies it by the
native squared-update scalar, and passes the result to `0x216710`. This helper
checks planar magnitude against `.001f`, removes vertical carry, subtracts the
decay from planar magnitude with a zero clamp, and adds the remaining carry
to the current request. It does not hard-stop ordinary full-run entry.

The nominal static decrement is about .0018 per update, consistent with the
retained measured .001802944 used by OBP. This audit leaves that measured rate
unchanged; it does not substitute a stronger braking value based on feel.
The host smoke and pure controller tests now bound the full-speed coast and
check continued holding after rest. Contact/slope behavior and entry tick
ordering remain bounded host approximations, not a new retail replay.

This trace also corrected the earlier update-order label: `0x216f10` processes
delayed-action timers, while the movement executor is called inside `0x211670`.
The walk/run selector now receives the updated controller scalar instead of
the previous update's scalar. This fixes a one-update animation selection delay
without changing translation or crouch braking.

### Distance feedback correction

The user clarified that the concern is total slide distance, rather than
stopping time. The earlier within-one-second confirmation did not resolve that
concern. OBP's observed 2.45-unit coast is about 1.71 times the decoded standing
height (1.4298922646861245 units), or 3.44 times the standing depth. The avatar
attachment and local-to-Godot conversion add no size multiplier; this ratio is
an OBP measurement, not a validated retail ratio.

The previous regression's distance window only locked in the current OBP
result. That assertion has been removed from the controller and host checks.
The host continues recording total distance, now also in standing heights,
alongside timing, support and acceleration checks.

Further static inspection of shared action entry `0x2223f8` and the direct
action-4 entry found no additional ordinary entry-speed reduction. This does
not establish the complete contact-resolved retail trajectory. A matched
full-speed-to-crouch position trajectory (including entry displacement and
support context), or recovery of the remaining contact execution, is still
needed before accepting or changing OBP's total slide distance. **Distance
parity remains unresolved.**

The [contact-motion follow-up](RAC1_CONTACT_MOTION_CARRY.md) now recovers the
P+0x110 producer and connects it to ordinary static-contact crouch entry in
OBP. It is distinct from final P+0x100 displacement and from the running scalar.
This closes a concrete entry-feedback gap while retaining the total-distance
validation boundary above.

### Presentation boundary

`Rac1AnimationFrameClock` owns the shared recovered full-loop/blend arithmetic.
The R&C1 presentation controller drives crouch, yaw-selected turning and the
stand-up curve once per 60 Hz physics update. It snapshots the advanced pose
when a blend is interrupted. Rendering consumes that fixed sample. Walking
can interrupt stand-up, and jumping/attacking/reset clears crouch history.

Admission still uses OBP's supported ordinary movement facts. This is not the
complete retail action machine: special equipment, unsupported grace periods,
and action-history overrides remain outside this change. In particular the
minimum release age governs presentation here; the existing movement controller
still responds immediately to released crouch input. A very short tap with
movement may therefore translate during the retained crouch presentation.

The frame/selection/curve rules are source-backed. Interpolating baked vertex
poses remains an OBP approximation of native joint blending; procedural pose
corrections are still absent. Headless checks can establish sequence/timing
integration but cannot establish visual parity with retail.
