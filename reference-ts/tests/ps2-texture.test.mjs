import test from "node:test";
import assert from "node:assert/strict";
import {
  decodePs2Paletted8,
  mapPaletteIndex,
  swizzlePalette,
  multiplyAlphas,
} from "../.build/packages/ps2-texture/src/index.js";

const rgba = (r, g, b, a) => ((a << 24) | (b << 16) | (g << 8) | r) >>> 0;

test("mapPaletteIndex swaps CLUT bits 3 and 4 when they differ", () => {
  assert.equal(mapPaletteIndex(0), 0);
  assert.equal(mapPaletteIndex(0b00001000), 0b00010000); // 8 -> 16
  assert.equal(mapPaletteIndex(0b00010000), 0b00001000); // 16 -> 8
  assert.equal(mapPaletteIndex(0b00011000), 0b00011000); // both set -> unchanged
  assert.equal(mapPaletteIndex(0b10100101), 0b10100101); // neither bit set -> unchanged
});

test("multiplyAlphas scales 0..128 to 0..255", () => {
  const pal = Uint32Array.of(rgba(10, 20, 30, 0), rgba(0, 0, 0, 0x40), rgba(0, 0, 0, 0x80), rgba(0, 0, 0, 0xff));
  multiplyAlphas(pal);
  assert.equal(pal[0] >>> 24, 0);
  assert.equal(pal[1] >>> 24, 0x80);
  assert.equal(pal[2] >>> 24, 255);
  assert.equal(pal[3] >>> 24, 255);
  assert.equal(pal[0] & 0xffffff, rgba(10, 20, 30, 0) & 0xffffff); // rgb untouched
});

test("swizzlePalette applies the reorder to every entry", () => {
  const pal = new Uint32Array(256);
  for (let i = 0; i < 256; i++) pal[i] = rgba(i, 0, 0, 0);
  const out = swizzlePalette(pal);
  assert.equal(out[8] & 0xff, 16);
  assert.equal(out[16] & 0xff, 8);
  assert.equal(out[0] & 0xff, 0);
});

test("decodePs2Paletted8 looks up reordered, alpha-scaled colours", () => {
  const palette = new Uint32Array(256);
  palette[16] = rgba(200, 10, 10, 0x40); // after reorder, index 8 -> this
  palette[3] = rgba(0, 128, 0, 0x80);
  const pixels = Uint8Array.of(8, 3, 3, 8); // 2x2
  const out = decodePs2Paletted8({ width: 2, height: 2, pixels, palette });

  assert.equal(out.width, 2);
  assert.equal(out.rgba.length, 16);
  // pixel 0 = palette index 8 -> reordered to 16 -> (200,10,10) alpha 0x40*2=0x80
  assert.deepEqual([...out.rgba.slice(0, 4)], [200, 10, 10, 0x80]);
  // pixel 1 = index 3 -> (0,128,0) alpha 0x80 -> 255
  assert.deepEqual([...out.rgba.slice(4, 8)], [0, 128, 0, 255]);
});

test("options can disable the fixups", () => {
  const palette = new Uint32Array(256);
  palette[8] = rgba(1, 2, 3, 0x10);
  const out = decodePs2Paletted8(
    { width: 1, height: 1, pixels: Uint8Array.of(8), palette },
    { reorderPalette: false, scaleAlpha: false },
  );
  assert.deepEqual([...out.rgba], [1, 2, 3, 0x10]);
});

test("rejects an impossible size or short pixel buffer", () => {
  assert.throws(() => decodePs2Paletted8({ width: 0, height: 8, pixels: new Uint8Array(0), palette: new Uint32Array(256) }), /Invalid PS2 texture size/);
  assert.throws(() => decodePs2Paletted8({ width: 4, height: 4, pixels: new Uint8Array(8), palette: new Uint32Array(256) }), /pixel buffer/);
});
