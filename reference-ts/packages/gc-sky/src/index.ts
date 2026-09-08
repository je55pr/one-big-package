/**
 * Reader for the Going Commando / UYA **sky** — `LevelCoreHeader.sky`, a section
 * of the decompressed level-core asset blob.
 *
 * The sky is a set of up to 8 concentric *shells* (a static backdrop layer plus
 * nearer cloud / haze layers). Each shell is a plain indexed mesh split into
 * *clusters*; there is no VIF list to walk. The game redraws the sky centred on
 * the camera every frame with no depth, so shell vertex positions describe a
 * small unit-ish dome (`s16 / 1024`).
 *
 * ```text
 * SkyHeader (0x40), little-endian
 *   0x00 u8  colour[4]         // r,g,b,a  (a == 0x80 => opaque)
 *   0x04 s16 clear_screen
 *   0x06 s16 shell_count       // <= 8
 *   0x0a s16 maximum_sprite_count
 *   0x0c s16 texture_count
 *   0x0e s16 fx_count
 *   0x10 s32 texture_defs      // -> SkyTexture[texture_count]
 *   0x14 s32 texture_data      // base for palette_offset / texture_offset
 *   0x20 s32 shells[8]         // -> RacGcSkyShellHeader
 *
 * RacGcSkyShellHeader (GC/RAC)
 *   0x00 s32 cluster_count
 *   0x04 s32 flags             // bit0 set => untextured (gouraud) shell
 *   0x10 SkyClusterHeader[cluster_count]
 *
 * SkyClusterHeader (0x20)
 *   0x10 s32 data              // base offset (within the sky section) for this cluster
 *   0x14 s16 vertex_count      0x16 s16 tri_count
 *   0x18 u16 vertex_offset     0x1a u16 st_offset     0x1c u16 tri_offset
 *
 * SkyVertex (0x08):   s16 x, y, z, alpha            // pos = xyz / 1024
 * SkyTexCoord (0x04): s16 s, t                      // uv = st / 4096
 * SkyFace (0x04):     u8 i0, i1, i2, texture        // texture 0xff => untextured
 *
 * SkyTexture (0x10):  s32 palette_offset, texture_offset, width, height
 * ```
 *
 * Verified against retail Going Commando (see research/GC_SKY.md). Implemented
 * from the format description; the per-vertex alpha (horizon fade) is preserved
 * but not yet applied by the OBP viewer.
 */

import { decodePs2Paletted8 } from "../../ps2-texture/src/index.js";
import { gcLevelCoreSectionRange } from "../../gc-level-core/src/index.js";
import type { GcLevelCore } from "../../gc-level-core/src/index.js";

export const SKY_HEADER_SIZE = 0x40;
export const SKY_CLUSTER_HEADER_SIZE = 0x20;
export const SKY_TEXTURE_ENTRY_SIZE = 0x10;

export interface GcSkyShell {
  readonly textured: boolean;
  /** Local-space positions (native Z-up units), 3 per vertex. */
  readonly positions: Float64Array;
  /** 2 per vertex. */
  readonly uvs: Float32Array;
  /** Per-vertex alpha 0..1 (the sky format keeps RGB white and varies alpha). */
  readonly alpha: Float32Array;
  readonly indices: Uint32Array;
  /** Per-triangle sky-texture index, or -1 for untextured faces. */
  readonly triangleTextureIds: Int32Array;
}

export interface GcSkyTexture {
  readonly index: number;
  readonly width: number;
  readonly height: number;
  /** `width * height * 4` straight-alpha RGBA. */
  readonly rgba: Uint8Array;
}

export interface GcSky {
  /** Base sky colour, RGBA 0..1. */
  readonly colour: readonly [number, number, number, number];
  readonly clearScreen: boolean;
  readonly maximumSpriteCount: number;
  readonly shells: readonly GcSkyShell[];
  readonly textures: readonly GcSkyTexture[];
}

function readSkyTextures(bytes: Uint8Array, view: DataView, defsOffset: number, dataOffset: number, count: number): GcSkyTexture[] {
  const out: GcSkyTexture[] = [];
  for (let i = 0; i < count; i++) {
    const at = defsOffset + i * SKY_TEXTURE_ENTRY_SIZE;
    if (at + SKY_TEXTURE_ENTRY_SIZE > bytes.length) break;
    const paletteOffset = view.getInt32(at + 0x00, true);
    const textureOffset = view.getInt32(at + 0x04, true);
    const width = view.getInt32(at + 0x08, true);
    const height = view.getInt32(at + 0x0c, true);
    if (width <= 0 || height <= 0 || width > 1024 || height > 1024) continue;

    const pixelStart = dataOffset + textureOffset;
    const paletteStart = dataOffset + paletteOffset;
    if (pixelStart + width * height > bytes.length || paletteStart + 256 * 4 > bytes.length) continue;

    const palette = new Uint32Array(256);
    for (let p = 0; p < 256; p++) palette[p] = view.getUint32(paletteStart + p * 4, true);
    const pixels = bytes.subarray(pixelStart, pixelStart + width * height);

    const decoded = decodePs2Paletted8({ width, height, pixels, palette });
    out.push({ index: i, width, height, rgba: decoded.rgba });
  }
  return out;
}

