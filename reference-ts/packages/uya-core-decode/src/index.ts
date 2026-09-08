import type { RandomAccessReader } from "../../importer-common/src/index.js";
import { Sha256 } from "../../hashing/src/index.js";
import { readRcCollision } from "../../rc-collision/src/index.js";
import type { RcCollisionMesh } from "../../rc-collision/src/index.js";
import { decompressWad } from "../../wad-lz/src/index.js";
import type {
  UyaCoreDataWadLzHeaderPublicLead,
  UyaLevelCoreHeaderPublicLead,
  UyaPublicLeadArrayRange,
} from "../../uya-level-core/src/index.js";
import type {
  UyaLevelDataHeaderPublicLead,
  UyaLevelWadHeaderPublicLead,
} from "../../uya-level-wad/src/index.js";

export const UYA_CORE_BOUNDARY_WRENCH_SOURCE = {
  repository: "chaoticgd/wrench",
  commit: "e67ea46e5e60ec52a0c31fcde1e3f46b20aa4cfb",
  path: "src/wrenchbuild/level/level_core.cpp",
} as const;

const DEFAULT_MAX_CORE_INDEX_BYTES = 16 * 1024 * 1024;
const DEFAULT_MAX_DECOMPRESSED_BYTES = 128 * 1024 * 1024;
const DEFAULT_MAX_CLASS_ENTRIES = 65_536;

export interface UyaPublicLeadByteRange {
  readonly offset: number;
  readonly size: number;
}

export interface UyaDecodedCoreDataPublicLead {
  readonly evidenceStatus: "public-format-leads-applied-to-observed-and-decoded-bytes";
  readonly sourceName: string;
  readonly coreIndex: Uint8Array;
  readonly coreIndexSha256: string;
  readonly compressedCoreData: Uint8Array;
  readonly compressedCoreDataSha256: string;
  readonly assets: Uint8Array;
  readonly assetsSha256: string;
  readonly compatibilityChecks: {
    readonly publicCompressedSizeMatchesWad: boolean | undefined;
    readonly publicDecompressedSizeMatchesOutput: boolean | undefined;
  };
  readonly sectionBoundaries: readonly number[];
  readonly publicCollisionRange?: UyaPublicLeadByteRange;
  readonly publicCollisionSha256?: string;
  readonly warnings: readonly string[];
  readonly publicSource: typeof UYA_CORE_BOUNDARY_WRENCH_SOURCE;
}

export interface UyaCoreDecodePublicLeadOptions {
  readonly maxCoreIndexBytes?: number;
  readonly maxDecompressedBytes?: number;
  readonly maxClassEntries?: number;
}

export interface UyaRcCollisionCompatibilityPublicLead {
  readonly evidenceStatus: "existing-gc-parser-applied-to-uya-candidate-bytes";
  readonly collisionSha256: string;
  readonly byteLength: number;
  readonly octantCount: number;
  readonly vertexCount: number;
  readonly triangleCount: number;
  readonly materialIds: readonly number[];
  readonly nativeBounds: RcCollisionMesh["bounds"];
  readonly meshOffset: number;
  readonly heroGroupsOffset: number;
  readonly heroGroupCount: number;
}

/**
 * Explicitly test the pinned GC/UYA coreData compatibility lead. This reads the exact compressed
 * block declared by the already-observed WAD header, runs the existing GC-retail-verified WAD-LZ
 * codec without relaxing it, and derives public core-section boundaries from the separate index.
 */
