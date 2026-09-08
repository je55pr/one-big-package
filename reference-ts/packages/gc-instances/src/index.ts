import type { RandomAccessReader } from "../../importer-common/src/index.js";
import { readWadLz } from "../../wad-lz/src/index.js";

/**
 * Reader for the Going Commando / UYA **gameplay** lump (level WAD header slot 2,
 * `gameplay` SectorRange) — the instance placement data.
 *
 * The lump is WAD-LZ compressed. The decompressed buffer starts with a table of
 * `s32` block pointers; a block's pointer sits at a fixed byte offset in that
 * table (e.g. `0x34` for GC tie instances). Each instance block is
 * `{ s32 count; pad to 0x10 }` then `count` packed instance structs.
 *
 * | block | GC header offset | packed struct | size |
 * |---|---|---|---|
 * | tie instances | `0x34` | `s32 oClass; s32 drawDist; ...; Mat4 matrix @ 0x10; ...` | `0x60` |
 * | shrub instances | `0x40` | `s32 oClass; f32 drawDist; ...; Mat4 matrix @ 0x10; ...` | `0x70` |
 * | moby instances | `0x4c` | (different block header) — not read here | — |
 *
 * `Mat4` is 16 little-endian `f32`, column-major: elements `[0,4,8]` / `[1,5,9]`
 * / `[2,6,10]` are the basis vectors, `[12,13,14]` the translation.
 *
 * Verified against retail Going Commando (see research/GC_INSTANCES.md).
 */

export interface GcInstance {
  readonly index: number;
  readonly oClass: number;
  /** 16 f32, column-major. */
  readonly matrix: readonly number[];
}

export interface GcMobyInstance {
  readonly index: number;
  readonly oClass: number;
  readonly scale: number;
  readonly position: readonly [number, number, number];
  /** Euler rotation in radians (XYZ). */
  readonly rotation: readonly [number, number, number];
  /** Static/ambient light colour baked at this instance (`Rgb96` @ 0x74, s32 per channel / 255). */
  readonly lightColour: readonly [number, number, number];
  /** Index into {@link GcGameplayInstances.dirLights} (@ 0x80); out of range = ambient only. */
  readonly lightIndex: number;
}

/**
 * A Going Commando directional light — the main light type. Two components
 * (a + b, key + fill); each has an RGB colour and a unit world-space direction
 * (native Z-up). `DirectionalLightPacked` 0x40: `Vec4f colourA, directionA,
 * colourB, directionB` (the 4th float of each is unused / a small bias).
 */
export interface GcDirLight {
  readonly colourA: readonly [number, number, number];
  readonly directionA: readonly [number, number, number];
  readonly colourB: readonly [number, number, number];
  readonly directionB: readonly [number, number, number];
}

/** A point light — position, radius and RGB colour (native Z-up). Only affects mobies. `GcUyaPointLightPacked` 0x10. */
export interface GcPointLight {
  readonly position: readonly [number, number, number];
  readonly radius: number;
  readonly colour: readonly [number, number, number];
}

/**
 * An environment sample point — a spatial probe. The nearest one to a point sets
 * its ambient ("hero") colour + directional light, and its fog + reverb.
 * `GcUyaDlEnvSamplePointPacked` 0x20. Position is native Z-up, `× 1/4`.
 */
export interface GcEnvSample {
  readonly position: readonly [number, number, number];
  readonly heroLightIndex: number;
  readonly heroColour: readonly [number, number, number];
  /** null when the sample carries no fog override (all-zero distances). */
  readonly fog: {
    readonly colour: readonly [number, number, number];
    readonly nearDistance: number;
    readonly farDistance: number;
    readonly nearIntensity: number;
    readonly farIntensity: number;
  } | null;
}

/** One end of an env transition — the region state on that side of the volume. */
export interface GcEnvState {
  readonly heroColour: readonly [number, number, number];
  readonly heroLightIndex: number;
  readonly fogColour: readonly [number, number, number];
  readonly fogNearDistance: number;
  readonly fogFarDistance: number;
  readonly fogNearIntensity: number;
  readonly fogFarIntensity: number;
}

/**
 * A box volume across which the hero (player) lighting and/or fog blend from
 * {@link stateA} to {@link stateB} — a doorway. `EnvTransitionPacked` 0x80:
 * `Mat4 inverseMatrix` (world → box local, column-major), then the two states'
 * colours / light indices / fog, then `u32 flags` (bit0 hero, bit1 fog).
 * `boundingSphere` is `[cx, cy, cz, radius]`, world space (native Z-up).
 */
