import { SubRangeReader } from "../../importer-common/src/index.js";
import type { RandomAccessReader } from "../../importer-common/src/index.js";
import { openRac1LevelCoreRange } from "../../rac1-disc-index/src/index.js";
import type { Rac1NativeLevelCore } from "../../rac1-disc-index/src/index.js";

/** 11 native ByteRange pairs at the start of R&C1 outer positional range 0. */
export const RAC1_LEVEL_DATA_DIRECTORY_SIZE = 0x58;
export const RAC1_LEVEL_DATA_RANGE_COUNT = 11;
/** Retail ranges begin after padding to 0x80; descriptive, not a parser requirement. */
export const RAC1_LEVEL_DATA_RETAIL_FIRST_RANGE_OFFSET = 0x80;

/**
 * These positional slots have been correlated across all 19 retail levels.
 * Slot 2 contains the core index; slot 10 contains WAD-LZ core data; slot 3 is
 * the GS-RAM image used by the core texture palettes/allocation table.
 */
export const RAC1_LEVEL_DATA_CORE_INDEX_SLOT = 2;
export const RAC1_LEVEL_DATA_GS_RAM_SLOT = 3;
export const RAC1_LEVEL_DATA_CORE_DATA_SLOT = 10;

export interface Rac1LevelDataRange {
  readonly slot: number;
  readonly fieldOffset: number;
  readonly offsetBytes: number;
  readonly sizeBytes: number;
  readonly present: boolean;
}

export interface Rac1LevelDataDirectory {
  readonly ranges: readonly Rac1LevelDataRange[];
  readonly sourceSize: number;
}

export interface Rac1LevelDataPacking {
  readonly presentRanges: number;
  readonly allOffsetsAligned40: boolean;
  readonly nonOverlapping: boolean;
  readonly firstPresentOffset: number | null;
  readonly tailSlackBytes: number | null;
  readonly gaps: readonly { readonly afterSlot: number; readonly bytes: number }[];
}

/** Read only the fixed 0x58-byte native directory; payloads stay range-addressed. */
export async function readRac1LevelDataDirectory(reader: RandomAccessReader): Promise<Rac1LevelDataDirectory> {
  if (reader.size < RAC1_LEVEL_DATA_DIRECTORY_SIZE) {
    throw new Error(`R&C1 level-data source '${reader.name}' is smaller than the 0x58-byte directory.`);
  }
  const bytes = await reader.read(0, RAC1_LEVEL_DATA_DIRECTORY_SIZE);
  if (bytes.length !== RAC1_LEVEL_DATA_DIRECTORY_SIZE) throw new Error(`Short R&C1 level-data directory read from '${reader.name}'.`);
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  const ranges: Rac1LevelDataRange[] = [];

  for (let slot = 0; slot < RAC1_LEVEL_DATA_RANGE_COUNT; slot++) {
    const fieldOffset = slot * 8;
    const offsetBytes = view.getInt32(fieldOffset, true);
    const sizeBytes = view.getInt32(fieldOffset + 4, true);
    if (sizeBytes < 0) throw new Error(`R&C1 level-data range ${slot} has negative size ${sizeBytes}.`);
    const present = sizeBytes > 0;
    if (present) {
      if (offsetBytes < 0) throw new Error(`R&C1 level-data range ${slot} has data but negative offset ${offsetBytes}.`);
      if (offsetBytes > reader.size || sizeBytes > reader.size - offsetBytes) {
        throw new Error(`R&C1 level-data range ${slot} (${offsetBytes}+${sizeBytes}) lies outside '${reader.name}'.`);
      }
    } else if (offsetBytes !== 0 && offsetBytes !== -1) {
      throw new Error(`R&C1 level-data range ${slot} has zero size but unexpected offset ${offsetBytes}.`);
    }
    ranges.push({ slot, fieldOffset, offsetBytes, sizeBytes, present });
  }

  return { ranges, sourceSize: reader.size };
}

/** Describe the 0x40-aligned, gap-padded packing observed across the retail census. */
export function analyzeRac1LevelDataPacking(directory: Rac1LevelDataDirectory): Rac1LevelDataPacking {
  const present = directory.ranges.filter((range) => range.present).slice().sort((a, b) => a.offsetBytes - b.offsetBytes);
  const gaps: { afterSlot: number; bytes: number }[] = [];
  let nonOverlapping = true;
  for (let i = 1; i < present.length; i++) {
    const previous = present[i - 1]!;
    const current = present[i]!;
    const end = previous.offsetBytes + previous.sizeBytes;
    if (current.offsetBytes < end) nonOverlapping = false;
    else if (current.offsetBytes > end) gaps.push({ afterSlot: previous.slot, bytes: current.offsetBytes - end });
  }
  const last = present.at(-1);
  return {
    presentRanges: present.length,
    allOffsetsAligned40: present.every((range) => range.offsetBytes % 0x40 === 0),
    nonOverlapping,
    firstPresentOffset: present[0]?.offsetBytes ?? null,
    tailSlackBytes: last ? directory.sourceSize - (last.offsetBytes + last.sizeBytes) : null,
    gaps,
  };
}

/** Open one native level-data directory range without copying it. */
export function openRac1LevelDataRange(
  reader: RandomAccessReader,
  directory: Rac1LevelDataDirectory,
  slot: number,
): RandomAccessReader | undefined {
  if (!Number.isInteger(slot) || slot < 0 || slot >= RAC1_LEVEL_DATA_RANGE_COUNT) {
    throw new RangeError(`R&C1 level-data range slot ${slot} is out of range 0..${RAC1_LEVEL_DATA_RANGE_COUNT - 1}.`);
  }
  const range = directory.ranges[slot];
  if (!range || !range.present) return undefined;
  return new SubRangeReader(reader, range.offsetBytes, range.sizeBytes, `${reader.name}#range0x${range.fieldOffset.toString(16)}`);
}

/**
 * Open the native level-data source found in outer positional range 0. This name
 * is based on the directly observed inner directory, not an assumption that the
 * outer header is binary-compatible with GC.
 */
export function openRac1LevelDataSource(reader: RandomAccessReader, level: Rac1NativeLevelCore): RandomAccessReader {
  const source = openRac1LevelCoreRange(reader, level, 0);
  if (!source) throw new Error(`R&C1 level ${level.levelId} has no outer positional range 0.`);
  return source;
}
