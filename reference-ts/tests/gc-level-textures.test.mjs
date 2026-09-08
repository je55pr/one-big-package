import test from "node:test";
import assert from "node:assert/strict";
import { readGcLevelTextures, TEXTURE_ENTRY_SIZE } from "../.build/packages/gc-level-textures/src/index.js";

const rgba = (r, g, b, a) => ((a << 24) | (b << 16) | (g << 8) | r) >>> 0;

/** Minimal GcLevelCore shape with one 2x2 tfrag texture. */
function synthCore({ width = 2, height = 2, palette = 3, dataOffset = 0, texturesBaseOffset = 0x40 } = {}) {
  const index = new Uint8Array(0x200);
  const iv = new DataView(index.buffer);
  const tableOffset = 0x80;
  // one TextureEntry
  iv.setInt32(tableOffset + 0x0, dataOffset, true);
  iv.setInt16(tableOffset + 0x4, width, true);
  iv.setInt16(tableOffset + 0x6, height, true);
  iv.setInt16(tableOffset + 0x8, 3, true); // type
  iv.setInt16(tableOffset + 0xa, palette, true);

  const assets = new Uint8Array(texturesBaseOffset + width * height + 16);
  assets.set([1, 2, 2, 1], texturesBaseOffset + dataOffset); // 2x2 indices

  const gsRam = new Uint8Array((palette + 1) * 0x100 + 256 * 4);
  const gv = new DataView(gsRam.buffer);
  gv.setUint32(palette * 0x100 + 1 * 4, rgba(255, 0, 0, 0x80), true); // palette[1] -> red
  gv.setUint32(palette * 0x100 + 2 * 4, rgba(0, 0, 255, 0x40), true); // palette[2] -> blue, half alpha

  return {
    coreHeader: {
      tfragTextures: { count: 1, offset: tableOffset },
      mobyTextures: { count: 0, offset: 0 },
      tieTextures: { count: 0, offset: 0 },
      shrubTextures: { count: 0, offset: 0 },
      partTextures: { count: 0, offset: 0 },
      fxTextures: { count: 0, offset: 0 },
      texturesBaseOffset,
    },
    index,
    assets,
    gsRam,
  };
}

test("decodes a tfrag texture from the level-core tables", () => {
  const core = synthCore();
  const textures = readGcLevelTextures(core, "tfrag");
  assert.equal(textures.length, 1);
  const t = textures[0];
  assert.equal(t.index, 0);
  assert.equal(t.width, 2);
  assert.equal(t.height, 2);
  assert.equal(t.paletteSlot, 3);
  assert.equal(t.rgba.length, 16);

  // pixel 0 -> palette index 1 (reorder leaves 1 alone) -> red, alpha 0x80 -> 255
  assert.deepEqual([...t.rgba.slice(0, 4)], [255, 0, 0, 255]);
  // pixel 1 -> index 2 -> blue, alpha 0x40 -> 0x80
  assert.deepEqual([...t.rgba.slice(4, 8)], [0, 0, 255, 0x80]);
});

test("skips entries with an out-of-range pixel span or palette slot", () => {
  const core = synthCore();
  // point the entry's data offset past the asset blob
  new DataView(core.index.buffer).setInt32(0x80, 0x10000, true);
  assert.deepEqual(readGcLevelTextures(core, "tfrag"), []);
});

test("skips zero / oversized dimensions", () => {
  const core = synthCore();
  new DataView(core.index.buffer).setInt16(0x80 + 4, 0, true); // width 0
  assert.deepEqual(readGcLevelTextures(core, "tfrag"), []);
});

test("an empty table yields no textures", () => {
  const core = synthCore();
  core.coreHeader.tieTextures = { count: 0, offset: 0 };
  assert.deepEqual(readGcLevelTextures(core, "tie"), []);
  assert.equal(TEXTURE_ENTRY_SIZE, 16);
});
