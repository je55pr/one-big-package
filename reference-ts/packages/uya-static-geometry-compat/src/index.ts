import type { GcLevelCore, GcLevelCoreHeader } from "../../gc-level-core/src/index.js";
import { readGcShrubClasses, SHRUB_CLASS_ENTRY_SIZE, SHRUB_CLASS_HEADER_SIZE } from "../../gc-shrub/src/index.js";
import { readGcTieClasses, TIE_CLASS_ENTRY_SIZE, TIE_CLASS_HEADER_SIZE } from "../../gc-tie/src/index.js";
import { Sha256 } from "../../hashing/src/index.js";
import type { UyaDecodedCoreDataPublicLead } from "../../uya-core-decode/src/index.js";
import type { UyaLevelCoreFieldsPublicLead, UyaPublicLeadArrayRange } from "../../uya-level-core/src/index.js";

export type UyaStaticGeometryKind = "tie" | "shrub";

export interface UyaStaticClassPrerequisiteCensus {
  readonly declaredClassCount: number;
  readonly tableOffset: number;
  readonly entrySize: number;
  readonly tableByteLength: number;
  readonly tableWithinCoreIndex: boolean;
  readonly invalidAssetOffsetCount: number;
  readonly classHeaderOutsideAssetCount: number;
  readonly candidateEntryCount: number;
  readonly candidateUniqueOClassCount: number;
  readonly duplicateOClassCount: number;
}

export interface UyaStaticGeometryCompatibilityPublicLead {
  readonly evidenceStatus: "existing-gc-static-geometry-parser-applied-to-uya-candidate-bytes-with-prerequisite-census";
  readonly kind: UyaStaticGeometryKind;
  readonly publicClassRange: UyaPublicLeadArrayRange;
  readonly publicTextureCount: number;
  readonly prerequisiteCensus: UyaStaticClassPrerequisiteCensus;
  readonly parserDecodedClassCount: number;
  readonly parserDecodedAllCandidateOClasses: boolean;
  readonly totalVertices: number;
  readonly totalTriangles: number;
  readonly textureIdsObserved: readonly number[];
  readonly textureIdsOutsidePublicTable: readonly number[];
  readonly nonFinitePositionCount: number;
  readonly bounds?: {
    readonly min: readonly [number, number, number];
    readonly max: readonly [number, number, number];
  };
  readonly geometrySha256: string;
}

export interface UyaStaticGeometryCompatibilityPairPublicLead {
  readonly sectionBoundaries: readonly number[];
  readonly tie: UyaStaticGeometryCompatibilityPublicLead;
  readonly shrub: UyaStaticGeometryCompatibilityPublicLead;
}

/**
 * Apply the unchanged GC TIE and shrub class readers to retail-decoded UYA core bytes.
 *
 * The wrapper independently validates the public-derived class table bounds and class asset
 * offsets before invoking the existing readers. It then compares the readers' decoded oClass
 * sets with the independently eligible oClass set and reports geometry/texture sanity.
 */
export function probeUyaGcStaticGeometryCompatibilityPublicLead(
  decoded: UyaDecodedCoreDataPublicLead,
  publicFields: UyaLevelCoreFieldsPublicLead,
): UyaStaticGeometryCompatibilityPairPublicLead {
  const sectionBoundaries = enumerateCandidateSectionBoundaries(decoded, publicFields);
  const core = makeGcCompatibilityCore(decoded, publicFields, sectionBoundaries);
  return {
    sectionBoundaries,
    tie: probeKind(decoded, publicFields, core, "tie"),
    shrub: probeKind(decoded, publicFields, core, "shrub"),
  };
}

