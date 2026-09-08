import type { OBPMesh, OBPSourceRef } from "../../core/src/index.js";
import {
  readVifCommandList,
  filterVifUnpacks,
  VIF_UNPACK,
  type VifPacket,
} from "../../ps2-vif/src/index.js";

/**
 * Reader for Going Commando / UYA **tfrags** — the static level render geometry.
 *
 * A tfrags blob is `TfragsHeader` + a table of `TfragHeader` (0x40 bytes each);
 * each tfrag's `data` region holds four VIF command lists (LOD 2 / common /
 * LOD 1 / LOD 01 / LOD 0) that in hardware fill VU1 memory with position, UV,
 * strip and index arrays. This reader parses the **LOD 0** (full-detail)
 * geometry: positions (`base + s16 delta`, `/1024`), UVs (`s16 / 4096`), baked
 * vertex colours (`TfragRgba`) and per-triangle texture id, from strips over an
 * index buffer. LOD 1 / LOD 2, the tface parent hierarchy, normals and the GIF
 * material state beyond `tex0` are not recovered.
 *
 * Verified against retail Going Commando `LEVEL1.WAD` chunk 0 (401 tfrags).
 * See research/GC_TFRAG.md. Implemented from the format description.
 *
 * ```text
 * TfragsHeader   0x00 s32 tableOffset;  0x04 s32 tfragCount;  0x08 f32;  0x0c u32
 * TfragHeader (0x40)
 *   0x00 f32 bsphere[4]
 *   0x10 s32 data            (offset from tableOffset to this tfrag's data)
 *   0x14 u16 lod2Ofs   0x16 u16 sharedOfs   0x18 u16 lod1Ofs   0x1a u16 lod0Ofs
 *   0x1c u16 texOfs    0x1e u16 rgbaOfs
 *   0x20 u8 commonSize 0x21 u8 lod2Size 0x22 u8 lod1Size 0x23 u8 lod0Size
 *   0x27 u8 baseOnly   0x28 u8 textureCount   0x29 u8 rgbaSize
 *   0x3c u8 vertCount  0x3d u8 triCount
 * ```
 */

export const TFRAG_HEADER_SIZE = 0x40;
const SECTOR = 0x10;

export interface GcTfragTexture {
  /** `tex0.data_lo` from the tfrag's GIF A+D texture primitive: a level texture index. */
  readonly texId: number;
}

export interface GcTfragMesh {
  /** Flat world-space positions (Y-up), 3 per vertex. */
  readonly positions: Float64Array;
  /** Per-vertex UV, 2 per vertex. */
  readonly uvs: Float32Array;
  /** Per-vertex baked RGB colour in 0..1, 3 per vertex. */
  readonly colors: Float32Array;
  /** Triangle vertex indices, 3 per triangle. */
  readonly indices: Uint32Array;
  /** Level texture id per triangle (`-1` when the tfrag declares none). */
  readonly triangleTextureIds: Int32Array;
  readonly tfragCount: number;
  readonly bounds: { readonly min: [number, number, number]; readonly max: [number, number, number] };
  /** Distinct texture ids seen, ascending. */
  readonly textureIds: readonly number[];
}

export interface GcTfragOptions {
  readonly maxVertices?: number;
  readonly maxTriangles?: number;
  /** Convert native RC Z-up to OBP Y-up ((x,y,z) -> (x,z,y)). Default true. */
  readonly toYUp?: boolean;
}

const DEFAULT_MAX_VERTICES = 12_000_000;
const DEFAULT_MAX_TRIANGLES = 12_000_000;

