import type { RandomAccessReader } from "../../importer-common/src/index.js";
import type { Rac1NativeLevelCore } from "../../rac1-disc-index/src/index.js";
import {
  RAC1_LEVEL_DATA_CORE_DATA_SLOT,
  RAC1_LEVEL_DATA_CORE_INDEX_SLOT,
  openRac1LevelDataRange,
  openRac1LevelDataSource,
  readRac1LevelDataDirectory,
} from "../../rac1-level-data/src/index.js";
import { readRcCollision } from "../../rc-collision/src/index.js";
import type { RcCollisionMesh, RcCollisionOptions } from "../../rc-collision/src/index.js";
import { readRcSky } from "../../rc-sky/src/index.js";
import type { RcSky } from "../../rc-sky/src/index.js";
import { readRcTfrags } from "../../rc-tfrag/src/index.js";
import type { RcTfragMesh, RcTfragOptions } from "../../rc-tfrag/src/index.js";
import { readWadLz } from "../../wad-lz/src/index.js";

/** Public-correlated and retail-validated fixed core-index prefix size. */
export const RAC1_CORE_INDEX_HEADER_SIZE = 0xbc;

export interface Rac1CoreIndexHeader {
  /** Exact signed little-endian words from 0x00..0xbb, preserved for archaeology. */
  readonly rawWords: readonly number[];
  /** Four positional block offsets at 0x08, 0x0c, 0x10, 0x14. */
  readonly leadingBlockOffsets: readonly number[];
  /** Offset 0x08. Retail-validated static tfrag block start on all 19 authority levels. */
  readonly tfragsOffset: number;
  /** First later positive leading block boundary after {@link tfragsOffset}. */
  readonly tfragsEndBoundary: number;
  /** Offset 0x10. Retail-validated sky block start on all 19 authority levels. */
  readonly skyOffset: number;
  /** The collision block at 0x14 directly follows the sky block on the authority build. */
  readonly skyEndBoundary: number;
  /** Offset 0x14. Retail-validated shared collision block start on all 19 authority levels. */
  readonly collisionOffset: number;
  /**
   * Offset 0x60. Retail-validated as both the collision end and terrain-texture
   * pixel-data base on all 19 authority levels.
   */
  readonly collisionEndBoundary: number;
  /** Offset 0x88; equals the WAD-LZ compressed size on all 19 authority levels. */
  readonly assetsCompressedSize: number;
  /** Offset 0x8c; equals decompressed core-data size on all 19 authority levels. */
  readonly assetsDecompressedSize: number;
}

export interface Rac1CoreDataResult {
  readonly compressedSize: number;
  readonly decompressedSize: number;
  readonly data: Uint8Array;
}

export interface Rac1CollisionResult {
  readonly coreIndex: Rac1CoreIndexHeader;
  readonly coreDataCompressedSize: number;
  readonly coreDataDecompressedSize: number;
  readonly collisionOffset: number;
  readonly collisionSize: number;
  readonly mesh: RcCollisionMesh;
}

export interface Rac1TfragsResult {
  readonly coreIndex: Rac1CoreIndexHeader;
  readonly coreDataCompressedSize: number;
  readonly coreDataDecompressedSize: number;
  readonly tfragsOffset: number;
  readonly tfragsSize: number;
  readonly mesh: RcTfragMesh;
}

export interface Rac1SkyResult {
  readonly coreIndex: Rac1CoreIndexHeader;
  readonly coreDataCompressedSize: number;
  readonly coreDataDecompressedSize: number;
  readonly skyOffset: number;
  readonly skySize: number;
  readonly sky: RcSky;
}

