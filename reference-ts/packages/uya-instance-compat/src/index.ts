import { parseGcGameplayInstances, type GcInstance } from "../../gc-instances/src/index.js";
import { SHRUB_CLASS_ENTRY_SIZE, SHRUB_CLASS_HEADER_SIZE } from "../../gc-shrub/src/index.js";
import { TIE_CLASS_ENTRY_SIZE, TIE_CLASS_HEADER_SIZE } from "../../gc-tie/src/index.js";
import { Sha256 } from "../../hashing/src/index.js";
import type { UyaDecodedCoreDataPublicLead } from "../../uya-core-decode/src/index.js";
import type { UyaLevelCoreFieldsPublicLead, UyaPublicLeadArrayRange } from "../../uya-level-core/src/index.js";

export type UyaStaticInstanceKind = "tie" | "shrub";

const INSTANCE_LAYOUT = {
  tie: { pointerOffset: 0x34, structSize: 0x60 },
  shrub: { pointerOffset: 0x40, structSize: 0x70 },
} as const;

export interface UyaInstanceBlockPrerequisiteCensus {
  readonly pointerOffset: number;
  readonly structSize: number;
  readonly pointerReadable: boolean;
  readonly blockOffset?: number;
  readonly blockHeaderReadable: boolean;
  readonly declaredCount?: number;
  readonly declaredCountValid: boolean;
  readonly fullDeclaredSpanWithinGameplay: boolean;
  readonly fullStructReadableCount: number;
  readonly nonFiniteMatrixComponentCount: number;
  readonly completeFiniteEntryCount: number;
}

export interface UyaGcInstancePlacementCompatibilityPublicLead {
  readonly evidenceStatus: "existing-gc-instance-parser-applied-to-uya-gameplay-bytes-with-prerequisite-census";
  readonly kind: UyaStaticInstanceKind;
  readonly prerequisiteCensus: UyaInstanceBlockPrerequisiteCensus;
  readonly candidateClassCount: number;
  readonly parserDecodedInstanceCount: number;
  readonly parserDecodedAllDeclaredEntries: boolean;
  readonly oClassesObserved: readonly number[];
  readonly oClassesMissingFromCandidateClassTable: readonly number[];
  readonly nonFiniteParsedMatrixComponentCount: number;
  readonly translationBounds?: {
    readonly min: readonly [number, number, number];
    readonly max: readonly [number, number, number];
  };
  readonly matricesSha256: string;
}

export interface UyaGcInstancePlacementCompatibilityPairPublicLead {
  readonly gameplayBytes: number;
  readonly tie: UyaGcInstancePlacementCompatibilityPublicLead;
  readonly shrub: UyaGcInstancePlacementCompatibilityPublicLead;
}

/**
 * Apply the unchanged GC gameplay-instance parser to decoded UYA gameplay bytes.
 *
 * The wrapper independently validates block pointers, declared counts, complete struct spans,
 * and matrix finiteness before comparing the existing parser output with the declared retail data.
 * Referenced oClasses are also checked against independently eligible class-table entries.
 */
export function probeUyaGcInstancePlacementCompatibilityPublicLead(
  gameplayData: Uint8Array,
  decodedCore: UyaDecodedCoreDataPublicLead,
  publicFields: UyaLevelCoreFieldsPublicLead,
): UyaGcInstancePlacementCompatibilityPairPublicLead {
  const parsed = parseGcGameplayInstances(gameplayData);
  return {
    gameplayBytes: gameplayData.length,
    tie: probeKind(gameplayData, parsed.tieInstances, decodedCore, publicFields, "tie"),
    shrub: probeKind(gameplayData, parsed.shrubInstances, decodedCore, publicFields, "shrub"),
  };
}