export function readGcTfrags(blob: Uint8Array, options: GcTfragOptions = {}): GcTfragMesh {
  const maxVertices = options.maxVertices ?? DEFAULT_MAX_VERTICES;
  const maxTriangles = options.maxTriangles ?? DEFAULT_MAX_TRIANGLES;
  const toYUp = options.toYUp ?? true;

  if (blob.length < 0x10) throw new Error("GC tfrags blob shorter than the 16-byte header.");
  const view = new DataView(blob.buffer, blob.byteOffset, blob.byteLength);
  const tableOffset = view.getInt32(0, true);
  const tfragCount = view.getInt32(4, true);
  if (tableOffset < 0x10 || tableOffset > blob.length) throw new Error(`GC tfrags tableOffset 0x${(tableOffset >>> 0).toString(16)} out of range.`);
  if (tfragCount < 0 || tableOffset + tfragCount * TFRAG_HEADER_SIZE > blob.length) throw new Error(`GC tfrags count ${tfragCount} out of range.`);

  const positions: number[] = [];
  const uvs: number[] = [];
  const colors: number[] = [];
  const indices: number[] = [];
  const triangleTextureIds: number[] = [];
  const textureIdSet = new Set<number>();
  const min: [number, number, number] = [Infinity, Infinity, Infinity];
  const max: [number, number, number] = [-Infinity, -Infinity, -Infinity];

  for (let t = 0; t < tfragCount; t++) {
    const ho = tableOffset + t * TFRAG_HEADER_SIZE;
    const h = {
      data: view.getInt32(ho + 0x10, true),
      lod2Ofs: view.getUint16(ho + 0x14, true),
      sharedOfs: view.getUint16(ho + 0x16, true),
      lod1Ofs: view.getUint16(ho + 0x18, true),
      lod0Ofs: view.getUint16(ho + 0x1a, true),
      rgbaOfs: view.getUint16(ho + 0x1e, true),
      commonSize: blob[ho + 0x20]!,
      lod2Size: blob[ho + 0x21]!,
      lod1Size: blob[ho + 0x22]!,
      rgbaSize: blob[ho + 0x29]!,
    };
    const dataStart = tableOffset + h.data;
    if (dataStart < 0 || dataStart > blob.length) continue;
    const sub = (start: number, end: number): Uint8Array => blob.subarray(dataStart + start, dataStart + Math.min(end, blob.length - dataStart));

    // --- common: [sharedOfs, lod1Ofs) ---
    const commonList = readVifCommandList(sub(h.sharedOfs, h.lod1Ofs));
    const commonUnpacks = filterVifUnpacks(commonList);
    if (commonUnpacks.length < 4) continue;

    const strow = commonList[5];
    if (!strow || strow.data.length < 12) continue;
    const sv = new DataView(strow.data.buffer, strow.data.byteOffset, strow.data.byteLength);
    const baseX = sv.getInt32(0, true);
    const baseY = sv.getInt32(4, true);
    const baseZ = sv.getInt32(8, true);

    const textures = readTexturePrimitives(commonUnpacks[1]!);
    const commonVertexInfo = readVertexInfo(commonUnpacks[2]!);
    const commonPositions = readPositions(commonUnpacks[3]!);

    // --- LOD 01: [lod0Ofs, sharedOfs + lod1Size*0x10) ---
    const lod01List = readVifCommandList(sub(h.lod0Ofs, h.sharedOfs + h.lod1Size * SECTOR));
    const lod01Unpacks = filterVifUnpacks(lod01List);
    let lod01Positions: number[][] = [];
    let lod01VertexInfo: VertexInfo[] = [];
    {
      let i = 0;
      // parent_indices (V4_8) and unknown_indices_2 (V4_8) are skipped for LOD 0 recovery
      while (i < lod01Unpacks.length && lod01Unpacks[i]!.code.vnvl === VIF_UNPACK.V4_8) i++;
      if (i < lod01Unpacks.length && lod01Unpacks[i]!.code.vnvl === VIF_UNPACK.V4_16) lod01VertexInfo = readVertexInfo(lod01Unpacks[i++]!);
      if (i < lod01Unpacks.length && lod01Unpacks[i]!.code.vnvl === VIF_UNPACK.V3_16) lod01Positions = readPositions(lod01Unpacks[i++]!);
    }

    // --- LOD 0: [sharedOfs + lod1Size*0x10, ...) ---
    const lod0Start = h.sharedOfs + h.lod1Size * SECTOR;
    const lod0Size = h.rgbaOfs - (h.lod1Size + h.lod2Size - h.commonSize) * SECTOR;
    const lod0List = readVifCommandList(sub(lod0Start, lod0Start + Math.max(0, lod0Size)));
    const lod0Unpacks = filterVifUnpacks(lod0List);
    let lod0Positions: number[][] = [];
    let lod0VertexInfo: VertexInfo[] = [];
    let strips: Uint8Array = new Uint8Array();
    let stripIndices: Uint8Array = new Uint8Array();
    {
      let i = 0;
      if (i < lod0Unpacks.length && lod0Unpacks[i]!.code.vnvl === VIF_UNPACK.V3_16) lod0Positions = readPositions(lod0Unpacks[i++]!);
      if (i < lod0Unpacks.length) strips = lod0Unpacks[i++]!.data.slice();
      if (i < lod0Unpacks.length) stripIndices = lod0Unpacks[i++]!.data.slice();
      // optional parent_indices / unknown_indices_2
      while (i < lod0Unpacks.length && lod0Unpacks[i]!.code.vnvl === VIF_UNPACK.V4_8) i++;
      if (i < lod0Unpacks.length && lod0Unpacks[i]!.code.vnvl === VIF_UNPACK.V4_16) lod0VertexInfo = readVertexInfo(lod0Unpacks[i++]!);
    }
    if (strips.length === 0 || stripIndices.length === 0) continue;

    const allPositions = [...commonPositions, ...lod01Positions, ...lod0Positions];
    const allVertexInfo = [...commonVertexInfo, ...lod01VertexInfo, ...lod0VertexInfo];
    const rgbas = readRgbas(blob, dataStart + h.rgbaOfs, h.rgbaSize * 4);

    // one mesh vertex per vertex_info
    const vertexBase = positions.length / 3;
    for (const info of allVertexInfo) {
      const pi = info.vertex >> 1;
      const p = allPositions[pi] ?? [0, 0, 0];
      let x = (baseX + p[0]!) / 1024;
      let y = (baseY + p[1]!) / 1024;
      let z = (baseZ + p[2]!) / 1024;
      if (toYUp) { const ny = z; z = y; y = ny; }
      positions.push(x, y, z);
      if (x < min[0]) min[0] = x; if (y < min[1]) min[1] = y; if (z < min[2]) min[2] = z;
      if (x > max[0]) max[0] = x; if (y > max[1]) max[1] = y; if (z > max[2]) max[2] = z;

      let s = info.s / 4096;
      let tc = info.t / 4096;
      if (s < 0) s *= 0.5;
      if (tc < 0) tc *= 0.5;
      uvs.push(s, tc);

      const c = rgbas[pi];
      if (c) colors.push(c[0] / 255, c[1] / 255, c[2] / 255);
      else colors.push(0.7, 0.7, 0.7);
    }

    if (positions.length / 3 > maxVertices) throw new Error(`GC tfrags exceeds maxVertices ${maxVertices}.`);

    for (const face of recoverFaces(strips, stripIndices, textures)) {
      const [a, b, cc] = face.tri;
      if (a >= allVertexInfo.length || b >= allVertexInfo.length || cc >= allVertexInfo.length) continue;
      indices.push(vertexBase + a, vertexBase + b, vertexBase + cc);
      triangleTextureIds.push(face.texId);
      if (face.texId >= 0) textureIdSet.add(face.texId);
      if (indices.length / 3 > maxTriangles) throw new Error(`GC tfrags exceeds maxTriangles ${maxTriangles}.`);
    }
  }

  const empty = positions.length === 0;
  return {
    positions: Float64Array.from(positions),
    uvs: Float32Array.from(uvs),
    colors: Float32Array.from(colors),
    indices: Uint32Array.from(indices),
    triangleTextureIds: Int32Array.from(triangleTextureIds),
    tfragCount,
    bounds: {
      min: empty ? [0, 0, 0] : min,
      max: empty ? [0, 0, 0] : max,
    },
    textureIds: [...textureIdSet].sort((p, q) => p - q),
  };
}

