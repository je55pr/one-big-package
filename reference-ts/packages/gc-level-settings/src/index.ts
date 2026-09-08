/**
 * Reader for the Going Commando / UYA **level settings** block — the first block
 * of the gameplay lump (level WAD slot 2, WAD-LZ compressed). The gameplay lump
 * opens with a table of `s32` block pointers; the pointer at byte `0x00` points
 * at the level settings block, which begins with a fixed `first part` struct.
 *
 * This is where the level records its **death height** (the Y-plane below which
 * the player dies — under Oozla it sits just under the toxic goo surface), its
 * background / fog colours and distances, and, for the "spherical world" gravity
 * levels, the planet centre.
 *
 * ```text
 * GcUyaDlLevelSettingsFirstPart (0x5c), little-endian
 *   0x00 s32 background_colour[3]   // 0..255 per channel, or r == -1 => unset
 *   0x0c s32 fog_colour[3]
 *   0x18 f32 fog_near_distance
 *   0x1c f32 fog_far_distance
 *   0x20 f32 fog_near_intensity
 *   0x24 f32 fog_far_intensity
 *   0x28 f32 death_height
 *   0x2c s32 is_spherical_world
 *   0x30 f32 sphere_centre[3]
 *   0x3c f32 ship_position[3]
 *   0x48 f32 ship_rotation_z
 *   0x4c s32 ship_path
 *   0x50 s32 ship_camera_cuboid_start
 *   0x54 s32 ship_camera_cuboid_end
 *   0x58 u32 pad
 * ```
 *
 * Only the first part is decoded here; the chunk planes / sound counts / debug
 * arrays that follow are left for later. Verified against retail Going Commando
 * (see research/GC_LEVEL_SETTINGS.md). Implemented from the format description.
 */

import type { RandomAccessReader } from "../../importer-common/src/index.js";
import { readWadLz } from "../../wad-lz/src/index.js";

export const GC_LEVEL_SETTINGS_FIRST_PART_SIZE = 0x5c;

export interface GcLevelSettings {
  /** Background clear colour, RGB 0..1, or `null` when the level leaves it unset. */
  readonly backgroundColour: readonly [number, number, number] | null;
  /** Fog colour, RGB 0..1, or `null` when unset. */
  readonly fogColour: readonly [number, number, number] | null;
  readonly fogNearDistance: number;
  readonly fogFarDistance: number;
  readonly fogNearIntensity: number;
  readonly fogFarIntensity: number;
  /** The kill-plane height, in native (Z-up) world units. */
  readonly deathHeight: number;
  readonly isSphericalWorld: boolean;
  /** Planet centre for spherical-gravity levels, native (Z-up) units. */
  readonly sphereCentre: readonly [number, number, number];
  /** Where the ship parks, native (Z-up) units. */
  readonly shipPosition: readonly [number, number, number];
  readonly shipRotationZ: number;
  /** Byte offset of the block within the decompressed gameplay lump. */
  readonly blockOffset: number;
}

function readRgb96(view: DataView, offset: number): readonly [number, number, number] | null {
  const r = view.getInt32(offset, true);
  if (r === -1) return null;
  return [r / 255, view.getInt32(offset + 4, true) / 255, view.getInt32(offset + 8, true) / 255];
}

export function parseGcLevelSettings(gameplayData: Uint8Array): GcLevelSettings {
  const view = new DataView(gameplayData.buffer, gameplayData.byteOffset, gameplayData.byteLength);
  if (gameplayData.length < 4) {
    throw new Error(`GC level settings: gameplay lump is only ${gameplayData.length} bytes.`);
  }
  const blockOffset = view.getInt32(0x00, true);
  if (blockOffset <= 0 || blockOffset + GC_LEVEL_SETTINGS_FIRST_PART_SIZE > gameplayData.length) {
    throw new Error(`GC level settings: block pointer 0x${blockOffset.toString(16)} is out of range.`);
  }

  const f32 = (rel: number): number => view.getFloat32(blockOffset + rel, true);
  const vec3 = (rel: number): [number, number, number] => [f32(rel), f32(rel + 4), f32(rel + 8)];

  return {
    backgroundColour: readRgb96(view, blockOffset + 0x00),
    fogColour: readRgb96(view, blockOffset + 0x0c),
    fogNearDistance: f32(0x18),
    fogFarDistance: f32(0x1c),
    fogNearIntensity: f32(0x20),
    fogFarIntensity: f32(0x24),
    deathHeight: f32(0x28),
    isSphericalWorld: view.getInt32(blockOffset + 0x2c, true) !== 0,
    sphereCentre: vec3(0x30),
    shipPosition: vec3(0x3c),
    shipRotationZ: f32(0x48),
    blockOffset,
  };
}

export async function readGcLevelSettings(
  gameplayLump: RandomAccessReader,
  options: { maxDecompressedBytes?: number } = {},
): Promise<GcLevelSettings> {
  const { data } = await readWadLz(gameplayLump, 0, {
    maxOutputBytes: options.maxDecompressedBytes ?? 64 * 1024 * 1024,
  });
  return parseGcLevelSettings(data);
}
