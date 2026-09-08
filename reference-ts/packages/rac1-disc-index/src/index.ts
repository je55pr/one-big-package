import { SubRangeReader } from "../../importer-common/src/index.js";
import type { RandomAccessReader } from "../../importer-common/src/index.js";

/** Retail R&C1 uses 0x800-byte DVD sectors for its hidden raw-disc index. */
export const RAC1_DISC_SECTOR_BYTES = 0x800;
/** Directly observed on the supported NTSC-U authority build. */
export const RAC1_DISC_INDEX_LBA = 1500;
/** Directly observed `header_size` / total index size on the supported build. */
export const RAC1_DISC_INDEX_SIZE = 0x2960;
/** Offset of the final 19 pairs in the 0x2960-byte index. */
export const RAC1_LEVEL_TABLE_OFFSET = 0x28c8;
export const RAC1_LEVEL_TABLE_COUNT = 19;
/** Directly observed on-disc native level-header size for all 19 retail levels. */
export const RAC1_NATIVE_LEVEL_HEADER_SIZE = 0x2434;
/** Only this fixed prefix is required to catalogue the four first native ranges. */
export const RAC1_NATIVE_LEVEL_CORE_PREFIX_SIZE = 0x28;
export const RAC1_NATIVE_LEVEL_HEADER_SECTORS = Math.ceil(RAC1_NATIVE_LEVEL_HEADER_SIZE / RAC1_DISC_SECTOR_BYTES);
export const RAC1_NATIVE_LEVEL_CORE_RANGE_COUNT = 4;

export interface Rac1SectorRange {
  /** Byte offset of this field within the native 0x2434 header. */
  readonly fieldOffset: number;
  readonly offsetSectors: number;
  readonly sizeSectors: number;
  readonly offsetBytes: number;
  readonly sizeBytes: number;
}

/**
 * One raw pair from index offset 0x28c8. The first word is retail-confirmed to
 * address a 0x2434 native level header. The second word is deliberately unnamed:
 * public tools disagree about / do not depend on its semantics, and OBP has not
 * yet established it from executable behaviour.
 */
export interface Rac1LevelTableEntry {
  readonly tableSlot: number;
  readonly headerLba: number;
  readonly rawSecondWord: number;
  readonly present: boolean;
}

export interface Rac1DiscIndex {
  readonly indexLba: number;
  readonly version: number;
  readonly declaredSize: number;
  readonly levelEntries: readonly Rac1LevelTableEntry[];
}

/**
 * Bounded view of only the directly decoded prefix of the native on-disc level
 * header. `coreRanges` stay positional until R&C1 retail/executable evidence
 * establishes semantics; public names are research leads, not API contracts.
 */
export interface Rac1NativeLevelCore {
  readonly tableSlot: number;
  readonly tableRawSecondWord: number;
  readonly headerLba: number;
  readonly levelId: number;
  readonly headerSize: number;
  /** Four SectorRange fields at 0x08, 0x10, 0x18 and 0x20. */
  readonly coreRanges: readonly Rac1SectorRange[];
}

export interface Rac1LevelCatalogue {
  readonly index: Rac1DiscIndex;
  readonly levels: readonly Rac1NativeLevelCore[];
}

export interface Rac1LevelPackingObservation {
  readonly tableSlot: number;
  readonly levelId: number;
  readonly rangesContiguousAfterHeader: boolean;
  readonly coreEndLba: number;
  /** null for the final present table entry. */
  readonly nextHeaderBeginsAtCoreEnd: boolean | null;
}

/**
 * Read the fixed R&C1 authority-layout disc index with a single 0x2960-byte read.
 *
 * This intentionally refuses unknown index versions/sizes instead of pretending
 * another R&C1 revision uses the same layout. Supporting another build requires
 * evidence for that build first.
 */
