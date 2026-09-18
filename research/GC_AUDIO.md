# Going Commando native audio formats

**Build:** `rac2-ntscu-v1.01` (`SCUS-97268`, SHA-256
`9db2e33e276133cc283647fa3279b37911955e123d6199d10065547eaa9b1ce5`).

This note records the minimum native audio slice needed for level music/ambience
and ordinary one-shot effects. Retail audio bytes remain private inputs. The
committed implementation contains parsers, metadata, synthetic codec tests and
retail-gated scalar assertions only.

## Recovered path

```text
environment sample music_track
  -> /G/AUDIO<n>.WAD bin pair
  -> two mono VAGp elementary streams (left/right)
  -> PS2 SPU ADPCM frames

native SBlk sound id
  -> SFX record
  -> grain list
  -> tone grain (types 1 or 9)
  -> tone.SampleOffset in the bank sample-data chunk
  -> END-terminated mono PS2 SPU ADPCM sample
```

The first path is sufficient to locate and decode the two elementary streams for
the selected level music variant. The second maps a native level-bank sound
index to directly referenced playable sample data. Event dispatch, complete
sound-remap semantics, mixer behavior and playback hosting remain separate work.

## Per-level `AUDIO<n>.WAD`

The selected build uses a `0x1018`-byte header:

| Offset | Field |
|---|---|
| `0x0000` | `s32 header_size` = `0x1018` |
| `0x0004` | `s32 sector` (zero in the observed file) |
| `0x0008` | 511 `SectorByteRange` entries |
| `0x1000` | upgrade sample `SectorByteRange` |
| `0x1008` | Thermanator freeze `SectorByteRange` |
| `0x1010` | Thermanator thaw `SectorByteRange` |

Each `SectorByteRange` is `{ s32 sector; s32 size_bytes; }`; byte offset is
`sector * 2048`. `GcLevelAudioWad` validates every populated range against
the bounded file reader and never scans outside a declared extent.

Direct retail witness for `/G/AUDIO0.WAD`:

| bin | sector | bytes | VAG name | VAG payload bytes |
|---:|---:|---:|---|---:|
| 0 | 3 | 372,272 | `rc2_l00_main_al` | 372,224 |
| 1 | 185 | 372,272 | `rc2_l00_main_ar` | 372,224 |
| 2 | 367 | 2,233,200 | `rc2_l00_main_bl` | 2,233,152 |
| 3 | 1458 | 2,233,200 | `rc2_l00_main_br` | 2,233,152 |

The three special ranges are upgrade `2549 + 78,944`, freeze
`2588 + 67,312`, and thaw `2621 + 79,744`.

### Environment selector mapping

`GcUyaDlEnvSamplePointPacked + 0x0c` is the signed 16-bit `music_track`
selector. LEVEL1/Oozla contains ten environment samples with the retail sequence:

```text
0, 0, 0, 4, 0, 0, 0, 4, 4, 0
```

In the matching `AUDIO0.WAD`, selector `0` resolves bins `0/1`
(`main_a` left/right) and selector `4` resolves bins `2/3`
(`main_b` left/right). The recovered GC rule used by
`GcLevelAudioWad.ResolveMusicPair` is therefore:

```text
left_bin  = (music_track / 4) * 2
right_bin = left_bin + 1
```

The bounded implementation rejects negative selectors, non-multiples of four,
and pairs whose native ranges are absent.

## Global `AUDIO.WAD`

The global file uses a `0x1800`-byte sector catalogue:

| Offset | Entries | Group |
|---|---:|---|
| `0x0008` | 254 | vendor |
| `0x0400` | 256 | help English |
| `0x0800` | 256 | help French |
| `0x0c00` | 256 | help German |
| `0x1000` | 256 | help Spanish |
| `0x1400` | 256 | help Italian |

The NTSC-U authority has 215 populated vendor entries and 144 populated English
help entries; the four other language tables are zero. Entries store a start
sector only, so the bounded extent is from that start to the next populated
sector, or to the containing file end for the final entry. The first observed
vendor entries are valid 44.1 kHz `VAGp` streams.

## VAGp elementary streams

`OBP.PS2.Audio.Vagp` owns only the generic PS2 elementary-stream layer.
Observed Going Commando streams have this `0x30`-byte header:

| Offset | Encoding | Meaning |
|---|---|---|
| `0x00` | ASCII | `VAGp` |
| `0x04` | u32 BE | version, `0x20` here |
| `0x0c` | s32 BE | encoded data bytes |
| `0x10` | s32 BE | sample rate |
| `0x1e` | u8 | raw channel-count byte |
| `0x20` | char[16] | stream name |

