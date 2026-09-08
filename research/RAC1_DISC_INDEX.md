# R&C1 raw disc index and native level headers

Authority build: `rac1-ntscu-original` / `SCUS-97199` / NTSC-U original retail.

Payload SHA-256 (already authority-verified by Stage 0):
`ab849fe7cc9cc81c487d61b0d3ea15b5849943481b6a6ebf4d9aa9cf7bc40d9d`.

This document records direct bounded reads from the retail authority disc. Public tooling is cited only as corroboration / a source of hypotheses. No game bytes are stored here.

## Result

R&C1 does **not** expose its level containers through ISO-9660. Its hidden raw-disc index begins at absolute DVD LBA **1500** (`0x2ee000` bytes at 2048 bytes/sector).

The retail index begins:

| Offset | Direct retail observation |
|---|---|
| `0x0000` | signed word `1` |
| `0x0004` | signed word `0x2960` (10,592 bytes) |

The final 19 pairs start at index offset `0x28c8`. For every pair in this authority build:

- the first word points to an on-disc structure whose first word is the native level id;
- those ids are exactly `0..18` in table order;
- the structure's second word is exactly `0x2434` for all 19 entries;
- only the **first** table word has a retail-confirmed meaning so far (level-header LBA);
- the second table word is preserved by OBP as `rawSecondWord`. Do not call it a file length until executable or other retail evidence establishes that meaning.

`packages/rac1-disc-index` now encodes only those supported-build facts. It reads one 10,592-byte index plus a 40-byte prefix per present level header: 11,352 bytes total for this retail catalogue.

## Native level-header prefix

Direct retail layout established so far:

```text
0x00  s32  native level id
0x04  s32  native header size = 0x2434
0x08  {u32 sector, u32 sectors}  positional range 0
0x10  {u32 sector, u32 sectors}  positional range 1
0x18  {u32 sector, u32 sectors}  positional range 2
0x20  {u32 sector, u32 sectors}  positional range 3
...   remainder of 0x2434-byte header not yet adopted as native semantics by OBP
```

The four range names remain positional in production code. Wrench calls them `data`, `gameplay_ntsc`, `gameplay_pal`, and `occlusion`; those names are useful leads, not retail semantic authority yet.

The 0x2434-byte header occupies five 0x800-byte sectors on disc. On this authority build, all four positional ranges follow it contiguously. For levels 0 through 17, the end of range 3 is **exactly** the LBA of the next native level header.

## Retail census

Notation `LBA+sectors` records the raw pair directly; it does not assign a semantic name.

| table | native id | header LBA | raw second word | range 0 | range 1 | range 2 | range 3 |
|---:|---:|---:|---:|---:|---:|---:|---:|
| 0 | 0 | 1885903 | 19462 | 1885908+6562 | 1892470+210 | 1892680+210 | 1892890+7 |
| 1 | 1 | 1892897 | 28979 | 1892902+9113 | 1902015+250 | 1902265+250 | 1902515+12 |
| 2 | 2 | 1902527 | 27477 | 1902532+8027 | 1910559+257 | 1910816+257 | 1911073+19 |
| 3 | 3 | 1911092 | 37363 | 1911097+8349 | 1919446+312 | 1919758+312 | 1920070+26 |
| 4 | 4 | 1920096 | 17250 | 1920101+7859 | 1927960+220 | 1928180+220 | 1928400+9 |
| 5 | 5 | 1928409 | 39290 | 1928414+9033 | 1937447+282 | 1937729+282 | 1938011+14 |
| 6 | 6 | 1938025 | 30312 | 1938030+9311 | 1947341+283 | 1947624+283 | 1947907+15 |
| 7 | 7 | 1947922 | 29527 | 1947927+7855 | 1955782+216 | 1955998+216 | 1956214+10 |
| 8 | 8 | 1956224 | 33488 | 1956229+7717 | 1963946+360 | 1964306+360 | 1964666+17 |
| 9 | 9 | 1964683 | 11275 | 1964688+8255 | 1972943+285 | 1973228+285 | 1973513+13 |
| 10 | 10 | 1973526 | 21649 | 1973531+8126 | 1981657+273 | 1981930+273 | 1982203+13 |
| 11 | 11 | 1982216 | 35236 | 1982221+9027 | 1991248+255 | 1991503+255 | 1991758+12 |
| 12 | 12 | 1991770 | 25386 | 1991775+8785 | 2000560+358 | 2000918+358 | 2001276+24 |
| 13 | 13 | 2001300 | 20245 | 2001305+9047 | 2010352+232 | 2010584+232 | 2010816+14 |
| 14 | 14 | 2010830 | 25683 | 2010835+8862 | 2019697+262 | 2019959+262 | 2020221+12 |
| 15 | 15 | 2020233 | 26282 | 2020238+9094 | 2029332+256 | 2029588+256 | 2029844+20 |
| 16 | 16 | 2029864 | 26879 | 2029869+8392 | 2038261+365 | 2038626+365 | 2038991+21 |
| 17 | 17 | 2039012 | 14518 | 2039017+8065 | 2047082+207 | 2047289+207 | 2047496+10 |
| 18 | 18 | 2047506 | 33170 | 2047511+8713 | 2056224+357 | 2056581+357 | 2056938+21 |

