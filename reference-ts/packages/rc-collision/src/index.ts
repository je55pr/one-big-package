import type { OBPBounds, OBPCollisionMesh, OBPSourceRef } from "../../core/src/index.js";

/**
 * Reader for the octree collision mesh format shared by the PS2 Ratchet & Clank
 * games (Wrench uses one `read_collision` entry point for RC1/RC2/RC3/Deadlocked).
 *
 * Retail verification currently includes:
 *
 * - Going Commando NTSC-U `LEVEL1.WAD` chunk collision (`research/GC_COLLISION.md`).
 * - R&C1 NTSC-U original: the native collision block recovered independently
 *   from all 19 level cores parses under the same validation with no R&C1
 *   special cases (`research/RAC1_COLLISION.md`).
 *
 * Implemented from the documented format, not translated from any external
 * implementation.
 *
 * ```text
 * CollisionHeader                @ 0x00
 *   s32 meshOffset               (0x40 on retail: header is padded to 0x40)
 *   s32 heroGroupsOffset         (0 when absent)
 *
 * mesh @ meshOffset:
 *   s16 zCoord;  u16 zCount;  u16 zOffsets[zCount]      // *4 => byte offset from meshOffset; 0 = empty
 *   z node @ zOff:  s16 yCoord; u16 yCount; u32 yOffsets[yCount]   // 0 = empty
 *   y node @ yOff:  s16 xCoord; u16 xCount; u32 xOffsets[xCount]   // (xOff >> 8) => octant byte offset; 0 = empty
 *   octant @ octOff:
 *     u16 faceCount; u8 vertexCount; u8 quadCount        // faceCount >= quadCount
 *     u32 packedVertex[vertexCount]                      // bits: z[20..31]/64, y[10..19]/16, x[0..9]/16 (all signed)
 *     { u8 v0; u8 v1; u8 v2; u8 type }[faceCount]
 *     u8 v3[quadCount]                                   // promotes the first quadCount faces to quads
 * ```
 *
 * Octant grid cells are 4 units. World vertex = local vertex + (gridX*4+2,
 * gridY*4+2, gridZ*4+2). Quads are triangulated (v0,v1,v2)+(v0,v2,v3); the
 * native face `type` byte (0..255, the collision material / surface id) is kept
 * per triangle. Hero collision groups are counted but not decoded here.
 */

export const RC_COLLISION_HEADER_SIZE = 8;
export const RC_COLLISION_OCTANT_UNITS = 4;

export interface RcCollisionOctant {
  readonly index: number;
  readonly byteOffset: number;
  readonly gridX: number;
  readonly gridY: number;
  readonly gridZ: number;
  readonly originX: number;
  readonly originY: number;
  readonly originZ: number;
  readonly vertexCount: number;
  readonly faceCount: number;
  readonly quadCount: number;
  /** Index of this octant's first vertex in {@link RcCollisionMesh.positions}. */
  readonly firstVertex: number;
}

export interface RcCollisionTriangle {
  readonly a: number;
  readonly b: number;
  readonly c: number;
  /** Native collision face `type` byte (surface / material id). */
  readonly materialId: number;
  readonly octantIndex: number;
  readonly fromQuad: boolean;
}

export interface RcCollisionMesh {
  readonly meshOffset: number;
  readonly heroGroupsOffset: number;
  readonly heroGroupCount: number;
  readonly octants: readonly RcCollisionOctant[];
  /** Flat world-space vertex positions, 3 floats per vertex. */
  readonly positions: Float64Array;
  readonly triangles: readonly RcCollisionTriangle[];
  readonly bounds: OBPBounds;
  /** Distinct native `type` bytes seen, ascending. */
  readonly materialIds: readonly number[];
}

export interface RcCollisionOptions {
  readonly maxOctants?: number;
  readonly maxVertices?: number;
  readonly maxTriangles?: number;
}

const DEFAULT_MAX_OCTANTS = 200_000;
const DEFAULT_MAX_VERTICES = 8_000_000;
const DEFAULT_MAX_TRIANGLES = 8_000_000;

