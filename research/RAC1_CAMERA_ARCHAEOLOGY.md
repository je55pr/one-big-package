# R&C1 camera archaeology harness

Authority: NTSC-U retail `SCUS-97199`. The capture harness remains generic,
but this note now retains the ordinary horizontal camera/control-heading,
unobstructed chase framing/follow, and executable-backed obstruction-response
contracts proven by the fixed savestate, loaded-overlay signatures and
frame-advanced trials. Unresolved producer stages are called out rather than
inferred.

## Known live fields

The camera harness reuses the deterministic PCSX2 input-recording and PINE
machinery from `tools/rac1-analogue-movement-harness.py`. Every emulated frame
is advanced once, then sampled once with a single batched PINE read.

The fixed fields are already supported by retained R&C1 evidence:

- player state yaw: `0x0013f3d0 + 0x18`
- live Ratchet Moby position: `0x01845e80 + 0x10/+0x14/+0x18`
- live Ratchet Moby yaw: `0x01845e80 + 0x48`
- player movement target yaw: `0x0013f3d0 + 0x100`
- ordinary control-heading source: `0x00166dd8`
- horizontal camera/control basis witnesses: `0x00166c80 = sin(controlHeading)`
  and `0x00166fe4 = cos(controlHeading)`
- camera forward basis: `0x00166c88/+0x18/+0x28` relative to `0x00166c80`
- final eye position: `0x00166dc0/+0x04/+0x08`
- camera pitch: `0x00166dd4`
- filtered chase target: `0x00166e10/+0x04/+0x08`, raw target Z at `+0x0c`,
  and stored vertical-follow velocity at `+0x10`
- raw player-position copy: `0x00166e70/+0x04/+0x08`
- conditioned native right stick: `I+0x100/+0x104`, where `I=0x0013c940`
- conditioned native left stick: `I+0x108/+0x10c`
- native directional flags consumed by the heading-step branch: `I+0x1a0`
- literal left/right-stick bytes are also retained per movie frame

The default candidate scan is `0x00166c00..0x00166fff`. That span is a
discovery aid around the known control-heading field, not a claim that every
word in it belongs to a camera structure.

## Recovered horizontal contract

The fixed authority state proves that `controlHeading` is the azimuth carried
by the horizontal camera/control basis, not Ratchet's facing yaw. Across the
idle, movement, turn and manual-input captures, the basis witnesses track
`sin(controlHeading)` and `cos(controlHeading)` to float precision.

Movement consumes this camera-relative heading directly. For active conditioned
left stick, the retained target relation is:

`targetYaw = WrapPi(controlHeading + atan2(-leftX, -leftY))`

Forward movement therefore targets `controlHeading`; rightward left-stick
input targets `controlHeading - pi/2`. Ratchet's current yaw follows that
target separately. At the PINE-after-`FrameAdvance(1)` sample boundary, steady
active-stick target yaw matches the previous sampled `controlHeading` plus the
current stick term to about `3e-7` rad; same-sample comparisons can differ by
roughly one camera update. Treat that as an update-order witness, not yet as a
decoded mandatory one-frame storage delay. In the authority state, Ratchet can
begin almost pi radians away from `controlHeading` while the heading itself
remains stationary, then turn toward it once forward input begins.

A neutral right stick does not freeze the camera heading. A sustained left-stick
turn changes `controlHeading` through the ordinary chase path. A horizontal
right-stick pulse also changes `controlHeading` directly. Release alone does
not snap or recenter toward Ratchet: in the stationary left/right pulse cases,
Ratchet yaw stays fixed while the heading continues another roughly `0.282`
rad in the same direction. During forward movement, Ratchet's yaw instead
converges back toward the moving camera/control heading. After the dedicated
left-stick turn, the heading-minus-player gap grows to about `1.309` rad and
the forward follow phase closes it to about `0.029` rad. No separate ordinary
reset/snap trigger is proven by this matrix.

The loaded overlay also contains a verified direction-flag heading-step branch
at `0x001f3aa0..0x001f3f7c`. It consumes `I+0x1a0`, keeps a step at
`0x0016c058+0x80`, uses a `0.002` increment, clamps to `+/-0.04`, divides
the released step by `1.5`, and writes
`WrapPi(controlHeading - step)` through helper `0x00200130`. This remains a
separate producer stage because its invocation/flag selection is not equivalent
to raw right-stick input.

