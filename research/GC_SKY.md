# Going Commando sky

**Build:** `rac2-ntscu-v1.01` (`SCUS-97268`, SHA-256 `9db2e33e…a9b1ce5`).

The sky is `LevelCoreHeader.sky` — a byte range of the decompressed level-core asset blob (same blob as tfrags / collision; section end = the next section offset above it). It is a set of up to 8 concentric **shells**: a big untextured gouraud backdrop plus smaller textured cloud / haze layers. The game redraws it centred on the camera every frame with no depth write.

The original decoder lives in the TypeScript reference as `reference-ts/packages/gc-sky` (`readGcSky(bytes)`, `readGcLevelSky(core)`), with the corresponding native C# implementation under `OBP.RAC2`.

Reference reproduction:

```bash
cd reference-ts
node tools/gc-world.mjs "<GC iso>" --level 1 --out captures/level1.world.json
# add --no-sky to omit it
```

## Layout

```text
SkyHeader (0x40), little-endian
  0x00 u8  colour[4]          r, g, b, a   (a == 0x80 => opaque)
  0x04 s16 clear_screen
  0x06 s16 shell_count        <= 8
  0x0a s16 maximum_sprite_count
  0x0c s16 texture_count
  0x0e s16 fx_count
  0x10 s32 texture_defs       -> SkyTexture[texture_count]
  0x14 s32 texture_data       base for the SkyTexture offsets
  0x18 s32 fx_list            0x1c s32 sprites
  0x20 s32 shells[8]          -> RacGcSkyShellHeader

RacGcSkyShellHeader (RAC / GC)
  0x00 s32 cluster_count
  0x04 s32 flags              bit0 set => untextured (gouraud) shell
  0x10 SkyClusterHeader[cluster_count]
(UYA / DL use UyaDlSkyShellHeader: s16 cluster_count, s16 flags, Vec3s16
 rotation, Vec3s16 angular_velocity — not needed for GC.)

SkyClusterHeader (0x20)
  0x00 Vec4f bounding_sphere
  0x10 s32 data               base offset, within the sky section, for this cluster
  0x14 s16 vertex_count       0x16 s16 tri_count
  0x18 u16 vertex_offset      0x1a u16 st_offset      0x1c u16 tri_offset
  0x1e s16 data_size

SkyVertex   (0x08):  s16 x, y, z, alpha        pos = xyz / 1024
SkyTexCoord (0x04):  s16 s, t                  uv  = st / 4096
SkyFace     (0x04):  u8 i0, i1, i2, texture    texture 0xff => untextured face

SkyTexture  (0x10):  s32 palette_offset, texture_offset, width, height
```

- Vertex alpha: `0x80 -> opaque`, else `alpha * 2` (0..255). RGB is always white in the sky format; colour is the texture, or `SkyHeader.colour` for gouraud shells. The per-vertex alpha is the horizon / edge fade and is carried through by the reconstructed mesh.
- Faces: winding is reversed on read (`i2, i1, i0`) to match the retail render.
- Textures: 8-bit paletted, pixels linear `width * height` at `texture_data + texture_offset`, a 256-entry RGBA CLUT at `texture_data + palette_offset`. Decoded with the shared indexed PS2 texture path (GS CLUT reorder + 0..128 alpha scale; GC is not Deadlocked so pixels are not swizzled).

## Verification (retail)

| level | shells | textures | tris | notes |
|---|---|---|---|---|
| `LEVEL1` (Oozla)  | 4 | 3 (128², 512×128, 512×256) | 1,694 | gouraud backdrop + 3 cloud layers |
| `LEVEL2` (Maktar) | 4 | 9 | 4,864 | starfield — a space station, no ground |
| `LEVEL3` | 8 | 7 | 2,782 | Endako (Megapolis) — cloud-deck cityscape |
| `LEVEL4` (Endako) | 8 | 7 | 3,196 | |
| `LEVEL19` | 6 | 7 | 5,536 | night sky, stars + aurora + mountain silhouettes |

Sky parses for all 27 levels (probed). Every shell's positions fall in a `±31`-unit dome (`x`/`y` symmetric, `z` from below to above the horizon), and textures decode at power-of-two sizes.

## Presentation note

The shell geometry/texture decode above is retail-backed. **How a debug/runtime renderer places that shell is presentation policy.**

The preserved TypeScript reference (`reference-ts/tools/gc-world.mjs` + `reference-ts/apps/viewer`) scales the shell set to a camera-centred backdrop and keeps it out of world bounds. The native Godot runtime also treats reconstructed sky shells as camera-relative presentation, and active GC runtime work continues to refine how individual shell types, alpha and atmosphere should be represented.

Do not turn a particular debug-viewer scale or camera trick into a claim about native level geometry. The level background/fog source data is documented separately in [`GC_LEVEL_SETTINGS.md`](GC_LEVEL_SETTINGS.md).

## Next

1. UYA / DL shell rotation + angular velocity.
2. Sky sprites (`maximum_sprite_count`, `fx_list`) — sun / lens-style elements and other effects not covered by shell geometry alone.
