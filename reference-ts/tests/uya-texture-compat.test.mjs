import test from "node:test";
import assert from "node:assert/strict";
import {
  censusGcTextureReaderPrerequisites,
  probeUyaGcTextureCompatibilityPublicLead,
} from "../.build/packages/uya-texture-compat/src/index.js";

function syntheticFixture() {
  const index = new Uint8Array(0x200);
  const iv = new DataView(index.buffer);
  const tableOffset = 0x80;
  const texturesBaseOffset = 0x40;

  // Texture 0: valid 2x2 paletted texture.
  iv.setInt32(tableOffset + 0x00, 0, true);
  iv.setInt16(tableOffset + 0x04, 2, true);
  iv.setInt16(tableOffset + 0x06, 2, true);
  iv.setInt16(tableOffset + 0x08, 3, true);
  iv.setInt16(tableOffset + 0x0a, 0, true);

  // Texture 1: deliberately invalid dimensions, matching the GC reader's first skip gate.
  const second = tableOffset + 0x10;
  iv.setInt32(second + 0x00, 4, true);
  iv.setInt16(second + 0x04, 0, true);
  iv.setInt16(second + 0x06, 2, true);
  iv.setInt16(second + 0x08, 7, true);
  iv.setInt16(second + 0x0a, 0, true);

  const assets = new Uint8Array(0x100);
  assets.set([1, 2, 2, 1], texturesBaseOffset);
  const gsRam = new Uint8Array(0x400);
  const gv = new DataView(gsRam.buffer);
  gv.setUint32(1 * 4, 0x800000ff, true);
  gv.setUint32(2 * 4, 0x80ff0000, true);

  const range = { count: 2, offset: tableOffset };
  const publicFields = {
    tfragTextures: range,
    mobyTextures: { count: 0, offset: 0 },
    tieTextures: { count: 0, offset: 0 },
    shrubTextures: { count: 0, offset: 0 },
    partTextures: { count: 0, offset: 0 },
    fxTextures: { count: 0, offset: 0 },
    texturesBaseOffset,
  };
  const decoded = { coreIndex: index, assets };
  return { index, assets, gsRam, range, publicFields, decoded, texturesBaseOffset };
}

test("censuses the unchanged GC texture reader prerequisites", () => {
  const f = syntheticFixture();
  const census = censusGcTextureReaderPrerequisites(f.index, f.assets, f.gsRam, f.range, f.texturesBaseOffset);
  assert.equal(census.declaredTextureCount, 2);
  assert.equal(census.tableWithinCoreIndex, true);
  assert.equal(census.invalidDimensionCount, 1);
  assert.equal(census.invalidPixelRangeCount, 0);
  assert.equal(census.invalidPaletteRangeCount, 0);
  assert.equal(census.parserEligibleEntryCount, 1);
  assert.deepEqual(census.parserEligibleIndices, [0]);
  assert.deepEqual(census.typeIds, [3, 7]);
});

test("unchanged GC texture parser output matches the independent eligibility census", () => {
  const f = syntheticFixture();
  const result = probeUyaGcTextureCompatibilityPublicLead(f.decoded, f.publicFields, f.gsRam, "tfrag");
  assert.equal(result.parserDecodedTextureCount, 1);
  assert.deepEqual(result.parserDecodedIndices, [0]);
  assert.equal(result.parserDecodedAllEligibleEntries, true);
  assert.equal(result.decodedRgbaByteLength, 16);
  assert.equal(result.decodedRgbaSha256.length, 64);
});
