# Going Commando tie instances — the instanced environment

**Build:** `rac2-ntscu-v1.01` (`SCUS-97268`, SHA-256 `9db2e33e…a9b1ce5`).

Tfrags are mostly terrain. The built environment — buildings, rock spires, trees,
walls, platforms — is **tie instances**: a per-class mesh placed many times by a
4×4 matrix. Oozla (`LEVEL1`) has **1,617 tie instances** across **91 tie classes**
(and 2,825 shrub instances, not yet decoded). Adding ties takes the Oozla render
from ~26k triangles (tfrags only) to ~700k.

Reproduce:

```
node tools/gc-world.mjs "<GC iso>" --level 1 --no-collision --out captures/level1.world.json
```

## Instance placement — `packages/gc-instances`

The level WAD `gameplay` lump (header slot 2) is **WAD-LZ compressed**. The
decompressed buffer starts with a table of `s32` block pointers; each block's
pointer sits at a fixed byte offset in that table (`GC_UYA_GAMEPLAY_BLOCKS`):

| block | GC pointer offset | packed struct | size |
|---|---|---|---|
| tie instances | `0x34` | `s32 oClass; s32 drawDist; …; Mat4 matrix @ 0x10; …` | `0x60` |
| shrub instances | `0x40` | `s32 oClass; f32 drawDist; …; Mat4 matrix @ 0x10; …` | `0x70` |
| moby instances | `0x4c` | different block header | — |

Each instance block is `{ s32 count; pad to 0x10 }` then `count` packed structs.
`Mat4` is 16 little-endian `f32`, **column-major**: elements `[0,4,8]` / `[1,5,9]`
/ `[2,6,10]` are the basis vectors, `[12,13,14]` the translation. Verified: Oozla
tie instances land in the level's coordinate range with sensible rotations.

## Tie class geometry — `packages/gc-tie`

`LevelCoreHeader.tieClasses` ([`GC_LEVEL_CORE.md`](GC_LEVEL_CORE.md)) is an array
of `TieClassEntry` (0x20): `s32 offsetInAssetWad; s32 oClass; s32; s32;
u8 textures[16]`. The class geometry sits at `assets[offsetInAssetWad]` and runs
to the next section boundary.

```text
GcUyaDlTieClassHeader (0x80)
  0x00 s32 packets[3]     offset to each LOD's packet table
  0x0c u8  packetCount[3]
  0x40 f32 scale
TiePacketHeader (0x10):  0x00 s32 data;  0x08 u8 vertOfs;  0x09 u8 vertSize   (offsets * 0x10)
packet GS data:
  0x00 s32 adGifDestOffsets[4]
  0x10 s32 adGifSrcOffsets[4]
  0x23 u8  stripCount
  0x28 u8  dinkyVerticesSizePlusFour     dinkyCount = (val - 4) / 2
  0x2c TieStrip[stripCount]  { u8 vertexCount; u8 pad; u8 gifTagOffset; u8 windingOrder }
  vertOfs*0x10  TieDinkyVertex[dinkyCount] (0x10) then TieFatVertex[] (0x18)
```

`TieDinkyVertex`: `s16 x,y,z; u16 gsWriteOfs; u16 s,t,q; u16 gsWriteOfs2`.
A vertex with a non-zero, different `gsWriteOfs2` is duplicated at that offset.

**GS-packet walk** (recovers LOD 0): sort vertices by `gsWriteOfs`, drop
consecutive duplicates, then step a `nextOffset` counter (starting at 6):

- `strips[i].gifTagOffset == nextOffset` → start a new primitive with the active
  material slot and winding; `nextOffset += 1`
- `vertices[j].gsWriteOfs == nextOffset` → append the vertex; `nextOffset += 3`
- `adGifDestOffsets[k-1] == nextOffset` → switch material slot to
  `adGifSrcOffsets[k] / 0x50`; `nextOffset += 6`

Per primitive: `pos = s16 * scale / 1024`, `uv = u16 / 4096`, triangle-strip
faces with `(i % 2) == winding` winding. The class-local material slot is mapped
to a level `tie` texture id through `TieClassEntry.textures[slot]`
([`GC_TEXTURES.md`](GC_TEXTURES.md), `readGcLevelTextures(core, "tie")`).

## Verification (retail Oozla)

| | result |
|---|---|
| tie instances parsed | 1,617 / 1,617 |
| tie classes parsed with geometry | 91 / 91 |
| instantiated triangles | 674,252 |
| instantiated bounds vs collision | contained in the level volume |
| visual | the Oozla swamp — mushroom-canopy trees, rock spires, the Megacorp Outlet platform — recognisable in the viewer |

## Not decoded / next

1. **Shrub instances** (2,825 in Oozla) + shrub class geometry (`shrub.cpp`,
   often billboards) — the smaller foliage.
2. **Moby instances** + moby class models — enemies, crates, the vendor, gadgets.
   `GcUyaMobyBlock` header differs; moby models are their own VIF format.
3. Tie LOD 1 / LOD 2, tie ambient RGBA (per-instance baked lighting), normals.
4. Tie/shrub/moby instance grouping (`tie_groups` etc.).
