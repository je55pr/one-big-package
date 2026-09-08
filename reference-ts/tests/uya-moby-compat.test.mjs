import test from "node:test";
import assert from "node:assert/strict";
import {
  censusUyaMobyClassPrerequisites,
  censusUyaMobyInstancePrerequisites,
} from "../.build/packages/uya-moby-compat/src/index.js";

test("UYA Moby class census exposes candidate offsets and duplicate oClasses", () => {
  const index = new Uint8Array(0x100);
  const view = new DataView(index.buffer);
  // Two 0x20 entries at 0x20, both structurally valid but same oClass.
  view.setInt32(0x20, 0x80, true);
  view.setInt32(0x24, 123, true);
  view.setInt32(0x40, 0x100, true);
  view.setInt32(0x44, 123, true);
  const assets = new Uint8Array(0x400);
  const result = censusUyaMobyClassPrerequisites(index, assets, { count: 2, offset: 0x20 });
  assert.equal(result.tableWithinCoreIndex, true);
  assert.equal(result.invalidAssetOffsetCount, 0);
  assert.equal(result.classHeaderOutsideAssetCount, 0);
  assert.equal(result.candidateEntryCount, 2);
  assert.equal(result.candidateUniqueOClassCount, 1);
  assert.equal(result.duplicateOClassCount, 1);
});

test("UYA Moby instance census validates full 0x88 records and finite transforms", () => {
  const gameplay = new Uint8Array(0x400);
  const view = new DataView(gameplay.buffer);
  view.setInt32(0x4c, 0x100, true);
  view.setInt32(0x100, 2, true);
  for (let i = 0; i < 2; i++) {
    const at = 0x110 + i * 0x88;
    view.setInt32(at, 0x88, true);
    view.setInt32(at + 0x28, 200 + i, true);
    view.setFloat32(at + 0x2c, 1 + i, true);
    view.setFloat32(at + 0x40, 10 + i, true);
    view.setFloat32(at + 0x44, 20 + i, true);
    view.setFloat32(at + 0x48, 30 + i, true);
    view.setFloat32(at + 0x4c, 0.1 * i, true);
    view.setFloat32(at + 0x50, 0.2 * i, true);
    view.setFloat32(at + 0x54, 0.3 * i, true);
  }
  const result = censusUyaMobyInstancePrerequisites(gameplay);
  assert.equal(result.pointerReadable, true);
  assert.equal(result.blockHeaderReadable, true);
  assert.equal(result.declaredCount, 2);
  assert.equal(result.fullDeclaredSpanWithinGameplay, true);
  assert.equal(result.fullStructReadableCount, 2);
  assert.equal(result.wrongSizeFieldCount, 0);
  assert.equal(result.nonFiniteScaleCount, 0);
  assert.equal(result.nonFinitePositionComponentCount, 0);
  assert.equal(result.nonFiniteRotationComponentCount, 0);
  assert.equal(result.completeFiniteEntryCount, 2);
});

test("UYA Moby instance census keeps malformed size/non-finite evidence visible", () => {
  const gameplay = new Uint8Array(0x200);
  const view = new DataView(gameplay.buffer);
  view.setInt32(0x4c, 0x80, true);
  view.setInt32(0x80, 1, true);
  const at = 0x90;
  view.setInt32(at, 0x70, true);
  view.setFloat32(at + 0x2c, Number.NaN, true);
  view.setFloat32(at + 0x40, Number.POSITIVE_INFINITY, true);
  const result = censusUyaMobyInstancePrerequisites(gameplay);
  assert.equal(result.fullStructReadableCount, 1);
  assert.equal(result.wrongSizeFieldCount, 1);
  assert.equal(result.nonFiniteScaleCount, 1);
  assert.equal(result.nonFinitePositionComponentCount, 1);
  assert.equal(result.completeFiniteEntryCount, 0);
});
