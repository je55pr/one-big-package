import type { GcLevelCore, GcLevelCoreHeader } from "../../gc-level-core/src/index.js";
import { parseGcGameplayInstances } from "../../gc-instances/src/index.js";
import {
  readGcMobyClasses,
  MOBY_CLASS_ENTRY_SIZE,
  MOBY_CLASS_HEADER_SIZE,
} from "../../gc-moby/src/index.js";
import { Sha256 } from "../../hashing/src/index.js";
import type { UyaDecodedCoreDataPublicLead } from "../../uya-core-decode/src/index.js";
import type { UyaLevelCoreFieldsPublicLead, UyaPublicLeadArrayRange } from "../../uya-level-core/src/index.js";

const MOBY_INSTANCE_POINTER_OFFSET = 0x4c;
const MOBY_INSTANCE_SIZE = 0x88;
const NO_TEXTURE_SENTINEL = 0xff;
const MAX_COUNT = 200_000;

export interface UyaMobyClassPrerequisiteCensus {
  readonly declaredClassCount: number;
  readonly tableOffset: number;
  readonly tableByteLength: number;
  readonly tableWithinCoreIndex: boolean;
  /** Valid table entries with no level-local class-core bytes under the pinned public format. */
  readonly zeroAssetOffsetCount: number;
  /** Nonzero offsets that are structurally outside the decoded asset blob. */
  readonly invalidAssetOffsetCount: number;
  readonly classHeaderOutsideAssetCount: number;
  /** Entries with a nonzero, structurally readable local class core. */
  readonly candidateEntryCount: number;
  readonly declaredUniqueOClassCount: number;
  readonly candidateUniqueOClassCount: number;
  readonly duplicateOClassCount: number;
}

export interface UyaMobyClassCompatibilityPublicLead {
  readonly evidenceStatus: "existing-gc-moby-parser-applied-to-uya-candidate-bytes-with-prerequisite-census";
  readonly publicClassRange: UyaPublicLeadArrayRange;
  readonly publicTextureCount: number;
  readonly prerequisiteCensus: UyaMobyClassPrerequisiteCensus;
  readonly parserDecodedClassCount: number;
  readonly parserDecodedAllCandidateOClasses: boolean;
  readonly candidateOClassesMissingFromParser: readonly number[];
  readonly totalVertices: number;
  readonly totalTriangles: number;
  readonly skinnedClassCount: number;
  readonly skinningAppliedClassCount: number;
  readonly nonFinitePositionCount: number;
  readonly textureIdsObserved: readonly number[];
  /** Includes 0xff when the unchanged GC reader maps an unused class-local slot through the table. */
  readonly textureIdsOutsidePublicTable: readonly number[];
  readonly textureIdsOutsidePublicTableExcludingNoTextureSentinel: readonly number[];
  readonly noTextureSentinelTriangleCount: number;
  readonly geometrySha256: string;
}

export interface UyaMobyInstancePrerequisiteCensus {
  readonly pointerOffset: number;
  readonly structSize: number;
  readonly pointerReadable: boolean;
  readonly blockOffset?: number;
  readonly blockHeaderReadable: boolean;
  readonly declaredCount?: number;
  readonly declaredCountValid: boolean;
  readonly fullDeclaredSpanWithinGameplay: boolean;
  readonly fullStructReadableCount: number;
  readonly wrongSizeFieldCount: number;
  readonly nonFiniteScaleCount: number;
  readonly nonFinitePositionComponentCount: number;
  readonly nonFiniteRotationComponentCount: number;
  readonly completeFiniteEntryCount: number;
}

export interface UyaMobyInstanceCompatibilityPublicLead {
  readonly evidenceStatus: "existing-gc-moby-instance-parser-applied-to-uya-gameplay-bytes-with-prerequisite-census";
  readonly prerequisiteCensus: UyaMobyInstancePrerequisiteCensus;
  readonly parserDecodedInstanceCount: number;
  readonly parserDecodedAllDeclaredEntries: boolean;
  readonly observedOClasses: readonly number[];
  /** Legacy coarse view: observed classes without a nonzero local core candidate. */
  readonly oClassesMissingFromCandidateClassTable: readonly number[];
  /** Observed classes absent even from the complete declared class table. */
  readonly oClassesMissingFromDeclaredClassTable: readonly number[];
  /** Observed classes that are declared but have offset_in_asset_wad == 0. */
  readonly oClassesWithZeroLocalAsset: readonly number[];
  /** Observed classes for which the unchanged GC class reader emitted no geometry object. */
  readonly oClassesMissingFromDecodedClassGeometry: readonly number[];
  readonly instanceCountWithZeroLocalAsset: number;
  readonly instanceCountWithoutDecodedClassGeometry: number;
  readonly nonFiniteParsedComponentCount: number;
  readonly placementSha256: string;
}

