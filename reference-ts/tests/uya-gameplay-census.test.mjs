import test from "node:test";
import assert from "node:assert/strict";
import { censusUyaGameplayHeaderBlocks } from "../.build/packages/uya-gameplay-census/src/index.js";

test("censuses all 32 gameplay header slots without assigning block semantics", () => {
  const data = new Uint8Array(0x200);
  const view = new DataView(data.buffer);
  view.setInt32(0x00, 0x100, true);
  view.setInt32(0x04, 0x140, true);
  view.setInt32(0x08, 0x100, true);
  view.setInt32(0x100, 3, true);
  view.setInt32(0x140, 7, true);

  const census = censusUyaGameplayHeaderBlocks(data);
  assert.equal(census.slots.length, 32);
  assert.equal(census.nonZeroSlotCount, 3);
  assert.equal(census.uniquePointerCount, 2);
  assert.equal(census.slots[0].apparentExtentBytes, 0x40);
  assert.deepEqual(census.slots[0].aliasSlotIndices, [0, 2]);
  assert.equal(census.slots[1].apparentExtentBytes, 0xc0);
  assert.equal(census.slots[3].present, false);
});

test("rejects a nonzero top-level pointer outside the gameplay lump", () => {
  const data = new Uint8Array(0x100);
  new DataView(data.buffer).setInt32(0x10, 0x200, true);
  assert.throws(() => censusUyaGameplayHeaderBlocks(data), /slot 4 pointer/);
});
