# R&C1 Moby animation / sound correlation

Status: retained static-only findings for the pinned NTSC-U authority
`rac1-ntscu-original` / `SCUS-97199`.

This note records payload-free structural and executable correlations recovered by
w353. No PCSX2/PINE session was required. Animation and sound presentation alone
must not be promoted into gameplay, attack, NPC, or mission semantics.

## Native sequence and class fields

For an ordinary Moby class payload:

| Location | Recovered meaning |
|---|---|
| class `+0x0d` | number of class-local sound definitions |
| class `+0x28` | pointer/offset to the class-local sound table |
| sound row stride | `0x20` bytes |
| sequence `+0x11` | class-local sound ID; `0xff` means none |
| sequence `+0x12` | timed sound-trigger word count |
| sequence `+0x14` | opaque; not consumed by the generic timed-sound path |

`Rac1MobyAnimation` now exposes the class-local sound-table count/offset and
row offsets without interpreting the 0x20-byte sound-definition contents.
Timed words follow the sequence frame-pointer list. Their executable decode is:

`high16 = animation position in 1/16-frame units`
`low16  = class-local sound ID`

The parser exposes both the raw scalar word and the decoded timed cue. This
channel is presentation/audio timing. It is not a generic gameplay-event list.

Sequence field `+0x14` remains intentionally opaque. Across the full authored
census it is nonzero in only four sequence occurrences, all on level 4 and only
for classes 217 and 563. No semantic name is assigned from that rarity.

## Executable witnesses

The retained SCUS-97199 witnesses are:

- `0x0020c880`: binding a sequence copies sequence `+0x11` to
  live `Moby+0x7c` and sequence `+0x12` to `Moby+0x7e`.
- `0x0020c940`: maintains sequence-bound sound lifetime through
  `0x0022da68` and `0x0022d798`.
- `0x0022da68`: bounds-checks the class-local sound ID against
  class header byte `+0x0d`, then indexes the table at class `+0x28`
  using a `0x20`-byte stride.
- `0x0020d790..0x0020d81c`: crosses timed animation positions, decodes
  high16 position / low16 sound ID, and launches the sound through the same
  `0x0022da68` path.
These witnesses correct the former `SoundCount` interpretation of sequence
byte `+0x11`. The count belongs to the class header, not the sequence.

## Whole-game static acceptance boundary

Across all 19 authored level payloads:

| Measure | Retained total |
|---|---:|
| payload-bearing class occurrences | 2,968 |
| populated ordinary sequences | 10,745 |
| timed sound words | 1,960 |
| distinct classes with sound tables | 456 |
| distinct classes with direct sequence sound IDs | 49 |
| distinct classes with timed sound cues | 89 |
| classes using both direct and timed mechanisms | 21 |
| out-of-range direct sound references | 0 |
| out-of-range timed sound references | 0 |

Among the 813 instantiated authored classes, the corresponding feature counts
are 371 classes with sound tables, 47 with direct sequence sound IDs, and 80
with timed cues. Retail-gated tests rebuild these counts through the production
disc/class/animation codecs rather than checking in retail bytes.
## Representative class-family correlations

| Numeric family | Payload-free presentation result | Evidence boundary |
|---|---|---|
| 749 | 53 joints, 8 populated sequences, 9 sounds on levels 0 and 18. Independently selected sequence 5 has timed cues at 4.0 frames / sound 6 and 10.0 frames / sound 0. | The independently recovered gameplay attack marker is marker 34 and remains separate from these sound cues. |
| 1440 | 35 joints, 13 populated sequences, 11 sounds. Sequence 6 directly selects sound 5 and sequence 9 sound 10; other sequences also carry timed cues. | No behavioral/attack meaning is inferred because selector mapping is not independently established. |
| 572 / 865 / 866 | Shared 17-state updater but respectively 77/75/78 joints, 4/6/7 populated sequences, 6/7/7 sounds. Sequence 1 directly selects sound 4/5/5 and the timed schedules differ. | Shared update code does not imply identical presentation or semantics. |
| 1781 / 1782 | Each has 13 joints, 2 populated sequences, no class sounds and no timed cues. | Only 1781 has independent selector evidence: proximity plus player motion requests sequence 1; animation flag 0x02 returns sequence 0. 1782 is not admitted by structural similarity. |
| 500 / 501 / 502 / 505 / 511 | Zero joints, one one-frame sequence each, sound-table counts 1/2/2/2/1, no direct or timed animation-linked sounds. | Any use of those class sounds is behavior-code driven, not animation metadata. |
| 224 / 228 | One-frame sequence, no animation-linked sounds. | Updater sharing alone does not add semantics. |
| 758 / 803 | 758 has one joint and a 9-frame sequence; 803 has zero joints and a one-frame sequence. Both own one sound definition and neither links it through animation metadata. | Structural difference is retained without assigning a behavior name. |
The 572/865/866 sound-definition rows are stable across their repeated level
occurrences while their texture IDs are level-local. This is another reason not
to conflate stable class presentation structure with per-level resource numbers.

## Payload-free boundary

Committed material contains addresses, numeric class IDs, counts, offsets,
strides, decoded scalar cue positions/IDs used by deterministic tests, and
evidence-boundary prose only. It contains no executable bytes, sound-definition
payload rows, model/animation payload bytes, textures, PVars, or disc ranges.

The tests deliberately validate numeric presentation correlations without
turning animation shape, clip length, updater identity, or sound timing into
behavioral names.
