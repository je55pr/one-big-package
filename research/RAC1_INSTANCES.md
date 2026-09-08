# Ratchet & Clank 1 gameplay instance placements

Authority: `rac1-ntscu-original` (`SCUS-97199`), native levels 0..18.

R&C1 outer positional range 1 begins with WAD-LZ on all 19 authority levels. On the supported NTSC-U build its decompressed pointer table and payloads identify the native NTSC gameplay stream. The findings below were established from the retail bytes first and then compared with Wrench's `RAC_GAMEPLAY_BLOCKS` / packed structs.

## Static-instance blocks

The decompressed gameplay pointer table uses:

- `0x34` -> Tie instance block.
- `0x3c` -> Shrub instance block.
- `0x44` -> Moby instance block.

Tie and Shrub blocks begin with `s32 count` followed by 12 bytes of block-header padding, then fixed-size records.

### Tie placements

R&C1 Tie records are 0xe0 bytes. The directly consumed fields are:

- `0x00`: native class id.
- `0x10`: 16-float column-major transform matrix.
- `0x54`: uid (public correlation; preserved but not needed for geometry placement).

Complete retail census:

- 44,712 instances.
- 19 / 19 arrays end exactly at another positive native gameplay header pointer when sized as `0x10 + count * 0xe0`.
- 0 instance class ids absent from the independently recovered core `0x20` class table.
- 0 non-finite matrix values.

### Shrub placements

Shrub records are 0x70 bytes and share the later game's packed instance layout at the fields needed here:

- `0x00`: native class id.
- `0x10`: 16-float column-major transform matrix.

Complete retail census:

- 25,572 instances.
- 19 / 19 arrays end exactly at another positive native gameplay header pointer when sized as `0x10 + count * 0x70`.
- 0 instance class ids absent from the independently recovered core `0x28` class table.
- 0 non-finite matrix values.

The stored matrix bottom-right word is not uniformly `1`; authority data contains `0` and approximately `0.01`. Geometry placement therefore uses only the upper 3x4 affine basis + translation (`m[0..2]`, `m[4..6]`, `m[8..10]`, `m[12..14]`) and does not normalize or reinterpret the fourth row.

## Moby placements

The `0x44` block uses a 0x10-byte header (`static_count`, `spawnable_moby_count`, two padding words) followed by R&C1-specific 0x78-byte records. Directly validated fields include:

- record `0x00`: size word, always 0x78 in the census.
- `0x18`: class id.
- `0x1c`: uniform scale.
- `0x30`: native XYZ position.
- `0x3c`: native XYZ Euler rotation.

Complete retail census:

- 16,232 static Moby placements.
- every size word is 0x78.
- every class id exists in the independently recovered core `0x18` class table.
- every decoded scale/position/rotation component is finite.
- `spawnable_moby_count` is 256 on levels 0..16 and 18; level 17 records 400.

This proves placement independently of Moby mesh recovery. OBP should keep Moby placement support separate from the still-unfinished R&C1 Moby packet/geometry decoder.

## Evolution versus Going Commando

- R&C1 gameplay header offsets differ from GC (`shrub` and Moby-related blocks move in GC).
- R&C1 Tie instances are much larger (`0xe0`) because their record carries data absent or externalized in the later 0x60-byte GC form.
- Shrub placement's matrix-bearing core is shared at the currently decoded layer.
- Moby records are a distinct 0x78-byte R&C1 generation versus GC's 0x88-byte generation.

No retail gameplay bytes are committed; only structure, counts and validation results are retained.
