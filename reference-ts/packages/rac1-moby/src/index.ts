import { filterVifUnpacks, readVifCommandList, VIF_UNPACK } from "../../ps2-vif/src/index.js";
import { readRac1ClassDirectory } from "../../rac1-level-classes/src/index.js";
import type { Rac1ClassTableEntry } from "../../rac1-level-classes/src/index.js";

export const RAC1_MOBY_CLASS_HEADER_SIZE = 0x48;
export const RAC1_MOBY_PACKET_ENTRY_SIZE = 0x10;
export const RAC1_MOBY_VERTEX_HEADER_SIZE = 0x20;

export interface Rac1MobyBindPoseMesh {
  /** Class-local native Z-up positions. */
  readonly positions: Float64Array;
  readonly uvs: Float32Array;
  readonly indices: Uint32Array;
  /** Per triangle: class-local texture slot, or -1 for no texture. */
  readonly triangleMaterialSlots: Int32Array;
  readonly scale: number;
  readonly highLodPacketCount: number;
  readonly boundingRadius: number;
}

export interface Rac1MobyBindPoseClass {
  readonly oClass: number;
  readonly assetOffset: number;
  readonly mesh: Rac1MobyBindPoseMesh;
  /** Per triangle: level Moby texture id, or -1 for no texture. */
  readonly triangleTextureIds: Int32Array;
  readonly textureIds: readonly number[];
  readonly sourceEntry: Rac1ClassTableEntry;
}

export interface Rac1MobyBindPoseClasses {
  readonly classes: ReadonlyMap<number, Rac1MobyBindPoseClass>;
  readonly animatedClassIds: readonly number[];
  readonly geometryFreeClassIds: readonly number[];
}

/** Compatibility aliases for callers that intentionally select only jointCount=0 classes. */
export type Rac1RigidMobyMesh = Rac1MobyBindPoseMesh;
export type Rac1RigidMobyClass = Rac1MobyBindPoseClass;
export interface Rac1RigidMobyClasses {
  readonly classes: ReadonlyMap<number, Rac1RigidMobyClass>;
  readonly skippedAnimatedClassIds: readonly number[];
}

interface CachedVertex {
  readonly x: number;
  readonly y: number;
  readonly z: number;
}

interface PacketVertex extends CachedVertex {
  nativeIndex: number;
}

interface DecodedPacket {
  readonly positions: number[];
  readonly uvs: number[];
  readonly indices: number[];
  readonly materialSlots: number[];
  readonly activeTexture: number;
}

interface PacketState {
  readonly vertexCache: Map<number, CachedVertex>;
  activeTexture: number;
}

function asS8(value: number): number {
  return value < 0x80 ? value : value - 0x100;
}

function align(value: number, amount: number): number {
  return Math.ceil(value / amount) * amount;
}

/**
 * Decode one R&C1 Moby class's high-LOD bind/rest-pose mesh.
 *
 * Retail validation across all 2,968 payload classes shows that the base mesh
 * positions are the scaled native vertex coordinates for both rigid and animated
 * classes. Joint/blend state describes animation and does not need to be applied
 * to recover this base surface. The 0x20-byte RAC1 vertex-table header and the
 * shared packet/strip/duplicate-cache grammar are still validated strictly.
 */
export function readRac1MobyBindPoseClass(bytes: Uint8Array): Rac1MobyBindPoseMesh {
  if (bytes.length < RAC1_MOBY_CLASS_HEADER_SIZE) {
    throw new Error("R&C1 Moby class buffer shorter than the 0x48 header.");
  }
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  const packetTableOffset = view.getInt32(0x00, true);
  const highLodPacketCount = bytes[0x04]!;
  const scale = view.getFloat32(0x24, true);
  const boundingRadius = Math.abs(view.getFloat32(0x3c, true)) * (scale / 1024);

  if (!Number.isFinite(scale)) throw new Error(`R&C1 Moby class has non-finite scale ${scale}.`);
  if (highLodPacketCount === 0) {
    return {
      positions: new Float64Array(),
      uvs: new Float32Array(),
      indices: new Uint32Array(),
      triangleMaterialSlots: new Int32Array(),
      scale,
      highLodPacketCount,
      boundingRadius,
    };
  }
  if (packetTableOffset <= 0 || packetTableOffset + highLodPacketCount * RAC1_MOBY_PACKET_ENTRY_SIZE > bytes.length) {
    throw new Error(`R&C1 Moby packet table ${packetTableOffset}+${highLodPacketCount * RAC1_MOBY_PACKET_ENTRY_SIZE} is out of range.`);
  }

  const state: PacketState = { vertexCache: new Map(), activeTexture: 0 };
  const positions: number[] = [];
  const uvs: number[] = [];
  const indices: number[] = [];
  const materialSlots: number[] = [];

  for (let packetIndex = 0; packetIndex < highLodPacketCount; packetIndex++) {
    const packet = decodeMobyBindPosePacket(bytes, packetTableOffset + packetIndex * RAC1_MOBY_PACKET_ENTRY_SIZE, scale, state, packetIndex);
    const vertexBase = positions.length / 3;
    positions.push(...packet.positions);
    uvs.push(...packet.uvs);
    for (const index of packet.indices) indices.push(vertexBase + index);
    materialSlots.push(...packet.materialSlots);
    state.activeTexture = packet.activeTexture;
  }

  if (indices.length / 3 !== materialSlots.length) {
    throw new Error("R&C1 Moby triangle/material counts diverged during bind-pose decode.");
  }
  return {
    positions: Float64Array.from(positions),
    uvs: Float32Array.from(uvs),
    indices: Uint32Array.from(indices),
    triangleMaterialSlots: Int32Array.from(materialSlots),
    scale,
    highLodPacketCount,
    boundingRadius,
  };
}

