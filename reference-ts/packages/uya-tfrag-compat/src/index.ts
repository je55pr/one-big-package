import { readGcTfrags, TFRAG_HEADER_SIZE } from "../../gc-tfrag/src/index.js";
import type { GcTfragMesh } from "../../gc-tfrag/src/index.js";
import { Sha256 } from "../../hashing/src/index.js";
import {
  filterVifUnpacks,
  readVifCommandList,
  vifPacketSize,
  VIF_UNPACK,
  type VifPacket,
} from "../../ps2-vif/src/index.js";
import type { UyaDecodedCoreDataPublicLead } from "../../uya-core-decode/src/index.js";

export interface UyaTfragParserPrerequisiteCensus {
  readonly tableOffset: number;
  readonly declaredTfragCount: number;
  readonly parserCandidateTfragCount: number;
  readonly skippedByDataStart: number;
  readonly skippedByCommonUnpacks: number;
  readonly skippedByStrow: number;
  readonly skippedByLod0Streams: number;
  readonly commonVifPacketCount: number;
  readonly commonVifUnpackCount: number;
  readonly commonVifTailBytes: number;
  readonly lod0VifPacketCount: number;
  readonly lod0VifUnpackCount: number;
  readonly lod0VifTailBytes: number;
}

export interface UyaGcTfragCompatibilityPublicLead {
  readonly evidenceStatus: "existing-gc-tfrag-parser-applied-to-uya-candidate-bytes-with-prerequisite-census";
  readonly publicRange: { readonly offset: number; readonly size: number };
  readonly tfragsSha256: string;
  readonly byteLength: number;
  readonly prerequisiteCensus: UyaTfragParserPrerequisiteCensus;
  readonly declaredTfragCount: number;
  readonly vertexCount: number;
  readonly triangleCount: number;
  readonly textureIds: readonly number[];
  readonly textureIdsOutsidePublicTable: readonly number[];
  readonly nonFinitePositionCount: number;
  readonly bounds: GcTfragMesh["bounds"];
}

export interface UyaGcTfragCompatibilityOptions {
  /** Public core-header tfrag texture count, used only for a non-mutating range diagnostic. */
  readonly publicTfragTextureCount?: number;
  readonly maxVertices?: number;
  readonly maxTriangles?: number;
}

/**
 * Apply the existing retail-GC tfrag/VIF decoder to one public-derived UYA coreData section.
 *
 * Unlike collision, a tfrags section may legitimately begin at asset offset zero. The section
 * extent therefore uses the first strictly larger public core boundary. The GC reader itself is
 * not changed or relaxed. Because that reader intentionally skips individual fragments when its
 * required VIF shapes are missing, this wrapper independently reproduces those skip prerequisites
 * as a census so a superficially successful mesh cannot hide widespread parser rejection.
 */
export function probeUyaGcTfragCompatibilityPublicLead(
  decoded: UyaDecodedCoreDataPublicLead,
  publicTfragsOffset: number,
  options: UyaGcTfragCompatibilityOptions = {},
): UyaGcTfragCompatibilityPublicLead {
  const publicRange = publicSectionRangeAllowZero(publicTfragsOffset, decoded.sectionBoundaries, decoded.assets.length);
  if (!publicRange) {
    throw new Error(`No public UYA tfrags range is available at asset offset ${publicTfragsOffset}.`);
  }
  const bytes = decoded.assets.subarray(publicRange.offset, publicRange.offset + publicRange.size);
  const prerequisiteCensus = censusUyaTfragBlobGcPrerequisites(bytes);

  // Deliberately unchanged retail-GC parser. Any global parser failure is compatibility evidence.
  const mesh = readGcTfrags(bytes, {
    ...(options.maxVertices !== undefined ? { maxVertices: options.maxVertices } : {}),
    ...(options.maxTriangles !== undefined ? { maxTriangles: options.maxTriangles } : {}),
  });
  if (mesh.tfragCount !== prerequisiteCensus.declaredTfragCount) {
    throw new Error(`GC tfrag parser declared count ${mesh.tfragCount} disagrees with census ${prerequisiteCensus.declaredTfragCount}.`);
  }

  const textureLimit = options.publicTfragTextureCount;
  const textureIdsOutsidePublicTable = textureLimit === undefined || textureLimit < 0
    ? []
    : mesh.textureIds.filter((id) => id < 0 || id >= textureLimit);
  let nonFinitePositionCount = 0;
  for (const value of mesh.positions) if (!Number.isFinite(value)) nonFinitePositionCount++;

  return {
    evidenceStatus: "existing-gc-tfrag-parser-applied-to-uya-candidate-bytes-with-prerequisite-census",
    publicRange,
    tfragsSha256: sha256(bytes),
    byteLength: bytes.length,
    prerequisiteCensus,
    declaredTfragCount: mesh.tfragCount,
    vertexCount: mesh.positions.length / 3,
    triangleCount: mesh.indices.length / 3,
    textureIds: mesh.textureIds,
    textureIdsOutsidePublicTable,
    nonFinitePositionCount,
    bounds: mesh.bounds,
  };
}

/**
 * Reproduce only the GC reader's per-fragment early-continue prerequisites. This does not decode
 * geometry and does not replace the real parser; it provides observability around its tolerant
 * skip behaviour.
 */