export async function decodeUyaCoreDataPublicLead(
  levelReader: RandomAccessReader,
  outer: UyaLevelWadHeaderPublicLead,
  dataHeader: UyaLevelDataHeaderPublicLead,
  coreHeader: UyaLevelCoreHeaderPublicLead,
  wadHeader: UyaCoreDataWadLzHeaderPublicLead,
  options: UyaCoreDecodePublicLeadOptions = {},
): Promise<UyaDecodedCoreDataPublicLead> {
  const maxCoreIndexBytes = checkedPositive(options.maxCoreIndexBytes ?? DEFAULT_MAX_CORE_INDEX_BYTES, "maxCoreIndexBytes");
  const maxDecompressedBytes = checkedPositive(options.maxDecompressedBytes ?? DEFAULT_MAX_DECOMPRESSED_BYTES, "maxDecompressedBytes");
  const maxClassEntries = checkedPositive(options.maxClassEntries ?? DEFAULT_MAX_CLASS_ENTRIES, "maxClassEntries");
  const data = requireDataRange(outer);
  if (!dataHeader.coreIndex.present) throw new Error("UYA candidate data header has no public coreIndex range.");
  if (!dataHeader.coreData.present) throw new Error("UYA candidate data header has no public coreData range.");
  if (dataHeader.coreIndex.size > maxCoreIndexBytes) {
    throw new Error(`UYA candidate coreIndex ${dataHeader.coreIndex.size} exceeds probe cap ${maxCoreIndexBytes}.`);
  }

  const coreIndexOffset = checkedAdd(data.offsetBytes, dataHeader.coreIndex.offset, "coreIndex level offset");
  assertRange(levelReader, coreIndexOffset, dataHeader.coreIndex.size, "coreIndex");
  const coreIndex = await levelReader.read(coreIndexOffset, dataHeader.coreIndex.size);
  if (coreIndex.length !== dataHeader.coreIndex.size) throw new Error(`Short UYA coreIndex read from '${levelReader.name}'.`);

  const compressedOffset = checkedAdd(data.offsetBytes, dataHeader.coreData.offset, "coreData level offset");
  if (wadHeader.offsetInLevelBytes !== compressedOffset) {
    throw new Error(`Observed UYA WAD header offset ${wadHeader.offsetInLevelBytes} disagrees with public coreData offset ${compressedOffset}.`);
  }
  if (wadHeader.compressedSize > dataHeader.coreData.size) {
    throw new Error(`Observed UYA WAD size ${wadHeader.compressedSize} exceeds public coreData range ${dataHeader.coreData.size}.`);
  }
  assertRange(levelReader, compressedOffset, wadHeader.compressedSize, "compressed coreData");
  const compressedCoreData = await levelReader.read(compressedOffset, wadHeader.compressedSize);
  if (compressedCoreData.length !== wadHeader.compressedSize) throw new Error(`Short UYA compressed coreData read from '${levelReader.name}'.`);

  // No UYA-specific leniency: this is deliberately the existing strict OBP WAD-LZ implementation.
  const assets = decompressWad(compressedCoreData, { maxOutputBytes: maxDecompressedBytes }).data;
  const warnings: string[] = [];
  const declaredCompressed = coreHeader.publicFields.assetsCompressedSize;
  const declaredDecompressed = coreHeader.publicFields.assetsDecompressedSize;
  const publicCompressedSizeMatchesWad = declaredCompressed > 0 ? declaredCompressed === compressedCoreData.length : undefined;
  const publicDecompressedSizeMatchesOutput = declaredDecompressed > 0 ? declaredDecompressed === assets.length : undefined;
  if (publicCompressedSizeMatchesWad === false) {
    warnings.push(`Public core header compressed-size field ${declaredCompressed} disagrees with observed WAD size ${compressedCoreData.length}.`);
  }
  if (publicDecompressedSizeMatchesOutput === false) {
    warnings.push(`Public core header decompressed-size field ${declaredDecompressed} disagrees with decoded ${assets.length} bytes.`);
  }

  const boundaryResult = enumeratePublicCoreBoundaries(coreHeader, coreIndex, assets.length, maxClassEntries);
  warnings.push(...boundaryResult.warnings);
  const publicCollisionRange = sectionRange(coreHeader.publicFields.collision, boundaryResult.boundaries);
  let publicCollisionSha256: string | undefined;
  if (coreHeader.publicFields.collision > 0 && !publicCollisionRange) {
    warnings.push(`Public collision offset ${coreHeader.publicFields.collision} has no valid following public section boundary.`);
  } else if (publicCollisionRange) {
    publicCollisionSha256 = sha256(assets.subarray(publicCollisionRange.offset, publicCollisionRange.offset + publicCollisionRange.size));
  }

  return {
    evidenceStatus: "public-format-leads-applied-to-observed-and-decoded-bytes",
    sourceName: levelReader.name,
    coreIndex,
    coreIndexSha256: sha256(coreIndex),
    compressedCoreData,
    compressedCoreDataSha256: sha256(compressedCoreData),
    assets,
    assetsSha256: sha256(assets),
    compatibilityChecks: {
      publicCompressedSizeMatchesWad,
      publicDecompressedSizeMatchesOutput,
    },
    sectionBoundaries: boundaryResult.boundaries,
    ...(publicCollisionRange ? { publicCollisionRange } : {}),
    ...(publicCollisionSha256 ? { publicCollisionSha256 } : {}),
    warnings,
    publicSource: UYA_CORE_BOUNDARY_WRENCH_SOURCE,
  };
}

/**
 * Apply the unchanged retail-GC `readRcCollision` parser to the public candidate UYA collision
 * range. Parser failure is intentionally allowed to throw: divergence must not be hidden by relaxed validation.
 */
export function probeUyaRcCollisionCompatibilityPublicLead(decoded: UyaDecodedCoreDataPublicLead): UyaRcCollisionCompatibilityPublicLead {
  const range = decoded.publicCollisionRange;
  if (!range) throw new Error("No public UYA collision range is available for compatibility probing.");
  const bytes = decoded.assets.subarray(range.offset, range.offset + range.size);
  const mesh = readRcCollision(bytes);
  return {
    evidenceStatus: "existing-gc-parser-applied-to-uya-candidate-bytes",
    collisionSha256: sha256(bytes),
    byteLength: bytes.length,
    octantCount: mesh.octants.length,
    vertexCount: mesh.positions.length / 3,
    triangleCount: mesh.triangles.length,
    materialIds: mesh.materialIds,
    nativeBounds: mesh.bounds,
    meshOffset: mesh.meshOffset,
    heroGroupsOffset: mesh.heroGroupsOffset,
    heroGroupCount: mesh.heroGroupCount,
  };
}