The ordinary type-0 manual-input path is now independently bounded. Update
`0x002e9c28` reaches core `0x002e9bb0`; `0x002e89b0` selects conditioned
right-stick X/Y when present and only falls back to camera-state commands when
the manual axis is neutral. `0x002e8bb8` then applies the native damped helper
with `0.02` acceleration and `0.2` damping. The retained horizontal
pulse/release traces are reproduced by the recovered `0.022689279` heading
scale, including the continued same-direction release tail. Vertical input has
a second `+/-0.3` dead zone and remaps the remaining travel across a
`0.69813168` rad span.

## Recovered unobstructed chase framing and follow

The same authority savestate was replayed through five ordinary, unobstructed
scenarios: fixed heading, straight movement, left-stick turning, long idle and
forward follow after a turn. Together they retain 950 frame-advanced samples.
Separate retained stationary left/right right-stick pulse movies now cover the
manual producer and release inertia. The obstruction contract below comes from
the loaded overlay plus a fixed-state clear-line control, not a retained
physical wall-contact movie.

At stationary fixed-heading/idle equilibrium, the final eye is `5.999955`
planar units from Ratchet, `2.005249` units above his origin, with pitch
`0.08400285` rad. Projecting the recovered forward basis back to Ratchet's XY
places the stationary look height at filtered-target Z `+ 1.500083`. Treat
these as ordinary stationary framing witnesses, not universal constants.

Across all representative movement cases the eye-to-Ratchet planar ray remains
locked to `controlHeading` to sub-microradian precision. The forward basis is
also consistent with:

`forward = (cos(yaw)*cos(pitch), sin(yaw)*cos(pitch), -sin(pitch))`

There is therefore no evidence in this matrix for a separately lagged ordinary
eye azimuth. Movement-follow instead changes camera translation, radial
separation and pitch while yaw follows the recovered control heading. Straight
movement reaches roughly `5.843..6.480` planar units and per-update planar eye
steps up to `0.103484`; turn/follow cases widen the radial excursion further.
Do not replace this with a fitted single exponential coefficient: that model
does not explain the observed radial behavior.

The ordinary follow producer at `0x001eca70..0x001ecde8` is stronger evidence.
Its ordinary branch copies player X/Y directly into `0x00166e10/+0x04`, while
Z is advanced by helper `0x001eb240..0x001eb320` using a stored velocity:

`v += 0.0075 * (targetZ - currentZ) - 0.175 * v`

The helper clamps the step against overshooting the remaining Z delta, then
returns `currentZ + v`. Replaying that law against the three moving traces
reproduces the retained filtered Z to within about `2e-6` world units per
sample. The producer also has a distinct alternate path gated when
`state+0x2284 == 0x50` and `state+0x2084 != 0x11`; consequently the recovered
ordinary constants must not be promoted as all-mode camera constants.

The horizontal framing producer is now traced far enough to replace that
curve-fit boundary with executable dataflow. Main update `0x001ed0a8..0x001ed2cc`
first runs the filtered-player follow producer, then dispatches the active
camera object through `0x001eba20..0x001ebb30`. The authority state points
`0x00166e00` at object `0x00167290`, whose type halfword is `0`; dispatch entry
`0x001ea880` selects init callback `0x002e6d60` and update callback
`0x002e9c28`. The object's `+0x30` eye equals global eye `0x00166dc0` exactly
in the authority state, and the main update publishes that object eye after the
type-specific update.

Type 0 owns persistent state through object `+0x70`; the authority witness is
`0x00169110`. In the unobstructed stationary state, `state+0x90` is the eye
anchor and `state+0x140` is the radial offset. Their vector sum reproduces the
global eye within `9.8e-6` units. The offset magnitude is `5.999962`, while
`state+0x15c` holds preferred radius `5.999970`; `state+0x200`, subtracted from
the preferred radius before the radial step, is zero in this unobstructed
witness.