export interface GcEnvTransition {
  readonly inverseMatrix: readonly number[];
  readonly boundingSphere: readonly [number, number, number, number];
  readonly enableHero: boolean;
  readonly enableFog: boolean;
  readonly stateA: GcEnvState;
  readonly stateB: GcEnvState;
}

export interface GcGameplayInstances {
  readonly tieInstances: readonly GcInstance[];
  readonly shrubInstances: readonly GcInstance[];
  readonly mobyInstances: readonly GcMobyInstance[];
  readonly dirLights: readonly GcDirLight[];
  readonly pointLights: readonly GcPointLight[];
  readonly envSamples: readonly GcEnvSample[];
  readonly envTransitions: readonly GcEnvTransition[];
  readonly decompressedSize: number;
}

const GC_DIR_LIGHTS_PTR = 0x04;
const GC_TIE_INSTANCES_PTR = 0x34;
const GC_SHRUB_INSTANCES_PTR = 0x40;
const GC_MOBY_INSTANCES_PTR = 0x4c;
const GC_POINT_LIGHTS_PTR = 0x80;
const GC_ENV_TRANSITIONS_PTR = 0x84;
const GC_ENV_SAMPLES_PTR = 0x8c;
const GC_TIE_INSTANCE_SIZE = 0x60;
const GC_SHRUB_INSTANCE_SIZE = 0x70;
const GC_MOBY_INSTANCE_SIZE = 0x88;

export async function readGcGameplayInstances(
  gameplayLump: RandomAccessReader,
  options: { maxDecompressedBytes?: number } = {},
): Promise<GcGameplayInstances> {
  const { data } = await readWadLz(gameplayLump, 0, { maxOutputBytes: options.maxDecompressedBytes ?? 64 * 1024 * 1024 });
  return parseGcGameplayInstances(data);
}

