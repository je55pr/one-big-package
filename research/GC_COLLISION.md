# Going Commando collision — chunk collision decoded

**Build:** `rac2-ntscu-v1.01` (`SCUS-97268`, SHA-256 `9db2e33e…a9b1ce5`).

Reproduce:

```
node tools/gc-collision.mjs "<GC iso>" --all
node tools/gc-collision.mjs "<GC iso>" --level 1 --obj level1.collision.obj
```

## Path from the level WAD to a collision mesh

```
LEVEL<n>.WAD                     GcUyaLevelWadHeader, header slots (research/GC_LEVEL_WAD.md)
  slot 4/5/6  = chunks.chunks[0..2]   SectorRange
      +0x00  ChunkHeader { s32 tfrags; s32 collision }   byte offsets within the chunk range
      +collision  WAD-LZ block  ── decompress ──▶  RC octree collision
  slot 7/8/9  = chunks.sound_banks[0..2]
```

Slot→field mapping was cross-checked with Wrench's `GcUyaLevelWadHeader`
(`0x60`; our retail build is the `0x60` variant, **not** the `0x68`
`GcLevelWadHeader68`) and confirmed by the retail bytes: slot 1 / 7 / 8 all
carry the `"SBlk"` sound-bank magic, slot 3 is the tiny occlusion table, etc.

Only the 8 large streamed levels have chunks: `LEVEL` 1, 2, 4, 7, 8, 11, 19, 20
(19 & 20 have 3 chunks; the rest have 2). The other 19 levels keep their
collision in the `data` / level-core lump (slot 0) — decoded via
`packages/gc-level-core` (see [`GC_LEVEL_CORE.md`](GC_LEVEL_CORE.md)). For a
streamed level, its level-core collision section equals chunk 0's collision.

`tools/gc-collision.mjs --all` now covers **all 27 levels**: streamed levels
through the chunk path, the rest through the level-core path. Every one parses
cleanly.

## WAD-LZ container — `packages/wad-lz`

```text
0x00  "WAD"
0x03  s32 compressedSize   (little-endian, unaligned; measured from 0x00)
0x07  u8[9]                 name / padding — retail values vary, sometimes garbage
0x10  LZ77 packet stream    ends at 0x00 + compressedSize
```

Packet grammar (implemented from the format, verified against retail data — see
`packages/wad-lz/src/index.ts` for the full description):

| flag | meaning |
|---|---|
| `0x00` | big literal, `next + 18` bytes |
| `0x01`–`0x0f` | literal, `flag + 3` bytes (may not be followed by another literal) |
| `0x10`–`0x1f` | far match; `lookback == out` + `size != 1` → skip to next `0x1000` boundary, end packet |
| `0x20`–`0x3f` | medium/big match |
| `0x40`–`0xff` | little match |

Every match packet is followed by `(secondToLastByteRead & 3)` inline literal
bytes. Match copies are byte-by-byte (overlapping runs expand).

