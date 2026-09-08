# Going Commando shrubs and mobies

**Build:** `rac2-ntscu-v1.01` (`SCUS-97268`, SHA-256 `9db2e33e…a9b1ce5`).

> **Historical scope note:** this document records the instance/static-environment checkpoint at which shrub geometry and Moby placement were first recovered. Its old Moby-model TODO has since been superseded by substantial class-geometry/bind-pose work. For the current Moby geometry/skinning evidence, use [`GC_MOBY.md`](GC_MOBY.md).

Beyond tfrags (terrain) and ties (structures — [`GC_TIES.md`](GC_TIES.md)), the gameplay lump also places **shrubs** (small foliage) and **mobies** (enemies, crates, the vendor, pickups). Oozla (`LEVEL1`): 2,825 shrub instances / 24 classes, 748 moby instances / ~227 classes.

Reference reproduction path:

```bash
cd reference-ts
node tools/gc-world.mjs "<GC iso>" --level 1 --no-collision --out captures/level1.world.json
```

The production C# runtime now contains corresponding GC instance/geometry decoders; this command remains useful as the TypeScript equivalence oracle.

## Shrub instances — TypeScript reference `packages/gc-instances` + `packages/gc-shrub`

Instances: gameplay pointer `0x40`, `ShrubInstancePacked` (0x70): `s32 oClass`, `f32 drawDistance`, …, `Mat4 matrix @ 0x10` (same column-major layout as ties).

Class geometry (`LevelCoreHeader.shrubClasses` → `ShrubClassEntry` 0x30: `s32 offset; s32 oClass; s32; s32; u8 textures[16]; ShrubBillboardInfo`):

```text
ShrubClassHeader (0x40):  0x20 f32 scale;  0x28 s16 packetCount
ShrubPacketEntry[packetCount] @ 0x40:  { s32 offset; s32 size }
each packet is a VIF command list with 3 UNPACKs:
  [0] ShrubPacketHeader { s32 textureCount; s32 gifTagCount; s32 vertexCount; s32 }
      + ShrubVertexGifTag[gifTagCount]  (0x10: u64 tag; u32 regs; s32 gsOffset)
      + ShrubTexturePrimitive[textureCount]  (0x40: ...; s32 gsOffset @ 0x0c; tex0.data_lo @ 0x30)
  [1] ShrubVertexPart1[vertexCount]  { s16 x,y,z; s16 gsOffset }
  [2] ShrubVertexPart2[vertexCount]  { s16 s,t,h; s16 nAndStop }
```

GS-packet walk over a `nextOffset` counter (starting at 0): a GIF tag (`+1`, primitive type from bits 47–49 of the 64-bit tag: `3` = list, `4` = strip), an AD-GIF (`+5`, texture change), or a vertex (`+3`). Positions are `s16 * scale / 1024`, UVs `u16 / 4096`. Class-local material slots map to level `shrub` texture ids through `ShrubClassEntry.textures[]`.

Verified at this checkpoint: 2,825 / 2,825 Oozla shrub instances place across 24 / 24 classes (~853k triangles); the swamp ground-cover is visible in the reference viewer.

## Moby instances — TypeScript reference `packages/gc-instances`

Instances: gameplay pointer `0x4c`. `MobyBlockHeader { s32 staticCount; s32 spawnableMobyCount; s32 pad[2] }` (0x10) then `GcUyaMobyInstance` (0x88):

| off | field |
|---|---|
| `0x00` | `s32 size` — always `0x88` (used as a sanity check) |
| `0x28` | `s32 oClass` |
| `0x2c` | `f32 scale` |
| `0x40` | `Vec3f position` |
| `0x4c` | `Vec3f rotation` — XYZ euler, radians |

`matrixFromPosRotScale()` builds a column-major `Mat4` (`R = Rz·Ry·Rx`).

Verified: 748 Oozla Moby instances parse; 735 sit in the level's coordinate range, 13 are skybox / camera dummies thousands of units up (culled from the old marker-mesh checkpoint).

## Moby geometry status — superseded checkpoint

At the time this document was first written, Moby class models were represented only by oriented marker cubes. That statement is **no longer current**.

OBP subsequently recovered substantial `MobyClassHeader` packet geometry, skeleton/bind data and the working VU0-style skinning path for many GC classes. Some class variants and animation remain unresolved. See [`GC_MOBY.md`](GC_MOBY.md) for the maintained evidence and limitations.

The old marker fallback remains historically useful for understanding why early world captures showed cubes for unresolved objects, but it is not the current project capability boundary.

## Remaining work associated with this area

1. Continue unresolved Moby class variants and animation/sequence archaeology; see [`GC_MOBY.md`](GC_MOBY.md).
2. Shrub billboards (`ShrubBillboardInfo` / `ShrubBillboard`) for distant/simple shrubs.
3. Broader instance semantics, groups, pvars and any remaining per-instance material/lighting behaviour, preserving retail evidence vs runtime interpretation.