interface VertexInfo { s: number; t: number; parent: number; vertex: number; }

function readPositions(packet: VifPacket): number[][] {
  const out: number[][] = [];
  const d = new DataView(packet.data.buffer, packet.data.byteOffset, packet.data.byteLength);
  for (let o = 0; o + 6 <= packet.data.length; o += 6) {
    out.push([d.getInt16(o, true), d.getInt16(o + 2, true), d.getInt16(o + 4, true)]);
  }
  return out;
}

function readVertexInfo(packet: VifPacket): VertexInfo[] {
  const out: VertexInfo[] = [];
  const d = new DataView(packet.data.buffer, packet.data.byteOffset, packet.data.byteLength);
  for (let o = 0; o + 8 <= packet.data.length; o += 8) {
    out.push({ s: d.getInt16(o, true), t: d.getInt16(o + 2, true), parent: d.getInt16(o + 4, true), vertex: d.getInt16(o + 6, true) });
  }
  return out;
}

function readTexturePrimitives(packet: VifPacket): GcTfragTexture[] {
  const out: GcTfragTexture[] = [];
  const d = new DataView(packet.data.buffer, packet.data.byteOffset, packet.data.byteLength);
  // TfragTexturePrimitive = 5 x GifAdData16 (0x10) = 0x50; tex0 is the first, data_lo @ +0x00.
  for (let o = 0; o + 0x50 <= packet.data.length; o += 0x50) {
    out.push({ texId: d.getInt32(o, true) });
  }
  return out;
}

