# R&C1 camera archaeology harness

Authority: NTSC-U retail `SCUS-97199`. This note defines the capture boundary only.
It does not promote a chase-camera implementation or assign semantics to an
unproven RAM word.

## Known live fields

The camera harness reuses the deterministic PCSX2 input-recording and PINE
machinery from `tools/rac1-analogue-movement-harness.py`. Every emulated frame
is advanced once, then sampled once with a single batched PINE read.

The fixed fields are already supported by retained R&C1 evidence:

- player state yaw: `0x0013f3d0 + 0x18`
- live Ratchet Moby position: `0x01845e80 + 0x10/+0x14/+0x18`
- live Ratchet Moby yaw: `0x01845e80 + 0x48`
- ordinary control-heading source: `0x00166dd8`
- conditioned native right stick: `I+0x100/+0x104`, where `I=0x0013c940`
- literal right-stick bytes are also retained per movie frame

The default candidate scan is `0x00166c00..0x00166fff`. That span is a
discovery aid around the known control-heading field, not a claim that every
word in it belongs to a camera structure.
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
5. `recenter`: sustained forward movement, a horizontal right-stick-left
   pulse, then a long neutral-right-stick recenter tail.
6. `obstruction`: forward approach and idle tail from a dedicated obstruction
   anchor savestate.

The first five scenarios use one common authority savestate. The obstruction
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
- right-stick command path and conditioned native right-stick summaries;
- control-heading and player yaw/position ranges;
- min/max/range/change-count summaries for explicitly mapped camera fields;
- candidate address change counts and plausible-float ranges;
- a cross-scenario shortlist when a candidate changes more under recenter,
  turning or obstruction than the corresponding neutral baseline.

It does not copy candidate raw words or per-frame camera payloads into the
derived report.

## Commands

```text
py -3.12 tools/rac1-camera-archaeology.py scenarios
py -3.12 tools/rac1-camera-archaeology.py capture --pid <pcsx2-pid> --pine-port 28099 --savestate <authority.p2s> --obstruction-savestate <obstructed.p2s> --field-map <camera-fields.json> --out-dir captures/rac1-camera
py -3.12 tools/rac1-camera-archaeology.py derive --capture-dir captures/rac1-camera --out research/generated/rac1-camera-archaeology.json
```

A derived JSON file should only be committed after its semantic field map and
savestate provenance are independently justified. Until then, the harness is
the retained artifact and the raw captures stay local.