export function censusUyaTfragBlobGcPrerequisites(bytes: Uint8Array): UyaTfragParserPrerequisiteCensus {
  if (bytes.length < 0x10) throw new Error("UYA candidate tfrags blob shorter than the 16-byte header.");
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  const tableOffset = view.getInt32(0, true);
  const declaredTfragCount = view.getInt32(4, true);
  if (tableOffset < 0x10 || tableOffset > bytes.length) {
    throw new Error(`UYA candidate tfrags tableOffset 0x${(tableOffset >>> 0).toString(16)} out of range.`);
  }
  if (declaredTfragCount < 0 || tableOffset + declaredTfragCount * TFRAG_HEADER_SIZE > bytes.length) {
    throw new Error(`UYA candidate tfrags count ${declaredTfragCount} out of range.`);
  }

  let parserCandidateTfragCount = 0;
  let skippedByDataStart = 0;
  let skippedByCommonUnpacks = 0;
  let skippedByStrow = 0;
  let skippedByLod0Streams = 0;
  let commonVifPacketCount = 0;
  let commonVifUnpackCount = 0;
  let commonVifTailBytes = 0;
  let lod0VifPacketCount = 0;
  let lod0VifUnpackCount = 0;
  let lod0VifTailBytes = 0;

  for (let t = 0; t < declaredTfragCount; t++) {
    const ho = tableOffset + t * TFRAG_HEADER_SIZE;
    const data = view.getInt32(ho + 0x10, true);
    const sharedOfs = view.getUint16(ho + 0x16, true);
    const lod1Ofs = view.getUint16(ho + 0x18, true);
    const lod0Ofs = view.getUint16(ho + 0x1a, true);
    const commonSize = bytes[ho + 0x20]!;
    const lod2Size = bytes[ho + 0x21]!;
    const lod1Size = bytes[ho + 0x22]!;
    const rgbaOfs = view.getUint16(ho + 0x1e, true);
    const dataStart = tableOffset + data;
    if (dataStart < 0 || dataStart > bytes.length) {
      skippedByDataStart++;
      continue;
    }

    const sub = (start: number, end: number): Uint8Array =>
      bytes.subarray(dataStart + start, dataStart + Math.min(end, bytes.length - dataStart));

    const commonBytes = sub(sharedOfs, lod1Ofs);
    const commonList = readVifCommandList(commonBytes);
    const commonUnpacks = filterVifUnpacks(commonList);
    commonVifPacketCount += commonList.length;
    commonVifUnpackCount += commonUnpacks.length;
    commonVifTailBytes += vifTailBytes(commonBytes, commonList);
    if (commonUnpacks.length < 4) {
      skippedByCommonUnpacks++;
      continue;
    }

    const strow = commonList[5];
    if (!strow || strow.data.length < 12) {
      skippedByStrow++;
      continue;
    }

    // Match the existing reader's LOD-0 slice/UNPACK selection exactly enough to identify its
    // final early-continue condition (empty strip or index stream).
    const lod0Start = sharedOfs + lod1Size * 0x10;
    const lod0Size = rgbaOfs - (lod1Size + lod2Size - commonSize) * 0x10;
    const lod0Bytes = sub(lod0Start, lod0Start + Math.max(0, lod0Size));
    const lod0List = readVifCommandList(lod0Bytes);
    const lod0Unpacks = filterVifUnpacks(lod0List);
    lod0VifPacketCount += lod0List.length;
    lod0VifUnpackCount += lod0Unpacks.length;
    lod0VifTailBytes += vifTailBytes(lod0Bytes, lod0List);

    let i = 0;
    if (i < lod0Unpacks.length && lod0Unpacks[i]!.code.vnvl === VIF_UNPACK.V3_16) i++;
    const strips = i < lod0Unpacks.length ? lod0Unpacks[i++]!.data : undefined;
    const stripIndices = i < lod0Unpacks.length ? lod0Unpacks[i++]!.data : undefined;
    if (!strips || strips.length === 0 || !stripIndices || stripIndices.length === 0) {
      skippedByLod0Streams++;
      continue;
    }

    parserCandidateTfragCount++;
  }

  return {
    tableOffset,
    declaredTfragCount,
    parserCandidateTfragCount,
    skippedByDataStart,
    skippedByCommonUnpacks,
    skippedByStrow,
    skippedByLod0Streams,
    commonVifPacketCount,
    commonVifUnpackCount,
    commonVifTailBytes,
    lod0VifPacketCount,
    lod0VifUnpackCount,
    lod0VifTailBytes,
  };
}

function publicSectionRangeAllowZero(
  offset: number,
  boundaries: readonly number[],
  assetsLength: number,
): { readonly offset: number; readonly size: number } | undefined {
  if (!Number.isSafeInteger(offset) || offset < 0 || offset >= assetsLength) return undefined;
  let next: number | undefined;
  for (const bound of boundaries) {
    if (bound > offset && bound <= assetsLength && (next === undefined || bound < next)) next = bound;
  }
  return next === undefined ? undefined : { offset, size: next - offset };
}

function vifTailBytes(bytes: Uint8Array, packets: readonly VifPacket[]): number {
  if (bytes.length === 0) return 0;
  const last = packets.at(-1);
  if (!last) return bytes.length;
  const consumed = last.offset + vifPacketSize(last.code);
  return Math.max(0, bytes.length - consumed);
}

function sha256(bytes: Uint8Array): string {
  return new Sha256().update(bytes).digestHex();
}