function readRgbas(blob: Uint8Array, offset: number, count: number): [number, number, number][] {
  const out: [number, number, number][] = [];
  for (let i = 0; i < count; i++) {
    const o = offset + i * 4;
    if (o + 3 > blob.length) break;
    out.push([blob[o]!, blob[o + 1]!, blob[o + 2]!]);
  }
  return out;
}

interface RecoveredFace { tri: [number, number, number]; texId: number; }

/** Walk the tfrag triangle/quad strips over `indices` into a triangle list. */
function recoverFaces(strips: Uint8Array, indices: Uint8Array, textures: GcTfragTexture[]): RecoveredFace[] {
  const faces: RecoveredFace[] = [];
  let activeAdGif = -1;
  let next = 0;
  const idx = (i: number): number => indices[i] ?? 0;
  const texFor = (): number => (activeAdGif >= 0 && activeAdGif < textures.length ? textures[activeAdGif]!.texId : -1);

  for (let s = 0; s + 4 <= strips.length; s += 4) {
    let vc = strips[s]! << 24 >> 24; // s8
    const adGifOffset = strips[s + 2]! << 24 >> 24; // s8
    if (vc <= 0) {
      if (vc === 0) break;
      if (adGifOffset >= 0) activeAdGif = Math.floor(adGifOffset / 5);
      vc += 128;
    }
    if (next + vc > indices.length) break;

    if (vc % 2 === 0) {
      // quad strip
      for (let i = 0; i + 4 <= vc; i += 2) {
        const q0 = idx(next + i + 0);
        const q1 = idx(next + i + 1);
        const q2 = idx(next + i + 2);
        const q3 = idx(next + i + 3);
        // quad (q2, q3, q1, q0) -> two triangles
        faces.push({ tri: [q2, q3, q1], texId: texFor() });
        faces.push({ tri: [q2, q1, q0], texId: texFor() });
      }
    } else {
      // triangle strip
      for (let i = 0; i + 3 <= vc; i++) {
        const t0 = idx(next + i + 0);
        const t1 = idx(next + i + 1);
        const t2 = idx(next + i + 2);
        faces.push({ tri: (i & 1) ? [t1, t0, t2] : [t0, t1, t2], texId: texFor() });
      }
    }
    next += vc;
  }
  return faces;
}

/**
 * Normalize a tfrag mesh into OBP meshes, one per texture id, keeping baked
 * colours + UVs. `meshIdPrefix` uniquifies the mesh ids (e.g. per chunk);
 * `materialIdPrefix` (default `meshIdPrefix`) is the shared material id stem.
 */
export function toObpTfragMeshes(
  mesh: GcTfragMesh,
  source: OBPSourceRef,
  meshIdPrefix: string,
  materialIdPrefix: string = meshIdPrefix,
): OBPMesh[] {
  const byTex = new Map<number, number[]>();
  for (let f = 0; f < mesh.triangleTextureIds.length; f++) {
    const tex = mesh.triangleTextureIds[f]!;
    let list = byTex.get(tex);
    if (!list) byTex.set(tex, (list = []));
    list.push(mesh.indices[f * 3]!, mesh.indices[f * 3 + 1]!, mesh.indices[f * 3 + 2]!);
  }

  const out: OBPMesh[] = [];
  for (const [tex, tris] of [...byTex.entries()].sort((a, b) => a[0] - b[0])) {
    // Compact to only the vertices this submesh uses.
    const remap = new Map<number, number>();
    const positions: number[] = [];
    const colors: number[] = [];
    const uvs: number[] = [];
    const indices: number[] = [];
    for (const v of tris) {
      let nv = remap.get(v);
      if (nv === undefined) {
        nv = positions.length / 3;
        remap.set(v, nv);
        positions.push(mesh.positions[v * 3]!, mesh.positions[v * 3 + 1]!, mesh.positions[v * 3 + 2]!);
        colors.push(mesh.colors[v * 3]!, mesh.colors[v * 3 + 1]!, mesh.colors[v * 3 + 2]!);
        uvs.push(mesh.uvs[v * 2]!, mesh.uvs[v * 2 + 1]!);
      }
      indices.push(nv);
    }
    out.push({
      id: `${meshIdPrefix}-tex${tex}`,
      name: `tfrags texture ${tex}`,
      geometry: { positions, indices, colors, uvs },
      materialId: `${materialIdPrefix}-tex${tex}`,
      source: { ...source, assetKind: "tfrag", originalId: tex },
    });
  }
  return out;
}
