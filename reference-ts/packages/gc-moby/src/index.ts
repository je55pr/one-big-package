import type { GcLevelCore } from "../../gc-level-core/src/index.js";
import { readVifCommandList, filterVifUnpacks } from "../../ps2-vif/src/index.js";

/**
 * Reader for Going Commando / UYA **moby** class geometry — the dynamic objects
 * (enemies, crates, the vendor, gadget pickups, breakables). The most involved
 * R&C geometry format.
 *
 * Recovers the **high-LOD mesh at bind pose**. Each packet's vertex table holds
 * positions (`s16 x,y,z`) and, for animated classes, per-vertex bone bindings
 * that are decoded through the PS2's VU0 matrix-slot machine (a running
 * `blend[64]` buffer seeded from the packet's "matrix transfers" and updated by
 * every vertex). A skinned vertex is stored in its bone's local space, so the
 * bind pose is `pos_model = Σ_k w_k · globalBind[joint_k] · pos_local`, where
 * `globalBind[j] = translate(-skeleton[j] row 3)` — every GC bind-pose joint
 * rotation is identity, so no hierarchy walk is needed (see research/GC_MOBY.md).
 * The joint index is pipelined `VERTEX_PIPELINE` entries ahead of the vertex it
 * binds. Without skinning the limbs collapse toward the origin ("folded").
 * Animation frames are not applied — this is the rest pose.
 *
 * The index buffer is Insomniac's triangle-strip `s8` stream: an index `<= 0`
 * twice in a row starts a new strip, a lone `<= 0` is a fan-pivot restart, and
 * `0` triggers the "secret index" path for texture switches / end-of-packet.
 *
 * Verified against retail Going Commando (see research/GC_MOBY.md). Implemented
 * from the format description.
 *
 * ```text
 * MobyClassHeader (0x48):
 *   0x00 s32 packetTableOffset
 *   0x04 u8 highLodCount; u8 lowLodCount; u8 metalCount; u8 metalBegin
 *   0x08 u8 jointCount
 *   0x0b u8 formatByte    (0 => RAC2 moby format)
 *   0x14 s32 skeletonOffset      -> Mat3[jointCount], 0x40 stride (48 used: 3 rows of [Rx Ry Rz | T])
 *   0x18 s32 commonTransOffset   -> MobyTrans[jointCount] (0x10: Vec3f vector; u16 parentByteOffset; u16)
 *   0x24 f32 scale
 * MobyPacketEntry (0x10):
 *   0x00 u32 vifListOffset;  0x04 u16 vifListSize (*0x10);  0x08 u32 vertexOffset
 * GcUyaDlVertexTableHeader (0x10):
 *   0x00 u16 matrixTransferCount; 0x02 u16 twoWayBlend; 0x04 u16 threeWayBlend;
 *   0x06 u16 mainVertexCount; 0x08 u16 duplicateVertexCount; 0x0c u16 vertexTableOffset
 * MobyVertex (0x10): u16 lowHalfword (bits 0..8 vertex index, bits 9..15 spr joint);
 *   bytes 2..7 = VU0 matrix load/store addresses + blend weights; s16 x@0xa, y@0xc, z@0xe
 * ```
 */

export const MOBY_CLASS_HEADER_SIZE = 0x48;
export const MOBY_CLASS_ENTRY_SIZE = 0x20;
const JOINT_STRIDE = 0x40;
const COMMON_TRANS_STRIDE = 0x10;
/**
 * The PS2 vertex loop is software-pipelined: the `lowHalfword` of each
 * `MobyVertex` (vertex index in bits 0..8, spr joint in bits 9..15) is staged
 * this many entries ahead of the vertex it actually describes.
 */
const VERTEX_PIPELINE = 7;

export interface GcMobyMesh {
  readonly positions: Float64Array;
  readonly uvs: Float32Array;
  /**
   * Per-vertex unit normal in the class's local frame (native Z-up), from the
   * `MobyVertex` spherical angles at bytes 0x08 / 0x09. Mobies carry no baked
   * vertex colour — they are lit at runtime from these normals.
   */
  readonly normals: Float32Array;
  readonly indices: Uint32Array;
  /** Per triangle: class-local material slot (`tex0.data_lo`; may be negative for chrome/glass). */
  readonly triangleMaterialSlots: Int32Array;
  readonly scale: number;
  /** True if any decoded packet used bone blending. */
  readonly skinned: boolean;
  /** True if the skeleton was found and applied (bind pose reconstructed). */
  readonly skinningApplied: boolean;
}