export function parseGcGameplayInstances(data: Uint8Array): GcGameplayInstances {
  const view = new DataView(data.buffer, data.byteOffset, data.byteLength);

  const readBlock = (pointerOffset: number, structSize: number): GcInstance[] => {
    if (pointerOffset + 4 > data.length) return [];
    const blockOffset = view.getInt32(pointerOffset, true);
    if (blockOffset <= 0 || blockOffset + 0x10 > data.length) return [];
    const count = view.getInt32(blockOffset, true);
    if (count < 0 || count > 200_000) return [];
    const out: GcInstance[] = [];
    for (let i = 0; i < count; i++) {
      const at = blockOffset + 0x10 + i * structSize;
      if (at + 0x50 > data.length) break;
      const matrix: number[] = [];
      for (let m = 0; m < 16; m++) matrix.push(view.getFloat32(at + 0x10 + m * 4, true));
      out.push({ index: i, oClass: view.getInt32(at, true), matrix });
    }
    return out;
  };

  // Moby instances: `MobyBlockHeader { s32 staticCount; ... }` (0x10) then
  // `GcUyaMobyInstance` (0x88): s32 oClass @ 0x28; f32 scale @ 0x2c;
  // Vec3f position @ 0x40; Vec3f rotation (euler radians) @ 0x4c.
  const readMoby = (): GcMobyInstance[] => {
    if (GC_MOBY_INSTANCES_PTR + 4 > data.length) return [];
    const blockOffset = view.getInt32(GC_MOBY_INSTANCES_PTR, true);
    if (blockOffset <= 0 || blockOffset + 0x10 > data.length) return [];
    const count = view.getInt32(blockOffset, true);
    if (count < 0 || count > 200_000) return [];
    const out: GcMobyInstance[] = [];
    for (let i = 0; i < count; i++) {
      const at = blockOffset + 0x10 + i * GC_MOBY_INSTANCE_SIZE;
      if (at + GC_MOBY_INSTANCE_SIZE > data.length) break;
      if (view.getInt32(at, true) !== GC_MOBY_INSTANCE_SIZE) break; // `size` field is always 0x88
      const position: [number, number, number] = [
        view.getFloat32(at + 0x40, true), view.getFloat32(at + 0x44, true), view.getFloat32(at + 0x48, true),
      ];
      if (!position.every(Number.isFinite)) continue;
      out.push({
        index: i,
        oClass: view.getInt32(at + 0x28, true),
        scale: view.getFloat32(at + 0x2c, true),
        position,
        rotation: [view.getFloat32(at + 0x4c, true), view.getFloat32(at + 0x50, true), view.getFloat32(at + 0x54, true)],
        lightColour: [
          view.getInt32(at + 0x74, true) / 255,
          view.getInt32(at + 0x78, true) / 255,
          view.getInt32(at + 0x7c, true) / 255,
        ],
        lightIndex: view.getInt32(at + 0x80, true),
      });
    }
    return out;
  };

  // Directional lights: `InstanceBlock` — TableHeader (count @ 0) then
  // `DirectionalLightPacked` (0x40) at 0x10. See GcDirLight.
  const readDirLights = (): GcDirLight[] => {
    if (GC_DIR_LIGHTS_PTR + 4 > data.length) return [];
    const blockOffset = view.getInt32(GC_DIR_LIGHTS_PTR, true);
    if (blockOffset <= 0 || blockOffset + 0x10 > data.length) return [];
    const count = view.getInt32(blockOffset, true);
    if (count < 0 || count > 4096) return [];
    const out: GcDirLight[] = [];
    for (let i = 0; i < count; i++) {
      const at = blockOffset + 0x10 + i * 0x40;
      if (at + 0x40 > data.length) break;
      const v3 = (o: number): [number, number, number] =>
        [view.getFloat32(at + o, true), view.getFloat32(at + o + 4, true), view.getFloat32(at + o + 8, true)];
      out.push({ colourA: v3(0x00), directionA: v3(0x10), colourB: v3(0x20), directionB: v3(0x30) });
    }
    return out;
  };

  // Point lights: TableHeader (count @ 0), a 0x800-byte cell grid, then
  // `GcUyaPointLightPacked` (0x10) at 0x10 + 0x800. u16 fields: pos / 64,
  // radius / 64, colour / 65535. Radius 0 = an unused slot.
  const readPointLights = (): GcPointLight[] => {
    if (GC_POINT_LIGHTS_PTR + 4 > data.length) return [];
    const blockOffset = view.getInt32(GC_POINT_LIGHTS_PTR, true);
    if (blockOffset <= 0 || blockOffset + 0x10 > data.length) return [];
    const count = view.getInt32(blockOffset, true);
    if (count < 0 || count > 4096) return [];
    const out: GcPointLight[] = [];
    for (let i = 0; i < count; i++) {
      const at = blockOffset + 0x10 + 0x800 + i * 0x10;
      if (at + 0x10 > data.length) break;
      const u = (o: number): number => view.getUint16(at + o, true);
      const radius = u(0x06) / 64;
      if (radius <= 0) continue;
      out.push({
        position: [u(0x00) / 64, u(0x02) / 64, u(0x04) / 64],
        radius,
        colour: [u(0x08) / 65535, u(0x0a) / 65535, u(0x0c) / 65535],
      });
    }
    return out;
  };

  // Env sample points: InstanceBlock of `GcUyaDlEnvSamplePointPacked` (0x20).
  const readEnvSamples = (): GcEnvSample[] => {
    if (GC_ENV_SAMPLES_PTR + 4 > data.length) return [];
    const blockOffset = view.getInt32(GC_ENV_SAMPLES_PTR, true);
    if (blockOffset <= 0 || blockOffset + 0x10 > data.length) return [];
    const count = view.getInt32(blockOffset, true);
    if (count < 0 || count > 4096) return [];
    const out: GcEnvSample[] = [];
    for (let i = 0; i < count; i++) {
      const at = blockOffset + 0x10 + i * 0x20;
      if (at + 0x20 > data.length) break;
      const s16 = (o: number): number => view.getInt16(at + o, true);
      const u8 = (o: number): number => data[at + o]!;
      const nearD = s16(0x1a), farD = s16(0x1c);
      out.push({
        position: [s16(0x04) / 4, s16(0x06) / 4, s16(0x08) / 4],
        heroLightIndex: view.getInt32(at + 0x00, true),
        heroColour: [u8(0x10) / 255, u8(0x11) / 255, u8(0x12) / 255],
        fog: farD > nearD
          ? {
              colour: [u8(0x17) / 255, u8(0x18) / 255, u8(0x19) / 255],
              nearDistance: nearD,
              farDistance: farD,
              nearIntensity: u8(0x0e),
              farIntensity: u8(0x0f),
            }
          : null,
      });
    }
    return out;
  };

  // Env transitions: TableHeader (count @ 0), count × Vec4f bounding spheres,
  // then count × EnvTransitionPacked (0x80).
  const readEnvTransitions = (): GcEnvTransition[] => {
    if (GC_ENV_TRANSITIONS_PTR + 4 > data.length) return [];
    const blockOffset = view.getInt32(GC_ENV_TRANSITIONS_PTR, true);
    if (blockOffset <= 0 || blockOffset + 0x10 > data.length) return [];
    const count = view.getInt32(blockOffset, true);
    if (count < 0 || count > 4096) return [];
    const spheresAt = blockOffset + 0x10;
    const structsAt = spheresAt + count * 0x10;
    const out: GcEnvTransition[] = [];
    for (let i = 0; i < count; i++) {
      const so = spheresAt + i * 0x10;
      const at = structsAt + i * 0x80;
      if (at + 0x80 > data.length) break;
      const f = (o: number): number => view.getFloat32(at + o, true);
      const u8 = (o: number): number => data[at + o]!;
      const rgb = (o: number): [number, number, number] => [u8(o) / 255, u8(o + 1) / 255, u8(o + 2) / 255];
      const flags = view.getUint32(at + 0x50, true);
      const state = (side: 0 | 1): GcEnvState => ({
        heroColour: rgb(0x40 + side * 4),
        heroLightIndex: view.getInt32(at + 0x48 + side * 4, true),
        fogColour: rgb(0x54 + side * 4),
        fogNearDistance: f(0x5c + side * 0x10),
        fogNearIntensity: f(0x60 + side * 0x10),
        fogFarDistance: f(0x64 + side * 0x10),
        fogFarIntensity: f(0x68 + side * 0x10),
      });
      out.push({
        inverseMatrix: Array.from({ length: 16 }, (_, j) => f(j * 4)),
        boundingSphere: [
          view.getFloat32(so, true), view.getFloat32(so + 4, true),
          view.getFloat32(so + 8, true), view.getFloat32(so + 12, true),
        ],
        enableHero: (flags & 1) !== 0,
        enableFog: (flags & 2) !== 0,
        stateA: state(0),
        stateB: state(1),
      });
    }
    return out;
  };

  return {
    tieInstances: readBlock(GC_TIE_INSTANCES_PTR, GC_TIE_INSTANCE_SIZE),
    shrubInstances: readBlock(GC_SHRUB_INSTANCES_PTR, GC_SHRUB_INSTANCE_SIZE),
    mobyInstances: readMoby(),
    dirLights: readDirLights(),
    pointLights: readPointLights(),
    envSamples: readEnvSamples(),
    envTransitions: readEnvTransitions(),
    decompressedSize: data.length,
  };
}

