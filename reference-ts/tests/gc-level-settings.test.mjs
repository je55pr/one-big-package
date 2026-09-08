import test from "node:test";
import assert from "node:assert/strict";
import { parseGcLevelSettings, GC_LEVEL_SETTINGS_FIRST_PART_SIZE } from "../.build/packages/gc-level-settings/src/index.js";

/** Build a gameplay buffer: a pointer table whose slot 0x00 points at a level-settings first part. */
function buildGameplay(fill) {
  const headerSize = 0x100;
  const blockOffset = headerSize;
  const out = new Uint8Array(blockOffset + GC_LEVEL_SETTINGS_FIRST_PART_SIZE + 0x40);
  const dv = new DataView(out.buffer);
  dv.setInt32(0x00, blockOffset, true);
  fill(dv, blockOffset);
  return out;
}

test("parses death height, fog and background colour", () => {
  const data = buildGameplay((dv, b) => {
    dv.setInt32(b + 0x00, 6, true); dv.setInt32(b + 0x04, 16, true); dv.setInt32(b + 0x08, 12, true); // bg
    dv.setInt32(b + 0x0c, 10, true); dv.setInt32(b + 0x10, 40, true); dv.setInt32(b + 0x14, 30, true); // fog
    dv.setFloat32(b + 0x18, 25600, true); // fog near dist
    dv.setFloat32(b + 0x1c, 230400, true); // fog far dist
    dv.setFloat32(b + 0x28, 0, true); // death height
    dv.setInt32(b + 0x2c, 0, true); // spherical
    dv.setFloat32(b + 0x3c, 278.05, true); // ship pos x
  });

  const s = parseGcLevelSettings(data);
  assert.deepEqual(s.backgroundColour?.map((v) => Math.round(v * 255)), [6, 16, 12]);
  assert.deepEqual(s.fogColour?.map((v) => Math.round(v * 255)), [10, 40, 30]);
  assert.equal(s.fogNearDistance, 25600);
  assert.equal(s.fogFarDistance, 230400);
  assert.equal(s.deathHeight, 0);
  assert.equal(s.isSphericalWorld, false);
  assert.ok(Math.abs(s.shipPosition[0] - 278.05) < 0.01);
});

test("an unset colour (r == -1) becomes null", () => {
  const data = buildGameplay((dv, b) => {
    dv.setInt32(b + 0x00, -1, true);
    dv.setInt32(b + 0x0c, -1, true);
    dv.setFloat32(b + 0x28, 110, true);
  });
  const s = parseGcLevelSettings(data);
  assert.equal(s.backgroundColour, null);
  assert.equal(s.fogColour, null);
  assert.equal(s.deathHeight, 110);
});

test("spherical world exposes the sphere centre", () => {
  const data = buildGameplay((dv, b) => {
    dv.setInt32(b + 0x2c, 1, true);
    dv.setFloat32(b + 0x30, 1, true); dv.setFloat32(b + 0x34, 2, true); dv.setFloat32(b + 0x38, 3, true);
  });
  const s = parseGcLevelSettings(data);
  assert.equal(s.isSphericalWorld, true);
  assert.deepEqual(s.sphereCentre, [1, 2, 3]);
});

test("a bad block pointer is rejected", () => {
  const data = new Uint8Array(0x40);
  new DataView(data.buffer).setInt32(0, 0x1000, true); // past the end
  assert.throws(() => parseGcLevelSettings(data), /out of range/);
});