All four AUDIO0 music VAGs declare 44,100 Hz. Their raw channel-count byte is
zero. Retail naming and equal-sized left/right pairs establish that each file is
one mono elementary stream, so the GC parser normalizes zero to one channel and
keeps stereo pairing in the game-specific catalogue rather than in the VAG
codec.

`Vagp.DecodeMono` decodes the complete declared VAG payload. It does not stop
at the first SPU END marker because the observed music payload includes explicit
trailer markers. `AnalyzeFrameMarkers` exposes marker locations without
assigning higher-level loop semantics.

For AUDIO0 bin 0/1, each payload has 23,264 ADPCM frames:
- END markers at frames 23,262 and 23,263;
- LOOP_START at frame 23,263;
- frame 23,263 carries END + REPEAT + LOOP_START.

For bin 2/3 the same trailer shape appears at frames 139,570 and 139,571.
This is reliable raw loop-marker information, but it is **not** sufficient to
claim that frame 23,263 or 139,571 is a useful music-loop destination. No
separate semantic music-loop point was recovered in this bounded pass.

## PS2 SPU ADPCM

`OBP.PS2.Audio.Ps2SpuAdpcm` implements the shared elementary sample codec.
One frame is 16 bytes and yields 28 signed 16-bit PCM samples:

```text
byte 0: high nibble = predictor/filter, low nibble = right shift
byte 1: flags (END=1, REPEAT=2, LOOP_START=4)
byte 2..15: 28 signed 4-bit samples, low nibble first
```

The five predictor coefficient pairs are
`(0,0), (60,0), (115,-52), (98,-55), (122,-60)`, with predictor history
scaled by 64. Invalid filter/shift headers are rejected.

There are two deliberately separate APIs:
- `DecodeFrames` decodes an exact whole-frame extent and is used for complete
  VAG payloads;
- `AnalyzeSample` / `DecodeSample` stop at the first END frame and are used
  for raw 989snd sample voices whose containing bank chunk has no per-sample
  byte-length table.

## 989snd level sound bank

GC level-WAD sound-bank lumps are two-chunk 989snd files. LEVEL0 slot 1 gives
the deterministic witness:

```text
file type       3
chunk count     2
metadata        offset 24,    size 19,484
sample data     offset 19,508, size 1,482,000

metadata magic  SBlk
SBlk version    1
flags           0x4
bank id         0x00574144
bank number     0
sounds          192
grains          428
VAG voices      209
FirstSound      0x3c
FirstGrain      0x93c
VagDataSize     1,482,000
```

The selected build's version-1 SFX entry is 12 bytes:

```text
s8  volume
s8  volume_group
s16 pan
s8  num_grains
s8  instance_limit
u16 flags              bit 0 is the SFX loop flag
u32 first_grain_offset relative to FirstGrain
```

A version-1 grain is `0x28` bytes: `u32 type`, `s32 delay`, then a
`0x20`-byte payload. Grain types 1 and 9 carry the recovered `0x18` tone:

```text
s8  priority
s8  volume
s8  center_note
s8  center_fine
s16 pan
s8  map_low
s8  map_high
s8  pitch_bend_low
s8  pitch_bend_high
u16 adsr1
u16 adsr2
u16 flags
u32 sample_offset
u32 reserved
```

`sample_offset` is a byte offset into the second outer-file chunk. Across
LEVEL0, the 192 sounds reference exactly 209 unique aligned sample offsets,
matching the SBlk `NumVAGs=209` declaration.

### Native one-shot witness

Native SBlk sound ID 0 in the LEVEL0 bank has one tone grain. Its tone points to
sample offset `0x000000`. Scanning from that exact offset reaches END after 475
ADPCM frames, so the playable encoded extent is 7,600 bytes and one linear pass
contains 13,300 PCM samples. The final frame has END only and the voice does not
carry a raw repeat marker.

This is the recovered mapping required for the first ordinary one-shot path:

```text
SBlk sound id 0
  -> tone grain
  -> SampleOffset 0
  -> 7,600 encoded bytes
  -> 475 SPU ADPCM frames
  -> 13,300 mono PCM samples
```

The bank also contains sounds whose SFX flags have bit 0 set, proving a
bank-level loop-intent field independently of raw sample frame markers.
`GcSoundBank` preserves both forms: `Sound.Loop` for the SFX flag and
`SampleInfo` for raw SPU marker metadata.

### Rate metadata for one-shot tones

Raw SBlk sample bytes do not carry an independent Hz field. 989snd tone playback
derives pitch from the tone's signed center-note and center-fine fields plus the
block sound's playback note/fine state. The parser therefore preserves
`CenterNote`, `CenterFine` and pitch-bend limits as native metadata rather
than converting them to a guessed floating-point sample rate.

