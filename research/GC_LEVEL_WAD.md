# Going Commando level WAD — outer container header

**Build:** `rac2-ntscu-v1.01` — Going Commando NTSC-U v1.01 retail, serial
`SCUS-97268`, payload SHA-256 `9db2e33e276133cc283647fa3279b37911955e123d6199d10065547eaa9b1ce5`
(local ISO verified to hash exactly to the canonical authority manifest).

**Source files:** `/G/LEVEL0.WAD` … `/G/LEVEL26.WAD` (27 files) inside the ISO-9660
filesystem. See [`RETAIL_TRILOGY_DISC_LAYOUT.md`](RETAIL_TRILOGY_DISC_LAYOUT.md)
for why Going Commando is the only trilogy game whose level data is file-addressable.

Reproduce the raw header bytes:

```
node tools/container-headers.mjs "<GC iso>" --glob "/G/LEVEL{0..26}.WAD" --bytes 0x70
```

## Confirmed structure

All fields are unsigned 32-bit **little-endian**. The header occupies disc sector 0
of the WAD (bytes `0x00`–`0x5F`).

| Offset | Field | Type | Observed on `rac2-ntscu-v1.01` | Evidence | Shared w/ RC3? |
|---|---|---|---|---|---|
| `0x00` | `headerSize` | u32 | `0x60` on **all 27** files | directly evidenced | unknown (RC3 has no ISO-9660 level WADs) |
| `0x04` | `sector` | u32 | `0` in the standalone file; the container's disc LBA in the `RC2.HDR` copy | directly evidenced (see [`GC_LEVEL_LOADING.md`](GC_LEVEL_LOADING.md)) | — |
| `0x08` | `levelId` | u32 | file number for 26 of 27; `LEVEL21.WAD` → **30** | directly evidenced | unknown |
| `0x0c` | `reverb` | u32 | `{0, 3, 4}` (see below) | Wrench field name; retail values are small ints consistent with a reverb preset | unknown |
| `0x10` | `lump[0..9]` | `SectorRange[10]` | 3–10 present per file | directly evidenced | unknown |

### Slot → field (Wrench `GcUyaLevelWadHeader`, cross-checked against retail bytes)

| slot | field | retail evidence |
|---|---|---|
| 0 | `data` (level core) | largest lump; contains `GcUyaLevelDataHeader` |
| 1 | `sound_bank` | `"SBlk"` magic at the lump start, WAD index 0 |
| 2 | `gameplay` (instances) | `"WAD"` LZ block |
| 3 | `occlusion` | tiny `(index, value)` table |
| 4 | `chunks.chunks[0]` | `ChunkHeader { s32 tfrags; s32 collision }` + two WAD-LZ blocks |
| 5 | `chunks.chunks[1]` | same |
| 6 | `chunks.chunks[2]` | present only on `LEVEL19`, `LEVEL20` |
| 7 | `chunks.sound_banks[0]` | `"SBlk"` magic, WAD index 1 |
| 8 | `chunks.sound_banks[1]` | `"SBlk"` magic, WAD index 2 |
| 9 | `chunks.sound_banks[2]` | `LEVEL19`, `LEVEL20` only |

`ChunkWadHeader` = `SectorRange chunks[3]; SectorRange sound_banks[3];`. Collision
inside `chunks[i]` is decoded in [`GC_COLLISION.md`](GC_COLLISION.md).

`SectorRange` = `{ u32 offsetSectors; u32 sizeSectors }`, sector = 2048 bytes.
Byte range of a lump is `offsetSectors*2048` .. `+ sizeSectors*2048`.

### Lump table is positional, not disc-ordered

On disc the present lumps are laid out in the order
`slot 1, slot 0, slot 2, slot 3, slot 4, slot 5, (slot 6), slot 7, slot 8, (slot 9)`
— i.e. **slot 1 physically precedes slot 0**. Slot index is therefore a *field
identity* (as in Wrench's named-field `GcUyaLevelWadHeader`), not a sequence
position. The parser preserves slot indices verbatim and does not reorder.

### Observed layout invariants (descriptive — the parser does NOT require them)

For every one of the 27 retail files:

- Present lumps tile the file **contiguously from sector 1** with no gaps or
  overlaps (sector 0 is the header).
- Slot 1 is always `{ offsetSectors: 1, sizeSectors: slot0.offsetSectors - 1 }`.
- The sector-rounded end of the last lump overruns the real ISO-9660 file size by
  **< 1 sector** (96–2016 bytes of tail padding).
- Slot 6 appears **only** when slots 7, 8 and 9 also appear (`LEVEL19`, `LEVEL20`).

`tools/gc-level-wad.mjs` re-verifies all of the above from the ISO and is the
source of [`generated/rac2-level-catalogue.json`](generated/rac2-level-catalogue.json).

### `unknown0x0c`

| value | files |
|---|---|
| `0` | LEVEL 1–5,7–11,13,15,16,18–26 |
| `3` | LEVEL 0, 6, 12, 14 |
| `4` | LEVEL 4, 17 |

Not the lump count (LEVEL4 has value 4 with 8 lumps; LEVEL0 has value 3 with 4
lumps; LEVEL3 has value 0 with 4 lumps). Left as `unknown0x0c`.

### Lump slot size profiles

Across the 27 files, in sectors (field names from the slot→field table above):

| slot | typical size | notes |
|---|---|---|
| 0 `data` | 3,300 – 8,200 | largest lump; the level core |
| 1 `sound_bank` | = `slot0.offset − 1` | small leading block, sector 1 → slot 0 |
| 2 `gameplay` | 200 – 510 | present in all 27 |
| 3 `occlusion` | 2 – 25 | tiny; absent in LEVEL10/24/25 |
| 4 `chunks[0]` | 420 – 1,680 | present in 8 files (streamed levels) |
| 5 `chunks[1]` | 116 – 1,340 | present in 8 files (always with slot 4) |
| 6 `chunks[2]` | 276 – 497 | LEVEL19/20 only |
| 7, 8 `sound_banks[0..1]` | equal-sized, 45 – 214 | pair on chunked levels |
| 9 `sound_banks[2]` | equal to slots 7/8 | LEVEL19/20 only |

Interpretation of the lump *contents* is done per lump against its own bytes:
`chunks[i]` collision is decoded in [`GC_COLLISION.md`](GC_COLLISION.md); the
`data` core, `gameplay` instances and tfrags remain undecoded.

## Relationship to Wrench

`research/WRENCH_FORMAT_MATRIX.md` records that Wrench routes Going Commando and
UYA level WADs through one `GcUyaLevelWadHeader` / `unpack_gc_uya_level_wad` path,
and that Wrench additionally has a Going Commando branch where `header_size == 0x68`
carrying separate NTSC/PAL gameplay ranges.

**This retail NTSC-U v1.01 build uses `header_size == 0x60`, not `0x68`.** The
`0x68` Wrench branch does not describe this build. Whether it describes a PAL
Going Commando build or a different revision is not something our discs can
answer. `packages/gc-level-wad` deliberately rejects any `headerSize != 0x60` so
that a `0x68` (or other-family) container is a hard parse error rather than a
silent misparse.

## Parser

`packages/gc-level-wad` (`readGcLevelWadHeader`, `openGcLevelWadLump`,
`analyzeGcLevelWadTiling`). It performs bounded reads only (one 0x60-byte header
read; lump payloads are never read), preserves every raw field, exposes each
present lump as a clamped `SubRangeReader`, and rejects: short source, wrong
`headerSize`, a lump with size but no offset, a lump offset inside the header or
past the source, and sector-field overflow. Tests: `tests/gc-level-wad.test.mjs`.