export async function readRac1DiscIndex(reader: RandomAccessReader): Promise<Rac1DiscIndex> {
  const indexOffset = sectorsToBytes(RAC1_DISC_INDEX_LBA, "disc index LBA");
  if (indexOffset > reader.size || RAC1_DISC_INDEX_SIZE > reader.size - indexOffset) {
    throw new Error(
      `R&C1 source '${reader.name}' does not contain the ${RAC1_DISC_INDEX_SIZE}-byte index at LBA ${RAC1_DISC_INDEX_LBA}.`,
    );
  }

  const bytes = await reader.read(indexOffset, RAC1_DISC_INDEX_SIZE);
  if (bytes.length !== RAC1_DISC_INDEX_SIZE) throw new Error(`Short R&C1 disc-index read from '${reader.name}'.`);
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);

  const version = view.getInt32(0x00, true);
  const declaredSize = view.getInt32(0x04, true);
  if (version !== 1) throw new Error(`R&C1 disc index version ${version} is not the supported retail version 1.`);
  if (declaredSize !== RAC1_DISC_INDEX_SIZE) {
    throw new Error(
      `R&C1 disc index declares size 0x${declaredSize.toString(16)}, expected authority layout 0x${RAC1_DISC_INDEX_SIZE.toString(16)}.`,
    );
  }

  const levelEntries: Rac1LevelTableEntry[] = [];
  for (let tableSlot = 0; tableSlot < RAC1_LEVEL_TABLE_COUNT; tableSlot++) {
    const offset = RAC1_LEVEL_TABLE_OFFSET + tableSlot * 8;
    const headerLba = view.getUint32(offset, true);
    const rawSecondWord = view.getUint32(offset + 4, true);
    const present = headerLba !== 0 || rawSecondWord !== 0;
    if (present && headerLba === 0) {
      throw new Error(`R&C1 level-table slot ${tableSlot} is present but has zero header LBA.`);
    }
    if (headerLba !== 0) {
      const headerOffset = sectorsToBytes(headerLba, `level-table slot ${tableSlot} header LBA`);
      if (headerOffset > reader.size || RAC1_NATIVE_LEVEL_CORE_PREFIX_SIZE > reader.size - headerOffset) {
        throw new Error(`R&C1 level-table slot ${tableSlot} header LBA ${headerLba} lies outside '${reader.name}'.`);
      }
    }
    levelEntries.push({ tableSlot, headerLba, rawSecondWord, present });
  }

  return { indexLba: RAC1_DISC_INDEX_LBA, version, declaredSize, levelEntries };
}

/** Read only the 0x28-byte prefix needed to catalogue one native level header. */
export async function readRac1NativeLevelCore(
  reader: RandomAccessReader,
  entry: Rac1LevelTableEntry,
): Promise<Rac1NativeLevelCore> {
  if (!entry.present || entry.headerLba === 0) throw new Error(`R&C1 level-table slot ${entry.tableSlot} is absent.`);
  const headerOffset = sectorsToBytes(entry.headerLba, `level ${entry.tableSlot} header LBA`);
  if (headerOffset > reader.size || RAC1_NATIVE_LEVEL_CORE_PREFIX_SIZE > reader.size - headerOffset) {
    throw new Error(`R&C1 level-table slot ${entry.tableSlot} header prefix lies outside '${reader.name}'.`);
  }

  const bytes = await reader.read(headerOffset, RAC1_NATIVE_LEVEL_CORE_PREFIX_SIZE);
  if (bytes.length !== RAC1_NATIVE_LEVEL_CORE_PREFIX_SIZE) {
    throw new Error(`Short R&C1 native level-header read for table slot ${entry.tableSlot}.`);
  }
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  const levelId = view.getInt32(0x00, true);
  const headerSize = view.getInt32(0x04, true);
  if (headerSize !== RAC1_NATIVE_LEVEL_HEADER_SIZE) {
    throw new Error(
      `R&C1 level-table slot ${entry.tableSlot} header size 0x${headerSize.toString(16)} is not 0x${RAC1_NATIVE_LEVEL_HEADER_SIZE.toString(16)}.`,
    );
  }

  const coreRanges: Rac1SectorRange[] = [];
  for (let slot = 0; slot < RAC1_NATIVE_LEVEL_CORE_RANGE_COUNT; slot++) {
    const fieldOffset = 0x08 + slot * 8;
    const offsetSectors = view.getUint32(fieldOffset, true);
    const sizeSectors = view.getUint32(fieldOffset + 4, true);
    const offsetBytes = sectorsToBytes(offsetSectors, `level ${levelId} range ${slot} offset`);
    const sizeBytes = sectorsToBytes(sizeSectors, `level ${levelId} range ${slot} size`);
    if (sizeSectors > 0 && (offsetSectors === 0 || offsetBytes > reader.size || sizeBytes > reader.size - offsetBytes)) {
      throw new Error(
        `R&C1 level ${levelId} core range ${slot} (${offsetSectors}+${sizeSectors} sectors) lies outside '${reader.name}'.`,
      );
    }
    coreRanges.push({ fieldOffset, offsetSectors, sizeSectors, offsetBytes, sizeBytes });
  }

  return {
    tableSlot: entry.tableSlot,
    tableRawSecondWord: entry.rawSecondWord,
    headerLba: entry.headerLba,
    levelId,
    headerSize,
    coreRanges,
  };
}

