import type { RandomAccessReader } from "../../importer-common/src/index.js";
import { openRac1LevelCoreRange } from "../../rac1-disc-index/src/index.js";
import type { Rac1NativeLevelCore } from "../../rac1-disc-index/src/index.js";
import { readWadLz } from "../../wad-lz/src/index.js";

/** R&C1 outer native range 1 is the NTSC gameplay WAD on the supported build. */
export const RAC1_GAMEPLAY_RANGE_SLOT = 1;
export const RAC1_TIE_INSTANCES_POINTER_OFFSET = 0x34;
export const RAC1_SHRUB_INSTANCES_POINTER_OFFSET = 0x3c;
export const RAC1_MOBY_INSTANCES_POINTER_OFFSET = 0x44;
export const RAC1_TIE_INSTANCE_SIZE = 0xe0;
export const RAC1_SHRUB_INSTANCE_SIZE = 0x70;
export const RAC1_MOBY_INSTANCE_SIZE = 0x78;

export interface Rac1MatrixInstance {
  readonly index: number;
  readonly oClass: number;
  /** Native 16-float column-major transform. Only the upper 3x4 is required for point transforms. */
  readonly matrix: readonly number[];
  readonly raw0x04: number;
  readonly raw0x08: number;
  readonly raw0x0c: number;
}

export interface Rac1TieInstance extends Rac1MatrixInstance {
  readonly uid: number;
}

export interface Rac1ShrubInstance extends Rac1MatrixInstance {}

export interface Rac1MobyInstance {
  readonly index: number;
  readonly oClass: number;
  readonly scale: number;
  readonly position: readonly [number, number, number];
  /** Native XYZ Euler rotation in radians; retained without an OBP-axis reinterpretation here. */
  readonly rotation: readonly [number, number, number];
  readonly spawnableMobyCount: number;
}

export interface Rac1GameplayInstances {
  readonly tieInstances: readonly Rac1TieInstance[];
  readonly shrubInstances: readonly Rac1ShrubInstance[];
  readonly mobyInstances: readonly Rac1MobyInstance[];
  readonly spawnableMobyCount: number;
  readonly decompressedSize: number;
}

function readPointer(view: DataView, dataLength: number, pointerOffset: number, label: string): number {
  if (pointerOffset < 0 || pointerOffset + 4 > dataLength) throw new Error(`R&C1 gameplay ${label} pointer field is out of range.`);
  const offset = view.getInt32(pointerOffset, true);
  if (offset <= 0 || offset + 0x10 > dataLength) throw new Error(`R&C1 gameplay ${label} block offset ${offset} is out of range.`);
  return offset;
}

function readMatrixInstances<T extends Rac1MatrixInstance>(
  data: Uint8Array,
  pointerOffset: number,
  recordSize: number,
  label: string,
  finish: (base: Rac1MatrixInstance, view: DataView, at: number) => T,
): T[] {
  const view = new DataView(data.buffer, data.byteOffset, data.byteLength);
  const blockOffset = readPointer(view, data.length, pointerOffset, label);
  const count = view.getInt32(blockOffset, true);
  if (count < 0 || count > 200_000) throw new Error(`R&C1 gameplay ${label} count ${count} is invalid.`);
  const bytes = count * recordSize;
  if (!Number.isSafeInteger(bytes) || blockOffset + 0x10 > data.length || bytes > data.length - (blockOffset + 0x10)) {
    throw new Error(`R&C1 gameplay ${label} array runs past the decompressed stream.`);
  }

  const out: T[] = [];
  for (let i = 0; i < count; i++) {
    const at = blockOffset + 0x10 + i * recordSize;
    const matrix: number[] = [];
    for (let m = 0; m < 16; m++) {
      const value = view.getFloat32(at + 0x10 + m * 4, true);
      if (!Number.isFinite(value)) throw new Error(`R&C1 gameplay ${label} ${i} has a non-finite matrix value.`);
      matrix.push(value);
    }
    const base: Rac1MatrixInstance = {
      index: i,
      oClass: view.getInt32(at, true),
      matrix,
      raw0x04: view.getInt32(at + 4, true),
      raw0x08: view.getInt32(at + 8, true),
      raw0x0c: view.getInt32(at + 12, true),
    };
    out.push(finish(base, view, at));
  }
  return out;
}