/** Rigid-only compatibility wrapper; animated classes remain explicitly rejected here. */
export function readRac1RigidMobyClass(bytes: Uint8Array): Rac1RigidMobyMesh {
  if (bytes.length < RAC1_MOBY_CLASS_HEADER_SIZE) {
    throw new Error("R&C1 Moby class buffer shorter than the 0x48 header.");
  }
  const jointCount = bytes[0x08]!;
  if (jointCount !== 0) {
    throw new Error(`R&C1 Moby class has ${jointCount} joints; rigid decoder requires jointCount=0.`);
  }
  return readRac1MobyBindPoseClass(bytes);
}

function decodeMobyBindPosePacket(
  bytes: Uint8Array,
  packetEntryOffset: number,
  scale: number,
  state: PacketState,
  packetIndex: number,
): DecodedPacket {
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  const vifListOffset = view.getUint32(packetEntryOffset + 0x00, true);
  const vifListSize = view.getUint16(packetEntryOffset + 0x04, true) * 0x10;
  const vertexOffset = view.getUint32(packetEntryOffset + 0x08, true);
  const vertexDataSize = bytes[packetEntryOffset + 0x0c]! * 0x10;
  const unknownD = bytes[packetEntryOffset + 0x0d]!;
  const unknownE = bytes[packetEntryOffset + 0x0e]!;
  const transferVertexCount = bytes[packetEntryOffset + 0x0f]!;

  if (vifListOffset <= 0 || vifListSize <= 0 || vifListOffset + vifListSize > bytes.length) {
    throw new Error(`R&C1 Moby packet ${packetIndex} VIF range ${vifListOffset}+${vifListSize} is out of range.`);
  }
  if (vertexOffset <= 0 || vertexOffset + RAC1_MOBY_VERTEX_HEADER_SIZE > bytes.length) {
    throw new Error(`R&C1 Moby packet ${packetIndex} vertex header offset ${vertexOffset} is out of range.`);
  }

  const unpacks = filterVifUnpacks(readVifCommandList(bytes.subarray(vifListOffset, vifListOffset + vifListSize)));
  if (unpacks.length !== 2 && unpacks.length !== 3) {
    throw new Error(`R&C1 Moby packet ${packetIndex} has ${unpacks.length} UNPACKs; expected 2 or 3.`);
  }
  if (unpacks[0]!.code.vnvl !== VIF_UNPACK.V2_16 || unpacks[1]!.code.vnvl !== VIF_UNPACK.V4_8) {
    throw new Error(`R&C1 Moby packet ${packetIndex} has unexpected ST/index UNPACK shapes.`);
  }
  if (unpacks.length === 3 && unpacks[2]!.code.vnvl !== VIF_UNPACK.V4_32) {
    throw new Error(`R&C1 Moby packet ${packetIndex} has unexpected texture UNPACK shape.`);
  }

  const indexData = unpacks[1]!.data;
  if (indexData.length < 4 || indexData[3] !== 0) {
    throw new Error(`R&C1 Moby packet ${packetIndex} has an invalid index UNPACK header.`);
  }
  const secretIndices: number[] = [asS8(indexData[2]!)];
  const packetTextures: number[] = [];
  if (unpacks.length === 3) {
    const textureData = unpacks[2]!.data;
    if (textureData.length % 0x40 !== 0) {
      throw new Error(`R&C1 Moby packet ${packetIndex} texture UNPACK is not 0x40-byte aligned.`);
    }
    const textureView = new DataView(textureData.buffer, textureData.byteOffset, textureData.byteLength);
    const textureCount = textureData.length / 0x40;
    for (let i = 0; i < textureCount; i++) {
      secretIndices.push(asS8(textureData[i * 0x10 + 0x0c]!));
      const slot = textureView.getInt32(i * 0x40 + 0x20, true);
      if (slot < -1 || slot >= 16) {
        throw new Error(`R&C1 Moby packet ${packetIndex} uses invalid regular texture slot ${slot}.`);
      }
      packetTextures.push(slot);
    }
  }

  const matrixTransferCount = view.getUint32(vertexOffset + 0x00, true);
  const twoWayCount = view.getUint32(vertexOffset + 0x04, true);
  const threeWayCount = view.getUint32(vertexOffset + 0x08, true);
  const mainVertexCount = view.getUint32(vertexOffset + 0x0c, true);
  const duplicateVertexCount = view.getUint32(vertexOffset + 0x10, true);
  const headerTransferVertexCount = view.getUint32(vertexOffset + 0x14, true);
  const vertexTableOffset = view.getUint32(vertexOffset + 0x18, true);
  const vertexTailOffset = view.getUint32(vertexOffset + 0x1c, true);

  if (headerTransferVertexCount !== transferVertexCount ||
      headerTransferVertexCount !== twoWayCount + threeWayCount + mainVertexCount + duplicateVertexCount) {
    throw new Error(`R&C1 Moby packet ${packetIndex} has conflicting transfer vertex counts.`);
  }
  if (unknownD !== Math.floor((0x0f + transferVertexCount * 6) / 0x10) ||
      unknownE !== Math.floor((3 + transferVertexCount) / 4)) {
    throw new Error(`R&C1 Moby packet ${packetIndex} packet-entry derived fields disagree with transfer count ${transferVertexCount}.`);
  }
  if (vertexTableOffset > vertexDataSize || vertexTailOffset < vertexTableOffset || vertexTailOffset > vertexDataSize) {
    throw new Error(`R&C1 Moby packet ${packetIndex} vertex offsets ${vertexTableOffset}/${vertexTailOffset} exceed ${vertexDataSize}.`);
  }
  if (unpacks[0]!.data.length !== transferVertexCount * 4) {
    throw new Error(`R&C1 Moby packet ${packetIndex} ST count does not equal transfer vertex count.`);
  }

  const inFileVertexCount = twoWayCount + threeWayCount + mainVertexCount;
  if (vertexTableOffset + inFileVertexCount * 0x10 > vertexTailOffset) {
    throw new Error(`R&C1 Moby packet ${packetIndex} in-file vertex array crosses its native tail boundary.`);
  }
  if ((vertexTailOffset - vertexTableOffset) % 0x10 !== 0) {
    throw new Error(`R&C1 Moby packet ${packetIndex} vertex tail is not 0x10-byte aligned.`);
  }
  const epilogueVertexCount = (vertexTailOffset - vertexTableOffset) / 0x10 - inFileVertexCount;
  if (epilogueVertexCount < 0 || epilogueVertexCount >= 7) {
    throw new Error(`R&C1 Moby packet ${packetIndex} has invalid epilogue vertex count ${epilogueVertexCount}.`);
  }

  let duplicateArrayOffset = vertexOffset + RAC1_MOBY_VERTEX_HEADER_SIZE + matrixTransferCount * 2;
  duplicateArrayOffset = align(align(duplicateArrayOffset, 4), 8);
  if (duplicateArrayOffset + duplicateVertexCount * 2 > vertexOffset + vertexTableOffset) {
    throw new Error(`R&C1 Moby packet ${packetIndex} matrix/duplicate prelude crosses the vertex table.`);
  }

  const vertices: PacketVertex[] = [];
  const k = scale / 1024;
  for (let i = 0; i < inFileVertexCount; i++) {
    const at = vertexOffset + vertexTableOffset + i * 0x10;
    if (at + 0x10 > bytes.length) throw new Error(`R&C1 Moby packet ${packetIndex} vertex ${i} is out of range.`);
    vertices.push({
      x: view.getInt16(at + 0x0a, true) * k,
      y: view.getInt16(at + 0x0c, true) * k,
      z: view.getInt16(at + 0x0e, true) * k,
      nativeIndex: view.getUint16(at, true) & 0x1ff,
    });
  }

  // Native Moby indices are software-pipelined seven entries ahead.
  for (let i = 7; i < vertices.length; i++) vertices[i - 7]!.nativeIndex = vertices[i]!.nativeIndex;

  let epilogueCursor = vertexOffset + vertexTableOffset + inFileVertexCount * 0x10;
  epilogueCursor += Math.max(7 - inFileVertexCount, 0) * 0x10;
  for (let i = Math.max(7 - inFileVertexCount, 0); i < epilogueVertexCount; i++) {
    if (epilogueCursor + 0x10 > bytes.length) throw new Error(`R&C1 Moby packet ${packetIndex} epilogue is out of range.`);
    const destination = inFileVertexCount + i - 7;
    if (destination < 0 || destination >= vertices.length) throw new Error(`R&C1 Moby packet ${packetIndex} epilogue destination ${destination} is invalid.`);
    vertices[destination]!.nativeIndex = view.getUint16(epilogueCursor, true) & 0x1ff;
    epilogueCursor += 0x10;
  }
  const lastVertexOffset = epilogueCursor - 0x10;
  if (lastVertexOffset < vertexOffset || lastVertexOffset + 0x10 > bytes.length) {
    throw new Error(`R&C1 Moby packet ${packetIndex} final pipeline vertex is out of range.`);
  }
  for (let i = Math.max(7 - inFileVertexCount - epilogueVertexCount, 0); i < 6; i++) {
    const destination = inFileVertexCount + epilogueVertexCount + i - 7;
    if (destination >= 0 && destination < vertices.length) {
      vertices[destination]!.nativeIndex = view.getUint16(lastVertexOffset + 4 + i * 2, true) & 0x1ff;
    }
  }

  const duplicateIndices: number[] = [];
  for (let i = 0; i < duplicateVertexCount; i++) {
    duplicateIndices.push(view.getUint16(duplicateArrayOffset + i * 2, true) >> 7);
  }

  const stView = new DataView(unpacks[0]!.data.buffer, unpacks[0]!.data.byteOffset, unpacks[0]!.data.byteLength);
  const positions: number[] = [];
  const uvs: number[] = [];
  for (let i = 0; i < vertices.length; i++) {
    const vertex = vertices[i]!;
    positions.push(vertex.x, vertex.y, vertex.z);
    uvs.push(stView.getInt16(i * 4, true) / 4096, stView.getInt16(i * 4 + 2, true) / 4096);
    state.vertexCache.set(vertex.nativeIndex, { x: vertex.x, y: vertex.y, z: vertex.z });
  }
  for (let i = 0; i < duplicateIndices.length; i++) {
    const sourceIndex = duplicateIndices[i]!;
    const source = state.vertexCache.get(sourceIndex);
    if (!source) throw new Error(`R&C1 Moby packet ${packetIndex} duplicate references uncached native vertex ${sourceIndex}.`);
    positions.push(source.x, source.y, source.z);
    const stIndex = vertices.length + i;
    uvs.push(stView.getInt16(stIndex * 4, true) / 4096, stView.getInt16(stIndex * 4 + 2, true) / 4096);
  }

  const primitives: { material: number; strip: number[] }[] = [];
  let primitive: { material: number; strip: number[] } | null = null;
  let adGifIndex = 0;
  let activeTexture = state.activeTexture;
  const rawIndices = [...indexData.subarray(4)].map(asS8);

  for (let j = 0; j < rawIndices.length; j++) {
    let index = rawIndices[j]!;
    if (index === 0) {
      if (adGifIndex >= secretIndices.length) throw new Error(`R&C1 Moby packet ${packetIndex} exhausted its secret-index table.`);
      const secretIndex = secretIndices[adGifIndex]!;
      if (secretIndex === 0) {
        if (!primitive || primitive.strip.length < 3) throw new Error(`R&C1 Moby packet ${packetIndex} terminated without an active strip.`);
        primitive.strip.splice(primitive.strip.length - 3, 3);
        break;
      }
      index = secretIndex - 0x80;
      if (adGifIndex >= packetTextures.length) throw new Error(`R&C1 Moby packet ${packetIndex} texture switch ${adGifIndex} has no texture record.`);
      activeTexture = packetTextures[adGifIndex]!;
      adGifIndex++;
    }

    if (index <= 0) {
      if (j + 1 < rawIndices.length && rawIndices[j + 1]! <= 0) {
        primitive = { material: activeTexture, strip: [] };
        primitives.push(primitive);
      } else {
        if (!primitive || primitive.strip.length < 1) throw new Error(`R&C1 Moby packet ${packetIndex} has an invalid strip restart.`);
        primitive.strip.push(primitive.strip[primitive.strip.length - 1]!);
      }
    }
    if (!primitive) throw new Error(`R&C1 Moby packet ${packetIndex} index buffer emits a vertex before a strip.`);
    primitive.strip.push((index & 0x7f) - 1);
  }

  const indices: number[] = [];
  const materialSlots: number[] = [];
  const vertexCount = positions.length / 3;
  for (const current of primitives) {
    if (current.material < -1 || current.material >= 16) {
      throw new Error(`R&C1 Moby packet ${packetIndex} emits invalid material slot ${current.material}.`);
    }
    for (let i = 0; i + 2 < current.strip.length; i++) {
      const a = current.strip[i]!;
      const b = current.strip[i + 1]!;
      const c = current.strip[i + 2]!;
      if (a < 0 || b < 0 || c < 0 || a >= vertexCount || b >= vertexCount || c >= vertexCount) {
        throw new Error(`R&C1 Moby packet ${packetIndex} triangle index is outside ${vertexCount} vertices.`);
      }
      if (a === b || b === c || a === c) continue;
      if ((i & 1) === 0) indices.push(a, b, c);
      else indices.push(b, a, c);
      materialSlots.push(current.material);
    }
  }

  return { positions, uvs, indices, materialSlots, activeTexture };
}