interface MPrim { material: number; strip: number[]; }
/** Per-vertex bone binding: up to 3 (joint, weight) pairs; count 0 = rigid to model space. */
interface SkinAttr { count: number; joints: [number, number, number]; weights: [number, number, number]; }

/** Row-major 3x4 affine matrix `[R | t]` as 12 numbers (rows of 4). */
type Affine = number[];
const AFFINE_ID: Affine = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0];

function affineApply(m: Affine, x: number, y: number, z: number): [number, number, number] {
  return [
    m[0]! * x + m[1]! * y + m[2]! * z + m[3]!,
    m[4]! * x + m[5]! * y + m[6]! * z + m[7]!,
    m[8]! * x + m[9]! * y + m[10]! * z + m[11]!,
  ];
}

/** `out(p) = parent(child(p))` for two row-major 3x4 affines. */
function affineCompose(parent: Affine, child: Affine): Affine {
  const out = new Array<number>(12);
  for (let r = 0; r < 3; r++) {
    for (let c = 0; c < 3; c++) {
      out[r * 4 + c] = parent[r * 4]! * child[c]! + parent[r * 4 + 1]! * child[4 + c]! + parent[r * 4 + 2]! * child[8 + c]!;
    }
    out[r * 4 + 3] = parent[r * 4]! * child[3]! + parent[r * 4 + 1]! * child[7]! + parent[r * 4 + 2]! * child[11]! + parent[r * 4 + 3]!;
  }
  return out;
}

/**
 * Build one global bind matrix per joint from the moby skeleton. Returns `null`
 * when the skeleton is absent, out of range, or numerically unusable.
 *
 * Each skeleton entry (0x40 stride) holds a 4×4 matrix. Rows 0–2 are `[R |
 * T_local]` (parent-relative), and **row 3 is `-T_global`** — the negated
 * accumulated bind translation (verified arithmetically: `-row3[j]` equals the
 * sum of `T_local` up the parent chain). Every GC bind-pose joint rotation is
 * identity, so the global bind is simply `translate(-row3)`.
 */
function readGlobalBind(buf: Uint8Array, view: DataView, jointCount: number): Affine[] | null {
  const skeletonOffset = view.getInt32(0x14, true);
  const commonTransOffset = view.getInt32(0x18, true);
  if (jointCount <= 0 || jointCount > 256) return null;
  if (skeletonOffset <= 0 || commonTransOffset <= 0) return null;
  if (skeletonOffset + jointCount * JOINT_STRIDE > buf.length) return null;
  if (commonTransOffset + jointCount * COMMON_TRANS_STRIDE > buf.length) return null;

  const global: Affine[] = new Array(jointCount);
  for (let j = 0; j < jointCount; j++) {
    const o = skeletonOffset + j * JOINT_STRIDE;
    // row 3 (floats 12..14) = -T_global
    const gx = view.getFloat32(o + 48, true);
    const gy = view.getFloat32(o + 52, true);
    const gz = view.getFloat32(o + 56, true);
    if (!Number.isFinite(gx) || !Number.isFinite(gy) || !Number.isFinite(gz)) return null;
    global[j] = [1, 0, 0, -gx, 0, 1, 0, -gy, 0, 0, 1, -gz];
  }
  return global;
}

/**
 * Simulate the VU0 matrix-slot machine for one packet and return the bone
 * binding of each in-file vertex (`twoWay` first, then `threeWay`, then `main`).
 */
