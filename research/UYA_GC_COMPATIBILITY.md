# Up Your Arsenal ↔ Going Commando native compatibility

Authority build: UYA `rac3-ntscu-original` (`SCUS-97353`).

This document answers one question at a time: **which Going Commando native systems can be reused for Up Your Arsenal, and at what evidence level?**

The evidence order is retail UYA bytes, executable behaviour/structures, deterministic retail probes, pinned public implementations, then speculation. A shared public implementation is a useful experiment design, not proof of binary identity.

Pinned public corroboration used below:

- repository: `chaoticgd/wrench`
- commit: `e67ea46e5e60ec52a0c31fcde1e3f46b20aa4cfb`

Transport evidence is recorded in `research/HTTP_RANGE_TRANSPORT.md`. The first detailed row is in `research/UYA_RETAIL_ROW1_WORLD_COMPATIBILITY.md`; the broader four-row census is in `research/UYA_RETAIL_MULTIROW_COMPATIBILITY.md`.

## Current compatibility matrix

| Concept | Going Commando evidence | UYA evidence | Current classification |
|---|---|---|---|
| PS2 disc/build probe | retail-confirmed | retail-confirmed | **identical conceptual path** |
| campaign container discovery | named ISO-9660 `/G/LEVEL*`, `/G/AUDIO*`, `/G/SCENE*` plus `RC2.HDR` | retail ISO-9660 has no campaign WAD family; coherent raw-disc table observed | **same problem, substantially changed discovery path** |
| resident raw-disc ToC | GC retail/public archaeology exists | bounded retail window at public candidate LBA 1001 is deterministic/coherent; packed executable contains an LBA-1001 candidate instruction, but control-flow provenance is unresolved | **strong retail corroboration; native address provenance still provisional** |
| level-table physical part order | public `level, audio, scene` | retail bytes show physical header-size sequence `0x1818, 0x0060, 0x26f0`; public source names those slots `audio, level, scene` | **binary slot order retail-observed; semantic labels still provisional** |
| outer main-level header | retail GC 0x60 family | sampled retail rows 1/8/20/50 accept the 0x60 path and lead to coherent downstream data | **repeated retail structural corroboration; field semantics still public-derived** |
| level-data header | retail GC decoded | sampled rows accept the pinned-public 0x58 path and yield coherent core ranges | **repeated retail structural corroboration; semantic labels provisional** |
| level-core index header | retail GC decoded | sampled rows accept the 0xbc layout; sizes/boundaries lead to distinct valid coreData/collision blocks | **strong repeated retail structural corroboration; field names public-derived** |
| core-data compression | GC WAD-LZ retail-decoded/tested | unchanged OBP WAD-LZ strictly decodes four distinct UYA level coreData samples (rows 1/8/20/50), all size checks exact | **multi-row retail-confirmed UYA format-family compatibility** |
| collision | GC retail-decoded/tested in `rc-collision` | unchanged `readRcCollision()` accepts four distinct public-derived UYA retail candidate blocks with different hashes/sizes/censuses | **multi-row retail-confirmed parser compatibility; collision-field provenance still provisional** |
| tfrags / VIF | GC retail-decoded | shared public asset path, but low-level codec receives game identity | **same conceptual system; binary compatibility unknown — next major target** |
| textures | GC retail-decoded | shared public asset path; sampled core headers expose coherent candidate texture tables | **same conceptual system; low-level binary details untested** |
| TIE | GC retail-decoded | sampled public core layouts expose candidate class tables; shared public class path | **same conceptual system; binary compatibility unknown** |
| shrub | GC retail-decoded | sampled public core layouts expose candidate class tables; shared public class path | **same conceptual system; binary compatibility unknown** |
| Moby | GC retail partial model/skinning | sampled public core layouts expose candidate class tables; low-level public codec is game-aware | **same conceptual system; binary compatibility unknown** |
| sky | GC retail-decoded | public UYA/DL shell header adds rotation relative to RAC/GC; sampled candidate offsets are structurally coherent | **same concept, additional/changed fields likely** |
| level settings | GC retail-decoded | public GC/UYA/DL first-part family | **compatible-looking; dedicated retail UYA decode unconfirmed** |
| gameplay/PVars/instances | GC retail-decoded in part | shared public top-level instance asset with game-specific descriptions | **same concept, binary compatibility unknown** |

## Retail ToC evidence