function enumeratePublicCoreBoundaries(
  core: UyaLevelCoreHeaderPublicLead,
  indexBytes: Uint8Array,
  assetsLength: number,
  maxClassEntries: number,
): { readonly boundaries: readonly number[]; readonly warnings: readonly string[] } {
  const fields = core.publicFields;
  const index = new DataView(indexBytes.buffer, indexBytes.byteOffset, indexBytes.byteLength);
  const bounds = new Set<number>();
  const warnings: string[] = [];
  const addAssetOffset = (value: number, label: string): void => {
    if (value === 0) return;
    if (!Number.isSafeInteger(value) || value < 0 || value > assetsLength) {
      warnings.push(`Public ${label} offset ${value} lies outside decoded asset length ${assetsLength}.`);
      return;
    }
    bounds.add(value);
  };

  addAssetOffset(fields.tfrags, "tfrags");
  addAssetOffset(fields.occlusion, "occlusion");
  addAssetOffset(fields.sky, "sky");
  addAssetOffset(fields.collision, "collision");
  addAssetOffset(fields.texturesBaseOffset, "texturesBaseOffset");
  addAssetOffset(fields.mobySoundRemapOffset, "mobySoundRemapOffset");
  if (fields.assetsDecompressedSize > 0) addAssetOffset(fields.assetsDecompressedSize, "assetsDecompressedSize");
  bounds.add(assetsLength);

  const addClassOffsets = (range: UyaPublicLeadArrayRange, entryBytes: number, label: string): void => {
    if (range.count < 0 || range.count > maxClassEntries) {
      warnings.push(`Public ${label} count ${range.count} is outside 0..${maxClassEntries}; class boundaries skipped.`);
      return;
    }
    if (range.count === 0) return;
    const tableBytes = range.count * entryBytes;
    if (!Number.isSafeInteger(tableBytes) || range.offset < 0 || range.offset > index.byteLength || tableBytes > index.byteLength - range.offset) {
      warnings.push(`Public ${label} table ${range.offset}+${tableBytes} lies outside coreIndex ${index.byteLength}; class boundaries skipped.`);
      return;
    }
    for (let i = 0; i < range.count; i++) addAssetOffset(index.getInt32(range.offset + i * entryBytes, true), `${label}[${i}]`);
  };
  // Pinned Wrench structures; these are still public leads until UYA retail confirms them.
  addClassOffsets(fields.mobyClasses, 0x20, "mobyClasses");
  addClassOffsets(fields.tieClasses, 0x20, "tieClasses");
  addClassOffsets(fields.shrubClasses, 0x30, "shrubClasses");

  if (fields.ratchetSeqsRac123 > 0) {
    const bytes = 256 * 4;
    if (fields.ratchetSeqsRac123 <= index.byteLength && bytes <= index.byteLength - fields.ratchetSeqsRac123) {
      for (let i = 0; i < 256; i++) addAssetOffset(index.getInt32(fields.ratchetSeqsRac123 + i * 4, true), `ratchetSeqs[${i}]`);
    } else {
      warnings.push(`Public ratchetSeqs table ${fields.ratchetSeqsRac123}+${bytes} lies outside coreIndex ${index.byteLength}; sequence boundaries skipped.`);
    }
  }

  return { boundaries: [...bounds].sort((a, b) => a - b), warnings };
}

function sectionRange(offset: number, boundaries: readonly number[]): UyaPublicLeadByteRange | undefined {
  if (!Number.isSafeInteger(offset) || offset <= 0) return undefined;
  let next: number | undefined;
  for (const bound of boundaries) if (bound > offset && (next === undefined || bound < next)) next = bound;
  return next === undefined ? undefined : { offset, size: next - offset };
}

function requireDataRange(outer: UyaLevelWadHeaderPublicLead) {
  const data = outer.ranges[0];
  if (!data?.present) throw new Error("UYA candidate outer header has no public data range in slot 0.");
  return data;
}

function assertRange(reader: RandomAccessReader, offset: number, size: number, label: string): void {
  if (!Number.isSafeInteger(offset) || !Number.isSafeInteger(size) || offset < 0 || size < 0 || offset > reader.size || size > reader.size - offset) {
    throw new RangeError(`UYA candidate ${label} ${offset}+${size} lies outside '${reader.name}' (${reader.size} bytes).`);
  }
}

function checkedAdd(a: number, b: number, label: string): number {
  const value = a + b;
  if (!Number.isSafeInteger(value) || value < 0) throw new RangeError(`${label} must be a non-negative safe integer.`);
  return value;
}

function checkedPositive(value: number, label: string): number {
  if (!Number.isSafeInteger(value) || value <= 0) throw new RangeError(`${label} must be a positive safe integer.`);
  return value;
}

function sha256(bytes: Uint8Array): string {
  return new Sha256().update(bytes).digestHex();
}
