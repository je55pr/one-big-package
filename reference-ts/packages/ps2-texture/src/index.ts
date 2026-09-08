/**
 * PS2 paletted-texture decode for the Ratchet & Clank games.
 *
 * Level textures are 8-bit palettized: `width * height` linear index bytes plus a
 * 256-entry 32-bit CLUT. Two PS2-specific fixups apply before use:
 *
 * - **CLUT reorder** (`swizzlePalette`): the GS stores a 256-colour CLUT with
 *   bits 3 and 4 of the index swapped whenever they differ.
 * - **alpha scale** (`multiplyAlphas`): PS2 alpha is 0..128; `a < 0x80 -> a*2`,
 *   otherwise `255`.
 *
 * On RC2 / RC3 the *pixel* data is stored linearly (only Deadlocked swizzles the
 * pixels), so an RC2 texture is: reorder + alpha-scale the palette, then look up.
 *
 * Implemented from the format description. Verified against retail Going Commando
 * level textures (see research/GC_TEXTURES.md).
 */

export interface Ps2PalettedTexture {
  readonly width: number;
  readonly height: number;
  /** `width * height` palette indices, row-major, top-left origin. */
  readonly pixels: Uint8Array;
  /** Up to 256 CLUT entries as little-endian RGBA u32 (`a` in the high byte). */
  readonly palette: Uint32Array;
}

export interface DecodedTexture {
  readonly width: number;
  readonly height: number;
  /** `width * height * 4` bytes, RGBA, straight (non-premultiplied) alpha. */
  readonly rgba: Uint8Array;
}

/** GS CLUT index reorder: swap bits 3 and 4 when they differ. */
export function mapPaletteIndex(index: number): number {
  return (((index & 16) >> 1) !== (index & 8)) ? (index ^ 0b00011000) : index;
}

/** Return a new 256-entry palette with the GS CLUT reorder applied. */
export function swizzlePalette(palette: Uint32Array): Uint32Array {
  const out = new Uint32Array(256);
  for (let i = 0; i < 256; i++) out[i] = palette[mapPaletteIndex(i)] ?? 0;
  return out;
}

/** Scale PS2 0..128 alpha to 0..255 in place (operates on RGBA u32 entries). */
export function multiplyAlphas(palette: Uint32Array): void {
  for (let i = 0; i < palette.length; i++) {
    const c = palette[i]!;
    let a = (c >>> 24) & 0xff;
    a = a < 0x80 ? a * 2 : 255;
    palette[i] = ((c & 0x00ffffff) | (a << 24)) >>> 0;
  }
}

export interface DecodeOptions {
  /** Apply the GS CLUT reorder. Default true. */
  readonly reorderPalette?: boolean;
  /** Apply the 0..128 -> 0..255 alpha scale. Default true. */
  readonly scaleAlpha?: boolean;
}

/** Decode an 8-bit paletted PS2 texture (linear pixels) to straight RGBA. */
export function decodePs2Paletted8(texture: Ps2PalettedTexture, options: DecodeOptions = {}): DecodedTexture {
  const { width, height, pixels } = texture;
  if (!Number.isInteger(width) || !Number.isInteger(height) || width <= 0 || height <= 0 || width > 4096 || height > 4096) {
    throw new RangeError(`Invalid PS2 texture size ${width}x${height}.`);
  }
  if (pixels.length < width * height) throw new Error(`PS2 texture pixel buffer is ${pixels.length} bytes, need ${width * height}.`);

  let palette = new Uint32Array(256);
  palette.set(texture.palette.subarray(0, 256));
  if (options.scaleAlpha ?? true) multiplyAlphas(palette);
  if (options.reorderPalette ?? true) palette = swizzlePalette(palette);

  const rgba = new Uint8Array(width * height * 4);
  for (let i = 0; i < width * height; i++) {
    const c = palette[pixels[i]!]!;
    rgba[i * 4] = c & 0xff;
    rgba[i * 4 + 1] = (c >>> 8) & 0xff;
    rgba[i * 4 + 2] = (c >>> 16) & 0xff;
    rgba[i * 4 + 3] = (c >>> 24) & 0xff;
  }
  return { width, height, rgba };
}
