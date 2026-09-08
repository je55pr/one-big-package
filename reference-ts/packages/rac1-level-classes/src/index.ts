import type { RandomAccessReader } from "../../importer-common/src/index.js";
import type { Rac1NativeLevelCore } from "../../rac1-disc-index/src/index.js";
import {
  RAC1_LEVEL_DATA_CORE_DATA_SLOT,
  RAC1_LEVEL_DATA_CORE_INDEX_SLOT,
  openRac1LevelDataRange,
  openRac1LevelDataSource,
  readRac1LevelDataDirectory,
} from "../../rac1-level-data/src/index.js";
import { readRac1CoreData, readRac1CoreIndexHeader } from "../../rac1-level-core/src/index.js";
import { readRcShrubClass } from "../../rc-shrub/src/index.js";
import type { RcShrubMesh } from "../../rc-shrub/src/index.js";
import { RAC1_TIE_CLASS_LAYOUT, readRcTieClass } from "../../rc-tie/src/index.js";
import type { RcTieMesh } from "../../rc-tie/src/index.js";

export const RAC1_MOBY_CLASS_TABLE_FIELD_OFFSET = 0x18;
export const RAC1_TIE_CLASS_TABLE_FIELD_OFFSET = 0x20;
export const RAC1_SHRUB_CLASS_TABLE_FIELD_OFFSET = 0x28;
export const RAC1_MOBY_CLASS_ENTRY_SIZE = 0x20;
export const RAC1_TIE_CLASS_ENTRY_SIZE = 0x20;
export const RAC1_SHRUB_CLASS_ENTRY_SIZE = 0x30;

export type Rac1ClassFamily = "moby" | "tie" | "shrub";

export interface Rac1ClassTableEntry {
  readonly family: Rac1ClassFamily;
  readonly tableIndex: number;
  readonly entryOffset: number;
  readonly entrySize: number;
  readonly assetOffset: number;
  readonly oClass: number;
  /** Unknown native words retained positionally rather than assigned semantics. */
  readonly raw0x08: number;
  readonly raw0x0c: number;
  /** Exact 16-byte class-local -> level texture-id map. */
  readonly textureIds: readonly number[];
  /** Additional signed words after 0x20 (currently present only on shrub entries). */
  readonly rawTailWords: readonly number[];
}

export interface Rac1ClassTable {
  readonly family: Rac1ClassFamily;
  readonly fieldOffset: number;
  readonly entrySize: number;
  readonly count: number;
  readonly offset: number;
  readonly entries: readonly Rac1ClassTableEntry[];
}

export interface Rac1ClassDirectory {
  readonly moby: Rac1ClassTable;
  readonly tie: Rac1ClassTable;
  readonly shrub: Rac1ClassTable;
}

export interface Rac1TieClass {
  readonly oClass: number;
  readonly assetOffset: number;
  readonly mesh: RcTieMesh;
  readonly triangleTextureIds: Int32Array;
  readonly textureIds: readonly number[];
  readonly sourceEntry: Rac1ClassTableEntry;
}

export interface Rac1ShrubClass {
  readonly oClass: number;
  readonly assetOffset: number;
  readonly mesh: RcShrubMesh;
  readonly triangleTextureIds: Int32Array;
  readonly textureIds: readonly number[];
  readonly sourceEntry: Rac1ClassTableEntry;
}

export interface Rac1StaticClasses {
  readonly directory: Rac1ClassDirectory;
  readonly ties: ReadonlyMap<number, Rac1TieClass>;
  readonly shrubs: ReadonlyMap<number, Rac1ShrubClass>;
}

export interface Rac1CoreStaticClassesResult extends Rac1StaticClasses {
  readonly coreDataCompressedSize: number;
  readonly coreDataDecompressedSize: number;
}

const TABLE_SPECS: readonly {
  readonly family: Rac1ClassFamily;
  readonly fieldOffset: number;
  readonly entrySize: number;
}[] = [
  { family: "moby", fieldOffset: RAC1_MOBY_CLASS_TABLE_FIELD_OFFSET, entrySize: RAC1_MOBY_CLASS_ENTRY_SIZE },
  { family: "tie", fieldOffset: RAC1_TIE_CLASS_TABLE_FIELD_OFFSET, entrySize: RAC1_TIE_CLASS_ENTRY_SIZE },
  { family: "shrub", fieldOffset: RAC1_SHRUB_CLASS_TABLE_FIELD_OFFSET, entrySize: RAC1_SHRUB_CLASS_ENTRY_SIZE },
];

