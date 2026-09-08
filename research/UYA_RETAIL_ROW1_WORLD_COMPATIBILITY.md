# UYA retail row-1 world compatibility census

Authority build: `rac3-ntscu-original` (`SCUS-97353`, NTSC-U original retail).

This document records the first successful bounded retail UYA vertical world/core/collision run. It deliberately separates **observed retail parser compatibility** from semantics that are still selected through pinned public leads.

No retail source URL, Google Drive file ID, access token, or raw game payload is committed here.

## Reproduction checkpoint

GitLab pipeline: **2826787834** (iid **265**)

UYA world/core/collision job: **16348660664**

Branch checkpoint used by the run: `70e720a92e18006b0d715ec7e15c103d3a75c1e1`

Result: **PASS**.

The source was the sparse mapped retail split with only **parts 1 and 5** supplied through temporary pipeline inputs. Part 1 supplied authority/ToC bytes; part 5 supplied the row-1 candidate main payload. No complete ISO was reconstructed or downloaded.

The run also re-exercised the packed boot-executable lane successfully.

## Authority identity

Before semantic probing, the tool re-read the exact recorded candidate-ToC identity window:

- candidate LBA: `1001`
- byte offset: `2,050,048`
- bytes: `2,097,152`
- SHA-256: `a9e3e338df29045222ab0c0c0ba68fe1cff2048add582124f51915bb4ba55e5a`
- identity match: **true**

This proves that the bounded source window matches the recorded supported retail build. It still does **not** prove that LBA 1001 is the native loader's authoritative ToC address.

## Selected row and payload

Physical table row **1** was selected.

Observed physical row:

| Slot | Header LBA | Size sectors | Header bytes | Raw `+0x04` |
|---:|---:|---:|---:|---:|
| 0 | 1017 | 11,346 | 6,168 (`0x1818`) | 770,769 |
| 1 | 1021 | 9,439 | 96 (`0x60`) | 1,162,307 |
| 2 | 1022 | 13,678 | 9,968 (`0x26f0`) | 1,583,737 |

Under the pinned-public interpretation, slot 1 is the candidate main level payload. The resulting retail extent is **19,331,072 bytes** and lies wholly inside split part 5.

The candidate ToC scanner still reports an ambiguity warning: **31 offsets** match the provisional public header-size sequence, so the tool uses its conservative leading-header fallback. This does not invalidate the observed row bytes, but it means table-discovery uniqueness/native provenance is not established by this run.

## Retail fixed-header vertical path

The row-1 candidate payload accepted the full provisional fixed-header chain:

`0x60 outer -> 0x58 level-data -> 0xbc core -> 0x10 coreData WAD-LZ`

Observed outer-header ranges under the pinned-public labels:

| Slot | Public label | Offset sectors | Size sectors | Offset bytes | Size bytes |
|---:|---|---:|---:|---:|---:|
| 0 | primary | 788 | 7,976 | 1,613,824 | 16,334,848 |
| 1 | core-bank | 1 | 787 | 2,048 | 1,611,776 |
| 2 | gameplay | 8,764 | 663 | 17,948,672 | 1,357,824 |
| 3 | occlusion | 9,427 | 12 | 19,306,496 | 24,576 |

The outer header also yielded `publicLevelIdHint = 1` and `publicReverbHint = 0`.

Within the candidate level-data layout, retail bytes yielded:

- overlay: offset `128`, size `3,441,828`
- coreIndex: offset `3,441,984`, size `35,216`
- GS-RAM: offset `3,477,248`, size `497,664`
- HUD header: offset `3,974,912`, size `4,924`
- coreData: offset `4,166,784`, size `12,166,965`
- candidate core header location in level: `5,055,808`
- candidate coreData WAD-LZ location in level: `5,780,608`

The proposed `assetsCompressedSize` field and WAD-LZ header both report **12,166,965 bytes**.

These are direct retail observations through the pinned-public structural interpretation. The run strongly corroborates the layout on row 1, but semantic names remain separately promotable claims.

## 0xbc core header: retail structural corroboration

The complete 188-byte candidate core header parsed without structural rejection. Representative public-field interpretations include:

