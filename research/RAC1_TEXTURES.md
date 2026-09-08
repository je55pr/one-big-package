# R&C1 terrain textures

Authority: `rac1-ntscu-original` / `SCUS-97199` / SHA-256 `ab849fe7cc9cc81c487d61b0d3ea15b5849943481b6a6ebf4d9aa9cf7bc40d9d`.

This note covers the first directly validated R&C1 texture path: textures referenced by native static terrain. It does not yet claim every dynamic/object/particle texture variant follows the same rules.

## Native path

```text
native level outer range 0
→ 11-range R&C1 level-data directory
   → slot 2: core index
   → slot 3: GS-RAM image
   → slot 10: WAD-LZ core assets
→ core index 0x30: tfrag texture ArrayRange
→ core index 0x60: texture pixel base
→ 0x10-byte TextureEntry records
```

Each validated terrain entry is:

```text
0x00 s32 dataOffset
0x04 s16 width
0x06 s16 height
0x08 s16 nativeType
0x0a s16 paletteSlot
0x0c s16 native word (public tooling: mipmap)
0x0e s16 native word (public tooling: pad)
```

OBP preserves the last two native words without requiring those public semantic names.

## Retail census

All **19 / 19** levels passed strict table/range checks:

- 1,639 native terrain texture-table entries total;
- 1,633 distinct table entries are referenced by terrain VIF material records;
- every recovered texture reference lies inside its native table;
- every entry's `width * height` byte pixel range lies inside decompressed core assets;
- every 256-entry palette lies inside the independent GS-RAM level-data range;
- dimensions observed: 32×32, 64×64, 128×128, and three 256×256 entries on level 17;
- native `type` values observed in this terrain table: `3` (46 entries) and `4` (1,593 entries).

Six levels contain one table entry not referenced by the recovered static terrain. OBP therefore preserves native table entries independently of usage rather than defining the table from references.

| Level | Native table | Referenced | Reference range |
|---:|---:|---:|---:|
| 0 | 78 | 78 | 0–77 |
| 1 | 87 | 86 | 0–86 |
| 2 | 79 | 79 | 0–78 |
| 3 | 66 | 66 | 0–65 |
| 4 | 47 | 47 | 0–46 |
| 5 | 82 | 81 | 0–81 |
| 6 | 138 | 137 | 0–137 |
| 7 | 66 | 66 | 0–65 |
| 8 | 42 | 41 | 0–41 |
| 9 | 59 | 59 | 0–58 |
| 10 | 75 | 75 | 0–74 |
| 11 | 72 | 72 | 0–71 |
| 12 | 142 | 142 | 0–141 |
| 13 | 89 | 88 | 0–88 |
| 14 | 58 | 58 | 0–57 |
| 15 | 155 | 154 | 0–154 |
| 16 | 73 | 73 | 0–72 |
| 17 | 92 | 92 | 0–91 |
| 18 | 139 | 139 | 0–138 |

## Pixel and GS storage

The core index also carries a 16-byte GS allocation table. Across all 19 levels every entry was in range. The only observed storage codes were:

- `0x00` — 32-bit palette allocations;
- `0x13` — 8-bit indexed texture allocations.

For the terrain table, one byte per pixel at `texturesBaseOffset + dataOffset` plus 256 little-endian RGBA32 palette entries at `paletteSlot * 0x100` decodes correctly with the existing PS2 rules:

1. swap CLUT index bits 3 and 4 when they differ;
2. scale PS2 alpha `0..128` into `0..255`;
3. terrain pixel bytes themselves remain linear for this R&C1 path.

An internal level-0 texture atlas produced coherent rock, metal, panel and illuminated environment surfaces. No retail image output is committed.

## Public corroboration

Pinned comparison: `chaoticgd/wrench@e67ea46e5e60ec52a0c31fcde1e3f46b20aa4cfb`.

- `src/wrenchbuild/level/level_textures.h` defines the same 0x10 `TextureEntry` and GS storage values `0`, `1`, `0x13`.
- `src/wrenchbuild/level/level_textures.cpp` reads `width*height` index bytes, a 256-entry palette at `palette * 0x100`, applies alpha multiplication and palette swizzle, and only applies pixel swizzling to Deadlocked.

Those sources corroborate the directly observed R&C1 data; they are not the authority for the census.

## R&C1 ↔ GC classification

For the verified static-terrain texture-table path: **identical concept / identical recovered binary structure and PS2 decode rules**.

OBP's common decoder now lives in `packages/rc-level-textures`; `gc-level-textures` is a compatibility wrapper around it. R&C1 still owns its distinct outer level-data/container mapping.

Unresolved: exact meanings of native TextureEntry `type`, `0x0c`, and `0x0e`; mip selection/runtime sampling; blending/GIF material flags; non-terrain texture families.
