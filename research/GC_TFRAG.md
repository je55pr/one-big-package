# Going Commando tfrags — static level render geometry

**Build:** `rac2-ntscu-v1.01` (`SCUS-97268`, SHA-256 `9db2e33e…a9b1ce5`).

Tfrags ("terrain fragments") are the static, non-instanced level mesh. Reproduce:

```
node tools/gc-world.mjs "<GC iso>" --level 1 --no-collision --out captures/level1-tfrags.world.json
```

## Where the tfrag blob lives

| level kind | source |
|---|---|
| streamed (chunks) — `LEVEL` 1,2,4,7,8,11,19,20 | chunk `ChunkHeader.tfrags` -> WAD-LZ block. Chunk 0 is the detailed near geometry; chunks 1–2 are low-detail far-LOD proxies. |
| all others | level-core section at `LevelCoreHeader.tfrags` (offset `0` — the first section of the decompressed asset blob), size to the next section start ([`GC_LEVEL_CORE.md`](GC_LEVEL_CORE.md)). |

## Blob layout

```text
TfragsHeader   0x00 s32 tableOffset   0x04 s32 tfragCount   0x08 f32   0x0c u32
TfragHeader (0x40), per tfrag:
  0x00 f32 bsphere[4]
  0x10 s32 data           byte offset from tableOffset to this tfrag's data region
  0x14 u16 lod2Ofs   0x16 u16 sharedOfs   0x18 u16 lod1Ofs   0x1a u16 lod0Ofs
  0x1c u16 texOfs    0x1e u16 rgbaOfs
  0x20 u8 commonSize 0x21 u8 lod2Size 0x22 u8 lod1Size 0x23 u8 lod0Size
  0x27 u8 baseOnly   0x28 u8 textureCount   0x29 u8 rgbaSize (units of 4)
  0x3c u8 vertCount  0x3d u8 triCount
```

Each tfrag's data region holds **VIF1 command lists** (`packages/ps2-vif`) that
in hardware fill VU1 memory:

| list | byte range within the data region | UNPACKs used for LOD 0 recovery |
|---|---|---|
| common | `[sharedOfs, lod1Ofs)` | `[0]` VU header, `[1]` `V4_32` texture GIF A+D (`tex0.data_lo` = level texture id), `[2]` `V4_16` `TfragVertexInfo {s16 s,t,parent,vertex}`, `[3]` `V3_16` `TfragVertexPosition {s16 x,y,z}`. Raw packet **5** is a `STROW` holding the base position. |
| LOD 01 | `[lod0Ofs, sharedOfs + lod1Size*0x10)` | trailing `V4_16` vertex info, `V3_16` positions |
| LOD 0 | `[sharedOfs + lod1Size*0x10, rgbaOfs - (lod1Size + lod2Size - commonSize)*0x10)` | `V3_16` positions, `V4_8` strips (`TfragStrip {s8 vertexCountAndFlag, s8 eop, s8 adGifOffset, s8}`), `V4_8` indices, then optional `V4_8` parent/unknown and `V4_16` vertex info |
| RGBA | `blob[data + rgbaOfs ..]`, `rgbaSize*4` × `{u8 r,g,b,a}` | baked per-vertex colour |

## Reconstruction (`packages/gc-tfrag`, LOD 0)

- `positions = common ++ lod01 ++ lod0`; `vertexInfos = common ++ lod01 ++ lod0`.
- One mesh vertex per `vertexInfo`: position = `positions[vertexInfo.vertex >> 1]`,
  world = `(base + delta) / 1024`, converted Z-up → OBP Y-up.
- UV = `s16 / 4096` (with the retail quirk: negative components are halved).
- Colour = `rgbas[vertexInfo.vertex >> 1]`.
- Strips walk over the `indices` array (values index `vertexInfos`): a strip with
  `vertexCountAndFlag <= 0` sets the active GIF A+D (`adGifOffset / 5`) and adds
  128 to the count; even counts are quad strips, odd are triangle strips.
- `toObpTfragMeshes()` groups triangles by texture id → one `OBPMesh` per id with
  `geometry.colors` + `geometry.uvs`.

## Verification (retail Oozla, `LEVEL1.WAD` chunk 0)

| | result |
|---|---|
| tfrags parsed | 401 / 401 |
| output | 28,418 vertices, 26,192 triangles, 77 texture ids |
| longest triangle edge (all meshes) | 15.6 units — no stretched geometry |
| non-manifold edges / degenerate triangles | 0 |
| tfrag mesh AABB vs level-core collision AABB | contained: tfrag native x 227–469 ⊂ collision 159–541; y 304–650 ⊂ 245–663; z 101–163 ⊂ 50–157 |
| visual | recognisable Oozla lily-pad platforms + structures in the viewer |

## Not decoded / next

1. **Textures** — the `tex0.data_lo` ids map into `LevelCoreHeader` texture
   `ArrayRange`s (`TextureEntry` 16 B) + `GsRamEntry` palettes; PS2 unswizzle +
   CLUT. This turns the grey/vertex-coloured render into a textured one.
2. Normals (spherical `TfragLight {azimuth, elevation}` per vertex).
3. LOD 1 / LOD 2, the tface parent hierarchy, GIF state beyond `tex0`.
4. Moby / tie / shrub instance models (a separate track — same VIF machinery).
