import type { RandomAccessReader } from "../../importer-common/src/index.js";
import { openRac1LevelCoreRange } from "../../rac1-disc-index/src/index.js";
import type { Rac1NativeLevelCore } from "../../rac1-disc-index/src/index.js";
import { RAC1_GAMEPLAY_RANGE_SLOT } from "../../rac1-instances/src/index.js";
import { readWadLz } from "../../wad-lz/src/index.js";

/** Pointer to the first native gameplay block. */
export const RAC1_LEVEL_SETTINGS_POINTER_OFFSET = 0x00;
/** Directly validated R&C1 first-part size; GC later grows this structure to 0x5c. */
export const RAC1_LEVEL_SETTINGS_FIRST_PART_SIZE = 0x50;

export interface Rac1LevelSettings {
  /** Background clear colour, RGB 0..1, or null when native R is -1. */
  readonly backgroundColour: readonly [number, number, number] | null;
  /** Fog colour, RGB 0..1, or null when native R is -1. */
  readonly fogColour: readonly [number, number, number] | null;
  readonly fogNearDistance: number;
  readonly fogFarDistance: number;
  readonly fogNearIntensity: number;
  readonly fogFarIntensity: number;
  /** Native Z-up kill-plane height. This maps directly to OBP Y. */
  readonly deathHeight: number;
  /** Native ship parking position in Z-up coordinates. */
  readonly shipPosition: readonly [number, number, number];
  readonly shipRotationZ: number;
  readonly shipPath: number;
  readonly shipCameraCuboidStart: number;
  readonly shipCameraCuboidEnd: number;
  /** Exact trailing native words at 0x48/0x4c, retained for archaeology. */
  readonly rawPadWords: readonly [number, number];
  readonly blockOffset: number;
}

function readRgb96(view: DataView, offset: number, label: string): readonly [number, number, number] | null {
  const values: [number, number, number] = [
    view.getInt32(offset, true),
    view.getInt32(offset + 4, true),
    view.getInt32(offset + 8, true),
  ];
  if (values[0] === -1) return null;
  if (!values.every((value) => Number.isInteger(value) && value >= 0 && value <= 255)) {
    throw new Error(`R&C1 level settings ${label} RGB96 values [${values.join(", ")}] are invalid.`);
  }
  return [values[0] / 255, values[1] / 255, values[2] / 255];
}

/** Parse the retail R&C1 level-settings first part from decompressed gameplay bytes. */
export function parseRac1LevelSettings(gameplayData: Uint8Array): Rac1LevelSettings {
  if (gameplayData.length < 4) throw new Error(`R&C1 level settings gameplay stream is only ${gameplayData.length} bytes.`);
  const view = new DataView(gameplayData.buffer, gameplayData.byteOffset, gameplayData.byteLength);
  const blockOffset = view.getInt32(RAC1_LEVEL_SETTINGS_POINTER_OFFSET, true);
  if (blockOffset <= 0 || blockOffset > gameplayData.length || RAC1_LEVEL_SETTINGS_FIRST_PART_SIZE > gameplayData.length - blockOffset) {
    throw new Error(`R&C1 level settings block pointer 0x${(blockOffset >>> 0).toString(16)} is out of range.`);
  }

  const f32 = (relativeOffset: number, label: string): number => {
    const value = view.getFloat32(blockOffset + relativeOffset, true);
    if (!Number.isFinite(value)) throw new Error(`R&C1 level settings ${label} is non-finite.`);
    return value;
  };
  const vec3 = (relativeOffset: number, label: string): [number, number, number] => [
    f32(relativeOffset, `${label}.x`),
    f32(relativeOffset + 4, `${label}.y`),
    f32(relativeOffset + 8, `${label}.z`),
  ];

  return {
    backgroundColour: readRgb96(view, blockOffset + 0x00, "background"),
    fogColour: readRgb96(view, blockOffset + 0x0c, "fog"),
    fogNearDistance: f32(0x18, "fogNearDistance"),
    fogFarDistance: f32(0x1c, "fogFarDistance"),
    fogNearIntensity: f32(0x20, "fogNearIntensity"),
    fogFarIntensity: f32(0x24, "fogFarIntensity"),
    deathHeight: f32(0x28, "deathHeight"),
    shipPosition: vec3(0x2c, "shipPosition"),
    shipRotationZ: f32(0x38, "shipRotationZ"),
    shipPath: view.getInt32(blockOffset + 0x3c, true),
    shipCameraCuboidStart: view.getInt32(blockOffset + 0x40, true),
    shipCameraCuboidEnd: view.getInt32(blockOffset + 0x44, true),
    rawPadWords: [view.getUint32(blockOffset + 0x48, true), view.getUint32(blockOffset + 0x4c, true)],
    blockOffset,
  };
}

/** Decode one native gameplay WAD and parse its R&C1 level settings. */
export async function readRac1LevelSettings(
  gameplay: RandomAccessReader,
  options: { readonly maxDecompressedBytes?: number } = {},
): Promise<Rac1LevelSettings> {
  const decoded = await readWadLz(gameplay, 0, { maxOutputBytes: options.maxDecompressedBytes ?? 64 * 1024 * 1024 });
  return parseRac1LevelSettings(decoded.data);
}

/** End-to-end bounded settings path from a retail R&C1 level catalogue entry. */
export async function readRac1NativeLevelSettings(
  discReader: RandomAccessReader,
  level: Rac1NativeLevelCore,
  options: { readonly maxDecompressedBytes?: number } = {},
): Promise<Rac1LevelSettings> {
  const gameplay = openRac1LevelCoreRange(discReader, level, RAC1_GAMEPLAY_RANGE_SLOT);
  if (!gameplay) throw new Error(`R&C1 level ${level.levelId} has no native gameplay range ${RAC1_GAMEPLAY_RANGE_SLOT}.`);
  return readRac1LevelSettings(gameplay, options);
}