function readPacketSkin(
  buf: Uint8Array, view: DataView, vh: number, vbase: number,
  matrixTransferCount: number, twoWay: number, threeWay: number, count: number,
): SkinAttr[] {
  const blend: (SkinAttr | null)[] = new Array(64).fill(null);
  const set = (addr: number, a: SkinAttr): void => { if (addr !== 0xf4) blend[(addr & 0xff) >> 2] = a; };
  const get = (addr: number): SkinAttr => blend[(addr & 0xff) >> 2] ?? { count: 1, joints: [0, 0, 0], weights: [255, 0, 0] };

  for (let t = 0; t < matrixTransferCount; t++) {
    const o = vh + 0x10 + t * 2;
    if (o + 2 > buf.length) break;
    set(buf[o + 1]!, { count: 1, joints: [buf[o]!, 0, 0], weights: [255, 0, 0] });
  }

  const attrs: SkinAttr[] = [];
  for (let v = 0; v < count; v++) {
    const o = vbase + v * 0x10;
    // The spr joint shares the pipelined half-word with the vertex index, so it
    // is staged VERTEX_PIPELINE entries ahead of the vertex it binds. Reading it
    // in file order scrambles the binding wherever the joint changes quickly
    // (torsos, shoulders); the 7-ahead read cuts skinning spikes ~4x on retail
    // GC — see research/GC_MOBY.md.
    const sprOffset = vbase + Math.min(count - 1, v + VERTEX_PIPELINE) * 0x10;
    const sprJoint = (view.getUint16(sprOffset, true) & 0xfe00) >> 9;
    const b = (k: number): number => buf[o + k]!;
    let a: SkinAttr;
    if (v < twoWay) {
      set(b(6), { count: 1, joints: [sprJoint, 0, 0], weights: [255, 0, 0] });
      const s1 = get(b(2)), s2 = get(b(3));
      a = { count: 2, joints: [s1.joints[0], s2.joints[0], 0], weights: [b(4), b(5), 0] };
      set(b(7), a);
    } else if (v < twoWay + threeWay) {
      const s1 = get(b(2)), s2 = get(b(3)), s3 = get((sprJoint * 2) & 0xff);
      a = { count: 3, joints: [s1.joints[0], s2.joints[0], s3.joints[0]], weights: [b(4), b(5), b(6)] };
      set(b(7), a);
    } else {
      set(b(3), { count: 1, joints: [sprJoint, 0, 0], weights: [255, 0, 0] });
      a = get(b(2));
    }
    attrs.push(a);
  }
  return attrs;
}

export function readGcMobyClass(buf: Uint8Array): GcMobyMesh {
  if (buf.length < MOBY_CLASS_HEADER_SIZE) throw new Error("Moby class buffer shorter than the 0x48 header.");
  const view = new DataView(buf.buffer, buf.byteOffset, buf.byteLength);
  const boundingRadius = Math.abs(view.getFloat32(0x3c, true)) * (view.getFloat32(0x24, true) / 1024);

  const skinned = decodeMobyClass(buf, view, true);
  if (!skinned.skinningApplied) return skinned;

  // The skeleton reconstruction is only partially pinned (see
  // research/GC_MOBY.md), so a small number of vertices still land on the wrong
  // joint and stretch their triangles into spikes. Drop just those triangles;
  // a coherent bind pose loses a handful of slivers, a broken one loses most of
  // itself and we fall back to the folded (but bounded) mesh.
  const { mesh: pruned, removedFraction } = pruneSpikeTriangles(skinned);
  const folded = decodeMobyClass(buf, view, false);
  const se = meshExtent(pruned);
  const broken = se === 0
    || removedFraction > 0.03
    || se > Math.max(40, boundingRadius * 5, meshExtent(folded) * 4);
  return broken ? { ...folded, skinned: true } : pruned;
}

/** Largest axis span of a mesh's positions. */
function meshExtent(m: GcMobyMesh): number {
  let mn = Infinity, mx = -Infinity;
  for (let i = 0; i < m.positions.length; i++) { const c = m.positions[i]!; if (c < mn) mn = c; if (c > mx) mx = c; }
  return m.positions.length ? mx - mn : 0;
}

/**
 * Remove triangles whose longest edge is far above the mesh's typical edge
 * length — the spikes left by the partially-pinned skeleton. Returns the
 * filtered mesh plus the fraction of triangles removed (a broken
 * reconstruction loses a large fraction and should be discarded wholesale).
 */
function pruneSpikeTriangles(m: GcMobyMesh): { mesh: GcMobyMesh; removedFraction: number } {
  const p = m.positions, ix = m.indices;
  const triCount = (ix.length / 3) | 0;
  if (triCount === 0) return { mesh: m, removedFraction: 0 };
  const longest = new Float64Array(triCount);
  const edges: number[] = [];
  for (let t = 0; t < triCount; t++) {
    let e = 0;
    for (let s = 0; s < 3; s++) {
      const a = ix[t * 3 + s]! * 3, b = ix[t * 3 + ((s + 1) % 3)]! * 3;
      const d = Math.hypot(p[a]! - p[b]!, p[a + 1]! - p[b + 1]!, p[a + 2]! - p[b + 2]!);
      if (d > e) e = d;
    }
    longest[t] = e;
    edges.push(e);
  }
  edges.sort((x, y) => x - y);
  const median = edges[edges.length >> 1] ?? 0;
  const limit = Math.max(1.0, median * 10);
  const keep: number[] = [];
  const slots: number[] = [];
  let removed = 0;
  for (let t = 0; t < triCount; t++) {
    if (longest[t]! > limit) { removed++; continue; }
    keep.push(ix[t * 3]!, ix[t * 3 + 1]!, ix[t * 3 + 2]!);
    slots.push(m.triangleMaterialSlots[t]!);
  }
  if (removed === 0) return { mesh: m, removedFraction: 0 };
  return {
    mesh: { ...m, indices: Uint32Array.from(keep), triangleMaterialSlots: Int32Array.from(slots) },
    removedFraction: removed / triCount,
  };
}

