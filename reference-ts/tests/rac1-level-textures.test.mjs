import test from "node:test";
import assert from "node:assert/strict";
import { readRac1TextureTable } from "../.build/packages/rac1-level-textures/src/index.js";

function fixture() {
  const index = new Uint8Array(0x100);
  const view = new DataView(index.buffer);
  const tfragTable = 0x80;
  const tieTable = 0x90;
  view.setInt32(0x30, 1, true); view.setInt32(0x34, tfragTable, true);
  view.setInt32(0x40, 1, true); view.setInt32(0x44, tieTable, true);
  const writeEntry = (at, dataOffset, paletteSlot) => {
    view.setInt32(at, dataOffset, true);
    view.setInt16(at + 4, 2, true);
    view.setInt16(at + 6, 2, true);
    view.setInt16(at + 8, 0, true);
    view.setInt16(at + 10, paletteSlot, true);
  };
  writeEntry(tfragTable, 0, 0);
  writeEntry(tieTable, 4, 1);

  const assets = new Uint8Array(16);
  assets.set([0, 0, 0, 0], 0);
  assets.set([1, 1, 1, 1], 4);
  const gsRam = new Uint8Array(0x800);
  const gs = new DataView(gsRam.buffer);
  gs.setUint32(0, 0x800000ff, true);
  gs.setUint32(0x100 + 4, 0x8000ff00, true);
  return { index, assets, gsRam };
}

test("R&C1 generic texture-table path selects the requested native family", () => {
  const { index, assets, gsRam } = fixture();
  const tfrag = readRac1TextureTable(index, assets, gsRam, 0, "tfrag");
  const tie = readRac1TextureTable(index, assets, gsRam, 0, "tie");
  assert.equal(tfrag.kind, "tfrag");
  assert.equal(tie.kind, "tie");
  assert.equal(tfrag.textures[0].dataOffset, 0);
  assert.equal(tie.textures[0].dataOffset, 4);
  assert.equal(tie.textures[0].paletteSlot, 1);
});
