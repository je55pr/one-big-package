# UYA retail tfrag compatibility census

Status: retail-confirmed compatibility evidence for the existing GC tfrag/VIF parser across four sampled UYA worlds. Native-loader provenance and the public-derived semantic names/ranges remain separate questions.

## Authority and runs

Retail authority image:

- Ratchet & Clank: Up Your Arsenal NTSC-U original
- serial `SCUS-97353`
- size `4,379,377,664` bytes
- full-image SHA-256 `d2bb15c7c5b2205db868713fc0362c2b10e87751ca5bcc4e96c1e244a8c42444`
- bounded ToC authority window: LBA 1001, 2 MiB, SHA-256 `a9e3e338df29045222ab0c0c0ba68fe1cff2048add582124f51915bb4ba55e5a`

The original row-1 HTTP-range probe was GitLab pipeline `2826869643` / iid `281`, job `16349241800`.

The local four-row census was pipeline `2827215671` / iid `330`, job `16351810256`, executed on the project-scoped `obp-local` runner against the canonical full local retail ISO using bounded random-access reads.

The local probe reproduced row 1 exactly and added rows 8, 20, and 50.

## Census

| Table row | Tfrag extent bytes | Tfrag SHA-256 | Declared/parser candidates | Vertices | Triangles | Public tfrag textures | Texture IDs observed |
|---:|---:|---|---:|---:|---:|---:|---|
| 1 | 2,220,864 | `dad150c2b1e1582a8339ff2a9aedba398829be27a836967d3e4f01a4e8bb93f3` | 802 / 802 | 49,775 | 44,280 | 109 | 0–108 |
| 8 | 4,241,984 | `c05fe400708b1c53a64834697f92cc43f68209156983f7212ded7f02ee3829db` | 2,876 / 2,876 | 75,228 | 66,184 | 54 | 0–53 |
| 20 | 1,085,696 | `36e656886dba509a33f405ff0e72bdc1fbd7a6ec81ea3297bbc6b57b52dbac4f` | 383 / 383 | 24,291 | 21,052 | 76 | 0–75 |
| 50 | 1,577,216 | `58af8a46edcceed4c07670f5e7a0158daa7d90401fbbb0f78648c705048c164a` | 525 / 525 | 36,076 | 33,328 | 46 | 0–45 |

For **every sampled row**:

- `skippedByDataStart = 0`
- `skippedByCommonUnpacks = 0`
- `skippedByStrow = 0`
- `skippedByLod0Streams = 0`
- `commonVifTailBytes = 0`
- `lod0VifTailBytes = 0`
- `textureIdsOutsidePublicTable = []`
- `nonFinitePositionCount = 0`
- core WAD-LZ decode warnings were empty

This is materially stronger than a parser merely surviving the input. Every declared fragment passed the existing compatibility prerequisites, VIF streams terminated cleanly, geometry remained finite, and all texture references stayed within the independently decoded public-derived tfrag texture count for each row.

## VIF census

| Row | Common packets | Common UNPACKs | LOD0 packets | LOD0 UNPACKs |
|---:|---:|---:|---:|---:|
| 1 | 9,135 | 3,208 | 10,094 | 4,117 |
| 8 | 33,028 | 11,504 | 25,537 | 7,901 |
| 20 | 4,401 | 1,532 | 4,974 | 2,127 |
| 50 | 5,965 | 2,100 | 6,637 | 2,719 |

## Geometry bounds

Row 1:

- min `(158.5107421875, 29.240234375, 87.4296875)`
- max `(482.466796875, 176.953125, 460.59765625)`

Row 8:

- min `(-255.7744140625, 322.90625, -151.7568359375)`
- max `(1023.220703125, 616.65625, 1023.9013671875)`

Row 20:

- min `(137.4306640625, 198.03125, 121.654296875)`
- max `(415.93359375, 219.001953125, 462.0751953125)`

Row 50:

- min `(182.822265625, 173.16015625, 204.7802734375)`
- max `(618.2177734375, 231.6513671875, 603.3701171875)`

## Interpretation

The existing GC tfrag/VIF parser is now retail-confirmed compatible with the sampled UYA tfrag blocks. This does **not** prove that every UYA level uses the same format, nor does it independently prove the public-derived name `tfrags`, the enclosing `0x60 / 0x58 / 0xbc` semantic layering, or native loader provenance of the hidden ToC lead.

Those provenance questions should remain explicit and separate. For reconstruction work, however, the bytes and parser behaviour are strong enough to proceed to texture decoding without weakening or forking the GC tfrag parser.

## Next target

Trace each sampled row's emitted tfrag texture IDs into the corresponding UYA core texture table / GS-RAM data, then test the unchanged GC texture decoder where structurally applicable. Preserve public-derived table semantics as provisional until retail evidence independently corroborates them.