The radius is explicitly two-stage. `0x002e5e38..0x002e5ffc` damps
`state+0x15c` toward a profile or temporary override source using
`state+0x174` velocity and `state+0x178` acceleration; the latter is `0.003` in
the authority state. `0x002e9720..0x002e9a9c` then damps the magnitude of
`state+0x140` toward `state+0x15c - state+0x200`, storing radial velocity at
`state+0x158`. Its acceleration/damping pair is selected by `0x002e9518`, so a
single global horizontal smoothing constant is not justified. The routine
finally composes camera object `+0x30` from the resolved anchor plus
`state+0x140` through vector-add helper `0x001ff278`.

Height is layered similarly. `state+0x160` is the preferred eye height; its
profile transition uses `state+0x180` velocity and `state+0x184 = 0.003`
acceleration. The final ordinary height pass at `0x002e9010..0x002e944c`
damps current eye height `state+0x28` toward `state+0x160`, with velocity at
`state+0x30`, acceleration `0.004` and damping `0.2`. The stationary witness is
`1.999987` current versus `1.999993` preferred. Current look height at
`state+0x24` is `1.500004`, with its own velocity at `state+0x2c`.

Constructor `0x002e6d60` seeds radius `4.64`, eye height `2.0`, and profile
transition acceleration `0.003`. Do **not** treat `4.64` as the ordinary chase
distance: the same authority snapshot has preferred radius near `6.0`, while a
separate profile witness at `0x00169610+0x15c` still reads `4.64`. The
intervening profile/mode source and the GP-relative damping used by the
preferred-radius transition remain evidence-gated. This is exactly why chase
framing should be implemented from state transitions rather than one tuned
distance constant.

## Recovered obstruction response

Type-0 core `0x002e9bb0` calls obstruction/contact routine
`0x002e7d20..0x002e8368` immediately before radial follow
`0x002e9720..0x002e9a9c`. The obstruction stage therefore computes persistent
camera correction first; ordinary framing then consumes
`effectiveRadius = state+0x15c - state+0x200`. In the fixed clear-line authority
state, preferred radius is `5.999970`, radial correction `state+0x200` is zero,
release timer `state+0x204` is zero, and signed lateral side `state+0x208` is
zero.

The active pull-in branch subtracts `0.075 * frameScale` from its radial working
value per camera update. The fixed authority has `frameScale = 1.0` at
`0x0015ed60`. That branch arms `state+0x204` with `0x884` through native time
helper `0x001fef20`; the already-retained helper calibration makes this exactly
`2180` native ticks at unit time scale. The inner solver uses a `0.2` radial
floor. A later final guard enforces
`preferredRadius - correction >= 1.5`; when that guard clamps, it arms a
`0x7d0`, or `2000`-native-tick, release timer.

Clear-line release has a distinct slow-path rather than mirroring pull-in. On
the recovered no-contact arm, signed-16 helper `0x001fef98` decrements
`state+0x204`, the remaining timer is divided by the `2000`-tick duration, and
`0x00257e58` updates `state+0x200` with
`CosineEase(0, currentCorrection, remaining/duration)`, where the helper is
`a + (b-a) * 0.5 * (1 - cos(pi*t))`. Because the current correction is fed back
as the interpolation endpoint, this is a stateful recursive release, not a
single linear lerp. A controlled clear-state perturbation confirmed that a bare
synthetic `state+0x200` correction with no matching contact state is rejected by
the native obstruction stage; it is retained only as a control and not treated
as a physical wall trial.

Lateral handling is also explicit but only partially closed. Helper
`0x002e7318` classifies a signed side field at `state+0x208`: dot values above
`+0.75` select `+1`, below `-0.75` select `-1`, otherwise `0`. A qualifying
contact branch then invokes rotation helper `0x0025fc08` with exactly
`+/-0.017453292` rad (`+/-1 degree`) chosen from contact orientation. This proves
a native lateral correction mechanism, but without a retained physical
obstruction route the full multi-update lateral convergence law is not promoted.