function decodeMobyClass(buf: Uint8Array, view: DataView, allowSkinning: boolean): GcMobyMesh {
  const packetTableOffset = view.getInt32(0x00, true);
  const highLodCount = buf[0x04]!;
  const jointCount = buf[0x08]!;
  const scale = view.getFloat32(0x24, true);
  const k = scale / 1024;
  // Sanity bound for a skinned vertex, from the class bounding sphere.
  const boundingRadius = Math.abs(view.getFloat32(0x3c, true)) * k;
  const skinCap = Math.max(2000, boundingRadius * 8);

  const globalBind = allowSkinning && jointCount > 0 ? readGlobalBind(buf, view, jointCount) : null;

  const positions: number[] = [];
  const uvs: number[] = [];
  const normals: number[] = [];
  const indices: number[] = [];
  const triangleMaterialSlots: number[] = [];
  let skinned = false;
  let skinningApplied = false;

  // The PS2 GS texture register persists across the whole draw: a moby packet
  // only emits an AD-GIF (a `MobyTexturePrimitive`) when the texture *changes*,
  // so a packet/strip with none inherits the last texture set by an earlier
  // packet. Track `material` as one running value across every packet — resetting
  // it per packet drops the texture on ~85% of, e.g., Tabora's terrain, which
  // ships as a single moby (see research/GC_MOBY.md). Matches Wrench's
  // `recover_packets` (`texture_index` init 0, hoisted above the packet loop).
  let material = 0;

  for (let p = 0; p < highLodCount; p++) {
    const eo = packetTableOffset + p * 0x10;
    if (eo < 0 || eo + 0x10 > buf.length) break;
    const vifListOffset = view.getUint32(eo + 0x00, true);
    const vifListSize = view.getUint16(eo + 0x04, true) * 0x10;
    const vertexOffset = view.getUint32(eo + 0x08, true);

    if (vifListOffset + vifListSize > buf.length || vertexOffset + 0x10 > buf.length) continue;

    // --- VIF list: [0] STs, [1] index buffer, [2] optional textures ---
    const unpacks = filterVifUnpacks(readVifCommandList(buf.subarray(vifListOffset, vifListOffset + vifListSize)));
    if (unpacks.length < 2) continue;

    const stData = unpacks[0]!.data;
    const sts: [number, number][] = [];
    const sv = new DataView(stData.buffer, stData.byteOffset, stData.byteLength);
    for (let i = 0; i + 4 <= stData.length; i += 4) sts.push([sv.getInt16(i, true), sv.getInt16(i + 2, true)]);

    const idxData = unpacks[1]!.data;
    if (idxData.length < 4) continue;
    const secretIndices: number[] = [(idxData[2]! << 24) >> 24]; // MobyIndexHeader.secret_index (s8)
    const idxBuf: number[] = [];
    for (let i = 4; i < idxData.length; i++) idxBuf.push((idxData[i]! << 24) >> 24);

    const textures: number[] = [];
    if (unpacks.length >= 3) {
      const td = unpacks[2]!.data;
      const tv = new DataView(td.buffer, td.byteOffset, td.byteLength);
      for (let i = 0; i * 0x40 + 0x40 <= td.length; i++) {
        secretIndices.push((td[i * 0x10 + 0x0c]! << 24) >> 24);
        textures.push(tv.getInt32(i * 0x40 + 0x20, true)); // d3_tex0_1.data_lo
      }
    }

    // --- vertex table ---
    const vh = vertexOffset;
    const matrixTransferCount = view.getUint16(vh + 0x00, true);
    const twoWay = view.getUint16(vh + 0x02, true);
    const threeWay = view.getUint16(vh + 0x04, true);
    const mainCount = view.getUint16(vh + 0x06, true);
    const dupCount = view.getUint16(vh + 0x08, true);
    const vertexTableOffset = view.getUint16(vh + 0x0c, true);
    if (twoWay + threeWay > 0) skinned = true;

    const inFileCount = twoWay + threeWay + mainCount;
    const vbase = vh + vertexTableOffset;
    if (vbase + inFileCount * 0x10 > buf.length || inFileCount > 4096) continue;

    const applySkin = globalBind !== null;
    const skinAttrs = applySkin ? readPacketSkin(buf, view, vh, vbase, matrixTransferCount, twoWay, threeWay, inFileCount) : null;

    const rawX: number[] = [], rawY: number[] = [], rawZ: number[] = [], rawIdx: number[] = [];
    const norm: [number, number, number][] = [];
    for (let v = 0; v < inFileCount; v++) {
      const o = vbase + v * 0x10;
      rawIdx.push(view.getUint16(o, true) & 0x1ff);
      rawX.push(view.getInt16(o + 0x0a, true));
      rawY.push(view.getInt16(o + 0x0c, true));
      rawZ.push(view.getInt16(o + 0x0e, true));
      // Normal in spherical coords: azimuth @0x08, elevation @0x09, unit
      // (byte * PI/128). Not pipelined (like the position, unlike the index).
      const az = buf[o + 0x08]! * (Math.PI / 128);
      const el = buf[o + 0x09]! * (Math.PI / 128);
      norm.push([Math.sin(az) * Math.cos(el), Math.cos(az) * Math.cos(el), Math.sin(el)]);
    }
    // Vertex indices are pipelined VERTEX_PIPELINE entries ahead in the file.
    for (let i = VERTEX_PIPELINE; i < inFileCount; i++) rawIdx[i - VERTEX_PIPELINE] = rawIdx[i]!;

    // Bind-pose position of each in-file vertex.
    const posX: number[] = [], posY: number[] = [], posZ: number[] = [];
    let packetSkinBlewUp = false;
    for (let v = 0; v < inFileCount; v++) {
      const attr = skinAttrs?.[v];
      if (!globalBind || !attr || attr.count === 0) {
        posX.push(rawX[v]! * k); posY.push(rawY[v]! * k); posZ.push(rawZ[v]! * k);
        continue;
      }
      let sx = 0, sy = 0, sz = 0, tw = 0;
      for (let j = 0; j < attr.count; j++) {
        const w = attr.weights[j];
        if (!w) continue;
        const ji = attr.joints[j]!;
        const m = ji >= 0 && ji < globalBind.length ? globalBind[ji]! : AFFINE_ID;
        const [qx, qy, qz] = affineApply(m, rawX[v]!, rawY[v]!, rawZ[v]!);
        sx += w * qx; sy += w * qy; sz += w * qz; tw += w;
      }
      if (tw === 0) { const [qx, qy, qz] = affineApply(globalBind[attr.joints[0]!] ?? AFFINE_ID, rawX[v]!, rawY[v]!, rawZ[v]!); sx = qx; sy = qy; sz = qz; tw = 1; }
      const fx = (sx / tw) * k, fy = (sy / tw) * k, fz = (sz / tw) * k;
      if (!Number.isFinite(fx) || !Number.isFinite(fy) || !Number.isFinite(fz) ||
        Math.abs(fx) > skinCap || Math.abs(fy) > skinCap || Math.abs(fz) > skinCap) {
        packetSkinBlewUp = true;
        break;
      }
      posX.push(fx); posY.push(fy); posZ.push(fz);
    }
    if (packetSkinBlewUp) {
      // A degenerate joint matrix: fall back to the bind-pose positions for this packet.
      posX.length = 0; posY.length = 0; posZ.length = 0;
      for (let v = 0; v < inFileCount; v++) { posX.push(rawX[v]! * k); posY.push(rawY[v]! * k); posZ.push(rawZ[v]! * k); }
    } else if (applySkin && (twoWay + threeWay > 0)) {
      skinningApplied = true;
    }

    // Duplicate vertices: u16[] >> 7 at the aligned position after the matrix transfers.
    let arrayOfs = vh + 0x10 + matrixTransferCount * 2;
    if (arrayOfs % 4 !== 0) arrayOfs += 2;
    if (arrayOfs % 8 !== 0) arrayOfs += 4;
    const dupes: number[] = [];
    for (let d = 0; d < dupCount; d++) {
      const o = arrayOfs + d * 2;
      if (o + 2 > buf.length) break;
      dupes.push(view.getUint16(o, true) >> 7);
    }

    // Ordered vertex list = main vertices, then duplicates (resolved by index cache).
    const listX: number[] = [], listY: number[] = [], listZ: number[] = [], listS: number[] = [], listT: number[] = [];
    const listN: [number, number, number][] = [];
    const cache = new Map<number, number>(); // vertexIndex -> position in `list`
    for (let v = 0; v < inFileCount; v++) {
      const pos = listX.length;
      listX.push(posX[v]!); listY.push(posY[v]!); listZ.push(posZ[v]!);
      listN.push(norm[v] ?? [0, 0, 1]);
      const st = sts[v] ?? [0, 0];
      listS.push(st[0] / 4096); listT.push(st[1] / 4096);
      cache.set(rawIdx[v]!, pos);
    }
    dupes.forEach((dupe, di) => {
      const from = cache.get(dupe);
      if (from === undefined) return;
      listX.push(listX[from]!); listY.push(listY[from]!); listZ.push(listZ[from]!);
      listN.push(listN[from] ?? [0, 0, 1]);
      const st = sts[inFileCount + di] ?? [0, 0];
      listS.push(st[0] / 4096); listT.push(st[1] / 4096);
    });

    // --- index walk -> triangle strips ---
    const prims: MPrim[] = [];
    let prim: MPrim | null = null;
    let adGif = 0;
    for (let j = 0; j < idxBuf.length; j++) {
      let index = idxBuf[j]!;
      if (index === 0) {
        const secret = secretIndices[adGif] ?? 0;
        if (secret === 0) {
          if (prim && prim.strip.length >= 3) { prim.strip.pop(); prim.strip.pop(); prim.strip.pop(); }
          break;
        }
        index = secret - 0x80;
        material = adGif < textures.length ? textures[adGif]! : material;
        adGif++;
      }
      if (index <= 0) {
        if (j + 1 < idxBuf.length && idxBuf[j + 1]! <= 0) {
          prim = { material, strip: [] };
          prims.push(prim);
        } else if (prim && prim.strip.length >= 1) {
          prim.strip.push(prim.strip[prim.strip.length - 1]!);
        }
      }
      if (!prim) continue;
      prim.strip.push((index & 0x7f) - 1);
    }

    // Emit triangles from each strip.
    const vbaseOut = positions.length / 3;
    for (let v = 0; v < listX.length; v++) {
      positions.push(listX[v]!, listY[v]!, listZ[v]!);
      uvs.push(listS[v]!, listT[v]!);
      const n = listN[v] ?? [0, 0, 1];
      normals.push(n[0], n[1], n[2]);
    }
    for (const pr of prims) {
      for (let i = 0; i + 3 <= pr.strip.length; i++) {
        const a = pr.strip[i]!, b = pr.strip[i + 1]!, c = pr.strip[i + 2]!;
        if (a < 0 || b < 0 || c < 0 || a >= listX.length || b >= listX.length || c >= listX.length) continue;
        if (a === b || b === c || a === c) continue; // zero-area / restart
        if (i % 2 === 0) indices.push(vbaseOut + a, vbaseOut + b, vbaseOut + c);
        else indices.push(vbaseOut + b, vbaseOut + a, vbaseOut + c);
        triangleMaterialSlots.push(pr.material);
      }
    }
  }

  return {
    positions: Float64Array.from(positions),
    uvs: Float32Array.from(uvs),
    normals: Float32Array.from(normals),
    indices: Uint32Array.from(indices),
    triangleMaterialSlots: Int32Array.from(triangleMaterialSlots),
    scale,
    skinned,
    skinningApplied,
  };
}

