# UYA retail hidden table-of-contents census

Authority build: `rac3-ntscu-original` (`SCUS-97353`).

Evidence source: one bounded 2 MiB read at the pinned public candidate disc LBA 1001. Source bytes are not committed. The coherent structure observed at this address is direct retail-byte evidence; the claim that the native retail loader treats LBA 1001 as its authoritative ToC address remains provisional until independently corroborated from executable behaviour.

## Reproducible window identity

- absolute byte offset: `0x001f4800` (2,050,048)
- length: `0x200000` (2,097,152)
- SHA-256: `a9e3e338df29045222ab0c0c0ba68fe1cff2048add582124f51915bb4ba55e5a`

Pinned Wrench commit `e67ea46e5e60ec52a0c31fcde1e3f46b20aa4cfb` was used as a search lead. The values below were then read directly from the supported retail window.

## Resident/global header chain

Eight plausible headers tile the window prefix exactly from `0x0000` to `0x7400`:

| # | TOC offset | header size | raw word `+0x04` | Public Wrench size hint |
|---:|---:|---:|---:|---|
| 0 | `0x0000` | `0x0648` | `0x000009d3` | UYA MPEG |
| 1 | `0x0648` | `0x0048` | `0x000a4e85` | UYA misc |
| 2 | `0x0690` | `0x0bf0` | `0x000a57ca` | UYA bonus |
| 3 | `0x1280` | `0x0c30` | `0x000ab70f` | UYA space |
| 4 | `0x1eb0` | `0x0398` | `0x000aea89` | UYA armor |
| 5 | `0x2248` | `0x2340` | `0x000afb5a` | UYA audio |
| 6 | `0x4588` | `0x03c8` | `0x000e51de` | gadget family |
| 7 | `0x4950` | `0x2ab0` | `0x00117041` | UYA HUD |

**Retail conclusion:** the eight-header size sequence at the candidate address is confirmed. The descriptive WAD-family names in the final column remain public-source semantics until independently corroborated from executable behaviour or payload structure.

The exact sum of the eight observed header sizes is `0x7400`. The next 24 bytes are all zero, giving an empty table row 0. This makes `0x7400` the retail-supported structural table boundary within the sampled window for this build.

## Level-table topology

Each physical row is 24 bytes / three `(u32, u32)` pairs. A 100-row bounded census from `0x7400` found:

- **52 non-zero rows**;
- non-zero indices: `1–14`, `16–24`, `26–36`, `38–55`;
- empty holes inside that span: **0, 15, 25, 37**;
- rows `56–99` are zero in the inspected 100-row window.

Part presence by physical slot:

| Physical slot | Present rows | Observed pointed header size |
|---:|---:|---:|
| 0 | 35 | `0x1818` in 35/35 |
| 1 | 51 | `0x0060` in 51/51 |
| 2 | 51 | `0x26f0` in 51/51 |

Topology changes are real and should remain explicit:

- rows `1–14`, `16–24`, `26–36` contain all three slots;
- row **38 contains slot 0 only**;
- rows **39–55 contain slots 1 and 2 only**.

This directly confirms the binary part-size ordering expected by the public UYA lead within the sampled retail structure. Naming slot 0 “audio”, slot 1 “level”, and slot 2 “scene” is corroborated by Wrench but remains a semantic attribution rather than a fact derived from header size alone.

## Table index versus raw `0x60` header field

For every one of the **51** observed slot-1 headers of size `0x60`, the raw little-endian word at header offset `+0x08` equals the physical table index exactly:

- 51 checked
- 51 equal
- 0 mismatches

Examples: row 1 → `+0x08 = 1`, row 24 → `24`, row 39 → `39`, row 55 → `55`.

This is strong retail evidence that the field is tied directly to the table/native level identity. Wrench independently labels this field as the native level number, which corroborates the interpretation. OBP should still preserve both `tableIndex` and the raw/native header field rather than assuming they can never diverge in another build or special row.

Row 38 is particularly important: it has no `0x60` main header from which to read the `+0x08` field, yet it has a slot-0 associated container. This matches the public implementation's special handling of index 38 and is now backed by the retail table topology.

## Additional raw invariants worth preserving

- All 35 observed `0x1818` headers have raw word `+0x08 == 4`.
- The `0x26f0` header `+0x08` field is not constant: it is `5` on many earlier rows and `0` on later/minimal rows. No semantic meaning is assigned yet.
- Several later slot-2 table sizes are exactly 5 sectors; these should not be rejected as too small merely because campaign rows are much larger.

The complete raw census through index 55 is machine-readable in `generated/rac3-ntscu-original.uya-toc.csv`. Column names intentionally use `slot0/1/2` and `raw_word_*` rather than prematurely promoting public meanings.

## Immediate implications for GC compatibility

| Concept | Retail UYA result | Relationship to GC so far |
|---|---|---|
| campaign discovery | coherent hidden raw-LBA candidate structure observed at public lead LBA 1001 | same broad container concept is strongly suggested, but native loader provenance is not yet proven |
| resident TOC | header chain + sparse three-range table structure observed at candidate LBA 1001 | compatible lineage strongly supported; address semantics remain provisional |
| table row | three sector-range pairs | same conceptual structure, UYA physical ordering differs from GC public ordering |
| main header | physical slot 1, `0x60`, raw `+0x08` tracks index | strongly compatible-looking with GC `0x60` main header; payload copy/inner ranges still need retail comparison |
| associated header A | slot 0, `0x1818` | changed binary format versus GC level-audio header (`0x1018`) |
| associated header B | slot 2, `0x26f0` | changed binary format versus GC level-scene header (`0x137c`) |

The next archaeology step is to corroborate the candidate address from executable behaviour, then open one early slot-1 payload through the observed raw-LBA structure without reconstructing the full ISO.