function readClassTable(index: Uint8Array, assetsSize: number, spec: (typeof TABLE_SPECS)[number]): Rac1ClassTable {
  if (spec.fieldOffset + 8 > index.length) throw new Error(`R&C1 ${spec.family} class-table field is outside the core index.`);
  const view = new DataView(index.buffer, index.byteOffset, index.byteLength);
  const count = view.getInt32(spec.fieldOffset, true);
  const offset = view.getInt32(spec.fieldOffset + 4, true);
  if (!Number.isInteger(count) || count < 0 || count > 100_000) throw new Error(`R&C1 ${spec.family} class count ${count} is invalid.`);
  if (!Number.isInteger(offset) || offset < 0) throw new Error(`R&C1 ${spec.family} class table offset ${offset} is invalid.`);
  const bytes = count * spec.entrySize;
  if (!Number.isSafeInteger(bytes) || offset > index.length || bytes > index.length - offset) {
    throw new Error(`R&C1 ${spec.family} class table ${offset}+${bytes} lies outside the core index.`);
  }

  const entries: Rac1ClassTableEntry[] = [];
  for (let i = 0; i < count; i++) {
    const at = offset + i * spec.entrySize;
    const assetOffset = view.getInt32(at, true);
    const oClass = view.getInt32(at + 4, true);
    const raw0x08 = view.getInt32(at + 8, true);
    const raw0x0c = view.getInt32(at + 12, true);
    if (assetOffset < 0 || assetOffset > assetsSize) {
      throw new Error(`R&C1 ${spec.family} class ${i} asset offset ${assetOffset} is outside ${assetsSize}-byte core data.`);
    }
    const textureIds = [...index.subarray(at + 0x10, at + 0x20)];
    const rawTailWords: number[] = [];
    for (let tail = 0x20; tail + 4 <= spec.entrySize; tail += 4) rawTailWords.push(view.getInt32(at + tail, true));
    entries.push({
      family: spec.family,
      tableIndex: i,
      entryOffset: at,
      entrySize: spec.entrySize,
      assetOffset,
      oClass,
      raw0x08,
      raw0x0c,
      textureIds,
      rawTailWords,
    });
  }
  return { family: spec.family, fieldOffset: spec.fieldOffset, entrySize: spec.entrySize, count, offset, entries };
}

/** Read the three retail-validated class ArrayRanges while preserving unknown entry words. */
export function readRac1ClassDirectory(index: Uint8Array, assetsSize: number): Rac1ClassDirectory {
  if (!Number.isSafeInteger(assetsSize) || assetsSize < 0) throw new RangeError(`Invalid R&C1 core asset size ${assetsSize}.`);
  const tables = TABLE_SPECS.map((spec) => readClassTable(index, assetsSize, spec));
  return { moby: tables[0]!, tie: tables[1]!, shrub: tables[2]! };
}

function mapTextureSlots(
  slots: Int32Array,
  entry: Rac1ClassTableEntry,
): { readonly perTriangle: Int32Array; readonly used: readonly number[] } {
  const mapped = new Int32Array(slots.length);
  const used = new Set<number>();
  for (let i = 0; i < slots.length; i++) {
    const slot = slots[i]!;
    if (slot < 0 || slot >= entry.textureIds.length) {
      throw new Error(`R&C1 ${entry.family} class ${entry.oClass} uses out-of-range texture slot ${slot}.`);
    }
    const textureId = entry.textureIds[slot]!;
    mapped[i] = textureId;
    used.add(textureId);
  }
  return { perTriangle: mapped, used: [...used].sort((a, b) => a - b) };
}

/**
 * Decode R&C1's static class libraries from already-decompressed core bytes.
 * Every present class is strict: a malformed class aborts the decode rather than
 * being silently omitted, matching the complete 19-level retail census policy.
 */