/** Read and preserve the fixed 0xbc-byte native core-index prefix. */
export async function readRac1CoreIndexHeader(reader: RandomAccessReader): Promise<Rac1CoreIndexHeader> {
  if (reader.size < RAC1_CORE_INDEX_HEADER_SIZE) {
    throw new Error(`R&C1 core-index source '${reader.name}' is smaller than 0xbc bytes.`);
  }
  const bytes = await reader.read(0, RAC1_CORE_INDEX_HEADER_SIZE);
  if (bytes.length !== RAC1_CORE_INDEX_HEADER_SIZE) throw new Error(`Short R&C1 core-index read from '${reader.name}'.`);
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  const rawWords: number[] = [];
  for (let offset = 0; offset < RAC1_CORE_INDEX_HEADER_SIZE; offset += 4) rawWords.push(view.getInt32(offset, true));

  const leadingBlockOffsets = [0x08, 0x0c, 0x10, 0x14].map((offset) => view.getInt32(offset, true));
  const tfragsOffset = view.getInt32(0x08, true);
  const skyOffset = view.getInt32(0x10, true);
  const collisionOffset = view.getInt32(0x14, true);
  const collisionEndBoundary = view.getInt32(0x60, true);
  const assetsCompressedSize = view.getInt32(0x88, true);
  const assetsDecompressedSize = view.getInt32(0x8c, true);
  for (const [name, value] of [
    ["tfragsOffset", tfragsOffset],
    ["skyOffset", skyOffset],
    ["collisionOffset", collisionOffset],
    ["collisionEndBoundary", collisionEndBoundary],
    ["assetsCompressedSize", assetsCompressedSize],
    ["assetsDecompressedSize", assetsDecompressedSize],
  ] as const) {
    if (!Number.isSafeInteger(value) || value < 0) throw new Error(`R&C1 core-index ${name} is invalid: ${value}.`);
  }

  const tfragsEndBoundary = [
    ...leadingBlockOffsets.slice(1),
    collisionEndBoundary,
    assetsDecompressedSize,
  ].filter((value) => value > tfragsOffset).sort((a, b) => a - b)[0];
  if (tfragsEndBoundary === undefined) {
    throw new Error(`R&C1 core-index has no positive boundary after tfrags offset ${tfragsOffset}.`);
  }
  const skyEndBoundary = skyOffset > 0 ? collisionOffset : 0;
  if (skyOffset > 0 && skyEndBoundary <= skyOffset) {
    throw new Error(`R&C1 core-index sky boundary ${skyEndBoundary} does not follow offset ${skyOffset}.`);
  }
  if (collisionEndBoundary <= collisionOffset) {
    throw new Error(`R&C1 core-index collision boundary ${collisionEndBoundary} does not follow offset ${collisionOffset}.`);
  }
  return {
    rawWords,
    leadingBlockOffsets,
    tfragsOffset,
    tfragsEndBoundary,
    skyOffset,
    skyEndBoundary,
    collisionOffset,
    collisionEndBoundary,
    assetsCompressedSize,
    assetsDecompressedSize,
  };
}

/** Decode only the WAD-LZ core-data block and verify both native size words. */
export async function readRac1CoreData(
  reader: RandomAccessReader,
  header: Rac1CoreIndexHeader,
): Promise<Rac1CoreDataResult> {
  const decoded = await readWadLz(reader, 0);
  if (decoded.compressedSize !== header.assetsCompressedSize) {
    throw new Error(
      `R&C1 core-data compressed size ${decoded.compressedSize} does not match core-index word 0x88 (${header.assetsCompressedSize}).`,
    );
  }
  if (decoded.data.length !== header.assetsDecompressedSize) {
    throw new Error(
      `R&C1 core-data decompressed size ${decoded.data.length} does not match core-index word 0x8c (${header.assetsDecompressedSize}).`,
    );
  }
  return { compressedSize: decoded.compressedSize, decompressedSize: decoded.data.length, data: decoded.data };
}

/** Decode and validate the native static-terrain tfrag block from core index/data sources. */
export async function readRac1CoreTfrags(
  coreIndexReader: RandomAccessReader,
  coreDataReader: RandomAccessReader,
  options: RcTfragOptions = {},
): Promise<Rac1TfragsResult> {
  const coreIndex = await readRac1CoreIndexHeader(coreIndexReader);
  const coreData = await readRac1CoreData(coreDataReader, coreIndex);
  if (coreIndex.tfragsEndBoundary > coreData.data.length) {
    throw new Error(
      `R&C1 tfrag end boundary ${coreIndex.tfragsEndBoundary} exceeds ${coreData.data.length}-byte decompressed core data.`,
    );
  }
  const tfrags = coreData.data.subarray(coreIndex.tfragsOffset, coreIndex.tfragsEndBoundary);
  const mesh = readRcTfrags(tfrags, options);
  return {
    coreIndex,
    coreDataCompressedSize: coreData.compressedSize,
    coreDataDecompressedSize: coreData.decompressedSize,
    tfragsOffset: coreIndex.tfragsOffset,
    tfragsSize: tfrags.length,
    mesh,
  };
}

