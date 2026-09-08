import test from "node:test";
import assert from "node:assert/strict";
import { readRcLevelTextureTable } from "../.build/packages/rc-level-textures/src/index.js";

test("shared RC texture table decodes linear IDTEX8 pixels through a GS RGBA32 palette", () => {
  const index = new Uint8Array(0x20);
  const iv = new DataView(index.buffer);
  iv.setInt32(0, 0, true);      // data offset
  iv.setInt16(4, 2, true);      // width
  iv.setInt16(6, 2, true);      // height
  iv.setInt16(8, 4, true);      // native type preserved
  iv.setInt16(10, 1, true);     // palette @ 0x100
  iv.setInt16(12, 7, true);
  iv.setInt16(14, 9, true);

  const assets = new Uint8Array(0x104);
  assets.set([0, 1, 8, 16], 0x100);
  const gsRam = new Uint8Array(0x500);
  const gv = new DataView(gsRam.buffer);
  // RGBA u32 is little-endian. Alpha 0x40 must scale to 0x80.
  gv.setUint32(0x100 + 0 * 4, 0x40030201, true);
  gv.setUint32(0x100 + 1 * 4, 0x80060504, true);
  // CLUT reorder swaps logical indices 8 and 16.
  gv.setUint32(0x100 + 16 * 4, 0x40090807, true);
  gv.setUint32(0x100 + 8 * 4, 0x800c0b0a, true);

  const textures = readRcLevelTextureTable(
    { index, assets, gsRam, texturesBaseOffset: 0x100 },
    { count: 1, offset: 0 },
    { strict: true },
  );
  assert.equal(textures.length, 1);
  assert.equal(textures[0].type, 4);
  assert.equal(textures[0].raw0x0c, 7);
  assert.equal(textures[0].raw0x0e, 9);
  assert.deepEqual([...textures[0].rgba], [
    1,2,3,128,
    4,5,6,255,
    7,8,9,128,
    10,11,12,255,
  ]);
});

test("strict shared texture decode rejects native ranges outside their sources", () => {
  const index = new Uint8Array(0x10);
  const iv = new DataView(index.buffer);
  iv.setInt32(0, 999, true);
  iv.setInt16(4, 64, true);
  iv.setInt16(6, 64, true);
  iv.setInt16(10, 0, true);
  assert.throws(
    () => readRcLevelTextureTable(
      { index, assets: new Uint8Array(128), gsRam: new Uint8Array(1024), texturesBaseOffset: 0 },
      { count: 1, offset: 0 },
      { strict: true },
    ),
    /pixel range/,
  );
});
