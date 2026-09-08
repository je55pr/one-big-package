# Up Your Arsenal retail disc layout

Authority build: `rac3-ntscu-original` (`SCUS-97353`, NTSC-U original retail).

This document separates direct OBP retail evidence from public-format leads. Public implementations are useful search accelerators; they are not native truth until checked against the supported retail bytes.

## Direct retail evidence already established

The existing bounded Stage-0 disc report (`research/generated/rac3-ntscu-original.stage0.md`) and authority manifest establish:

| Field | Retail result |
|---|---|
| ISO size | 4,379,377,664 bytes |
| ISO SHA-256 | `d2bb15c7c5b2205db868713fc0362c2b10e87751ca5bcc4e96c1e244a8c42444` |
| ISO-9660 volume id | `RATCHETANDCLANK3` |
| `SYSTEM.CNF` | LBA 1000, 60 bytes |
| Boot path | `cdrom0:\\SCUS_973.53;1` |
| Serial | `SCUS-97353` |
| Boot ELF | `/SCUS_973.53`, LBA 1463, 771,008 bytes |
| Boot ELF entry | `0x00800008` |
| Boot PT_LOAD | file `0x1000`, VA `0x00800000`, file size 766,458 bytes |
| ISO-9660 inventory | 44 files; no campaign `LEVEL*.WAD` family |

The ordinary ISO filesystem therefore does **not** expose the UYA campaign world pipeline in the GC fashion. Named early files occupy the bootstrap/online area; named late files are chiefly NETGUI/Sly 2 demo/padding. Most main-game data lies in the large raw-disc region outside ordinary ISO-9660 file records.

This is the first proven GC/UYA divergence relevant to OBP import: GC provides named `/G/LEVELn.WAD`, `/G/AUDIOn.WAD`, `/G/SCENEn.WAD` files, while the supported UYA retail image requires raw-LBA discovery before equivalent containers can be addressed.

## Candidate resident table of contents — public lead, retail verification pending

Pinned corroborating source:

- repository: `chaoticgd/wrench`
- commit: `e67ea46e5e60ec52a0c31fcde1e3f46b20aa4cfb`
- `docs/file_loading.md`
- `src/iso/table_of_contents.{h,cpp}`
- `src/iso/iso_unpacker.cpp`
- `src/iso/wad_identifier.cpp`

That snapshot models GC/UYA/Deadlocked as sharing a resident table-of-contents anchor at **LBA 1001** (`0x1f4800` bytes), directly after the common `SYSTEM.CNF` LBA. It models a leading chain of resident/global headers followed by level rows containing three sector ranges.

This is currently a **search hypothesis**, not a retail claim. OBP deliberately exports the address as `UYA_PUBLIC_TOC_LBA_HINT` / `RAC234_PUBLIC_LEAD_TOC_LBA`.

`packages/uya-disc-toc`, `packages/uya-disc`, and `tools/uya-disc-toc.mjs` test the hypothesis using one bounded read. The probe path:

1. reads at most 2 MiB beginning at the candidate LBA;
2. records the exact window SHA-256 in the CLI report;
3. structurally searches the first `0x10000` bytes for a level-table boundary using two consecutive rows / six pointed headers;
4. records the leading resident-header chain as raw sizes + word-at-`+4` values only after a boundary is independently selected;
5. records sparse candidate level rows as raw `(headerLba, sizeSectors)` triplets;
6. follows only headers that fall within the same bounded window;
7. keeps public Wrench header-size labels under explicitly named lead/hint fields.

The structural boundary step is important. A naive “keep consuming plausible header sizes” parser is ambiguous because the first level-table LBA can itself be a small plausible header size. CI caught exactly that failure in the first probe implementation; the corrected path now records whether discovery came from the six-header public structural lead or a labelled conservative fallback.

It does **not** read level payloads, reconstruct the ISO, or claim that public labels are native semantics.

Run against either the complete ISO or the first split chunk (the candidate 2 MiB window lies entirely inside `.iso.001`):

```text
npm run build
node tools/uya-disc-toc.mjs "<path>/Ratchet & Clank - Up Your Arsenal (USA) (En,Fr,Es).iso.001"
```

A generated retail report should be committed only after its values have been deterministically reproduced; never commit the source bytes themselves.

## Public lead: UYA level-table physical order

Wrench's documentation/writer explicitly distinguishes the physical table field order:

| Physical pair | GC public interpretation | UYA / Deadlocked public interpretation |
|---|---|---|
| 0 | level | **audio** |
| 1 | audio | **level** |
| 2 | scene | scene |

Wrench's reader does not simply trust that order: it follows each pointed resident header and classifies the apparent WAD family. OBP should retain the same epistemic separation. `packages/uya-disc` therefore exposes physical entries first and provides the `audio → level → scene` mapping only through an explicitly named public-lead interpretation.

This is a likely GC/UYA binary divergence, but remains unconfirmed until the authority UYA ToC bytes are probed.

## Public header-size leads to verify