/** Parse the retail R&C1 NTSC gameplay stream after WAD-LZ decompression. */
export function parseRac1GameplayInstances(data: Uint8Array): Rac1GameplayInstances {
  if (data.length < 0x94) throw new Error("R&C1 gameplay stream is shorter than the observed 0x94-byte pointer header.");
  const view = new DataView(data.buffer, data.byteOffset, data.byteLength);

  const tieInstances = readMatrixInstances<Rac1TieInstance>(
    data,
    RAC1_TIE_INSTANCES_POINTER_OFFSET,
    RAC1_TIE_INSTANCE_SIZE,
    "tie instances",
    (base, source, at) => ({ ...base, uid: source.getInt32(at + 0x54, true) }),
  );
  const shrubInstances = readMatrixInstances<Rac1ShrubInstance>(
    data,
    RAC1_SHRUB_INSTANCES_POINTER_OFFSET,
    RAC1_SHRUB_INSTANCE_SIZE,
    "shrub instances",
    (base) => base,
  );

  const mobyBlockOffset = readPointer(view, data.length, RAC1_MOBY_INSTANCES_POINTER_OFFSET, "moby instances");
  const mobyCount = view.getInt32(mobyBlockOffset, true);
  const spawnableMobyCount = view.getInt32(mobyBlockOffset + 4, true);
  if (mobyCount < 0 || mobyCount > 200_000) throw new Error(`R&C1 gameplay moby count ${mobyCount} is invalid.`);
  if (spawnableMobyCount < 0 || spawnableMobyCount > 200_000) throw new Error(`R&C1 gameplay spawnable moby count ${spawnableMobyCount} is invalid.`);
  const mobyBytes = mobyCount * RAC1_MOBY_INSTANCE_SIZE;
  if (!Number.isSafeInteger(mobyBytes) || mobyBytes > data.length - (mobyBlockOffset + 0x10)) {
    throw new Error("R&C1 gameplay moby instance array runs past the decompressed stream.");
  }

  const mobyInstances: Rac1MobyInstance[] = [];
  for (let i = 0; i < mobyCount; i++) {
    const at = mobyBlockOffset + 0x10 + i * RAC1_MOBY_INSTANCE_SIZE;
    const declaredSize = view.getInt32(at, true);
    if (declaredSize !== RAC1_MOBY_INSTANCE_SIZE) {
      throw new Error(`R&C1 gameplay moby ${i} declares size 0x${declaredSize.toString(16)}, expected 0x78.`);
    }
    const scale = view.getFloat32(at + 0x1c, true);
    const position: [number, number, number] = [
      view.getFloat32(at + 0x30, true),
      view.getFloat32(at + 0x34, true),
      view.getFloat32(at + 0x38, true),
    ];
    const rotation: [number, number, number] = [
      view.getFloat32(at + 0x3c, true),
      view.getFloat32(at + 0x40, true),
      view.getFloat32(at + 0x44, true),
    ];
    if (!Number.isFinite(scale) || !position.every(Number.isFinite) || !rotation.every(Number.isFinite)) {
      throw new Error(`R&C1 gameplay moby ${i} has a non-finite transform.`);
    }
    mobyInstances.push({
      index: i,
      oClass: view.getInt32(at + 0x18, true),
      scale,
      position,
      rotation,
      spawnableMobyCount,
    });
  }

  return { tieInstances, shrubInstances, mobyInstances, spawnableMobyCount, decompressedSize: data.length };
}

/** Decode only the selected native gameplay WAD. */
export async function readRac1GameplayInstances(
  gameplay: RandomAccessReader,
  options: { readonly maxDecompressedBytes?: number } = {},
): Promise<Rac1GameplayInstances> {
  const decoded = await readWadLz(gameplay, 0, { maxOutputBytes: options.maxDecompressedBytes ?? 64 * 1024 * 1024 });
  return parseRac1GameplayInstances(decoded.data);
}

/** End-to-end bounded placement path from one retail R&C1 level catalogue entry. */
export async function readRac1LevelGameplayInstances(
  discReader: RandomAccessReader,
  level: Rac1NativeLevelCore,
  options: { readonly maxDecompressedBytes?: number } = {},
): Promise<Rac1GameplayInstances> {
  const gameplay = openRac1LevelCoreRange(discReader, level, RAC1_GAMEPLAY_RANGE_SLOT);
  if (!gameplay) throw new Error(`R&C1 level ${level.levelId} has no native gameplay range ${RAC1_GAMEPLAY_RANGE_SLOT}.`);
  return readRac1GameplayInstances(gameplay, options);
}