/** Decode and validate the native sky block from already-open core index/data sources. */
export async function readRac1CoreSky(
  coreIndexReader: RandomAccessReader,
  coreDataReader: RandomAccessReader,
): Promise<Rac1SkyResult | null> {
  const coreIndex = await readRac1CoreIndexHeader(coreIndexReader);
  if (coreIndex.skyOffset === 0) return null;
  const coreData = await readRac1CoreData(coreDataReader, coreIndex);
  if (coreIndex.skyEndBoundary > coreData.data.length) {
    throw new Error(
      `R&C1 sky end boundary ${coreIndex.skyEndBoundary} exceeds ${coreData.data.length}-byte decompressed core data.`,
    );
  }
  const bytes = coreData.data.subarray(coreIndex.skyOffset, coreIndex.skyEndBoundary);
  const sky = readRcSky(bytes);
  return {
    coreIndex,
    coreDataCompressedSize: coreData.compressedSize,
    coreDataDecompressedSize: coreData.decompressedSize,
    skyOffset: coreIndex.skyOffset,
    skySize: bytes.length,
    sky,
  };
}

/** Decode and validate the native collision block from already-open core index/data sources. */
export async function readRac1CoreCollision(
  coreIndexReader: RandomAccessReader,
  coreDataReader: RandomAccessReader,
  options: RcCollisionOptions = {},
): Promise<Rac1CollisionResult> {
  const coreIndex = await readRac1CoreIndexHeader(coreIndexReader);
  const coreData = await readRac1CoreData(coreDataReader, coreIndex);
  if (coreIndex.collisionEndBoundary > coreData.data.length) {
    throw new Error(
      `R&C1 collision end boundary ${coreIndex.collisionEndBoundary} exceeds ${coreData.data.length}-byte decompressed core data.`,
    );
  }
  const collision = coreData.data.subarray(coreIndex.collisionOffset, coreIndex.collisionEndBoundary);
  const mesh = readRcCollision(collision, options);
  return {
    coreIndex,
    coreDataCompressedSize: coreData.compressedSize,
    coreDataDecompressedSize: coreData.decompressedSize,
    collisionOffset: coreIndex.collisionOffset,
    collisionSize: collision.length,
    mesh,
  };
}

async function openRac1ValidatedCoreSources(
  discReader: RandomAccessReader,
  level: Rac1NativeLevelCore,
): Promise<{ readonly coreIndex: RandomAccessReader; readonly coreData: RandomAccessReader }> {
  const levelData = openRac1LevelDataSource(discReader, level);
  const directory = await readRac1LevelDataDirectory(levelData);
  const coreIndex = openRac1LevelDataRange(levelData, directory, RAC1_LEVEL_DATA_CORE_INDEX_SLOT);
  const coreData = openRac1LevelDataRange(levelData, directory, RAC1_LEVEL_DATA_CORE_DATA_SLOT);
  if (!coreIndex || !coreData) throw new Error(`R&C1 level ${level.levelId} is missing its validated core index/data ranges.`);
  return { coreIndex, coreData };
}

/** End-to-end bounded static-terrain path from a retail disc reader. */
export async function readRac1LevelTfrags(
  discReader: RandomAccessReader,
  level: Rac1NativeLevelCore,
  options: RcTfragOptions = {},
): Promise<Rac1TfragsResult> {
  const { coreIndex, coreData } = await openRac1ValidatedCoreSources(discReader, level);
  return readRac1CoreTfrags(coreIndex, coreData, options);
}

/** End-to-end bounded sky path from a retail disc reader. */
export async function readRac1LevelSky(
  discReader: RandomAccessReader,
  level: Rac1NativeLevelCore,
): Promise<Rac1SkyResult | null> {
  const { coreIndex, coreData } = await openRac1ValidatedCoreSources(discReader, level);
  return readRac1CoreSky(coreIndex, coreData);
}

/** End-to-end bounded collision path from a retail disc reader. */
export async function readRac1LevelCollision(
  discReader: RandomAccessReader,
  level: Rac1NativeLevelCore,
  options: RcCollisionOptions = {},
): Promise<Rac1CollisionResult> {
  const { coreIndex, coreData } = await openRac1ValidatedCoreSources(discReader, level);
  return readRac1CoreCollision(coreIndex, coreData, options);
}