export interface GcMobyDebugPacket {
  readonly vifUnpackCount: number;
  readonly stCount: number;
  readonly secretIndices: readonly number[];
  readonly idxBuf: readonly number[];
  readonly textures: readonly number[];
  readonly matrixTransferCount: number;
  readonly twoWay: number;
  readonly threeWay: number;
  readonly mainCount: number;
  readonly dupCount: number;
  readonly inFileCount: number;
  readonly rawIdxRaw: readonly number[];
}

/** Dump the raw per-packet index / vertex-table fields for a moby class. Diagnostic only. */
export function debugGcMobyClassPackets(buf: Uint8Array): GcMobyDebugPacket[] {
  const view = new DataView(buf.buffer, buf.byteOffset, buf.byteLength);
  const packetTableOffset = view.getInt32(0x00, true);
  const highLodCount = buf[0x04]!;
  const out: GcMobyDebugPacket[] = [];
  for (let p = 0; p < highLodCount; p++) {
    const eo = packetTableOffset + p * 0x10;
    if (eo < 0 || eo + 0x10 > buf.length) break;
    const vifListOffset = view.getUint32(eo + 0x00, true);
    const vifListSize = view.getUint16(eo + 0x04, true) * 0x10;
    const vertexOffset = view.getUint32(eo + 0x08, true);
    if (vifListOffset + vifListSize > buf.length || vertexOffset + 0x10 > buf.length) continue;
    const unpacks = filterVifUnpacks(readVifCommandList(buf.subarray(vifListOffset, vifListOffset + vifListSize)));
    if (unpacks.length < 2) continue;
    const stData = unpacks[0]!.data;
    const idxData = unpacks[1]!.data;
    if (idxData.length < 4) continue;
    const secretIndices: number[] = [(idxData[2]! << 24) >> 24];
    const idxBuf: number[] = [];
    for (let i = 4; i < idxData.length; i++) idxBuf.push((idxData[i]! << 24) >> 24);
    const textures: number[] = [];
    if (unpacks.length >= 3) {
      const td = unpacks[2]!.data;
      const tv = new DataView(td.buffer, td.byteOffset, td.byteLength);
      for (let i = 0; i * 0x40 + 0x40 <= td.length; i++) {
        secretIndices.push((td[i * 0x10 + 0x0c]! << 24) >> 24);
        textures.push(tv.getInt32(i * 0x40 + 0x20, true));
      }
    }
    const vh = vertexOffset;
    const matrixTransferCount = view.getUint16(vh + 0x00, true);
    const twoWay = view.getUint16(vh + 0x02, true);
    const threeWay = view.getUint16(vh + 0x04, true);
    const mainCount = view.getUint16(vh + 0x06, true);
    const dupCount = view.getUint16(vh + 0x08, true);
    const vertexTableOffset = view.getUint16(vh + 0x0c, true);
    const inFileCount = twoWay + threeWay + mainCount;
    const vbase = vh + vertexTableOffset;
    const rawIdxRaw: number[] = [];
    if (vbase + inFileCount * 0x10 <= buf.length) {
      for (let v = 0; v < inFileCount; v++) rawIdxRaw.push(view.getUint16(vbase + v * 0x10, true) & 0x1ff);
    }
    out.push({
      vifUnpackCount: unpacks.length, stCount: (stData.length / 4) | 0, secretIndices, idxBuf, textures,
      matrixTransferCount, twoWay, threeWay, mainCount, dupCount, inFileCount, rawIdxRaw,
    });
  }
  return out;
}