export function censusUyaInstanceBlockPrerequisites(
  gameplayData: Uint8Array,
  kind: UyaStaticInstanceKind,
): UyaInstanceBlockPrerequisiteCensus {
  const { pointerOffset, structSize } = INSTANCE_LAYOUT[kind];
  const pointerReadable = pointerOffset + 4 <= gameplayData.length;
  if (!pointerReadable) {
    return {
      pointerOffset,
      structSize,
      pointerReadable,
      blockHeaderReadable: false,
      declaredCountValid: false,
      fullDeclaredSpanWithinGameplay: false,
      fullStructReadableCount: 0,
      nonFiniteMatrixComponentCount: 0,
      completeFiniteEntryCount: 0,
    };
  }

  const view = new DataView(gameplayData.buffer, gameplayData.byteOffset, gameplayData.byteLength);
  const blockOffset = view.getInt32(pointerOffset, true);
  const blockHeaderReadable = blockOffset > 0 && blockOffset <= gameplayData.length && 0x10 <= gameplayData.length - blockOffset;
  if (!blockHeaderReadable) {
    return {
      pointerOffset,
      structSize,
      pointerReadable,
      blockOffset,
      blockHeaderReadable,
      declaredCountValid: false,
      fullDeclaredSpanWithinGameplay: false,
      fullStructReadableCount: 0,
      nonFiniteMatrixComponentCount: 0,
      completeFiniteEntryCount: 0,
    };
  }

  const declaredCount = view.getInt32(blockOffset, true);
  const declaredCountValid = declaredCount >= 0 && declaredCount <= 200_000;
  if (!declaredCountValid) {
    return {
      pointerOffset,
      structSize,
      pointerReadable,
      blockOffset,
      blockHeaderReadable,
      declaredCount,
      declaredCountValid,
      fullDeclaredSpanWithinGameplay: false,
      fullStructReadableCount: 0,
      nonFiniteMatrixComponentCount: 0,
      completeFiniteEntryCount: 0,
    };
  }

  const payloadStart = blockOffset + 0x10;
  const declaredBytes = declaredCount * structSize;
  const fullDeclaredSpanWithinGameplay = Number.isSafeInteger(declaredBytes) && payloadStart <= gameplayData.length && declaredBytes <= gameplayData.length - payloadStart;
  let fullStructReadableCount = 0;
  let nonFiniteMatrixComponentCount = 0;
  let completeFiniteEntryCount = 0;
  for (let i = 0; i < declaredCount; i++) {
    const at = payloadStart + i * structSize;
    if (at < 0 || at > gameplayData.length || structSize > gameplayData.length - at) break;
    fullStructReadableCount++;
    let finite = true;
    for (let m = 0; m < 16; m++) {
      const value = view.getFloat32(at + 0x10 + m * 4, true);
      if (!Number.isFinite(value)) {
        nonFiniteMatrixComponentCount++;
        finite = false;
      }
    }
    if (finite) completeFiniteEntryCount++;
  }

  return {
    pointerOffset,
    structSize,
    pointerReadable,
    blockOffset,
    blockHeaderReadable,
    declaredCount,
    declaredCountValid,
    fullDeclaredSpanWithinGameplay,
    fullStructReadableCount,
    nonFiniteMatrixComponentCount,
    completeFiniteEntryCount,
  };
}