function readSkyShell(bytes: Uint8Array, view: DataView, shellOffset: number): GcSkyShell | null {
  if (shellOffset <= 0 || shellOffset + 0x10 > bytes.length) return null;
  const clusterCount = view.getInt32(shellOffset + 0x00, true);
  const flags = view.getInt32(shellOffset + 0x04, true);
  if (clusterCount < 0 || clusterCount > 4096) return null;
  const textured = (flags & 1) === 0;

  const positions: number[] = [];
  const uvs: number[] = [];
  const alpha: number[] = [];
  const indices: number[] = [];
  const triangleTextureIds: number[] = [];

  for (let c = 0; c < clusterCount; c++) {
    const ch = shellOffset + 0x10 + c * SKY_CLUSTER_HEADER_SIZE;
    if (ch + SKY_CLUSTER_HEADER_SIZE > bytes.length) break;
    const data = view.getInt32(ch + 0x10, true);
    const vertexCount = view.getInt16(ch + 0x14, true);
    const triCount = view.getInt16(ch + 0x16, true);
    const vertexOffset = view.getUint16(ch + 0x18, true);
    const stOffset = view.getUint16(ch + 0x1a, true);
    const triOffset = view.getUint16(ch + 0x1c, true);
    if (vertexCount <= 0 || triCount < 0 || data <= 0) continue;

    const vbase = data + vertexOffset;
    const sbase = data + stOffset;
    const fbase = data + triOffset;
    if (vbase + vertexCount * 8 > bytes.length || sbase + vertexCount * 4 > bytes.length || fbase + triCount * 4 > bytes.length) {
      continue;
    }

    const base = positions.length / 3;
    for (let v = 0; v < vertexCount; v++) {
      positions.push(
        view.getInt16(vbase + v * 8 + 0, true) / 1024,
        view.getInt16(vbase + v * 8 + 2, true) / 1024,
        view.getInt16(vbase + v * 8 + 4, true) / 1024,
      );
      const a = view.getInt16(vbase + v * 8 + 6, true);
      alpha.push(a === 0x80 ? 1 : Math.max(0, Math.min(1, (a * 2) / 255)));
      uvs.push(view.getInt16(sbase + v * 4 + 0, true) / 4096, view.getInt16(sbase + v * 4 + 2, true) / 4096);
    }
    for (let f = 0; f < triCount; f++) {
      const i0 = bytes[fbase + f * 4 + 0]!;
      const i1 = bytes[fbase + f * 4 + 1]!;
      const i2 = bytes[fbase + f * 4 + 2]!;
      const tex = bytes[fbase + f * 4 + 3]!;
      if (i0 >= vertexCount || i1 >= vertexCount || i2 >= vertexCount) continue;
      // Reverse the winding order (matches the retail render).
      indices.push(base + i2, base + i1, base + i0);
      triangleTextureIds.push(tex === 0xff ? -1 : tex);
    }
  }

  return {
    textured,
    positions: Float64Array.from(positions),
    uvs: Float32Array.from(uvs),
    alpha: Float32Array.from(alpha),
    indices: Uint32Array.from(indices),
    triangleTextureIds: Int32Array.from(triangleTextureIds),
  };
}

export function readGcSky(bytes: Uint8Array): GcSky {
  if (bytes.length < SKY_HEADER_SIZE) {
    throw new Error(`GC sky: buffer is ${bytes.length} bytes, shorter than the 0x40 header.`);
  }
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);

  const r = bytes[0]!, g = bytes[1]!, b = bytes[2]!, a = bytes[3]!;
  const colour: [number, number, number, number] = [r / 255, g / 255, b / 255, a === 0x80 ? 1 : a / 128];
  const clearScreen = view.getInt16(0x04, true) !== 0;
  const shellCount = view.getInt16(0x06, true);
  const maximumSpriteCount = view.getInt16(0x0a, true);
  const textureCount = view.getInt16(0x0c, true);
  const textureDefs = view.getInt32(0x10, true);
  const textureData = view.getInt32(0x14, true);
  if (shellCount < 0 || shellCount > 8) {
    throw new Error(`GC sky: implausible shell count ${shellCount}.`);
  }

  const textures = textureCount > 0 && textureCount < 1024 && textureDefs > 0 && textureData > 0
    ? readSkyTextures(bytes, view, textureDefs, textureData, textureCount)
    : [];

  const shells: GcSkyShell[] = [];
  for (let i = 0; i < shellCount; i++) {
    const shell = readSkyShell(bytes, view, view.getInt32(0x20 + i * 4, true));
    if (shell && shell.indices.length > 0) shells.push(shell);
  }

  return { colour, clearScreen, maximumSpriteCount, shells, textures };
}

export function readGcLevelSky(core: GcLevelCore): GcSky | null {
  if (!core.coreHeader.sky) return null;
  const range = gcLevelCoreSectionRange(core, core.coreHeader.sky);
  if (!range) return null;
  return readGcSky(core.assets.subarray(range.offset, range.offset + range.size));
}