/**
 * One skeleton joint. `parent` is the parent joint index (self = root), from
 * `MobyTrans.parentByteOffset / 0x40` (`commonTransOffset`, 0x10 stride).
 * `bind` is the joint's bind-pose global translation in raw class units
 * (`-skeletonRow3`, matching the rest-pose decoder). GC bind rotations are identity.
 */
export interface GcMobyJoint {
  readonly parent: number;
  readonly bind: readonly [number, number, number];
}

/** One animation frame: `speed` plus a local rotation quaternion (`x,y,z,w`, unit) per joint. */
export interface GcMobyFrame {
  readonly speed: number;
  readonly rotations: readonly (readonly [number, number, number, number])[];
}

/** A named motion — an ordered list of `GcMobyFrame`. */
export interface GcMobySequence {
  readonly index: number;
  readonly frames: readonly GcMobyFrame[];
}

const JOINT_STRIDE_BIND = 0x40;

/**
 * Read the moby skeleton hierarchy (`commonTransOffset`, `MobyTrans[jointCount]`,
 * 0x10 stride) plus each joint's bind global translation (`-skeletonRow3`).
 * `jointCount` is `u8 @ 0x08`.
 */
export function readGcMobyJoints(buf: Uint8Array): GcMobyJoint[] {
  const view = new DataView(buf.buffer, buf.byteOffset, buf.byteLength);
  const jointCount = buf[0x08]!;
  const joints: GcMobyJoint[] = [];
  if (jointCount <= 0 || jointCount > 256) return joints;

  const commonTransOffset = view.getInt32(0x18, true);
  const skeletonOffset = view.getInt32(0x14, true);
  if (commonTransOffset <= 0 || commonTransOffset + jointCount * COMMON_TRANS_STRIDE > buf.length) return joints;
  if (skeletonOffset <= 0 || skeletonOffset + jointCount * JOINT_STRIDE_BIND > buf.length) return joints;

  for (let j = 0; j < jointCount; j++) {
    const o = commonTransOffset + j * COMMON_TRANS_STRIDE;
    let parent = Math.floor(view.getUint16(o + 0x0c, true) / JOINT_STRIDE_BIND);
    if (parent < 0 || parent >= jointCount) parent = 0;
    const s = skeletonOffset + j * JOINT_STRIDE_BIND;
    const bx = -view.getFloat32(s + 48, true);
    const by = -view.getFloat32(s + 52, true);
    const bz = -view.getFloat32(s + 56, true);
    joints.push({
      parent,
      bind: Number.isFinite(bx) && Number.isFinite(by) && Number.isFinite(bz) ? [bx, by, bz] : [0, 0, 0],
    });
  }
  return joints;
}