export interface UyaMobyCompatibilityPublicLead {
  readonly classes: UyaMobyClassCompatibilityPublicLead;
  readonly instances: UyaMobyInstanceCompatibilityPublicLead;
}

interface MobyClassAvailability {
  readonly declared: Set<number>;
  readonly zeroLocalAsset: Set<number>;
  readonly candidate: Set<number>;
}

export function probeUyaGcMobyCompatibilityPublicLead(
  decoded: UyaDecodedCoreDataPublicLead,
  publicFields: UyaLevelCoreFieldsPublicLead,
  gameplayData: Uint8Array,
): UyaMobyCompatibilityPublicLead {
  const core = makeGcCompatibilityCore(decoded, publicFields);
  const availability = classifyMobyClasses(decoded, publicFields.mobyClasses);
  const classes = probeClasses(decoded, publicFields, core, availability);
  const instances = probeInstances(gameplayData, availability, classes);
  return { classes, instances };
}

export function censusUyaMobyClassPrerequisites(
  indexBytes: Uint8Array,
  assets: Uint8Array,
  range: UyaPublicLeadArrayRange,
): UyaMobyClassPrerequisiteCensus {
  if (!Number.isSafeInteger(range.count) || range.count < 0) throw new RangeError(`Moby class count ${range.count} is invalid.`);
  if (!Number.isSafeInteger(range.offset) || range.offset < 0) throw new RangeError(`Moby class table offset ${range.offset} is invalid.`);
  const tableByteLength = range.count * MOBY_CLASS_ENTRY_SIZE;
  if (!Number.isSafeInteger(tableByteLength)) throw new RangeError("Moby class table length exceeds safe integer range.");
  const tableWithinCoreIndex = range.offset <= indexBytes.length && tableByteLength <= indexBytes.length - range.offset;
  if (!tableWithinCoreIndex) {
    return {
      declaredClassCount: range.count, tableOffset: range.offset, tableByteLength,
      tableWithinCoreIndex, zeroAssetOffsetCount: 0, invalidAssetOffsetCount: 0,
      classHeaderOutsideAssetCount: 0, candidateEntryCount: 0,
      declaredUniqueOClassCount: 0, candidateUniqueOClassCount: 0, duplicateOClassCount: 0,
    };
  }
  const view = new DataView(indexBytes.buffer, indexBytes.byteOffset, indexBytes.byteLength);
  let zeroAssetOffsetCount = 0;
  let invalidAssetOffsetCount = 0;
  let classHeaderOutsideAssetCount = 0;
  let candidateEntryCount = 0;
  let duplicateOClassCount = 0;
  const declared = new Set<number>();
  const candidates = new Set<number>();
  for (let i = 0; i < range.count; i++) {
    const at = range.offset + i * MOBY_CLASS_ENTRY_SIZE;
    const assetOffset = view.getInt32(at, true);
    const oClass = view.getInt32(at + 4, true);
    if (declared.has(oClass)) duplicateOClassCount++;
    declared.add(oClass);
    if (assetOffset === 0) { zeroAssetOffsetCount++; continue; }
    if (assetOffset < 0 || assetOffset >= assets.length) { invalidAssetOffsetCount++; continue; }
    if (MOBY_CLASS_HEADER_SIZE > assets.length - assetOffset) { classHeaderOutsideAssetCount++; continue; }
    candidateEntryCount++;
    candidates.add(oClass);
  }
  return {
    declaredClassCount: range.count, tableOffset: range.offset, tableByteLength,
    tableWithinCoreIndex, zeroAssetOffsetCount, invalidAssetOffsetCount, classHeaderOutsideAssetCount,
    candidateEntryCount, declaredUniqueOClassCount: declared.size,
    candidateUniqueOClassCount: candidates.size, duplicateOClassCount,
  };
}