Verified: retail `LEVEL1.WAD` chunk 0 collision decompresses 2,339,529 → 2,836,912
bytes; the result begins with the collision header `mesh=0x40, hero_groups=0x2abb70`.
Some chunk collision blocks decompress to a common per-level size with trailing
zero padding (the octree ignores anything its offsets don't reach).

## RC octree collision — `packages/rc-collision`

Shared across RC1/RC2/RC3/Deadlocked (one Wrench `read_collision` entry point).
All multi-byte fields little-endian.

```text
CollisionHeader @ 0x00
  s32 meshOffset          0x40 on retail (8-byte header padded to 0x40)
  s32 heroGroupsOffset    0 when absent; else mesh spans [meshOffset, heroGroupsOffset)

octree, offsets relative to meshOffset:
  root:   s16 zCoord;  u16 zCount;  u16 zOffsets[zCount]     zOffsets[i]*4 → z-node; 0 = empty
  z-node: s16 yCoord;  u16 yCount;  u32 yOffsets[yCount]     0 = empty
  y-node: s16 xCoord;  u16 xCount;  u32 xOffsets[xCount]     (xOffsets[i] >> 8) → octant; 0 = empty
  octant: u16 faceCount; u8 vertexCount; u8 quadCount        (faceCount >= quadCount)
          u32 packedVertex[vertexCount]
          { u8 v0; u8 v1; u8 v2; u8 type }[faceCount]
          u8 v3[quadCount]                                   promotes the first quadCount faces to quads

packedVertex bit layout (all signed):
  x = signed(bits 0..9)  / 16
  y = signed(bits 10..19) / 16
  z = signed(bits 20..31) / 64
world vertex = local vertex + (gridX*4 + 2, gridY*4 + 2, gridZ*4 + 2)
  where gridX = xCoord + x-index, etc.
```

`type` is the native collision surface / material id (0..255). Quads become two
triangles `(v0,v1,v2)` + `(v0,v2,v3)`; the `type` byte is kept per triangle.
Hero ("Ratchet") collision groups at `heroGroupsOffset` are counted but not yet
decoded.

### Verification on the real build

`tools/gc-collision.mjs --all` runs the full pipeline over every chunked GC
level. All 18 chunk collision blocks parse with no out-of-range offset, no bad
vertex index, plausible bounds, and material-id bytes in range. Examples:

| Level | chunk | compressed → decompressed | octants | vertices | triangles | hero groups | material ids |
|---|---|---|---|---|---|---|---|
| LEVEL1 | 0 | 2,339,529 → 2,836,912 | 22,404 | 368,937 | 308,108 | 346 | 2,3,4,9,10,12,31,63,73,95,127 |
| LEVEL1 | 1 | 204,558 → 289,104 | 4,210 | 33,840 | 20,815 | 25 | 3,4,12,31 |
| LEVEL2 | 0 | 1,525,270 → 2,315,920 | 25,471 | 258,329 | 230,007 | 4 | 1,8,9,10,12,15,31,63,95 |
| LEVEL19 | 0/1/2 | — | 29,548 / 21,483 / 2,520 | — | 212,454 / 195,317 / 43,511 | — | — |

Every LEVEL1 collision triangle has an edge length ≤ 33 units (< the 4-unit
octant's diagonal scale), consistent with per-octant local geometry.

### OBP normalization

`toObpCollisionMesh()` produces an `OBPCollisionMesh` with world-space positions,
one triangle list, `triangleMaterialIds` = native `type` bytes, and provenance
notes (`meshOffset`, `heroGroupsOffset`, counts). `tools/gc-collision.mjs
--world out.json` writes a standalone collision-only `OBPWorld`.

## Confidence

| Claim | Status |
|---|---|
| chunk `ChunkHeader { tfrags, collision }` offsets | confirmed (retail bytes + Wrench agree) |
| WAD-LZ container + packet grammar | confirmed (retail chunk data round-trips to a valid octree) |
| RC octree collision layout, vertex bit-packing, world transform | confirmed (18/18 retail chunk blocks parse; geometry sane) |
| `type` byte = collision surface/material id | inferred (Wrench; values are plausible small ints, not verified against in-game surface behaviour) |
| level-core primary collision (all 27 levels) | confirmed — `packages/gc-level-core`; decompressed blob length matches `assetsDecompressedSize` and the collision section parses through `rc-collision` for 27/27 |
| hero collision group internals | **not decoded** (count only) |

## Not yet decoded / next

1. Hero collision groups (`PackedHeroCollisionGroup`: bsphere xyzr /64, tri/vertex
   counts, data offset; vertices `u16 xyz /64`; triangles `u8 v0 v1 v2`).
2. Chunk tfrags (WAD-LZ block at `ChunkHeader.tfrags`) — static render geometry,
   the next target after collision.
3. `LevelCoreHeader` texture tables + `gs_ram` palettes + PS2 texture pixel decode.