/**
 * Read every `MobySequence` from the offset list at `class + 0x48`
 * (`s32[sequenceCount]`, relative to the class). `sequenceCount` is `u8 @ 0x0c`.
 * `MobySequenceHeader`: `u8 frameCount @ 0x10`; frame offset table
 * `u32[frameCount] @ 0x1c` (`& 0x0fffffff` = offset relative to the class).
 * Frame body: 0x10 header then `s16[4] / 32768` per joint at `+0x10`.
 */
export function readGcMobySequences(buf: Uint8Array): GcMobySequence[] {
  const view = new DataView(buf.buffer, buf.byteOffset, buf.byteLength);
  const sequenceCount = buf[0x0c]!;
  const jointCount = buf[0x08]!;
  const sequences: GcMobySequence[] = [];
  if (sequenceCount <= 0 || sequenceCount > 64 || jointCount <= 0 || jointCount > 256) return sequences;

  const listOffset = MOBY_CLASS_HEADER_SIZE;
  if (listOffset + sequenceCount * 4 > buf.length) return sequences;

  for (let s = 0; s < sequenceCount; s++) {
    const seqOffset = view.getInt32(listOffset + s * 4, true);
    if (seqOffset <= 0 || seqOffset + 0x1c > buf.length) continue;

    const frameCount = buf[seqOffset + 0x10]!;
    const frameTable = seqOffset + 0x1c;
    if (frameCount <= 0 || frameTable + frameCount * 4 > buf.length) {
      sequences.push({ index: s, frames: [] });
      continue;
    }

    const frames: GcMobyFrame[] = [];
    for (let f = 0; f < frameCount; f++) {
      const entry = view.getUint32(frameTable + f * 4, true);
      const frameOffset = entry & 0x0fffffff;
      const dataOffset = frameOffset + 0x10;
      if (frameOffset <= 0 || dataOffset + jointCount * 8 > buf.length) continue;

      const speed = view.getFloat32(frameOffset, true);
      const rotations: [number, number, number, number][] = [];
      for (let j = 0; j < jointCount; j++) {
        const o = dataOffset + j * 8;
        rotations.push([
          view.getInt16(o, true) / 32768,
          view.getInt16(o + 2, true) / 32768,
          view.getInt16(o + 4, true) / 32768,
          view.getInt16(o + 6, true) / 32768,
        ]);
      }
      frames.push({ speed, rotations });
    }
    sequences.push({ index: s, frames });
  }
  return sequences;
}