/** Apply a native RC column-major instance matrix to a native Z-up class-local point. */
export function transformRac1InstancePoint(
  matrix: readonly number[],
  x: number,
  y: number,
  z: number,
): [number, number, number] {
  if (matrix.length !== 16) throw new Error(`R&C1 instance matrix has ${matrix.length} values, expected 16.`);
  return [
    matrix[0]! * x + matrix[4]! * y + matrix[8]! * z + matrix[12]!,
    matrix[1]! * x + matrix[5]! * y + matrix[9]! * z + matrix[13]!,
    matrix[2]! * x + matrix[6]! * y + matrix[10]! * z + matrix[14]!,
  ];
}

/**
 * Apply an R&C1 packed Moby placement to a class-local native Z-up point.
 *
 * The retail executable proves that the runtime Moby's Euler state feeds the
 * three native basis vectors. The exact Z/Y/X composition is independently
 * corroborated by pinned Wrench (`T * S * Rz * Ry * Rx`) and was exercised over
 * all 9,122 geometry-bearing rigid retail placements / 3,215,117 transformed
 * vertices with zero non-finite results. Axis conversion to OBP is deliberately
 * left to the caller.
 */
export function transformRac1MobyPoint(
  instance: Pick<Rac1MobyInstance, "position" | "rotation" | "scale">,
  x: number,
  y: number,
  z: number,
): [number, number, number] {
  const [rx, ry, rz] = instance.rotation;
  const sx = Math.sin(rx);
  const cx = Math.cos(rx);
  const sy = Math.sin(ry);
  const cy = Math.cos(ry);
  const sz = Math.sin(rz);
  const cz = Math.cos(rz);

  // Column vectors: Rz * Ry * Rx means points encounter Rx, then Ry, then Rz.
  const x1 = x;
  const y1 = cx * y - sx * z;
  const z1 = sx * y + cx * z;
  const x2 = cy * x1 + sy * z1;
  const y2 = y1;
  const z2 = -sy * x1 + cy * z1;
  const x3 = cz * x2 - sz * y2;
  const y3 = sz * x2 + cz * y2;

  return [
    instance.position[0] + instance.scale * x3,
    instance.position[1] + instance.scale * y3,
    instance.position[2] + instance.scale * z2,
  ];
}

/**
 * Convert native R&C1 Moby Euler radians into the equivalent OBP Y-up Euler.
 * OBP Euler uses the same column-vector XYZ convention: `Rz * Ry * Rx`.
 *
 * This is a pure coordinate-basis conversion. For C:(x,y,z)->(x,z,y), the
 * equivalent OBP rotation is `C * Rnative * C`. We then decompose that matrix
 * back into the OBP XYZ Euler convention, including a deterministic gimbal-lock
 * fallback with z=0.
 */
export function rac1MobyRotationToObpEuler(
  rotation: readonly [number, number, number],
): [number, number, number] {
  const [rx, ry, rz] = rotation;
  const sx = Math.sin(rx), cx = Math.cos(rx);
  const sy = Math.sin(ry), cy = Math.cos(ry);
  const sz = Math.sin(rz), cz = Math.cos(rz);

  // Native R = Rz * Ry * Rx.
  const n00 = cz * cy;
  const n01 = cz * sy * sx - sz * cx;
  const n02 = cz * sy * cx + sz * sx;
  const n10 = sz * cy;
  const n11 = sz * sy * sx + cz * cx;
  const n12 = sz * sy * cx - cz * sx;
  const n20 = -sy;
  const n21 = cy * sx;
  const n22 = cy * cx;

  // C * R * C swaps native Y/Z rows and columns.
  const o00 = n00;
  const o01 = n02;
  const o02 = n01;
  const o10 = n20;
  const o11 = n22;
  const o12 = n21;
  const o20 = n10;
  const o21 = n12;
  const o22 = n11;

  const clamped = Math.max(-1, Math.min(1, -o20));
  const oy = Math.asin(clamped);
  const cyOut = Math.cos(oy);
  if (Math.abs(cyOut) > 1e-10) {
    return [Math.atan2(o21, o22), oy, Math.atan2(o10, o00)];
  }

  // At |pitch|=pi/2, x and z are coupled. Choosing z=0 preserves the matrix.
  const ox = Math.atan2(-o12, o11);
  return [ox, oy, 0];
}