export function readRcCollision(bytes: Uint8Array, options: RcCollisionOptions = {}): RcCollisionMesh {
  const maxOctants = options.maxOctants ?? DEFAULT_MAX_OCTANTS;
  const maxVertices = options.maxVertices ?? DEFAULT_MAX_VERTICES;
  const maxTriangles = options.maxTriangles ?? DEFAULT_MAX_TRIANGLES;

  if (bytes.length < RC_COLLISION_HEADER_SIZE) throw new Error("RC collision source shorter than the 8-byte header.");
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);

  const meshOffset = view.getInt32(0, true);
  const heroGroupsOffset = view.getInt32(4, true);
  if (meshOffset < RC_COLLISION_HEADER_SIZE || meshOffset >= bytes.length) {
    throw new Error(`RC collision meshOffset 0x${(meshOffset >>> 0).toString(16)} is out of range.`);
  }
  if (heroGroupsOffset !== 0 && (heroGroupsOffset < meshOffset || heroGroupsOffset > bytes.length)) {
    throw new Error(`RC collision heroGroupsOffset 0x${(heroGroupsOffset >>> 0).toString(16)} is out of range.`);
  }
  const meshEnd = heroGroupsOffset !== 0 ? heroGroupsOffset : bytes.length;

  const u8 = (at: number): number => {
    if (at < 0 || at >= bytes.length) throw new Error(`RC collision read past end at ${at}.`);
    return bytes[at]!;
  };
  const s16 = (at: number): number => {
    if (at < 0 || at + 2 > bytes.length) throw new Error(`RC collision read past end at ${at}.`);
    return view.getInt16(at, true);
  };
  const u16 = (at: number): number => {
    if (at < 0 || at + 2 > bytes.length) throw new Error(`RC collision read past end at ${at}.`);
    return view.getUint16(at, true);
  };
  const u32 = (at: number): number => {
    if (at < 0 || at + 4 > bytes.length) throw new Error(`RC collision read past end at ${at}.`);
    return view.getUint32(at, true);
  };

  const octants: RcCollisionOctant[] = [];
  const positionList: number[] = [];
  const triangles: RcCollisionTriangle[] = [];
  const materialIdSet = new Set<number>();
  let min: [number, number, number] = [Infinity, Infinity, Infinity];
  let max: [number, number, number] = [-Infinity, -Infinity, -Infinity];

  const zCoord = s16(meshOffset);
  const zCount = u16(meshOffset + 2);
  const zOffsets = meshOffset + 4;

  for (let z = 0; z < zCount; z++) {
    const zByte = u16(zOffsets + z * 2) * 4;
    if (zByte === 0) continue;
    const zNode = meshOffset + zByte;
    const yCoord = s16(zNode);
    const yCount = u16(zNode + 2);
    const yOffsets = zNode + 4;

    for (let y = 0; y < yCount; y++) {
      const yByte = u32(yOffsets + y * 4);
      if (yByte === 0) continue;
      const yNode = meshOffset + yByte;
      const xCoord = s16(yNode);
      const xCount = u16(yNode + 2);
      const xOffsets = yNode + 4;

      for (let x = 0; x < xCount; x++) {
        const octByte = u32(xOffsets + x * 4) >>> 8;
        if (octByte === 0) continue;
        const octOffset = meshOffset + octByte;
        if (octOffset < meshOffset || octOffset + 4 > meshEnd) {
          throw new Error(`RC collision octant offset 0x${octByte.toString(16)} out of mesh range.`);
        }

        const faceCount = u16(octOffset);
        const vertexCount = u8(octOffset + 2);
        const quadCount = u8(octOffset + 3);
        if (quadCount > faceCount) throw new Error("RC collision octant quad count exceeds face count.");

        if (octants.length >= maxOctants) throw new Error(`RC collision exceeds maxOctants ${maxOctants}.`);
        if (positionList.length / 3 + vertexCount > maxVertices) throw new Error(`RC collision exceeds maxVertices ${maxVertices}.`);
        if (triangles.length + faceCount + quadCount > maxTriangles) throw new Error(`RC collision exceeds maxTriangles ${maxTriangles}.`);

        const gridX = xCoord + x;
        const gridY = yCoord + y;
        const gridZ = zCoord + z;
        const originX = gridX * RC_COLLISION_OCTANT_UNITS + 2;
        const originY = gridY * RC_COLLISION_OCTANT_UNITS + 2;
        const originZ = gridZ * RC_COLLISION_OCTANT_UNITS + 2;

        const firstVertex = positionList.length / 3;
        let cursor = octOffset + 4;
        for (let v = 0; v < vertexCount; v++) {
          const packed = u32(cursor);
          cursor += 4;
          const lx = ((packed << 22) >> 22) / 16;
          const ly = ((packed << 12) >> 22) / 16;
          const lz = ((packed << 0) >> 20) / 64;
          const wx = originX + lx;
          const wy = originY + ly;
          const wz = originZ + lz;
          positionList.push(wx, wy, wz);
          if (wx < min[0]) min[0] = wx;
          if (wy < min[1]) min[1] = wy;
          if (wz < min[2]) min[2] = wz;
          if (wx > max[0]) max[0] = wx;
          if (wy > max[1]) max[1] = wy;
          if (wz > max[2]) max[2] = wz;
        }

        const faceBase = cursor;
        const quadBase = faceBase + faceCount * 4;
        if (quadBase + quadCount > meshEnd) throw new Error("RC collision octant face table runs past the mesh.");

        for (let f = 0; f < faceCount; f++) {
          const fo = faceBase + f * 4;
          const v0 = u8(fo);
          const v1 = u8(fo + 1);
          const v2 = u8(fo + 2);
          const type = u8(fo + 3);
          const isQuad = f < quadCount;
          for (const vi of isQuad ? [v0, v1, v2, u8(quadBase + f)] : [v0, v1, v2]) {
            if (vi >= vertexCount) throw new Error(`RC collision face vertex index ${vi} >= octant vertex count ${vertexCount}.`);
          }
          materialIdSet.add(type);
          triangles.push({ a: firstVertex + v0, b: firstVertex + v1, c: firstVertex + v2, materialId: type, octantIndex: octants.length, fromQuad: isQuad });
          if (isQuad) {
            const v3 = u8(quadBase + f);
            triangles.push({ a: firstVertex + v0, b: firstVertex + v2, c: firstVertex + v3, materialId: type, octantIndex: octants.length, fromQuad: true });
          }
        }

        octants.push({
          index: octants.length,
          byteOffset: octOffset,
          gridX, gridY, gridZ,
          originX, originY, originZ,
          vertexCount, faceCount, quadCount,
          firstVertex,
        });
      }
    }
  }

  let heroGroupCount = 0;
  if (heroGroupsOffset !== 0 && heroGroupsOffset + 4 <= bytes.length) {
    heroGroupCount = view.getInt32(heroGroupsOffset, true);
    if (heroGroupCount < 0 || heroGroupCount > 4096) heroGroupCount = 0;
  }

  const positions = Float64Array.from(positionList);
  if (positions.length === 0) {
    min = [0, 0, 0];
    max = [0, 0, 0];
  }

  return {
    meshOffset,
    heroGroupsOffset,
    heroGroupCount,
    octants,
    positions,
    triangles,
    bounds: { min: { x: min[0], y: min[1], z: min[2] }, max: { x: max[0], y: max[1], z: max[2] } },
    materialIds: [...materialIdSet].sort((p, q) => p - q),
  };
}