export function censusUyaStaticClassPrerequisites(
  indexBytes: Uint8Array,
  assets: Uint8Array,
  range: UyaPublicLeadArrayRange,
  kind: UyaStaticGeometryKind,
): UyaStaticClassPrerequisiteCensus {
  const entrySize = kind === "tie" ? TIE_CLASS_ENTRY_SIZE : SHRUB_CLASS_ENTRY_SIZE;
  const headerSize = kind === "tie" ? TIE_CLASS_HEADER_SIZE : SHRUB_CLASS_HEADER_SIZE;
  if (!Number.isSafeInteger(range.count) || range.count < 0) throw new RangeError(`${kind} class count ${range.count} is invalid.`);
  if (!Number.isSafeInteger(range.offset) || range.offset < 0) throw new RangeError(`${kind} class table offset ${range.offset} is invalid.`);
  const tableByteLength = range.count * entrySize;
  if (!Number.isSafeInteger(tableByteLength)) throw new RangeError(`${kind} class table byte length exceeds safe integer range.`);
  const tableWithinCoreIndex = range.offset <= indexBytes.length && tableByteLength <= indexBytes.length - range.offset;
  if (!tableWithinCoreIndex) {
    return {
      declaredClassCount: range.count,
      tableOffset: range.offset,
      entrySize,
      tableByteLength,
      tableWithinCoreIndex,
      invalidAssetOffsetCount: 0,
      classHeaderOutsideAssetCount: 0,
      candidateEntryCount: 0,
      candidateUniqueOClassCount: 0,
      duplicateOClassCount: 0,
    };
  }

  const view = new DataView(indexBytes.buffer, indexBytes.byteOffset, indexBytes.byteLength);
  let invalidAssetOffsetCount = 0;
  let classHeaderOutsideAssetCount = 0;
  let candidateEntryCount = 0;
  const oClasses = new Set<number>();
  let duplicateOClassCount = 0;
  for (let i = 0; i < range.count; i++) {
    const at = range.offset + i * entrySize;
    const assetOffset = view.getInt32(at, true);
    const oClass = view.getInt32(at + 4, true);
    if (assetOffset <= 0 || assetOffset >= assets.length) {
      invalidAssetOffsetCount++;
      continue;
    }
    if (headerSize > assets.length - assetOffset) {
      classHeaderOutsideAssetCount++;
      continue;
    }
    candidateEntryCount++;
    if (oClasses.has(oClass)) duplicateOClassCount++;
    oClasses.add(oClass);
  }

  return {
    declaredClassCount: range.count,
    tableOffset: range.offset,
    entrySize,
    tableByteLength,
    tableWithinCoreIndex,
    invalidAssetOffsetCount,
    classHeaderOutsideAssetCount,
    candidateEntryCount,
    candidateUniqueOClassCount: oClasses.size,
    duplicateOClassCount,
  };
}

function probeKind(
  decoded: UyaDecodedCoreDataPublicLead,
  publicFields: UyaLevelCoreFieldsPublicLead,
  core: GcLevelCore,
  kind: UyaStaticGeometryKind,
): UyaStaticGeometryCompatibilityPublicLead {
  const classRange = kind === "tie" ? publicFields.tieClasses : publicFields.shrubClasses;
  const publicTextureCount = kind === "tie" ? publicFields.tieTextures.count : publicFields.shrubTextures.count;
  const prerequisiteCensus = censusUyaStaticClassPrerequisites(decoded.coreIndex, decoded.assets, classRange, kind);
  const expectedOClasses = candidateOClasses(decoded.coreIndex, classRange, kind, decoded.assets.length);
  const parsed = kind === "tie" ? readGcTieClasses(core) : readGcShrubClasses(core);
  const parsedOClasses = [...parsed.keys()].sort((a, b) => a - b);
  const parserDecodedAllCandidateOClasses = arraysEqual(parsedOClasses, expectedOClasses);

  let totalVertices = 0;
  let totalTriangles = 0;
  let nonFinitePositionCount = 0;
  const textureIds = new Set<number>();
  let minX = Number.POSITIVE_INFINITY;
  let minY = Number.POSITIVE_INFINITY;
  let minZ = Number.POSITIVE_INFINITY;
  let maxX = Number.NEGATIVE_INFINITY;
  let maxY = Number.NEGATIVE_INFINITY;
  let maxZ = Number.NEGATIVE_INFINITY;
  const hash = new Sha256();

  for (const oClass of parsedOClasses) {
    const cls = parsed.get(oClass)!;
    const mesh = cls.mesh;
    totalVertices += mesh.positions.length / 3;
    totalTriangles += mesh.indices.length / 3;
    for (let i = 0; i < mesh.positions.length; i += 3) {
      const x = mesh.positions[i]!;
      const y = mesh.positions[i + 1]!;
      const z = mesh.positions[i + 2]!;
      if (!Number.isFinite(x)) nonFinitePositionCount++;
      if (!Number.isFinite(y)) nonFinitePositionCount++;
      if (!Number.isFinite(z)) nonFinitePositionCount++;
      if (Number.isFinite(x) && Number.isFinite(y) && Number.isFinite(z)) {
        minX = Math.min(minX, x); minY = Math.min(minY, y); minZ = Math.min(minZ, z);
        maxX = Math.max(maxX, x); maxY = Math.max(maxY, y); maxZ = Math.max(maxZ, z);
      }
    }
    for (const textureId of cls.textureIds) textureIds.add(textureId);
    hash.update(utf8(`${kind}:${oClass}\n`));
    hash.update(bytesOf(mesh.positions));
    hash.update(bytesOf(mesh.uvs));
    hash.update(bytesOf(mesh.indices));
    hash.update(bytesOf(mesh.triangleMaterialSlots));
  }

  const textureIdsObserved = [...textureIds].sort((a, b) => a - b);
  const textureIdsOutsidePublicTable = textureIdsObserved.filter((id) => id < 0 || id >= publicTextureCount);
  const hasBounds = minX !== Number.POSITIVE_INFINITY;
  return {
    evidenceStatus: "existing-gc-static-geometry-parser-applied-to-uya-candidate-bytes-with-prerequisite-census",
    kind,
    publicClassRange: classRange,
    publicTextureCount,
    prerequisiteCensus,
    parserDecodedClassCount: parsed.size,
    parserDecodedAllCandidateOClasses,
    totalVertices,
    totalTriangles,
    textureIdsObserved,
    textureIdsOutsidePublicTable,
    nonFinitePositionCount,
    ...(hasBounds ? { bounds: { min: [minX, minY, minZ], max: [maxX, maxY, maxZ] } } : {}),
    geometrySha256: hash.digestHex(),
  };
}

