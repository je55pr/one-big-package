# R&C1 camera archaeology harness

Authority: NTSC-U retail `SCUS-97199`. The capture harness remains generic,
but this note now also retains the ordinary horizontal camera/control-heading
contract proven by the fixed savestate, loaded-overlay signatures and
frame-advanced input movies. Unresolved producer stages are called out rather
than inferred.

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
`WrapPi(controlHeading - step)` through helper `0x00200130`. This is only
one producer stage: live right-stick motion changes `controlHeading` while
`I+0x1a0` continues to report the left-stick forward flag, so the exact
right-stick-to-camera producer remains unresolved elsewhere in the overlay.

## Semantic camera field map

When executable tracing or a controlled live witness establishes camera fields,
pass a small JSON map to `--field-map`. Names beginning with `camera_` are
reported separately in the payload-free reduction. A position/orientation map
can use names such as:

```json
{
  "schema": 1,
  "fields": {
    "camera_position_x": {"address": "0x00123400", "kind": "f32"},
    "camera_position_y": {"address": "0x00123404", "kind": "f32"},
    "camera_position_z": {"address": "0x00123408", "kind": "f32"},
    "camera_forward_x": {"address": "0x00123410", "kind": "f32"},
    "camera_forward_y": {"address": "0x00123414", "kind": "f32"},
    "camera_forward_z": {"address": "0x00123418", "kind": "f32"}
  },
  "candidateRanges": [
    {"start": "0x00166c00", "bytes": 1024}
  ]
}
```

The addresses above are schema examples only. They are intentionally not
shipped as a field-map file because no retained evidence in this branch proves
those example addresses.
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
- min/max/range/change-count summaries for explicitly mapped camera fields;
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
```

The built-in semantic fields above are justified by the executable/runtime
witnesses retained in this note and may be reduced into committed payload-free
JSON. Any additional `--field-map` addresses still require independent
provenance before their semantic labels are committed. Raw captures stay local.