/** Build a column-major `Mat4` (16 f32) from position, XYZ-euler rotation (radians) and uniform scale. */
export function matrixFromPosRotScale(
  position: readonly [number, number, number],
  rotation: readonly [number, number, number],
  scale: number,
): number[] {
  const [rx, ry, rz] = rotation;
  const cx = Math.cos(rx), sx = Math.sin(rx);
  const cy = Math.cos(ry), sy = Math.sin(ry);
  const cz = Math.cos(rz), sz = Math.sin(rz);
  // R = Rz * Ry * Rx
  const m00 = cy * cz;
  const m01 = cy * sz;
  const m02 = -sy;
  const m10 = sx * sy * cz - cx * sz;
  const m11 = sx * sy * sz + cx * cz;
  const m12 = sx * cy;
  const m20 = cx * sy * cz + sx * sz;
  const m21 = cx * sy * sz - sx * cz;
  const m22 = cx * cy;
  return [
    m00 * scale, m10 * scale, m20 * scale, 0,
    m01 * scale, m11 * scale, m21 * scale, 0,
    m02 * scale, m12 * scale, m22 * scale, 0,
    position[0], position[1], position[2], 1,
  ];
}

/** Apply a column-major `Mat4` (16 f32) to a local point. */
export function transformPoint(matrix: readonly number[], x: number, y: number, z: number): [number, number, number] {
  return [
    matrix[0]! * x + matrix[4]! * y + matrix[8]! * z + matrix[12]!,
    matrix[1]! * x + matrix[5]! * y + matrix[9]! * z + matrix[13]!,
    matrix[2]! * x + matrix[6]! * y + matrix[10]! * z + matrix[14]!,
  ];
}