- GS-RAM: 448 entries at offset 21,296
- occlusion offset: 2,220,864
- sky offset: 2,731,712
- collision offset: 3,216,960
- Moby classes: 227 at offset 192
- TIE classes: 78 at offset 7,456
- shrub classes: 22 at offset 9,952
- tfrag textures: 109 at offset 11,008
- Moby textures: 143 at offset 12,752
- TIE textures: 166 at offset 15,040
- shrub textures: 55 at offset 17,696
- particle/part textures: 65 at offset 18,576
- FX textures: 105 at offset 19,616
- texture base: 5,688,448
- part bank: 10,615,424
- FX bank: 10,819,200
- proposed compressed asset size: 12,166,965
- proposed decompressed asset size: 20,000,480

The raw 47 u32 words remain the authority-preserving representation in `packages/uya-level-core`; the public names above are interpretations, not silently promoted native truth.

## Retail UYA WAD-LZ compatibility — confirmed for row 1

The strict coreData experiment reused the **unchanged OBP WAD-LZ decoder**.

| Evidence | Result |
|---|---|
| coreIndex bytes | 35,216 |
| coreIndex SHA-256 | `8e9b46845270a7678b473b621bf66fde0d752f7b0d921ace897a85981016de2c` |
| compressed coreData bytes | 12,166,965 |
| compressed coreData SHA-256 | `c17108438e245bd4db3342da5aace39e2d030bd35a062a3f3a81e5817f9eef39` |
| decompressed bytes | 20,000,480 |
| decompressed SHA-256 | `d8f6ebde468c115a9b6bf48d3c5723f03cea552cae192447dba919c426acd7f7` |

All strict size checks passed:

- core compressed-size field matches the WAD header;
- proposed compressed size matches the exact block read;
- proposed decompressed size matches decoder output.

There were **no warnings** and no UYA-specific decompression fallback.

Therefore: **the existing OBP WAD-LZ decoder is retail-confirmed compatible with this exact UYA row-1 coreData block.** This does not yet establish that every UYA WAD-LZ block is identical; additional representative rows can test generality.

## Retail UYA collision parser compatibility — confirmed for row-1 candidate block

Using the pinned-public core boundary interpretation, the candidate collision range was:

- offset in decompressed coreData: **3,216,960**
- bytes: **2,471,488**
- SHA-256: `0bcf82e12b70a35010f5315e07f169ca54fd8ff508268d2a3c9ffe7159ed1ef5`

The **unchanged retail-GC-verified `readRcCollision()` parser** accepted those UYA retail bytes without relaxed validation or a UYA-specific reader.

Deterministic census:

- octants: **21,510**
- vertices: **306,015**
- triangles: **264,313**
- native material IDs: `9, 10, 12, 31, 41, 44, 63, 95, 127, 159`
- mesh offset: `64`
- hero-groups offset: `2,445,392`
- hero-group count: `51`
- warnings: none

Native collision bounds:

- min: `(132.1875, 77, 29.546875)`
- max: `(493.5625, 504.9375, 165.59375)`

Therefore: **the existing GC collision binary parser is retail-confirmed compatible with this exact UYA candidate collision block.**

What is *not* yet promoted is the independent native semantic claim that public core word/offset `3,216,960` is definitively the retail engine's collision field. The block's structural acceptance, deterministic census, and public cross-game implementation make that interpretation very strong, but loader/core semantics remain subject to independent corroboration.

## Evidence promoted by this run

Safe promotions from pipeline iid 265:

1. The row-1 retail candidate payload physically exists at the address produced by the recorded public-table interpretation and is structurally coherent through the 0x60/0x58/0xbc/0x10 chain.
2. The candidate core header's compressed/decompressed sizes exactly agree with the retail WAD-LZ block and decoder output.
3. The unchanged OBP WAD-LZ decoder works on the exact row-1 retail UYA coreData block.
4. The unchanged GC collision parser works on the exact public-derived row-1 UYA candidate collision block.
5. Sparse HTTP parts 1 + 5 are sufficient for this complete row-1 experiment; no ISO reconstruction is necessary.

Still provisional:

- native loader provenance of LBA 1001;
- semantic slot names for the three ToC ranges;
- independent native naming of the outer/data/core fields;
- native provenance of the candidate collision offset itself;
- game-wide generality from one row.

## Next experiments

The cheapest confidence increase is to repeat the strict WAD-LZ + collision experiment on several structurally normal levels distributed across other split chunks. Repeated success with distinct hashes/layouts would distinguish one-row compatibility from a game-wide family.

In parallel, executable/decompilation archaeology should continue around the retail loader and the `0x0013d2ec` LBA-1001 candidate instruction, looking for control-flow evidence that ties the constant to disc I/O. Public decompilation/symbol work such as ProjectRYNO is useful corroboration but remains below retail executable behaviour in the evidence hierarchy.