export function readRac1StaticClasses(index: Uint8Array, assets: Uint8Array): Rac1StaticClasses {
  const directory = readRac1ClassDirectory(index, assets.length);
  const allEntries = [...directory.moby.entries, ...directory.tie.entries, ...directory.shrub.entries]
    .filter((entry) => entry.assetOffset > 0);
  const boundaries = [...new Set(allEntries.map((entry) => entry.assetOffset))].sort((a, b) => a - b);
  const classBytes = (entry: Rac1ClassTableEntry): Uint8Array => {
    if (entry.assetOffset <= 0) throw new Error(`R&C1 ${entry.family} class ${entry.oClass} has no asset payload.`);
    const next = boundaries.find((value) => value > entry.assetOffset) ?? assets.length;
    if (next <= entry.assetOffset) throw new Error(`R&C1 ${entry.family} class ${entry.oClass} has no positive asset range.`);
    return assets.subarray(entry.assetOffset, next);
  };

  const ties = new Map<number, Rac1TieClass>();
  for (const entry of directory.tie.entries) {
    if (entry.assetOffset === 0) continue;
    if (ties.has(entry.oClass)) throw new Error(`Duplicate R&C1 tie class id ${entry.oClass}.`);
    const mesh = readRcTieClass(classBytes(entry), RAC1_TIE_CLASS_LAYOUT);
    const mapped = mapTextureSlots(mesh.triangleMaterialSlots, entry);
    ties.set(entry.oClass, {
      oClass: entry.oClass,
      assetOffset: entry.assetOffset,
      mesh,
      triangleTextureIds: mapped.perTriangle,
      textureIds: mapped.used,
      sourceEntry: entry,
    });
  }

  const shrubs = new Map<number, Rac1ShrubClass>();
  for (const entry of directory.shrub.entries) {
    if (entry.assetOffset === 0) continue;
    if (shrubs.has(entry.oClass)) throw new Error(`Duplicate R&C1 shrub class id ${entry.oClass}.`);
    const bytes = classBytes(entry);
    if (bytes.length < 0x40) throw new Error(`R&C1 shrub class ${entry.oClass} is shorter than its shared 0x40-byte header.`);
    const payloadClass = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength).getInt16(0x24, true);
    if (payloadClass !== entry.oClass) {
      throw new Error(`R&C1 shrub table class ${entry.oClass} disagrees with payload class ${payloadClass}.`);
    }
    const mesh = readRcShrubClass(bytes);
    const mapped = mapTextureSlots(mesh.triangleMaterialSlots, entry);
    shrubs.set(entry.oClass, {
      oClass: entry.oClass,
      assetOffset: entry.assetOffset,
      mesh,
      triangleTextureIds: mapped.perTriangle,
      textureIds: mapped.used,
      sourceEntry: entry,
    });
  }

  return { directory, ties, shrubs };
}

/** Decode both R&C1 static class libraries with one bounded core-data decompression. */
export async function readRac1CoreStaticClasses(
  coreIndexReader: RandomAccessReader,
  coreDataReader: RandomAccessReader,
): Promise<Rac1CoreStaticClassesResult> {
  const coreHeader = await readRac1CoreIndexHeader(coreIndexReader);
  const index = await coreIndexReader.read(0, coreIndexReader.size);
  const coreData = await readRac1CoreData(coreDataReader, coreHeader);
  const decoded = readRac1StaticClasses(index, coreData.data);
  return {
    ...decoded,
    coreDataCompressedSize: coreData.compressedSize,
    coreDataDecompressedSize: coreData.decompressedSize,
  };
}

/** End-to-end bounded static-class path from a retail R&C1 disc reader. */
export async function readRac1LevelStaticClasses(
  discReader: RandomAccessReader,
  level: Rac1NativeLevelCore,
): Promise<Rac1CoreStaticClassesResult> {
  const levelData = openRac1LevelDataSource(discReader, level);
  const directory = await readRac1LevelDataDirectory(levelData);
  const coreIndex = openRac1LevelDataRange(levelData, directory, RAC1_LEVEL_DATA_CORE_INDEX_SLOT);
  const coreData = openRac1LevelDataRange(levelData, directory, RAC1_LEVEL_DATA_CORE_DATA_SLOT);
  if (!coreIndex || !coreData) throw new Error(`R&C1 level ${level.levelId} is missing its validated core index/data ranges.`);
  return readRac1CoreStaticClasses(coreIndex, coreData);
}