/** Catalogue every present native level using one index read plus one 0x28-byte read per level. */
export async function readRac1LevelCatalogue(reader: RandomAccessReader): Promise<Rac1LevelCatalogue> {
  const index = await readRac1DiscIndex(reader);
  const levels: Rac1NativeLevelCore[] = [];
  for (const entry of index.levelEntries) {
    if (entry.present) levels.push(await readRac1NativeLevelCore(reader, entry));
  }
  return { index, levels };
}

/**
 * Describe, but do not require, the contiguous packing invariant observed on the
 * supported NTSC-U retail disc. Keeping this separate prevents a one-build census
 * from silently becoming a cross-revision parser assumption.
 */
export function analyzeRac1LevelPacking(catalogue: Rac1LevelCatalogue): readonly Rac1LevelPackingObservation[] {
  const byTableSlot = new Map(catalogue.levels.map((level) => [level.tableSlot, level]));
  const presentEntries = catalogue.index.levelEntries.filter((entry) => entry.present);

  return presentEntries.map((entry, presentIndex) => {
    const level = byTableSlot.get(entry.tableSlot);
    if (!level) throw new Error(`R&C1 catalogue is missing decoded level for table slot ${entry.tableSlot}.`);

    let expectedLba = level.headerLba + RAC1_NATIVE_LEVEL_HEADER_SECTORS;
    let rangesContiguousAfterHeader = true;
    for (const range of level.coreRanges) {
      if (range.offsetSectors !== expectedLba) rangesContiguousAfterHeader = false;
      expectedLba = range.offsetSectors + range.sizeSectors;
    }

    const nextEntry = presentEntries[presentIndex + 1];
    return {
      tableSlot: entry.tableSlot,
      levelId: level.levelId,
      rangesContiguousAfterHeader,
      coreEndLba: expectedLba,
      nextHeaderBeginsAtCoreEnd: nextEntry ? nextEntry.headerLba === expectedLba : null,
    };
  });
}

/** Expose one of the four decoded native ranges without copying or buffering it. */
export function openRac1LevelCoreRange(
  reader: RandomAccessReader,
  level: Rac1NativeLevelCore,
  rangeSlot: number,
): RandomAccessReader | undefined {
  if (!Number.isInteger(rangeSlot) || rangeSlot < 0 || rangeSlot >= RAC1_NATIVE_LEVEL_CORE_RANGE_COUNT) {
    throw new RangeError(`R&C1 core range slot ${rangeSlot} is out of range 0..${RAC1_NATIVE_LEVEL_CORE_RANGE_COUNT - 1}.`);
  }
  const range = level.coreRanges[rangeSlot];
  if (!range || range.sizeSectors === 0) return undefined;
  return new SubRangeReader(
    reader,
    range.offsetBytes,
    range.sizeBytes,
    `${reader.name}#rac1-level${level.levelId}-range0x${range.fieldOffset.toString(16)}`,
  );
}

function sectorsToBytes(sectors: number, label: string): number {
  const bytes = sectors * RAC1_DISC_SECTOR_BYTES;
  if (!Number.isSafeInteger(bytes)) throw new RangeError(`R&C1 ${label} overflows the JavaScript safe integer range.`);
  return bytes;
}