`research/UYA_RETAIL_TOC.md` records a bounded authority read at the public candidate address. It sampled exactly 2 MiB at LBA 1001 and recorded SHA-256 `a9e3e338df29045222ab0c0c0ba68fe1cff2048add582124f51915bb4ba55e5a`.

The window contains eight sane resident headers that tile exactly to offset `0x7400`, followed by the sparse three-range level table. A 100-row census found the physical per-part header sizes expected by the public UYA lead:

- slot 0: `0x1818`, 35/35 observed parts;
- slot 1: `0x0060`, 51/51 observed parts;
- slot 2: `0x26f0`, 51/51 observed parts.

For all 51 observed slot-1 `0x60` headers, raw word `+0x08` equals the table index. This is direct retail structure evidence. It does **not** by itself prove that LBA 1001 is the native loader's authoritative address or that the three slot meanings are audio/level/scene.

The current table scanner reports that multiple offsets can satisfy the provisional public header-size sequence and therefore uses a conservative leading-header fallback. Table discovery uniqueness/native provenance remains unresolved even though the selected retail rows and downstream payloads are reproducible.

## Bounded authority transport

The previous large-input blocker no longer requires materialising split parts in the ChatGPT sandbox. `packages/importer-common/src/http-range.ts` provides a strict HTTP `RandomAccessReader`; GitLab CI is the execution backend for live authority ranges because the current ChatGPT sandbox lacks outbound DNS.

For UYA, `tools/uya-http-range-source.mjs` supports either one complete public/shared authority ISO or selected split chunks. The known retail split layout is 9 binary-concat chunks: parts 1-8 are 503,316,480 bytes and part 9 is 352,845,824 bytes. Each supplied chunk must match its exact expected size and is mapped to its real logical offset. Missing chunks remain hard-failing holes.

The user-facing handoff is one shared Drive folder. All nine chunks are now visible through folder enumeration. URLs and Drive IDs remain temporary transport inputs and are never committed.

Before HTTP UYA semantic probing, `tools/uya-retail-authority.mjs` hashes the exact 2 MiB candidate-ToC authority window and requires the known retail SHA-256 above. Passing this identifies the bounded source bytes; it does not promote the loader provenance or semantics of LBA 1001.

The transport has now proven both contained and cross-split payloads. Row 8 begins in part 5 and crosses into part 6; pipeline iid **271** completed the full decode/collision path through the mapped logical reader without ISO reconstruction.

## Retail fixed-header/core structural result

The provisional vertical chain has now succeeded on four distinct retail rows:

```text
candidate ToC row
  -> 0x60 outer header
  -> 0x58 level-data header
  -> 0xbc core header
  -> 0x10 coreData WAD-LZ header
```

Row 1 provides the detailed field census in `UYA_RETAIL_ROW1_WORLD_COMPATIBILITY.md`. Rows 8, 20 and 50 independently produced different core indexes, compressed coreData blocks, decompressed outputs and valid collision ranges.

This repeated success materially strengthens the public 0x60/0x58/0xbc structural interpretation. It still does not require prematurely promoting every public field label to native executable truth.

## Retail WAD-LZ compatibility — multi-row confirmed

The strict coreData experiment reused the **unchanged OBP WAD-LZ decoder** on four distinct retail UYA level samples:

| Row | Compressed bytes | Compressed SHA-256 | Decoded bytes | Decoded SHA-256 |
|---:|---:|---|---:|---|
| 1 | 12,166,965 | `c17108438e245bd4db3342da5aace39e2d030bd35a062a3f3a81e5817f9eef39` | 20,000,480 | `d8f6ebde468c115a9b6bf48d3c5723f03cea552cae192447dba919c426acd7f7` |
| 8 | 9,571,910 | `e86a32b1fc7da3b774e7ccc0c94603037a35fae421346cae4e94ad811a00b546` | 16,375,888 | `a80563feb7998d26738c45afe7ac4f3886b0c932c534034cd3682d1678b2e481` |
| 20 | 11,907,803 | `423a333f3076aa535cc5fcbdfc29949b2c96d5b1a686ef4670a521899c405653` | 20,511,872 | `49e5d42c3187c7bd479da027cd662cbfd7f38babc496129eac93466449dee7bd` |
| 50 | 10,268,247 | `98498108796a6a3875e9b038c5c97ec7dfdefc1d44cf8db2318b4207bacb571b` | 17,555,520 | `05215d3068e5fd924c601ca462ad27b59791950e1e2fb36bb381ed3d75e2a4b8` |

