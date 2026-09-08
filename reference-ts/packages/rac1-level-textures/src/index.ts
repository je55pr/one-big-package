import type { RandomAccessReader } from "../../importer-common/src/index.js";
import type { Rac1NativeLevelCore } from "../../rac1-disc-index/src/index.js";
import {
  RAC1_LEVEL_DATA_CORE_DATA_SLOT,
  RAC1_LEVEL_DATA_CORE_INDEX_SLOT,
  RAC1_LEVEL_DATA_GS_RAM_SLOT,
  openRac1LevelDataRange,
  openRac1LevelDataSource,
  readRac1LevelDataDirectory,
} from "../../rac1-level-data/src/index.js";
import { readRac1CoreData, readRac1CoreIndexHeader } from "../../rac1-level-core/src/index.js";
import { readRcLevelTextureTable } from "../../rc-level-textures/src/index.js";
import type { RcLevelTexture, RcLevelTextureOptions, RcTextureTableRange } from "../../rc-level-textures/src/index.js";

export type Rac1LevelTextureKind = "tfrag" | "moby" | "tie" | "shrub" | "part" | "fx";

/** Retail-validated native core-index ArrayRange fields. */
export const RAC1_TFRAG_TEXTURE_TABLE_FIELD_OFFSET = 0x30;
export const RAC1_MOBY_TEXTURE_TABLE_FIELD_OFFSET = 0x38;
export const RAC1_TIE_TEXTURE_TABLE_FIELD_OFFSET = 0x40;
export const RAC1_SHRUB_TEXTURE_TABLE_FIELD_OFFSET = 0x48;
export const RAC1_PART_TEXTURE_TABLE_FIELD_OFFSET = 0x50;
export const RAC1_FX_TEXTURE_TABLE_FIELD_OFFSET = 0x58;
/** Core-index word directly validated as the shared base for level texture pixel data. */
export const RAC1_TEXTURES_BASE_FIELD_OFFSET = 0x60;

export const RAC1_LEVEL_TEXTURE_TABLE_FIELD_OFFSETS: Readonly<Record<Rac1LevelTextureKind, number>> = {
  tfrag: RAC1_TFRAG_TEXTURE_TABLE_FIELD_OFFSET,
  moby: RAC1_MOBY_TEXTURE_TABLE_FIELD_OFFSET,
  tie: RAC1_TIE_TEXTURE_TABLE_FIELD_OFFSET,
  shrub: RAC1_SHRUB_TEXTURE_TABLE_FIELD_OFFSET,
  part: RAC1_PART_TEXTURE_TABLE_FIELD_OFFSET,
  fx: RAC1_FX_TEXTURE_TABLE_FIELD_OFFSET,
};

export interface Rac1LevelTexturesResult {
  readonly kind: Rac1LevelTextureKind;
  readonly table: RcTextureTableRange;
  readonly texturesBaseOffset: number;
  readonly textures: readonly RcLevelTexture[];
}

/** Backward-compatible name for the first terrain-only implementation. */
export type Rac1TfragTexturesResult = Rac1LevelTexturesResult;

/**
 * Decode one native R&C1 level texture ArrayRange from already-loaded core bytes.
 *
 * All six tables use the shared RC `TextureEntry` / IDTEX8 + RGBA32-palette path.
 * R&C1 class-family census independently validated that Moby/Tie/Shrub class-local
 * texture maps only reference ids inside their corresponding native tables.
 */
