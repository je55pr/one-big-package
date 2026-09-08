import { decodePs2Paletted8 } from "../../ps2-texture/src/index.js";

export const UYA_SKY_HEADER_SIZE = 0x40;
export const UYA_SKY_SHELL_HEADER_SIZE = 0x10;
export const UYA_SKY_CLUSTER_HEADER_SIZE = 0x20;
export const UYA_SKY_TEXTURE_ENTRY_SIZE = 0x10;

export interface UyaSkyTexture {
  readonly index: number;
  readonly width: number;
  readonly height: number;
  readonly rgba: Uint8Array;
}

export interface UyaSkyShell {
  readonly textured: boolean;
  readonly bloom: boolean;
  /** Raw signed 16-bit native values retained exactly. */
  readonly rotationRaw: readonly [number, number, number];
  readonly angularVelocityRaw: readonly [number, number, number];
  /** Wrench-compatible conversion for a caller-supplied game framerate. */
  readonly rotationRadiansPerSecond: readonly [number, number, number];
  readonly angularVelocityRadiansPerSecond: readonly [number, number, number];
  readonly positions: Float64Array;
  readonly uvs: Float32Array;
  readonly alpha: Float32Array;
  readonly indices: Uint32Array;
  /** Per-triangle texture-header index, or -1 for native 0xff/no-texture. */
  readonly triangleTextureIds: Int32Array;
  readonly clusterCount: number;
}

export interface UyaSky {
  readonly colour: readonly [number, number, number, number];
  readonly clearScreen: boolean;
  readonly spriteCount: number;
  readonly maximumSpriteCount: number;
  readonly fxCount: number;
  readonly shells: readonly UyaSkyShell[];
  readonly textures: readonly UyaSkyTexture[];
  readonly shellOffsets: readonly number[];
}

function requireRange(bytes: Uint8Array, offset: number, size: number, label: string): void {
  if (!Number.isSafeInteger(offset) || !Number.isSafeInteger(size) || offset < 0 || size < 0 || offset > bytes.length || size > bytes.length - offset) {
    throw new RangeError(`UYA sky: ${label} range ${offset}+${size} lies outside ${bytes.length} bytes.`);
  }
}

function rotationToRadiansPerSecond(angle: number, framerate: number): number {
  return angle * (framerate * ((2 * Math.PI) / 32768));
}

function vec3s16(view: DataView, offset: number): [number, number, number] {
  return [
    view.getInt16(offset + 0, true),
    view.getInt16(offset + 2, true),
    view.getInt16(offset + 4, true),
  ];
}

function convertedRotation(raw: readonly [number, number, number], framerate: number): [number, number, number] {
  return [
    rotationToRadiansPerSecond(raw[0], framerate),
    rotationToRadiansPerSecond(raw[1], framerate),
    rotationToRadiansPerSecond(raw[2], framerate),
  ];
}

function readTextures(bytes: Uint8Array, view: DataView, defsOffset: number, dataOffset: number, count: number): UyaSkyTexture[] {
  if (count === 0) return [];
  requireRange(bytes, defsOffset, count * UYA_SKY_TEXTURE_ENTRY_SIZE, "texture definition table");
  const out: UyaSkyTexture[] = [];
  for (let i = 0; i < count; i++) {
    const at = defsOffset + i * UYA_SKY_TEXTURE_ENTRY_SIZE;
    const paletteOffset = view.getInt32(at + 0x00, true);
    const textureOffset = view.getInt32(at + 0x04, true);
    const width = view.getInt32(at + 0x08, true);
    const height = view.getInt32(at + 0x0c, true);
    if (width <= 0 || height <= 0 || width > 1024 || height > 1024) {
      throw new Error(`UYA sky: texture ${i} has implausible dimensions ${width}x${height}.`);
    }
    const pixelStart = dataOffset + textureOffset;
    const paletteStart = dataOffset + paletteOffset;
    requireRange(bytes, pixelStart, width * height, `texture ${i} pixels`);
    requireRange(bytes, paletteStart, 256 * 4, `texture ${i} palette`);
    const palette = new Uint32Array(256);
    for (let p = 0; p < 256; p++) palette[p] = view.getUint32(paletteStart + p * 4, true);
    const pixels = bytes.subarray(pixelStart, pixelStart + width * height);
    const decoded = decodePs2Paletted8({ width, height, pixels, palette });
    out.push({ index: i, width, height, rgba: decoded.rgba });
  }
  return out;
}

