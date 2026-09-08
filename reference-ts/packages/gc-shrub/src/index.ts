import type { GcLevelCore } from "../../gc-level-core/src/index.js";
import { readVifCommandList, filterVifUnpacks } from "../../ps2-vif/src/index.js";

/**
 * Reader for Going Commando / UYA **shrub** class geometry — the small instanced
 * foliage (plants, ferns, grass clumps). Oozla has 2,825 shrub instances.
 *
 * A shrub class is a `ShrubClassHeader` (0x40) then `packetCount`
 * `ShrubPacketEntry {s32 offset; s32 size}`. Each packet is a VIF command list
 * with three UNPACKs: a header (`ShrubPacketHeader` + GIF-tag table + AD-GIF
 * table), then two parallel per-vertex arrays (`ShrubVertexPart1`: `s16 x,y,z`,
 * GS write offset; `ShrubVertexPart2`: `s16 s,t,h`, normal + strip-stop bit).
 * A GS-packet walk over a `nextOffset` counter emits triangle-list / -strip
 * primitives, switching texture on AD-GIF events.
 *
 * Verified against retail Going Commando (see research/GC_SHRUBS.md). Implemented
 * from the format description.
 *
 * ```text
 * ShrubClassHeader (0x40):  0x20 f32 scale;  0x24 s16 oClass;  0x28 s16 packetCount;  0x2c s32 normalsOffset
 * ShrubPacketHeader:  0x00 s32 textureCount;  0x04 s32 gifTagCount;  0x08 s32 vertexCount
 *   then ShrubVertexGifTag[gifTagCount] (0x10: u64 tag; u32 regs; s32 gsOffset)
 *   then ShrubTexturePrimitive[textureCount] (0x40: ...; s32 gsOffset @ 0x0c; ... tex0.data_lo @ 0x30)
 * ```
 */

export const SHRUB_CLASS_HEADER_SIZE = 0x40;
export const SHRUB_CLASS_ENTRY_SIZE = 0x30;

export interface GcShrubMesh {
  readonly positions: Float64Array;
  readonly uvs: Float32Array;
  readonly indices: Uint32Array;
  /** Per triangle: class-local material slot (`tex0.data_lo`). */
  readonly triangleMaterialSlots: Int32Array;
  readonly scale: number;
}

interface SVtx { x: number; y: number; z: number; ofs: number; s: number; t: number; }
interface SPrim { material: number; strip: boolean; verts: SVtx[]; }