/** Decode every Moby payload in a native R&C1 core into its bind/rest-pose surface. */
export function readRac1MobyBindPoseClasses(index: Uint8Array, assets: Uint8Array): Rac1MobyBindPoseClasses {
  const directory = readRac1ClassDirectory(index, assets.length);
  const allEntries = [...directory.moby.entries, ...directory.tie.entries, ...directory.shrub.entries]
    .filter((entry) => entry.assetOffset > 0);
  const boundaries = [...new Set(allEntries.map((entry) => entry.assetOffset))].sort((a, b) => a - b);
  const nextBoundary = (offset: number): number => boundaries.find((value) => value > offset) ?? assets.length;

  const classes = new Map<number, Rac1MobyBindPoseClass>();
  const animatedClassIds: number[] = [];
  const geometryFreeClassIds: number[] = [];
  for (const entry of directory.moby.entries) {
    if (entry.assetOffset === 0) continue;
    const end = nextBoundary(entry.assetOffset);
    if (end <= entry.assetOffset) throw new Error(`R&C1 Moby class ${entry.oClass} has no positive asset range.`);
    const bytes = assets.subarray(entry.assetOffset, end);
    if (bytes.length < RAC1_MOBY_CLASS_HEADER_SIZE) throw new Error(`R&C1 Moby class ${entry.oClass} is shorter than its 0x48-byte header.`);
    const jointCount = bytes[0x08]!;
    if (jointCount !== 0) animatedClassIds.push(entry.oClass);
    if (classes.has(entry.oClass)) throw new Error(`Duplicate R&C1 Moby class id ${entry.oClass}.`);
    const mesh = readRac1MobyBindPoseClass(bytes);
    if (mesh.highLodPacketCount === 0) geometryFreeClassIds.push(entry.oClass);
    const triangleTextureIds = new Int32Array(mesh.triangleMaterialSlots.length);
    const used = new Set<number>();
    for (let i = 0; i < mesh.triangleMaterialSlots.length; i++) {
      const slot = mesh.triangleMaterialSlots[i]!;
      if (slot === -1) {
        triangleTextureIds[i] = -1;
        continue;
      }
      if (slot < 0 || slot >= entry.textureIds.length) {
        throw new Error(`R&C1 Moby class ${entry.oClass} uses out-of-range texture slot ${slot}.`);
      }
      const textureId = entry.textureIds[slot]!;
      triangleTextureIds[i] = textureId;
      used.add(textureId);
    }
    classes.set(entry.oClass, {
      oClass: entry.oClass,
      assetOffset: entry.assetOffset,
      mesh,
      triangleTextureIds,
      textureIds: [...used].sort((a, b) => a - b),
      sourceEntry: entry,
    });
  }
  return {
    classes,
    animatedClassIds: animatedClassIds.sort((a, b) => a - b),
    geometryFreeClassIds: geometryFreeClassIds.sort((a, b) => a - b),
  };
}

/** Decode only rigid classes for callers that are not ready to expose animated class ids. */
export function readRac1RigidMobyClasses(index: Uint8Array, assets: Uint8Array): Rac1RigidMobyClasses {
  const all = readRac1MobyBindPoseClasses(index, assets);
  const classes = new Map<number, Rac1RigidMobyClass>();
  for (const [oClass, cls] of all.classes) {
    if (!all.animatedClassIds.includes(oClass)) classes.set(oClass, cls);
  }
  return { classes, skippedAnimatedClassIds: all.animatedClassIds };
}
