import { Sha256 } from "../../hashing/src/index.js";

export const UYA_GAMEPLAY_HEADER_CENSUS_BYTES = 0x80;
export const UYA_GAMEPLAY_HEADER_SLOT_COUNT = 32;

export interface UyaGameplayStrideHypothesis {
  readonly assumedHeaderBytes: number;
  readonly count: number;
  readonly strideBytes: number;
}

export interface UyaGameplayHeaderSlotCensus {
  readonly slotIndex: number;
  readonly headerOffset: number;
  readonly rawPointer: number;
  readonly present: boolean;
  readonly aliasSlotIndices: readonly number[];
  readonly nextTopLevelPointer: number | null;
  readonly apparentExtentBytes: number;
  readonly apparentExtentSha256: string | null;
  readonly firstWordsU32: readonly number[];
  readonly firstS32: number | null;
  readonly strideHypotheses: readonly UyaGameplayStrideHypothesis[];
}

export interface UyaGameplayHeaderCensus {
  readonly gameplayBytes: number;
  readonly headerSha256: string;
  readonly nonZeroSlotCount: number;
  readonly uniquePointerCount: number;
  readonly slots: readonly UyaGameplayHeaderSlotCensus[];
}
export function censusUyaGameplayHeaderBlocks(gameplay: Uint8Array): UyaGameplayHeaderCensus {
  if (gameplay.byteLength < UYA_GAMEPLAY_HEADER_CENSUS_BYTES) {
    throw new Error(`UYA gameplay lump is too small for a 0x80 header census (${gameplay.byteLength} bytes).`);
  }
  const view = new DataView(gameplay.buffer, gameplay.byteOffset, gameplay.byteLength);
  const pointers = Array.from({ length: UYA_GAMEPLAY_HEADER_SLOT_COUNT }, (_, slotIndex) => ({
    slotIndex,
    headerOffset: slotIndex * 4,
    rawPointer: view.getInt32(slotIndex * 4, true),
  }));

  for (const pointer of pointers) {
    if (pointer.rawPointer < 0 || pointer.rawPointer >= gameplay.byteLength) {
      if (pointer.rawPointer !== 0) {
        throw new RangeError(`UYA gameplay header slot ${pointer.slotIndex} pointer 0x${(pointer.rawPointer >>> 0).toString(16)} lies outside ${gameplay.byteLength} bytes.`);
      }
    }
  }

  const nonZero = pointers.filter((pointer) => pointer.rawPointer !== 0);
  const uniquePointers = [...new Set(nonZero.map((pointer) => pointer.rawPointer))].sort((a, b) => a - b);
  const slots = pointers.map((pointer): UyaGameplayHeaderSlotCensus => {
    if (pointer.rawPointer === 0) return absentSlot(pointer.slotIndex, pointer.headerOffset);
    const pointerIndex = uniquePointers.indexOf(pointer.rawPointer);
    const nextTopLevelPointer = uniquePointers[pointerIndex + 1] ?? gameplay.byteLength;
    const apparentExtentBytes = nextTopLevelPointer - pointer.rawPointer;
    if (apparentExtentBytes <= 0) throw new Error(`UYA gameplay pointer 0x${pointer.rawPointer.toString(16)} has no positive apparent extent.`);
    const extent = gameplay.subarray(pointer.rawPointer, nextTopLevelPointer);
    const extentView = new DataView(extent.buffer, extent.byteOffset, extent.byteLength);
    const wordCount = Math.min(8, Math.floor(extent.byteLength / 4));
    const firstWordsU32 = Array.from({ length: wordCount }, (_, index) => extentView.getUint32(index * 4, true));
    const firstS32 = extent.byteLength >= 4 ? extentView.getInt32(0, true) : null;
    return {
      slotIndex: pointer.slotIndex,
      headerOffset: pointer.headerOffset,
      rawPointer: pointer.rawPointer,
      present: true,
      aliasSlotIndices: nonZero.filter((other) => other.rawPointer === pointer.rawPointer).map((other) => other.slotIndex),
      nextTopLevelPointer: nextTopLevelPointer === gameplay.byteLength ? null : nextTopLevelPointer,
      apparentExtentBytes,
      apparentExtentSha256: sha256(extent),
      firstWordsU32,
      firstS32,
      strideHypotheses: inferStrideHypotheses(firstS32, apparentExtentBytes),
    };
  });

  return {
    gameplayBytes: gameplay.byteLength,
    headerSha256: sha256(gameplay.subarray(0, UYA_GAMEPLAY_HEADER_CENSUS_BYTES)),
    nonZeroSlotCount: nonZero.length,
    uniquePointerCount: uniquePointers.length,
    slots,
  };
}
function absentSlot(slotIndex: number, headerOffset: number): UyaGameplayHeaderSlotCensus {
  return {
    slotIndex,
    headerOffset,
    rawPointer: 0,
    present: false,
    aliasSlotIndices: [],
    nextTopLevelPointer: null,
    apparentExtentBytes: 0,
    apparentExtentSha256: null,
    firstWordsU32: [],
    firstS32: null,
    strideHypotheses: [],
  };
}

function inferStrideHypotheses(count: number | null, extentBytes: number): UyaGameplayStrideHypothesis[] {
  if (count === null || count <= 0 || count > 1_000_000) return [];
  const out: UyaGameplayStrideHypothesis[] = [];
  for (const assumedHeaderBytes of [0, 4, 8, 12, 16]) {
    const payloadBytes = extentBytes - assumedHeaderBytes;
    if (payloadBytes <= 0 || payloadBytes % count !== 0) continue;
    const strideBytes = payloadBytes / count;
    if (strideBytes <= 0 || strideBytes > 0x10000) continue;
    out.push({ assumedHeaderBytes, count, strideBytes });
  }
  return out;
}

function sha256(bytes: Uint8Array): string {
  return new Sha256().update(bytes).digestHex();
}