Public 989snd evidence distinguishes the PS1-compatible 44.1 kHz pitch basis
from the PS2 48 kHz basis using the center-note sign, but this bounded GC pass
does not reimplement or claim bit-exact native pitch-register quantization.
That conversion belongs with later playback/runtime work if executable evidence
requires it.

This is tone playback metadata, not a claim that the raw ADPCM sample itself has
a self-describing sample rate.

## Sound-remap boundary

`LevelCoreHeader.sound_remap_offset` remains a useful mapping lead, not a
decoded semantic table. On LEVEL0 it is `0x6520` inside the `coreIndex`;
`moby_sound_remap_offset` is zero. The pinned Wrench snapshot identifies an
8-byte `SoundRemapHeader` and 4-byte elements, but Wrench itself exports this
family as opaque binary data and does not document the semantic lookup rules.

This task therefore does not promote those fields into a runtime remap API.
The minimum ordinary path is already deterministic from the native SBlk sound
index to its tone/sample data. Event-to-sound remapping remains separate
archaeology and should be recovered from executable dispatch before use.

## Implementation boundary

- `OBP.PS2.Audio.Ps2SpuAdpcm`: generic SPU ADPCM frames, sample END/loop
  markers and PCM decoding.
- `OBP.PS2.Audio.Vagp`: bounded generic VAGp header, encoded extent,
  rate/channel metadata and raw frame-marker census.
- `OBP.RAC2.Audio.GcLevelAudioWad`: GC numbered audio catalogue and native
  environment music-pair resolution.
- `OBP.RAC2.Audio.GcGlobalAudioWad`: GC global sector catalogue.
- `OBP.RAC2.Audio.GcSoundBank`: selected GC 989snd SBlk v1 catalogue,
  grains/tones, sample extents and native sound-index mapping.
- `OBP.RAC2.Level.GcInstances.EnvSample.MusicTrack`: preserved native
  environment selector.

Unknown SBlk versions are rejected. Unknown control-grain semantics are
preserved only as type/delay and are not emulated. No parser copies an entire
AUDIO WAD merely to inspect a header; payload reads are bounded to a selected
VAG/sample when decoding is requested.

## Provenance

Direct retail evidence comes from bounded reads against the verified authority
build above. No retail audio bytes are committed.

Public structural leads were checked against:
- Wrench `chaoticgd/wrench` commit
  `e67ea46e5e60ec52a0c31fcde1e3f46b20aa4cfb`, especially
  `src/wrenchbuild/level/level_audio_wad.cpp`,
  `src/wrenchbuild/globals/audio_wad.cpp`,
  `src/engine/vag.h`, and `src/wrenchbuild/level/level_core.h`.
- OpenGOAL `open-goal/jak-project` commit
  `d04ac9d787be16e75ca1023b93efc8ad1a54d71c`, especially the 989snd loader,
  SFX/grain/tone definitions, pitch utility and shared SPU voice decoder.

Wrench is used under the repository's external-source policy as a GPL research
lead only. OBP implementation code was written from format facts plus direct
retail and synthetic tests, not copied or mechanically translated from Wrench.
OpenGOAL supplied independent corroboration for 989snd/SPU behavior; direct
selected-build probes establish the GC values used by the tests.

## Verification

Payload-free tests construct VAGp, SBlk and SPU ADPCM data in memory and cover:
- big-endian VAG metadata and the selected-build zero-channel normalization;
- complete VAG-frame decoding and raw loop-marker census;
- environment selector to left/right VAG-pair indexing;
- SBlk sound -> tone -> sample mapping;
- ADPCM predictor decoding and loop/end metadata.

The retail-gated witness checks, without committing payloads:
- the four populated AUDIO0 bin ranges and their stream names;
- selectors 0 and 4 resolving to the `main_a` and `main_b` pairs;
- 44.1 kHz mono elementary stream metadata;
- raw trailer-marker frame positions for both music pairs;
- global vendor/help catalogue counts;
- LEVEL0 SBlk counts and the sound-0 7,600-byte sample extent.

The environment parser's existing retail baseline also asserts Oozla's exact
ten-value `music_track` sequence shown above.

## Deliberate exclusions

This slice does not implement playback, mixing, reverb, dialogue routing,
cutscene/FMV audio, event dispatch, complete sound-remap semantics, every SBlk
grain opcode, or R&C1/UYA containers. It also does not turn raw VAG trailer
flags into an invented music-loop policy. Those behaviors require their own
native evidence.
