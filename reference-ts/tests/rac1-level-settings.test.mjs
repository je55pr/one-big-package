import test from "node:test";
import assert from "node:assert/strict";
import {
  RAC1_LEVEL_SETTINGS_FIRST_PART_SIZE,
  parseRac1LevelSettings,
} from "../.build/packages/rac1-level-settings/src/index.js";

function fixture() {
  const block = 0x80;
  const bytes = new Uint8Array(block + RAC1_LEVEL_SETTINGS_FIRST_PART_SIZE);
  const view = new DataView(bytes.buffer);
  view.setInt32(0, block, true);
  [71, 66, 58].forEach((value, i) => view.setInt32(block + i * 4, value, true));
  [47, 26, 15].forEach((value, i) => view.setInt32(block + 0x0c + i * 4, value, true));
  view.setFloat32(block + 0x18, 512, true);
  view.setFloat32(block + 0x1c, 240640, true);
  view.setFloat32(block + 0x20, 255, true);
  view.setFloat32(block + 0x24, 53.55, true);
  view.setFloat32(block + 0x28, 27, true);
  view.setFloat32(block + 0x2c, 20, true);
  view.setFloat32(block + 0x30, 21, true);
  view.setFloat32(block + 0x34, 22, true);
  view.setFloat32(block + 0x38, Math.PI / 2, true);
  view.setInt32(block + 0x3c, 75, true);
  view.setInt32(block + 0x40, 70, true);
  view.setInt32(block + 0x44, 69, true);
  return bytes;
}

test("R&C1 level settings decode the 0x50-byte RAC generation", () => {
  const settings = parseRac1LevelSettings(fixture());
  assert.equal(RAC1_LEVEL_SETTINGS_FIRST_PART_SIZE, 0x50);
  assert.deepEqual(settings.backgroundColour.map((v) => Math.round(v * 255)), [71, 66, 58]);
  assert.deepEqual(settings.fogColour.map((v) => Math.round(v * 255)), [47, 26, 15]);
  assert.equal(settings.fogNearDistance, 512);
  assert.equal(settings.fogFarDistance, 240640);
  assert.equal(settings.deathHeight, 27);
  assert.deepEqual(settings.shipPosition, [20, 21, 22]);
  assert.equal(settings.shipPath, 75);
  assert.deepEqual(settings.rawPadWords, [0, 0]);
});

test("R&C1 level settings preserve native unset RGB sentinel", () => {
  const bytes = fixture();
  new DataView(bytes.buffer).setInt32(0x80, -1, true);
  assert.equal(parseRac1LevelSettings(bytes).backgroundColour, null);
});

test("R&C1 level settings reject GC-sized assumptions hidden behind a bad pointer", () => {
  const bytes = fixture().subarray(0, 0x80 + 0x4f);
  assert.throws(() => parseRac1LevelSettings(bytes), /out of range/);
});