export function censusUyaMobyInstancePrerequisites(gameplayData: Uint8Array): UyaMobyInstancePrerequisiteCensus {
  const pointerReadable = MOBY_INSTANCE_POINTER_OFFSET + 4 <= gameplayData.length;
  if (!pointerReadable) return emptyInstanceCensus(false);
  const view = new DataView(gameplayData.buffer, gameplayData.byteOffset, gameplayData.byteLength);
  const blockOffset = view.getInt32(MOBY_INSTANCE_POINTER_OFFSET, true);
  const blockHeaderReadable = blockOffset > 0 && blockOffset <= gameplayData.length && 0x10 <= gameplayData.length - blockOffset;
  if (!blockHeaderReadable) return { ...emptyInstanceCensus(true), blockOffset };
  const declaredCount = view.getInt32(blockOffset, true);
  const declaredCountValid = declaredCount >= 0 && declaredCount <= MAX_COUNT;
  if (!declaredCountValid) {
    return { ...emptyInstanceCensus(true), blockOffset, blockHeaderReadable: true, declaredCount, declaredCountValid: false };
  }
  const payloadStart = blockOffset + 0x10;
  const declaredBytes = declaredCount * MOBY_INSTANCE_SIZE;
  const fullDeclaredSpanWithinGameplay = Number.isSafeInteger(declaredBytes) && payloadStart <= gameplayData.length && declaredBytes <= gameplayData.length - payloadStart;
  let fullStructReadableCount = 0;
  let wrongSizeFieldCount = 0;
  let nonFiniteScaleCount = 0;
  let nonFinitePositionComponentCount = 0;
  let nonFiniteRotationComponentCount = 0;
  let completeFiniteEntryCount = 0;
  for (let i = 0; i < declaredCount; i++) {
    const at = payloadStart + i * MOBY_INSTANCE_SIZE;
    if (at < 0 || at > gameplayData.length || MOBY_INSTANCE_SIZE > gameplayData.length - at) break;
    fullStructReadableCount++;
    const rightSize = view.getInt32(at, true) === MOBY_INSTANCE_SIZE;
    if (!rightSize) wrongSizeFieldCount++;
    const scale = view.getFloat32(at + 0x2c, true);
    const finiteScale = Number.isFinite(scale);
    if (!finiteScale) nonFiniteScaleCount++;
    let finitePos = true;
    let finiteRot = true;
    for (const off of [0x40, 0x44, 0x48]) if (!Number.isFinite(view.getFloat32(at + off, true))) { nonFinitePositionComponentCount++; finitePos = false; }
    for (const off of [0x4c, 0x50, 0x54]) if (!Number.isFinite(view.getFloat32(at + off, true))) { nonFiniteRotationComponentCount++; finiteRot = false; }
    if (rightSize && finiteScale && finitePos && finiteRot) completeFiniteEntryCount++;
  }
  return {
    pointerOffset: MOBY_INSTANCE_POINTER_OFFSET, structSize: MOBY_INSTANCE_SIZE, pointerReadable,
    blockOffset, blockHeaderReadable, declaredCount, declaredCountValid, fullDeclaredSpanWithinGameplay,
    fullStructReadableCount, wrongSizeFieldCount, nonFiniteScaleCount,
    nonFinitePositionComponentCount, nonFiniteRotationComponentCount, completeFiniteEntryCount,
  };
}