function candidateOClasses(
  indexBytes: Uint8Array,
  range: UyaPublicLeadArrayRange,
  kind: UyaStaticGeometryKind,
  assetsLength: number,
): number[] {
  const entrySize = kind === "tie" ? TIE_CLASS_ENTRY_SIZE : SHRUB_CLASS_ENTRY_SIZE;
  const headerSize = kind === "tie" ? TIE_CLASS_HEADER_SIZE : SHRUB_CLASS_HEADER_SIZE;
  const tableBytes = range.count * entrySize;
  if (range.offset < 0 || range.offset > indexBytes.length || tableBytes > indexBytes.length - range.offset) return [];
  const view = new DataView(indexBytes.buffer, indexBytes.byteOffset, indexBytes.byteLength);
  const out = new Set<number>();
  for (let i = 0; i < range.count; i++) {
    const at = range.offset + i * entrySize;
    const assetOffset = view.getInt32(at, true);
    if (assetOffset <= 0 || assetOffset >= assetsLength || headerSize > assetsLength - assetOffset) continue;
    out.add(view.getInt32(at + 4, true));
  }
  return [...out].sort((a, b) => a - b);
}

function enumerateCandidateSectionBoundaries(
  decoded: UyaDecodedCoreDataPublicLead,
  fields: UyaLevelCoreFieldsPublicLead,
): number[] {
  const assetsLength = decoded.assets.length;
  const bounds = new Set<number>();
  const add = (value: number): void => {
    if (Number.isSafeInteger(value) && value > 0 && value <= assetsLength) bounds.add(value);
  };
  add(fields.tfrags);
  add(fields.occlusion);
  add(fields.sky);
  add(fields.collision);
  add(fields.texturesBaseOffset);
  add(fields.partBankOffset);
  add(fields.fxBankOffset);
  add(fields.partDefsOffset);
  add(fields.soundRemapOffset);
  add(fields.ratchetSeqsRac123);
  add(fields.heightmapOffset);
  add(fields.occlusionOctOffset);
  add(fields.mobyGsStashList);
  add(fields.occlusionRadOffset);
  add(fields.mobySoundRemapOffset);
  add(fields.occlusionRad2Offset);
  add(fields.assetsDecompressedSize);
  add(assetsLength);

  const view = new DataView(decoded.coreIndex.buffer, decoded.coreIndex.byteOffset, decoded.coreIndex.byteLength);
  const addClassOffsets = (range: UyaPublicLeadArrayRange, entrySize: number): void => {
    if (range.count < 0 || range.offset < 0) return;
    for (let i = 0; i < range.count; i++) {
      const at = range.offset + i * entrySize;
      if (at < 0 || at + 4 > decoded.coreIndex.length) break;
      add(view.getInt32(at, true));
    }
  };
  addClassOffsets(fields.mobyClasses, 0x20);
  addClassOffsets(fields.tieClasses, TIE_CLASS_ENTRY_SIZE);
  addClassOffsets(fields.shrubClasses, SHRUB_CLASS_ENTRY_SIZE);
  return [...bounds].sort((a, b) => a - b);
}

function makeGcCompatibilityCore(
  decoded: UyaDecodedCoreDataPublicLead,
  fields: UyaLevelCoreFieldsPublicLead,
  sectionBoundaries: readonly number[],
): GcLevelCore {
  const coreHeader = {
    ...fields,
    ratchetSeqsOffset: fields.ratchetSeqsRac123,
    raw: [],
  } as unknown as GcLevelCoreHeader;
  return {
    dataHeader: {} as GcLevelCore["dataHeader"],
    coreHeader,
    index: decoded.coreIndex,
    assets: decoded.assets,
    gsRam: new Uint8Array(),
    sectionBoundaries,
  };
}

function arraysEqual(a: readonly number[], b: readonly number[]): boolean {
  return a.length === b.length && a.every((value, index) => value === b[index]);
}

function bytesOf(array: ArrayBufferView): Uint8Array {
  return new Uint8Array(array.buffer, array.byteOffset, array.byteLength);
}

function utf8(text: string): Uint8Array {
  return new TextEncoder().encode(text);
}