The final visible world-contact call is `0x001efc70`. At this call site the
camera passes two position pointers plus query context and an owner/exclusion
object; there is no explicit scalar camera-radius argument. That is compatible
with a segment-style query at this layer, but the helper internals are not yet
decoded far enough to distinguish an infinitesimal ray from swept or expanded
geometry. Nearby/contact candidate construction through `0x00259888` and
`0x00259a88` is therefore kept separate from the final geometric assumption.

This camera collision path is distinct from gameplay collision. During the
camera pass, the routine temporarily clears candidate Moby field `+0x94` for
selected objects, retains those objects in a scratch list, and restores the
field before return. Eight hard-coded class IDs bypass that temporary masking:
`0x72`, `0x0b`, `0x2c`, `0x9a`, `0xff`, `0x392`, `0x452`, and `0x353`. The
field's broader gameplay meaning is not inferred here; the important proven
boundary is that this mutation is scoped inside camera obstruction processing.

## Semantic camera field map

The recovered eye, forward basis, pitch, filtered target and raw player-copy
addresses above are now built-in semantic fields in the harness. They no longer
need an external field-map file. `--field-map` remains available only for new
candidate fields that have independent executable or controlled-runtime
provenance; it cannot override a promoted built-in name.

The default candidate scan remains useful for discovery, but candidate words
are never treated as semantic fields solely because they sit near the camera
structure.

## Runtime contract boundary

The promoted engine-neutral hand-off now lives under `OBP.Runtime.Camera`.
`RuntimeCameraState` carries only an OBP Y-up eye/forward pose, a world-up
control heading and preferred/effective distance. `RuntimeCameraObstructionProbe`
contains only two endpoints and deliberately does not choose ray versus swept
geometry; `RuntimeCameraObstructionFacts` returns contact plus the signed
orientation dot consumed by source-game camera rules.

The Godot host now has a matching consumption seam rather than another camera
model. `RuntimeCameraSceneAdapter` performs only the established OBP-to-Godot
handedness correction. `DebugPlayer.ApplyRac1CameraState` lets a recovered
source controller atomically own visible pose plus movement control-heading and
planar basis; F9 deliberately restores the old host chase camera as a development
fallback. Capture telemetry exposes only mode, heading and preferred/effective
distance, not retail memory payloads.

`OBP.RAC1.Camera.Rac1CameraState` retains the recovered native Z-up control,
follow, framing and obstruction fields. `Rac1CameraRecurrence` implements only
producer stages established above: the `I+0x1a0` heading-step recurrence, the
ordinary vertical-follow and final eye-height damped steps, radial working-value
pull-in, lateral-side classification / one-degree correction, recursive clear-line
cosine release, and the final `1.5` effective-radius guard.

`Rac1OrdinaryCameraController` now owns the normal free-camera type-0 state used
by Godot: recovered right-stick damping and release inertia, the retained
stationary framing/profile witness, vertical chase follow, radial response and
obstruction state. The Godot physics query supplies only contact/orientation
facts; the source-game recurrence decides pull-in, release and lateral response.
Normal R&C1 play consumes this source state after movement each physics update,
so movement reads the previously published control heading just as the retained
PINE ordering witnesses do. The old yaw/pitch/distance camera remains reachable
only through the explicit F9/fly development escape path.

The remaining ordinary boundary is narrower. Neutral right-stick input does
not imply an always-on recenter writer: the fallback command at
`state+0x1c4` is cleared by the ordinary update and its explicit producer route
is conditional on low right-stick magnitude, a long dwell and a valid target
pointer. All retained ordinary free-camera witnesses have that target pointer
null, so normal neutral play intentionally performs no synthetic recenter.

The active profile pointer is `0x00168fc0`; profile fields feed look height,
preferred radius and preferred eye height, while short-lived mode overrides can
replace those targets before consumption. The radial selector has recovered
baseline/fast coefficient families and a retained transition state. The
ordinary runtime uses the retained settled type-0 authority profile and keeps
selector-specific modes outside that baseline until their gameplay trigger is
independently exposed. Exact ray-versus-swept contact geometry and full
multi-update lateral convergence remain intentionally unpromoted.

## Human integration review: vertical framing

A short human comparison on 2026-09-21 against the base game found the recovered
horizontal camera/control feel acceptable, but identified a clear vertical
presentation mismatch: retail feels framed toward a point above Ratchet's head,
whereas the current Godot path visibly tilts the view up/down around the eye.
This is integration evidence, not a numeric retail-authority witness.