function probeClasses(
  decoded: UyaDecodedCoreDataPublicLead,
  fields: UyaLevelCoreFieldsPublicLead,
  core: GcLevelCore,
  availability: MobyClassAvailability,
): UyaMobyClassCompatibilityPublicLead {
  const range = fields.mobyClasses;
  const prerequisiteCensus = censusUyaMobyClassPrerequisites(decoded.coreIndex, decoded.assets, range);
  const candidates = [...availability.candidate].sort((a, b) => a - b);
  const parsed = readGcMobyClasses(core);
  const parsedIds = [...parsed.keys()].sort((a, b) => a - b);
  const parsedSet = new Set(parsedIds);
  const candidateOClassesMissingFromParser = candidates.filter((id) => !parsedSet.has(id));
  const parserDecodedAllCandidateOClasses = candidateOClassesMissingFromParser.length === 0 && parsedIds.length === candidates.length;
  let totalVertices = 0;
  let totalTriangles = 0;
  let skinnedClassCount = 0;
  let skinningAppliedClassCount = 0;
  let nonFinitePositionCount = 0;
  let noTextureSentinelTriangleCount = 0;
  const textureIds = new Set<number>();
  const hash = new Sha256();
  for (const id of parsedIds) {
    const cls = parsed.get(id)!;
    totalVertices += cls.mesh.positions.length / 3;
    totalTriangles += cls.mesh.indices.length / 3;
    if (cls.mesh.skinned) skinnedClassCount++;
    if (cls.mesh.skinningApplied) skinningAppliedClassCount++;
    for (const value of cls.mesh.positions) if (!Number.isFinite(value)) nonFinitePositionCount++;
    for (const tex of cls.textureIds) textureIds.add(tex);
    for (const tex of cls.triangleTextureIds) if (tex === NO_TEXTURE_SENTINEL) noTextureSentinelTriangleCount++;
    hash.update(utf8(`moby:${id}\n`));
    hash.update(bytesOf(cls.mesh.positions));
    hash.update(bytesOf(cls.mesh.uvs));
    hash.update(bytesOf(cls.mesh.indices));
    hash.update(bytesOf(cls.mesh.triangleMaterialSlots));
  }
  const textureIdsObserved = [...textureIds].sort((a, b) => a - b);
  const textureIdsOutsidePublicTable = textureIdsObserved.filter((id) => id < 0 || id >= fields.mobyTextures.count);
  return {
    evidenceStatus: "existing-gc-moby-parser-applied-to-uya-candidate-bytes-with-prerequisite-census",
    publicClassRange: range,
    publicTextureCount: fields.mobyTextures.count,
    prerequisiteCensus,
    parserDecodedClassCount: parsed.size,
    parserDecodedAllCandidateOClasses,
    candidateOClassesMissingFromParser,
    totalVertices,
    totalTriangles,
    skinnedClassCount,
    skinningAppliedClassCount,
    nonFinitePositionCount,
    textureIdsObserved,
    textureIdsOutsidePublicTable,
    textureIdsOutsidePublicTableExcludingNoTextureSentinel: textureIdsOutsidePublicTable.filter((id) => id !== NO_TEXTURE_SENTINEL),
    noTextureSentinelTriangleCount,
    geometrySha256: hash.digestHex(),
  };
}

function probeInstances(
  gameplayData: Uint8Array,
  availability: MobyClassAvailability,
  classResult: UyaMobyClassCompatibilityPublicLead,
): UyaMobyInstanceCompatibilityPublicLead {
  const prerequisiteCensus = censusUyaMobyInstancePrerequisites(gameplayData);
  const parsed = parseGcGameplayInstances(gameplayData).mobyInstances;
  const decodedClasses = new Set(availability.candidate);
  for (const missing of classResult.candidateOClassesMissingFromParser) decodedClasses.delete(missing);
  const observedOClasses = [...new Set(parsed.map((i) => i.oClass))].sort((a, b) => a - b);
  const oClassesMissingFromCandidateClassTable = observedOClasses.filter((id) => !availability.candidate.has(id));
  const oClassesMissingFromDeclaredClassTable = observedOClasses.filter((id) => !availability.declared.has(id));
  const oClassesWithZeroLocalAsset = observedOClasses.filter((id) => availability.zeroLocalAsset.has(id));
  const oClassesMissingFromDecodedClassGeometry = observedOClasses.filter((id) => !decodedClasses.has(id));
  let instanceCountWithZeroLocalAsset = 0;
  let instanceCountWithoutDecodedClassGeometry = 0;
  let nonFiniteParsedComponentCount = 0;
  const hash = new Sha256();
  for (const instance of parsed) {
    if (availability.zeroLocalAsset.has(instance.oClass)) instanceCountWithZeroLocalAsset++;
    if (!decodedClasses.has(instance.oClass)) instanceCountWithoutDecodedClassGeometry++;
    hash.update(utf8(`moby:${instance.index}:${instance.oClass}\n`));
    const values = [instance.scale, ...instance.position, ...instance.rotation];
    for (const value of values) if (!Number.isFinite(value)) nonFiniteParsedComponentCount++;
    hash.update(float64Bytes(values));
  }
  const declared = prerequisiteCensus.declaredCount;
  const parserDecodedAllDeclaredEntries =
    prerequisiteCensus.declaredCountValid && prerequisiteCensus.fullDeclaredSpanWithinGameplay &&
    declared !== undefined && prerequisiteCensus.fullStructReadableCount === declared &&
    prerequisiteCensus.completeFiniteEntryCount === declared && parsed.length === declared;
  return {
    evidenceStatus: "existing-gc-moby-instance-parser-applied-to-uya-gameplay-bytes-with-prerequisite-census",
    prerequisiteCensus,
    parserDecodedInstanceCount: parsed.length,
    parserDecodedAllDeclaredEntries,
    observedOClasses,
    oClassesMissingFromCandidateClassTable,
    oClassesMissingFromDeclaredClassTable,
    oClassesWithZeroLocalAsset,
    oClassesMissingFromDecodedClassGeometry,
    instanceCountWithZeroLocalAsset,
    instanceCountWithoutDecodedClassGeometry,
    nonFiniteParsedComponentCount,
    placementSha256: hash.digestHex(),
  };
}