/**
 * Convert a native RC vertex (Z-up, as the games store it) to OBP world space
 * (Y-up, as the OBP schema and viewer expect).
 */
export function rcToObpPosition(x: number, y: number, z: number): [number, number, number] {
  return [x, z, y];
}

/** OBP-space bounds of a parsed collision mesh (Y-up). */
export function obpCollisionBounds(mesh: RcCollisionMesh): OBPBounds {
  return {
    min: { x: mesh.bounds.min.x, y: mesh.bounds.min.z, z: mesh.bounds.min.y },
    max: { x: mesh.bounds.max.x, y: mesh.bounds.max.z, z: mesh.bounds.max.y },
  };
}

/** Normalize a parsed collision mesh into an OBP collision mesh, keeping native material ids and provenance. */
export function toObpCollisionMesh(mesh: RcCollisionMesh, source: OBPSourceRef, id: string, name: string): OBPCollisionMesh {
  const positions: number[] = new Array(mesh.positions.length);
  for (let i = 0; i < mesh.positions.length; i += 3) {
    const [x, y, z] = rcToObpPosition(mesh.positions[i]!, mesh.positions[i + 1]!, mesh.positions[i + 2]!);
    positions[i] = x;
    positions[i + 1] = y;
    positions[i + 2] = z;
  }
  const indices: number[] = [];
  const triangleMaterialIds: number[] = [];
  for (const tri of mesh.triangles) {
    indices.push(tri.a, tri.b, tri.c);
    triangleMaterialIds.push(tri.materialId);
  }
  return {
    id,
    name,
    geometry: { positions, indices },
    triangleMaterialIds,
    source: {
      ...source,
      notes: [
        ...(source.notes ?? []),
        `rc-collision: ${mesh.octants.length} octants, ${mesh.positions.length / 3} vertices, ${mesh.triangles.length} triangles`,
        `native meshOffset=0x${mesh.meshOffset.toString(16)} heroGroupsOffset=0x${mesh.heroGroupsOffset.toString(16)} heroGroups=${mesh.heroGroupCount}`,
      ],
    },
  };
}