The same pinned Wrench snapshot contains these UYA-facing header-size interpretations. They are listed only to make retail checks cheap:

| Header bytes | Public label | Status in OBP |
|---:|---|---|
| `0x0060` | level (GC/UYA family) | public lead only |
| `0x1818` | UYA level audio | public lead only |
| `0x26f0` | level scene (game unresolved by Wrench identifier) | public lead only |
| `0x0048` | UYA misc | public lead only |
| `0x03c8` | gadget family + discriminator | public lead only |
| `0x0648` | UYA MPEG + discriminator | public lead only |
| `0x0398` | UYA armor | public lead only |
| `0x0bf0` | UYA bonus (non-Japan) | public lead only |
| `0x0c30` | UYA space | public lead only |
| `0x2340` | UYA audio | public lead only |
| `0x2ab0` | UYA HUD | public lead only |

The first useful retail result is not “these names are correct”; it is a deterministic census of which sizes/sector ranges actually occur at the candidate UYA table and what bytes they point to.

## Public lead: proposed `0x60` main level header

Wrench documents GC and UYA as sharing this top-level main level-header shape:

| Offset | Public label |
|---:|---|
| `0x00` | header size (`0x60`) |
| `0x04` | WAD payload sector |
| `0x08` | native level number |
| `0x0c` | reverb |
| `0x10` | primary range |
| `0x18` | core bank range |
| `0x20` | gameplay range |
| `0x28` | occlusion range |
| `0x30` | chunk 0 |
| `0x38` | chunk 1 |
| `0x40` | chunk 2 |
| `0x48` | chunk sound bank 0 |
| `0x50` | chunk sound bank 1 |
| `0x58` | chunk sound bank 2 |

Even if retail confirms the shape and sizes, field meanings and all inner formats still need UYA-specific evidence.

## Public lead: level index versus native level ID

The pinned Wrench ISO unpacker derives native level ID from main level-header offset `0x08`, rather than equating table index with native ID.

It also contains a UYA-specific exception: **table index 38 may have level-associated data without a main level WAD**. Wrench assigns ID 38 only as a special handling case when that main header is absent.

The eventual machine-generated UYA catalogue must therefore preserve separately:

- table index;
- native level ID when a main header supplies one;
- optional main/audio/scene parts;
- special rows whose identity cannot be inferred from a main header.

Do not collapse these concepts merely to match GC's existing catalogue representation.

## Initial GC ↔ UYA compatibility matrix

This table is deliberately conservative and should be tightened only by retail UYA evidence.

| Concept | Going Commando | Up Your Arsenal | Current classification |
|---|---|---|---|
| PS2 build probe | retail-confirmed | retail-confirmed | identical conceptual path |
| campaign container discovery | named ISO-9660 WADs + `RC2.HDR` | no campaign WADs in ISO-9660; hidden ToC is public lead | **same conceptual system, changed discovery path** |
| resident ToC anchor | GC retail reconciliation/public LBA 1001 | public LBA 1001 lead | unknown pending retail UYA bytes |
| level-table field order | public level/audio/scene | public audio/level/scene | likely changed binary order |
| main level header | retail GC `0x60` | public GC/UYA `0x60` | compatible-looking, unconfirmed |
| WAD-LZ | retail GC decoded/tested | public reuse lead | unknown pending UYA sample |
| collision | retail GC decoded | shared public code lineage | unknown pending UYA core |
| tfrags/VIF | retail GC decoded | shared public lineage, game-aware low level | unknown |
| textures | retail GC decoded | shared public asset lineage | unknown |
| TIE | retail GC decoded | shared public class lineage | unknown |
| shrub | retail GC decoded | shared public class lineage | unknown |
| Moby | retail GC partial model/skinning | shared public class lineage, game-aware codec | unknown |
| sky | retail GC decoded | public UYA/DL shell extension | same concept, likely additional fields |
| level settings | retail GC decoded | public GC/UYA/DL first-part family | compatible-looking, unconfirmed |

## Current sandbox note

During this pass, the canonical first 480 MiB authority split materialised successfully, but container/private-Python execution immediately began returning client errors and remained unavailable. The large materialisation was not repeated. Library materialisation currently offers whole-file/page/line/locator selectors, not arbitrary binary byte ranges, so there is no safe connector-only route to the 2 MiB ToC window.

No retail ToC result is claimed from that failed execution path. All LBA-1001 semantics above remain explicitly public leads.

## Next retail checks

1. Run the bounded TOC probe against authority chunk `.001` and record the window hash, structural table candidates, resident-header chain and sparse table census.
2. Independently corroborate the candidate table from executable behaviour/structures before promoting LBA 1001 or field meanings to native truth.
3. Hash the retail boot executable separately (the ISO payload hash is already authoritative, but executable SHA-256 is still missing from the generated build report).
4. Use the confirmed table to produce the machine-generated UYA level/container catalogue before opening any level payload.
5. Select one early/simple campaign row and only then begin main WAD/core/compression/collision compatibility work.