function classifyMobyClasses(decoded: UyaDecodedCoreDataPublicLead, range: UyaPublicLeadArrayRange): MobyClassAvailability {
  const declared = new Set<number>();
  const zeroLocalAsset = new Set<number>();
  const candidate = new Set<number>();
  if (range.count < 0 || range.offset < 0) return { declared, zeroLocalAsset, candidate };
  const bytes = range.count * MOBY_CLASS_ENTRY_SIZE;
  if (!Number.isSafeInteger(bytes) || range.offset > decoded.coreIndex.length || bytes > decoded.coreIndex.length - range.offset) return { declared, zeroLocalAsset, candidate };
  const view = new DataView(decoded.coreIndex.buffer, decoded.coreIndex.byteOffset, decoded.coreIndex.byteLength);
  for (let i = 0; i < range.count; i++) {
    const at = range.offset + i * MOBY_CLASS_ENTRY_SIZE;
    const assetOffset = view.getInt32(at, true);
    const oClass = view.getInt32(at + 4, true);
    declared.add(oClass);
    if (assetOffset === 0) { zeroLocalAsset.add(oClass); continue; }
    if (assetOffset < 0 || assetOffset >= decoded.assets.length || MOBY_CLASS_HEADER_SIZE > decoded.assets.length - assetOffset) continue;
    candidate.add(oClass);
  }
  return { declared, zeroLocalAsset, candidate };
}

function makeGcCompatibilityCore(decoded: UyaDecodedCoreDataPublicLead, fields: UyaLevelCoreFieldsPublicLead): GcLevelCore {
  const coreHeader = { ...fields, ratchetSeqsOffset: fields.ratchetSeqsRac123, raw: [] } as unknown as GcLevelCoreHeader;
  return {
    dataHeader: {} as GcLevelCore["dataHeader"], coreHeader,
    index: decoded.coreIndex, assets: decoded.assets, gsRam: new Uint8Array(),
    sectionBoundaries: decoded.sectionBoundaries,
  };
}

function emptyInstanceCensus(pointerReadable: boolean): UyaMobyInstancePrerequisiteCensus {
  return {
    pointerOffset: MOBY_INSTANCE_POINTER_OFFSET, structSize: MOBY_INSTANCE_SIZE, pointerReadable,
    blockHeaderReadable: false, declaredCountValid: false, fullDeclaredSpanWithinGameplay: false,
    fullStructReadableCount: 0, wrongSizeFieldCount: 0, nonFiniteScaleCount: 0,
    nonFinitePositionComponentCount: 0, nonFiniteRotationComponentCount: 0, completeFiniteEntryCount: 0,
  };
}

function bytesOf(array: ArrayBufferView): Uint8Array {
  return new Uint8Array(array.buffer, array.byteOffset, array.byteLength);
}
function utf8(text: string): Uint8Array { return new TextEncoder().encode(text); }
function float64Bytes(values: readonly number[]): Uint8Array {
  const array = new Float64Array(values);
  return new Uint8Array(array.buffer, array.byteOffset, array.byteLength);
}