export function readRac1TextureTable(
  index: Uint8Array,
  assets: Uint8Array,
  gsRam: Uint8Array,
  texturesBaseOffset: number,
  kind: Rac1LevelTextureKind,
  options: RcLevelTextureOptions = {},
): Rac1LevelTexturesResult {
  const fieldOffset = RAC1_LEVEL_TEXTURE_TABLE_FIELD_OFFSETS[kind];
  if (fieldOffset + 8 > index.length) throw new Error(`R&C1 core index is missing the ${kind} texture ArrayRange.`);
  const view = new DataView(index.buffer, index.byteOffset, index.byteLength);
  const table = { count: view.getInt32(fieldOffset, true), offset: view.getInt32(fieldOffset + 4, true) };
  const textures = readRcLevelTextureTable(
    { index, assets, gsRam, texturesBaseOffset },
    table,
    { ...options, strict: options.strict ?? true },
  );
  if (textures.length !== table.count) {
    throw new Error(`R&C1 ${kind} texture table declares ${table.count} entries but decoded ${textures.length}.`);
  }
  return { kind, table, texturesBaseOffset, textures };
}

/** Decode one texture family from already-open native core sources. */
export async function readRac1CoreTextures(
  coreIndexReader: RandomAccessReader,
  coreDataReader: RandomAccessReader,
  gsRamReader: RandomAccessReader,
  kind: Rac1LevelTextureKind,
  options: RcLevelTextureOptions = {},
): Promise<Rac1LevelTexturesResult> {
  const coreHeader = await readRac1CoreIndexHeader(coreIndexReader);
  const index = await coreIndexReader.read(0, coreIndexReader.size);
  const view = new DataView(index.buffer, index.byteOffset, index.byteLength);
  if (RAC1_TEXTURES_BASE_FIELD_OFFSET + 4 > index.length) throw new Error("R&C1 core index is missing the texture-base field.");
  const texturesBaseOffset = view.getInt32(RAC1_TEXTURES_BASE_FIELD_OFFSET, true);
  if (texturesBaseOffset !== coreHeader.collisionEndBoundary) {
    throw new Error(`R&C1 texture base ${texturesBaseOffset} disagrees with validated core boundary ${coreHeader.collisionEndBoundary}.`);
  }
  const coreData = await readRac1CoreData(coreDataReader, coreHeader);
  const gsRam = await gsRamReader.read(0, gsRamReader.size);
  return readRac1TextureTable(index, coreData.data, gsRam, texturesBaseOffset, kind, options);
}

/** Compatibility wrapper for the terrain texture family. */
export async function readRac1CoreTfragTextures(
  coreIndexReader: RandomAccessReader,
  coreDataReader: RandomAccessReader,
  gsRamReader: RandomAccessReader,
  options: RcLevelTextureOptions = {},
): Promise<Rac1TfragTexturesResult> {
  return readRac1CoreTextures(coreIndexReader, coreDataReader, gsRamReader, "tfrag", options);
}

/** End-to-end bounded texture-family path from a retail R&C1 disc reader. */
export async function readRac1LevelTextures(
  discReader: RandomAccessReader,
  level: Rac1NativeLevelCore,
  kind: Rac1LevelTextureKind,
  options: RcLevelTextureOptions = {},
): Promise<Rac1LevelTexturesResult> {
  const levelData = openRac1LevelDataSource(discReader, level);
  const directory = await readRac1LevelDataDirectory(levelData);
  const coreIndex = openRac1LevelDataRange(levelData, directory, RAC1_LEVEL_DATA_CORE_INDEX_SLOT);
  const coreData = openRac1LevelDataRange(levelData, directory, RAC1_LEVEL_DATA_CORE_DATA_SLOT);
  const gsRam = openRac1LevelDataRange(levelData, directory, RAC1_LEVEL_DATA_GS_RAM_SLOT);
  if (!coreIndex || !coreData || !gsRam) throw new Error(`R&C1 level ${level.levelId} is missing a validated texture source range.`);
  return readRac1CoreTextures(coreIndex, coreData, gsRam, kind, options);
}

/** Compatibility wrapper for the terrain texture family. */
export async function readRac1LevelTfragTextures(
  discReader: RandomAccessReader,
  level: Rac1NativeLevelCore,
  options: RcLevelTextureOptions = {},
): Promise<Rac1TfragTexturesResult> {
  return readRac1LevelTextures(discReader, level, "tfrag", options);
}