export function readGcShrubClass(buf: Uint8Array): GcShrubMesh {
  if (buf.length < SHRUB_CLASS_HEADER_SIZE) throw new Error("Shrub class buffer shorter than the 0x40 header.");
  const view = new DataView(buf.buffer, buf.byteOffset, buf.byteLength);
  const scale = view.getFloat32(0x20, true);
  const packetCount = view.getInt16(0x28, true);

  const primitives: SPrim[] = [];

  for (let p = 0; p < packetCount; p++) {
    const entryAt = SHRUB_CLASS_HEADER_SIZE + p * 8;
    if (entryAt + 8 > buf.length) break;
    const offset = view.getInt32(entryAt, true);
    const size = view.getInt32(entryAt + 4, true);
    if (offset <= 0 || size <= 0 || offset + size > buf.length) continue;

    const unpacks = filterVifUnpacks(readVifCommandList(buf.subarray(offset, offset + size)));
    if (unpacks.length < 3) continue;

    const hd = new DataView(unpacks[0]!.data.buffer, unpacks[0]!.data.byteOffset, unpacks[0]!.data.byteLength);
    const textureCount = hd.getInt32(0, true);
    const gifTagCount = hd.getInt32(4, true);
    const vertexCount = hd.getInt32(8, true);
    if (vertexCount < 0 || vertexCount > 50_000 || gifTagCount < 0 || gifTagCount > 4096 || textureCount < 0 || textureCount > 256) continue;

    const gifTags: { primStrip: boolean; ofs: number }[] = [];
    for (let g = 0; g < gifTagCount; g++) {
      const at = 0x10 + g * 0x10;
      if (at + 0x10 > unpacks[0]!.data.length) break;
      const primType = (hd.getUint32(at + 4, true) >>> 15) & 0b111; // bits 47..49 of the 64-bit tag
      gifTags.push({ primStrip: primType === 0b100, ofs: hd.getInt32(at + 0x0c, true) });
    }
    const adGifs: { ofs: number; texId: number }[] = [];
    const adGifBase = 0x10 + gifTagCount * 0x10;
    for (let a = 0; a < textureCount; a++) {
      const at = adGifBase + a * 0x40;
      if (at + 0x34 > unpacks[0]!.data.length) break;
      adGifs.push({ ofs: hd.getInt32(at + 0x0c, true), texId: hd.getInt32(at + 0x30, true) });
    }

    const p1 = new DataView(unpacks[1]!.data.buffer, unpacks[1]!.data.byteOffset, unpacks[1]!.data.byteLength);
    const p2 = new DataView(unpacks[2]!.data.buffer, unpacks[2]!.data.byteOffset, unpacks[2]!.data.byteLength);
    const verts: SVtx[] = [];
    for (let v = 0; v < vertexCount; v++) {
      if ((v + 1) * 8 > unpacks[1]!.data.length || (v + 1) * 8 > unpacks[2]!.data.length) break;
      verts.push({
        x: p1.getInt16(v * 8, true), y: p1.getInt16(v * 8 + 2, true), z: p1.getInt16(v * 8 + 4, true),
        ofs: p1.getInt16(v * 8 + 6, true),
        s: p2.getInt16(v * 8, true), t: p2.getInt16(v * 8 + 2, true),
      });
    }

    let nextGifTag = 0;
    let nextAdGif = 0;
    let nextVertex = 0;
    let nextOffset = 0;
    let material = 0;
    let strip = true;
    let prim: SPrim | null = null;
    let guard = 0;
    while ((nextGifTag < gifTags.length || nextAdGif < adGifs.length || nextVertex < verts.length) && guard++ < 200_000) {
      if (nextGifTag < gifTags.length && gifTags[nextGifTag]!.ofs === nextOffset) {
        strip = gifTags[nextGifTag]!.primStrip;
        prim = null;
        nextGifTag++;
        nextOffset += 1;
      } else if (nextAdGif < adGifs.length && adGifs[nextAdGif]!.ofs === nextOffset) {
        material = adGifs[nextAdGif]!.texId;
        prim = null;
        nextAdGif++;
        nextOffset += 5;
      } else if (nextVertex < verts.length && verts[nextVertex]!.ofs === nextOffset) {
        if (!prim) { prim = { material, strip, verts: [] }; primitives.push(prim); }
        prim.verts.push(verts[nextVertex]!);
        nextVertex++;
        nextOffset += 3;
      } else if (nextVertex < verts.length && verts[nextVertex]!.ofs === nextOffset - 3) {
        break; // trailing padding vertices
      } else {
        break;
      }
    }
  }

  const positions: number[] = [];
  const uvs: number[] = [];
  const indices: number[] = [];
  const triangleMaterialSlots: number[] = [];
  const k = scale / 1024;
  for (const prim of primitives) {
    if (prim.verts.length < 3) continue;
    const base = positions.length / 3;
    for (const v of prim.verts) {
      positions.push(v.x * k, v.y * k, v.z * k);
      uvs.push(v.s / 4096, v.t / 4096);
    }
    if (prim.strip) {
      for (let i = 0; i < prim.verts.length - 2; i++) {
        indices.push(base + i, base + i + 1, base + i + 2);
        triangleMaterialSlots.push(prim.material);
      }
    } else {
      for (let i = 0; i + 3 <= prim.verts.length; i += 3) {
        indices.push(base + i, base + i + 1, base + i + 2);
        triangleMaterialSlots.push(prim.material);
      }
    }
  }

  return {
    positions: Float64Array.from(positions),
    uvs: Float32Array.from(uvs),
    indices: Uint32Array.from(indices),
    triangleMaterialSlots: Int32Array.from(triangleMaterialSlots),
    scale,
  };
}

export interface GcShrubClass {
  readonly oClass: number;
  readonly mesh: GcShrubMesh;
  readonly triangleTextureIds: Int32Array;
  readonly textureIds: readonly number[];
}

/** Parse every shrub class from `LevelCoreHeader.shrubClasses` (`ShrubClassEntry` 0x30: offset, oClass, pad, pad, u8 textures[16], billboard). */
export function readGcShrubClasses(core: GcLevelCore): Map<number, GcShrubClass> {
  const table = core.coreHeader.shrubClasses;
  const index = new DataView(core.index.buffer, core.index.byteOffset, core.index.byteLength);
  const boundaries = [...core.sectionBoundaries].sort((a, b) => a - b);
  const out = new Map<number, GcShrubClass>();

  for (let i = 0; i < table.count; i++) {
    const at = table.offset + i * SHRUB_CLASS_ENTRY_SIZE;
    if (at < 0 || at + SHRUB_CLASS_ENTRY_SIZE > core.index.length) break;
    const assetOffset = index.getInt32(at, true);
    const oClass = index.getInt32(at + 4, true);
    if (assetOffset <= 0 || assetOffset >= core.assets.length) continue;
    const textures: number[] = [];
    for (let k = 0; k < 16; k++) textures.push(core.index[at + 0x10 + k]!);

    const end = boundaries.find((b) => b > assetOffset) ?? core.assets.length;
    try {
      const mesh = readGcShrubClass(core.assets.subarray(assetOffset, end));
      const triangleTextureIds = Int32Array.from(mesh.triangleMaterialSlots, (slot) =>
        slot >= 0 && slot < 16 ? textures[slot]! : slot,
      );
      out.set(oClass, {
        oClass,
        mesh,
        triangleTextureIds,
        textureIds: [...new Set(triangleTextureIds)].filter((id) => id >= 0).sort((a, b) => a - b),
      });
    } catch {
      /* skip */
    }
  }
  return out;
}