export interface GcMobyClass {
  readonly oClass: number;
  readonly mesh: GcMobyMesh;
  readonly triangleTextureIds: Int32Array;
  readonly textureIds: readonly number[];
  readonly joints: readonly GcMobyJoint[];
  readonly sequences: readonly GcMobySequence[];
}

/** Parse moby classes from `LevelCoreHeader.mobyClasses` (`MobyClassEntry` 0x20: offset, oClass, u32, u32, u8 textures[16]). */
export function readGcMobyClasses(core: GcLevelCore): Map<number, GcMobyClass> {
  const table = core.coreHeader.mobyClasses;
  const index = new DataView(core.index.buffer, core.index.byteOffset, core.index.byteLength);
  const boundaries = [...core.sectionBoundaries].sort((a, b) => a - b);
  const out = new Map<number, GcMobyClass>();

  for (let i = 0; i < table.count; i++) {
    const at = table.offset + i * MOBY_CLASS_ENTRY_SIZE;
    if (at < 0 || at + MOBY_CLASS_ENTRY_SIZE > core.index.length) break;
    const assetOffset = index.getInt32(at, true);
    const oClass = index.getInt32(at + 4, true);
    if (assetOffset <= 0 || assetOffset >= core.assets.length) continue;
    const textures: number[] = [];
    for (let k = 0; k < 16; k++) textures.push(core.index[at + 0x10 + k]!);

    const end = boundaries.find((b) => b > assetOffset) ?? core.assets.length;
    try {
      const classBuf = core.assets.subarray(assetOffset, end);
      const mesh = readGcMobyClass(classBuf);
      const triangleTextureIds = Int32Array.from(mesh.triangleMaterialSlots, (slot) =>
        slot >= 0 && slot < 16 ? textures[slot]! : -1,
      );
      const joints = readGcMobyJoints(classBuf);
      out.set(oClass, {
        oClass,
        mesh,
        triangleTextureIds,
        textureIds: [...new Set(triangleTextureIds)].filter((id) => id >= 0).sort((a, b) => a - b),
        joints,
        sequences: joints.length > 0 ? readGcMobySequences(classBuf) : [],
      });
    } catch {
      /* skip a class that fails to parse */
    }
  }
  return out;
}