function probeKind(
  gameplayData: Uint8Array,
  instances: readonly GcInstance[],
  decodedCore: UyaDecodedCoreDataPublicLead,
  publicFields: UyaLevelCoreFieldsPublicLead,
  kind: UyaStaticInstanceKind,
): UyaGcInstancePlacementCompatibilityPublicLead {
  const prerequisiteCensus = censusUyaInstanceBlockPrerequisites(gameplayData, kind);
  const classRange = kind === "tie" ? publicFields.tieClasses : publicFields.shrubClasses;
  const candidateOClasses = readCandidateClassOClasses(decodedCore, classRange, kind);
  const candidateOClassSet = new Set(candidateOClasses);
  const oClassesObserved = [...new Set(instances.map((instance) => instance.oClass))].sort((a, b) => a - b);
  const oClassesMissingFromCandidateClassTable = oClassesObserved.filter((oClass) => !candidateOClassSet.has(oClass));

  let nonFiniteParsedMatrixComponentCount = 0;
  let minX = Number.POSITIVE_INFINITY;
  let minY = Number.POSITIVE_INFINITY;
  let minZ = Number.POSITIVE_INFINITY;
  let maxX = Number.NEGATIVE_INFINITY;
  let maxY = Number.NEGATIVE_INFINITY;
  let maxZ = Number.NEGATIVE_INFINITY;
  const hash = new Sha256();
  for (const instance of instances) {
    hash.update(utf8(`${kind}:${instance.index}:${instance.oClass}\n`));
    for (const value of instance.matrix) {
      if (!Number.isFinite(value)) nonFiniteParsedMatrixComponentCount++;
    }
    hash.update(float64Bytes(instance.matrix));
    const x = instance.matrix[12]!;
    const y = instance.matrix[13]!;
    const z = instance.matrix[14]!;
    if (Number.isFinite(x) && Number.isFinite(y) && Number.isFinite(z)) {
      minX = Math.min(minX, x); minY = Math.min(minY, y); minZ = Math.min(minZ, z);
      maxX = Math.max(maxX, x); maxY = Math.max(maxY, y); maxZ = Math.max(maxZ, z);
    }
  }

  const declaredCount = prerequisiteCensus.declaredCount;
  const parserDecodedAllDeclaredEntries =
    prerequisiteCensus.declaredCountValid &&
    prerequisiteCensus.fullDeclaredSpanWithinGameplay &&
    declaredCount !== undefined &&
    prerequisiteCensus.fullStructReadableCount === declaredCount &&
    prerequisiteCensus.completeFiniteEntryCount === declaredCount &&
    instances.length === declaredCount;
  const hasBounds = minX !== Number.POSITIVE_INFINITY;

  return {
    evidenceStatus: "existing-gc-instance-parser-applied-to-uya-gameplay-bytes-with-prerequisite-census",
    kind,
    prerequisiteCensus,
    candidateClassCount: candidateOClasses.length,
    parserDecodedInstanceCount: instances.length,
    parserDecodedAllDeclaredEntries,
    oClassesObserved,
    oClassesMissingFromCandidateClassTable,
    nonFiniteParsedMatrixComponentCount,
    ...(hasBounds ? { translationBounds: { min: [minX, minY, minZ], max: [maxX, maxY, maxZ] } } : {}),
    matricesSha256: hash.digestHex(),
  };
}

function readCandidateClassOClasses(
  decoded: UyaDecodedCoreDataPublicLead,
  range: UyaPublicLeadArrayRange,
  kind: UyaStaticInstanceKind,
): number[] {
  const entrySize = kind === "tie" ? TIE_CLASS_ENTRY_SIZE : SHRUB_CLASS_ENTRY_SIZE;
  const headerSize = kind === "tie" ? TIE_CLASS_HEADER_SIZE : SHRUB_CLASS_HEADER_SIZE;
  if (range.count < 0 || range.offset < 0) return [];
  const tableBytes = range.count * entrySize;
  if (!Number.isSafeInteger(tableBytes) || range.offset > decoded.coreIndex.length || tableBytes > decoded.coreIndex.length - range.offset) return [];
  const view = new DataView(decoded.coreIndex.buffer, decoded.coreIndex.byteOffset, decoded.coreIndex.byteLength);
  const out = new Set<number>();
  for (let i = 0; i < range.count; i++) {
    const at = range.offset + i * entrySize;
    const assetOffset = view.getInt32(at, true);
    if (assetOffset <= 0 || assetOffset >= decoded.assets.length || headerSize > decoded.assets.length - assetOffset) continue;
    out.add(view.getInt32(at + 4, true));
  }
  return [...out].sort((a, b) => a - b);
}

function utf8(text: string): Uint8Array {
  return new TextEncoder().encode(text);
}

function float64Bytes(values: readonly number[]): Uint8Array {
  const array = new Float64Array(values);
  return new Uint8Array(array.buffer, array.byteOffset, array.byteLength);
}