The implementation boundary explains why this needs recovery rather than tuning.
`Rac1OrdinaryCameraController` currently maps manual vertical state directly into
`Control.Pitch` while leaving the recovered eye, eye anchor and look-height state
unchanged; `Rac1CameraState.ToRuntimeState()` then rotates only the published
forward vector. No retained retail trace proves that pitch-only projection.
Do not hand-tune the vertical span or presentation offsets to hide the mismatch.
A bounded follow-up should capture vertical right-stick pulses while sampling eye,
forward/pitch, current/preferred look height and current/preferred eye height, then
trace the corresponding type-0 writers to determine whether manual vertical input
drives look height, eye height, both, or another orbit/framing state.

## Repeatable scenarios

`tools/rac1-camera-archaeology.py` builds the following literal DualShock 2
movies from fixed savestates:

1. `fixed-heading`: stationary no-input stability baseline.
2. `moving`: neutral settle followed by sustained forward movement.
3. `turning`: forward prelude followed by a player-facing right turn with a
   neutral right stick.
4. `idle`: long all-neutral chase/follow drift witness.
5. `manual-idle-left`: stationary right-stick-left pulse and release tail.
6. `manual-idle-right`: stationary right-stick-right pulse and release tail.
7. `recenter`: sustained forward movement, a horizontal right-stick-left
   pulse, then a long neutral-right-stick recenter tail.
8. `turn-release`: deliberate left-stick turn followed by forward movement
   with a neutral right stick.
9. `obstruction`: forward approach and idle tail from a dedicated obstruction
   anchor savestate.

The first eight scenarios use one common authority savestate. The obstruction
case accepts a separate `--obstruction-savestate` so the suite does not waste
capture time walking a flat anchor to a wall. If that state is omitted, the
tool marks only the obstruction case missing rather than silently substituting
a different scenario.
## Raw and derived boundaries

Raw `.p2m2` movies, companion `.p2s` states and per-frame PINE captures must
remain below ignored `captures/`. A raw row includes the literal left/right
stick bytes, known native fields, explicitly mapped camera fields and raw words
from the candidate ranges.

The committed reducer keeps only:

- scenario/movie/state hashes and sample cadence;
- literal/conditioned left- and right-stick summaries plus direction-flag paths;
- control-heading, player yaw, movement target and per-segment angular deltas;
- camera/control-basis consistency against `sin/cos(controlHeading)`;
- active-stick error against the recovered camera-relative target-yaw formula;
- min/max/range/change-count summaries for recovered camera fields;
- chase framing summaries for planar distance, eye height, pitch, forward-basis
  error, eye-ray yaw error, filtered-Z lag and per-update planar camera step;
- executable-backed vertical-follow residuals against the `0.0075/0.175` law;
- candidate address change counts and plausible-float ranges;
- a cross-scenario shortlist when a candidate changes more under recenter,
  turning or obstruction than the corresponding neutral baseline.

It does not copy candidate raw words or per-frame camera payloads into the
derived report.

## Commands

```text
py -3.12 tools/rac1-camera-archaeology.py scenarios
py -3.12 tools/rac1-camera-archaeology.py probe-producer --savestate <authority.p2s> --zstd-dll <zstd.dll>
py -3.12 tools/rac1-camera-archaeology.py capture --pid <pcsx2-pid> --pine-port 28099 --savestate <authority.p2s> --obstruction-savestate <obstructed.p2s> --field-map <camera-fields.json> --out-dir captures/rac1-camera
py -3.12 tools/rac1-camera-archaeology.py derive --capture-dir captures/rac1-camera --out research/generated/rac1-camera-control-heading.json
py -3.12 tools/rac1-camera-archaeology.py derive-chase --capture-dir captures/rac1-camera --out research/generated/rac1-camera-chase-framing.json
```

The built-in semantic fields above are justified by the executable/runtime
witnesses retained in this note and may be reduced into committed payload-free
JSON. Any additional `--field-map` addresses still require independent
provenance before their semantic labels are committed. Raw captures stay local.