Machine-readable metadata is in `research/generated/rac1-level-index-census.json`.

## Compression observation at this boundary

All 19 range-1 and all 19 range-2 starts have the three-byte `WAD` magic and the same unaligned signed compressed-size field at byte offset 3 that OBP's existing `wad-lz` decoder expects.

The existing codec was exercised directly against all **38** retail streams. Every stream decompressed successfully to its declared compressed-byte boundary. For each level, the two streams decompress to the same output length, while none of the NTSC/PAL outputs are byte-identical. This directly validates codec reuse for these R&C1 streams; it does **not** yet establish the gameplay semantics of every decompressed field.

Examples:

| level | range 1 compressed | range 2 compressed | decompressed bytes (both) |
|---:|---:|---:|---:|
| 0 | 429,708 | 429,720 | 1,113,280 |
| 1 | 510,928 | 510,871 | 1,430,528 |
| 9 | 583,043 | 582,997 | 1,772,096 |
| 18 | 730,428 | 730,261 | 2,204,224 |

## Public corroboration and discrepancy log

### Wrench

Snapshot: `chaoticgd/wrench` commit `e67ea46e5e60ec52a0c31fcde1e3f46b20aa4cfb` (current inspected head on 2026-09-07).

Relevant paths:

- `src/iso/table_of_contents.h`
  - `RAC_TABLE_OF_CONTENTS_LBA = 1500`
  - `RacWadInfo` size `0x2960`
  - final `SectorRange levels[19]` at `0x28c8`
  - on-disc `Rac1AmalgamatedWadHeader` size `0x2434`
- `src/iso/table_of_contents.cpp`
  - detects R&C1 level headers by reading candidate LBAs and checking header word 1 for `0x2434`
  - notably, Wrench's writer sets the second level-table word to `1`; therefore OBP must not treat the retail second word as an established level-file length merely from Wrench's `SectorRange` type name
- `src/wrenchbuild/level/level_wad.cpp`
  - public names for the four prefix ranges are `data`, `gameplay_ntsc`, `gameplay_pal`, `occlusion`

This public structure matches the directly observed retail offsets and sizes, but OBP keeps field semantics at the evidence level actually established from retail.

### `rac-dvd-toc-parser`

Snapshot: `maikelwever/rac-dvd-toc-parser` commit `9a82e13aba31495b81d37fa50cce1527cfb9df89` (2019-11-24).

Its parser also starts at LBA 1500, but splits the index into counts `479 / 240 / 165 / 90 / 900` and allocates 38 trailing `leveldirs` words without parsing them. The total byte count still happens to reach `0x2960`.

Wrench's later layout instead divides that same area as `479 / 240 / 167 / 88 / 900 / 19 pairs`, and the retail NTSC-U bytes strongly support the latter final-19-pairs interpretation because every first word points to a valid 0x2434 header with native id 0..18.

## R&C1 -> GC evolution status

| Concept | R&C1 authority result | GC authority result | Relationship |
|---|---|---|---|
| ordinary ISO level files | none | numbered `/G/LEVEL*.WAD` | redesigned storage boundary |
| disc/world index | fixed raw index at LBA 1500 | `RC2.HDR` + ISO files | same purpose, redesigned format |
| per-level outer representation | 0x2434 on-disc amalgamated header addressed by raw LBA | 0x60 retail level-WAD header | same purpose, redesigned format |
| regional gameplay-like streams | two adjacent WAD-LZ ranges per level | authority GC build uses its own level-WAD layout | conceptual lineage likely; binary/container relationship still being established |
| WAD LZ codec | retail-validated on all 38 paired streams | retail-validated previously | identical codec at this boundary |

## Next unanswered questions

1. Establish the native semantics and structure of positional range 0 independently from public naming.
2. Map its first-level data header and locate the native level-core index/data ranges.
3. Decode enough of the native core to reach collision without assuming GC's core layout.
4. Correlate the 19 native ids with planet/location names from retail/executable sources rather than a handwritten game list.
5. Determine the real meaning of the level-table second word from executable use or stronger retail correlation.
