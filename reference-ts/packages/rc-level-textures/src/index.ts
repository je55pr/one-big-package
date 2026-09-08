import { decodePs2Paletted8 } from "../../ps2-texture/src/index.js";
import type { DecodedTexture } from "../../ps2-texture/src/index.js";

/** Fixed entry size directly validated in retail R&C1 and Going Commando cores. */
export const RC_LEVEL_TEXTURE_ENTRY_SIZE = 0x10;

export interface RcTextureTableRange {
  readonly count: number;
  readonly offset: number;
}

export interface RcLevelTextureSources {
  /** Native level-core index block containing the 0x10-byte texture table entries. */
  readonly index: Uint8Array;
  /** Decompressed native level-core asset data containing linear 8-bit pixels. */
  readonly assets: Uint8Array;
  /** Native GS-RAM image containing 32-bit palettes. */
  readonly gsRam: Uint8Array;
  /** Native byte offset within `assets` to which TextureEntry.dataOffset is relative. */
  readonly texturesBaseOffset: number;
}

export interface RcLevelTexture {
  /** Positional/native texture id within this table. */
  readonly index: number;
  readonly width: number;
  readonly height: number;
  /** Native TextureEntry.type word; semantics are not normalized here. */
  readonly type: number;
  /** Native palette slot; palette byte offset is `paletteSlot * 0x100`. */
  readonly paletteSlot: number;
  readonly dataOffset: number;
  /** Native 0x0c word. Public tooling calls this `mipmap`; retained without depending on that meaning. */
  readonly raw0x0c: number;
  /** Native 0x0e word, preserved. */
  readonly raw0x0e: number;
  readonly rgba: Uint8Array;
}

export interface RcLevelTextureOptions {
  /** Reject dimensions larger than this. Default 1024. */
  readonly maxDimension?: number;
  /** Throw on malformed entries instead of omitting them. Default false for legacy GC compatibility. */
  readonly strict?: boolean;
}

/**
 * Decode one shared PS2 RC level texture table.
 *
 * Direct R&C1 retail validation across all 19 levels established that terrain
 * textures use one byte per pixel, palettes are 256 little-endian RGBA32 entries
 * at `paletteSlot * 0x100`, and the existing PS2 CLUT reorder + alpha scaling
 * produces coherent environment textures. The GS allocation tables contain only
 * PSM 0 (RGBA32 palette) and PSM 0x13 (IDTEX8) for this path.
 */
export function readRcLevelTextureTable(
  source: RcLevelTextureSources,
  range: RcTextureTableRange,
  options: RcLevelTextureOptions = {},
): RcLevelTexture[] {
  const maxDimension = options.maxDimension ?? 1024;
  const strict = options.strict ?? false;
  if (!Number.isInteger(range.count) || range.count < 0 || range.count > 1_000_000) {
    throw new Error(`RC level texture table count ${range.count} is invalid.`);
  }
  if (!Number.isInteger(range.offset) || range.offset < 0) {
    throw new Error(`RC level texture table offset ${range.offset} is invalid.`);
  }
  if (!Number.isInteger(source.texturesBaseOffset) || source.texturesBaseOffset < 0 || source.texturesBaseOffset > source.assets.length) {
    throw new Error(`RC level texture base offset ${source.texturesBaseOffset} is invalid.`);
  }

  const index = new DataView(source.index.buffer, source.index.byteOffset, source.index.byteLength);
  const gs = new DataView(source.gsRam.buffer, source.gsRam.byteOffset, source.gsRam.byteLength);
  const out: RcLevelTexture[] = [];

  const malformed = (message: string): boolean => {
    if (strict) throw new Error(message);
    return true;
  };

  for (let i = 0; i < range.count; i++) {
    const at = range.offset + i * RC_LEVEL_TEXTURE_ENTRY_SIZE;
    if (at < 0 || at + RC_LEVEL_TEXTURE_ENTRY_SIZE > source.index.length) {
      if (malformed(`RC level texture ${i} entry lies outside the core index.`)) continue;
    }

    const dataOffset = index.getInt32(at, true);
    const width = index.getInt16(at + 4, true);
    const height = index.getInt16(at + 6, true);
    const type = index.getInt16(at + 8, true);
    const paletteSlot = index.getInt16(at + 10, true);
    const raw0x0c = index.getInt16(at + 12, true);
    const raw0x0e = index.getInt16(at + 14, true);
    if (width <= 0 || height <= 0 || width > maxDimension || height > maxDimension) {
      if (malformed(`RC level texture ${i} has invalid dimensions ${width}x${height}.`)) continue;
    }

    const pixelStart = source.texturesBaseOffset + dataOffset;
    const pixelBytes = width * height;
    if (!Number.isSafeInteger(pixelStart) || pixelStart < 0 || pixelStart > source.assets.length || pixelBytes > source.assets.length - pixelStart) {
      if (malformed(`RC level texture ${i} pixel range ${pixelStart}+${pixelBytes} lies outside core assets.`)) continue;
    }

    const paletteStart = paletteSlot * 0x100;
    if (!Number.isSafeInteger(paletteStart) || paletteStart < 0 || paletteStart + 0x400 > source.gsRam.length) {
      if (malformed(`RC level texture ${i} palette slot ${paletteSlot} lies outside GS RAM.`)) continue;
    }

    const pixels = source.assets.subarray(pixelStart, pixelStart + pixelBytes);
    const palette = new Uint32Array(256);
    for (let k = 0; k < 256; k++) palette[k] = gs.getUint32(paletteStart + k * 4, true);

    let decoded: DecodedTexture;
    try {
      decoded = decodePs2Paletted8({ width, height, pixels, palette });
    } catch (error) {
      if (strict) throw error;
      continue;
    }
    out.push({ index: i, width, height, type, paletteSlot, dataOffset, raw0x0c, raw0x0e, rgba: decoded.rgba });
  }
  return out;
}