Every run passed all strict compressed/decompressed size checks with no UYA-specific fallback.

Therefore **OBP WAD-LZ is retail-confirmed compatible across multiple sampled UYA level coreData blocks**. This is strong UYA format-family evidence, though not an exhaustive claim about every compressed object anywhere on the disc.

## Retail collision compatibility — multi-row confirmed

At the pinned Wrench commit, RAC1, GC, UYA and Deadlocked all use one public top-level collision unpack path and one low-level `read_collision(bytes)` implementation with no game/build parameter. Its main-mesh binary reader matches OBP's GC parser field-for-field at the currently decoded layers.

The direct retail tests now cover four distinct public-derived candidate blocks:

| Row | Bytes | SHA-256 | Octants | Vertices | Triangles | Hero groups |
|---:|---:|---|---:|---:|---:|---:|
| 1 | 2,471,488 | `0bcf82e12b70a35010f5315e07f169ca54fd8ff508268d2a3c9ffe7159ed1ef5` | 21,510 | 306,015 | 264,313 | 51 |
| 8 | 1,045,184 | `a6ef51034bc663a580df1cf081f9bb14b078e43c8d8fa04760d8192bd60adcbf` | 9,750 | 133,922 | 112,813 | 14 |
| 20 | 2,126,272 | `bb72cc350c168231d72cef982b0022ef789ef5f2449a45157e157c7848af9775` | 20,835 | 257,071 | 236,309 | 5 |
| 50 | 2,472,256 | `7bc895487234ce837c0c34374216ffff28be6494a7d5c1bedfb5524452c7a973` | 34,049 | 289,862 | 222,264 | 16 |

The **unchanged `readRcCollision()`** parser accepted every block with no relaxed validation and no UYA-specific decoder. Distinct hashes, sizes, material sets, bounds and mesh counts demonstrate that these are independent meshes rather than duplicate reads.

Therefore **the existing GC collision binary parser is retail-confirmed compatible across multiple sampled UYA candidate collision blocks**.

The semantic caveat remains separate: the candidate blocks are selected using public-derived 0xbc core field/boundary semantics. Repeated structurally valid meshes make that interpretation extremely strong, but independent native evidence should still tie the field to the retail engine's collision semantics before the field name/provenance itself is called native-confirmed.

## Why the 0xbc core lead remains useful

Across the sampled rows, the proposed 0xbc `LevelCoreHeader` consistently yields ordered/scalar boundaries and exact compressed/decompressed size relationships that lead to independently valid downstream data.

`packages/uya-level-core` preserves all 47 raw little-endian u32 words alongside the public interpretation, so unresolved native semantics are retained rather than discarded. The next asset-family experiments can use these retail-confirmed coreData outputs without treating every public name as settled.

## What remains unconfirmed

The following remain provisional:

- native/executable provenance of the public LBA-1001 ToC address;
- semantic names `audio, level, scene` for the three physical ToC slot signatures;
- independent native names/meanings for the 0x60 outer ranges and 0x58 data ranges;
- independent native field semantics for all 0xbc words, including the collision offset;
- exhaustive whole-game coverage beyond the sampled rows;
- tfrag/VIF, texture, TIE, shrub and Moby low-level binary compatibility.

The following are **no longer pending compatibility questions** for the sampled UYA level family:

- unchanged WAD-LZ acceptance of retail UYA level coreData;
- exact compressed/decompressed size agreement;
- unchanged GC collision parser acceptance of retail UYA candidate collision meshes;
- mapped HTTP reads spanning an original split-file boundary.

## Next promotion sequence

1. Preserve pipelines iid **265, 271, 272 and 273** as the canonical initial multi-row compatibility set.
2. Stop spending routine CI on additional collision-only rows unless a later divergence needs diagnosis; four distinct successful samples are sufficient to move to the next uncertainty.
3. Use the confirmed coreData pipeline to begin deterministic retail tests of **tfrags/VIF and textures**, where the public low-level code is more game-aware and binary compatibility is genuinely unknown.
4. Continue packed-executable/control-flow archaeology around the LBA-1001 candidate at `0x0013d2ec`, ideally tying it to CD/DVD read functions or another independent loader reconstruction.
5. Approach TIE/shrub/Moby afterward with the same rule: preserve existing GC parsers, record exact retail failures/successes, and never relax validation merely to make UYA pass.

For exact multi-row hashes and censuses, see `research/UYA_RETAIL_MULTIROW_COMPATIBILITY.md`.