function readShell(bytes: Uint8Array, view: DataView, shellOffset: number, textureCount: number, framerate: number): UyaSkyShell {
  requireRange(bytes, shellOffset, UYA_SKY_SHELL_HEADER_SIZE, "shell header");
  const clusterCount = view.getInt16(shellOffset + 0x00, true);
  const flags = view.getInt16(shellOffset + 0x02, true);
  if (clusterCount < 0 || clusterCount > 4096) throw new Error(`UYA sky: implausible cluster count ${clusterCount}.`);
  requireRange(bytes, shellOffset + 0x10, clusterCount * UYA_SKY_CLUSTER_HEADER_SIZE, "shell cluster table");

  const rotationRaw = vec3s16(view, shellOffset + 0x04);
  const angularVelocityRaw = vec3s16(view, shellOffset + 0x0a);
  const positions: number[] = [];
  const uvs: number[] = [];
  const alpha: number[] = [];
  const indices: number[] = [];
  const triangleTextureIds: number[] = [];

  for (let c = 0; c < clusterCount; c++) {
    const ch = shellOffset + 0x10 + c * UYA_SKY_CLUSTER_HEADER_SIZE;
    const data = view.getInt32(ch + 0x10, true);
    const vertexCount = view.getInt16(ch + 0x14, true);
    const triCount = view.getInt16(ch + 0x16, true);
    const vertexOffset = view.getInt16(ch + 0x18, true);
    const stOffset = view.getInt16(ch + 0x1a, true);
    const triOffset = view.getInt16(ch + 0x1c, true);
    if (vertexCount < 0 || vertexCount > 32767 || triCount < 0 || triCount > 32767) {
      throw new Error(`UYA sky: cluster ${c} has implausible counts v=${vertexCount} t=${triCount}.`);
    }
    if (vertexOffset < 0 || stOffset < 0 || triOffset < 0) throw new Error(`UYA sky: cluster ${c} has negative local offsets.`);
    requireRange(bytes, data + vertexOffset, vertexCount * 8, `cluster ${c} vertices`);
    requireRange(bytes, data + stOffset, vertexCount * 4, `cluster ${c} texture coordinates`);
    requireRange(bytes, data + triOffset, triCount * 4, `cluster ${c} faces`);

    const base = positions.length / 3;
    for (let v = 0; v < vertexCount; v++) {
      const va = data + vertexOffset + v * 8;
      positions.push(
        view.getInt16(va + 0, true) / 1024,
        view.getInt16(va + 2, true) / 1024,
        view.getInt16(va + 4, true) / 1024,
      );
      const nativeAlpha = view.getInt16(va + 6, true);
      alpha.push(nativeAlpha === 0x80 ? 1 : Math.max(0, Math.min(1, (nativeAlpha * 2) / 255)));
      const ta = data + stOffset + v * 4;
      uvs.push(view.getInt16(ta + 0, true) / 4096, view.getInt16(ta + 2, true) / 4096);
    }

    for (let f = 0; f < triCount; f++) {
      const fa = data + triOffset + f * 4;
      const i0 = bytes[fa + 0]!;
      const i1 = bytes[fa + 1]!;
      const i2 = bytes[fa + 2]!;
      const texture = bytes[fa + 3]!;
      if (i0 >= vertexCount || i1 >= vertexCount || i2 >= vertexCount) {
        throw new Error(`UYA sky: cluster ${c} face ${f} references vertex outside ${vertexCount}.`);
      }
      if (texture !== 0xff && texture >= textureCount) {
        throw new Error(`UYA sky: cluster ${c} face ${f} references texture ${texture} outside ${textureCount}.`);
      }
      indices.push(base + i2, base + i1, base + i0);
      triangleTextureIds.push(texture === 0xff ? -1 : texture);
    }
  }

  return {
    textured: (flags & 1) === 0,
    bloom: ((flags >> 1) & 1) === 1,
    rotationRaw,
    angularVelocityRaw,
    rotationRadiansPerSecond: convertedRotation(rotationRaw, framerate),
    angularVelocityRadiansPerSecond: convertedRotation(angularVelocityRaw, framerate),
    positions: Float64Array.from(positions),
    uvs: Float32Array.from(uvs),
    alpha: Float32Array.from(alpha),
    indices: Uint32Array.from(indices),
    triangleTextureIds: Int32Array.from(triangleTextureIds),
    clusterCount,
  };
}

/**
 * Strict UYA/Deadlocked-family sky reader, currently retail-censused for UYA only.
 * The common 0x40 SkyHeader/texture/cluster records match the established GC family;
 * the shell header uses UYA's s16 cluster/flags + rotation/angular-velocity layout.
 */
export function readUyaSky(bytes: Uint8Array, { framerate = 60 } = {}): UyaSky {
  if (bytes.length < UYA_SKY_HEADER_SIZE) throw new Error(`UYA sky: buffer is ${bytes.length} bytes, shorter than the 0x40 header.`);
  if (!Number.isFinite(framerate) || framerate <= 0) throw new Error(`UYA sky: invalid framerate ${framerate}.`);
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  const shellCount = view.getInt16(0x06, true);
  const spriteCount = view.getInt16(0x08, true);
  const maximumSpriteCount = view.getInt16(0x0a, true);
  const textureCount = view.getInt16(0x0c, true);
  const fxCount = view.getInt16(0x0e, true);
  const textureDefs = view.getInt32(0x10, true);
  const textureData = view.getInt32(0x14, true);
  if (shellCount < 0 || shellCount > 8) throw new Error(`UYA sky: implausible shell count ${shellCount}.`);
  if (textureCount < 0 || textureCount > 1024) throw new Error(`UYA sky: implausible texture count ${textureCount}.`);
  if (fxCount < 0 || fxCount > textureCount) throw new Error(`UYA sky: implausible FX count ${fxCount} for ${textureCount} textures.`);
  if (spriteCount < 0 || maximumSpriteCount < 0) throw new Error(`UYA sky: negative sprite count.`);

  const shellOffsets: number[] = [];
  const shells: UyaSkyShell[] = [];
  for (let i = 0; i < shellCount; i++) {
    const offset = view.getInt32(0x20 + i * 4, true);
    if (offset <= 0) throw new Error(`UYA sky: shell ${i} has invalid offset ${offset}.`);
    shellOffsets.push(offset);
    shells.push(readShell(bytes, view, offset, textureCount, framerate));
  }

  const textures = textureCount === 0 ? [] : readTextures(bytes, view, textureDefs, textureData, textureCount);
  const r = bytes[0]!, g = bytes[1]!, b = bytes[2]!, a = bytes[3]!;
  return {
    colour: [r / 255, g / 255, b / 255, a === 0x80 ? 1 : a / 128],
    clearScreen: view.getInt16(0x04, true) !== 0,
    spriteCount,
    maximumSpriteCount,
    fxCount,
    shells,
    textures,
    shellOffsets,
  };
}
