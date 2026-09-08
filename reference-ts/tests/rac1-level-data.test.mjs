import test from "node:test";
import assert from "node:assert/strict";
import { BlobRandomAccessReader } from "../.build/packages/importer-common/src/index.js";
import {
  RAC1_LEVEL_DATA_CORE_DATA_SLOT,
  RAC1_LEVEL_DATA_CORE_INDEX_SLOT,
  readRac1LevelDataDirectory,
  analyzeRac1LevelDataPacking,
  openRac1LevelDataRange,
} from "../.build/packages/rac1-level-data/src/index.js";

function syntheticDirectory() {
  const bytes = new Uint8Array(0x1000);
  const view = new DataView(bytes.buffer);
  let offset = 0x80;
  for (let slot = 0; slot < 11; slot++) {
    const size = slot === 7 ? 0 : 0x40 + slot;
    if (size === 0) {
      view.setInt32(slot * 8, -1, true);
      view.setInt32(slot * 8 + 4, 0, true);
      continue;
    }
    offset = (offset + 0x3f) & ~0x3f;
    view.setInt32(slot * 8, offset, true);
    view.setInt32(slot * 8 + 4, size, true);
    bytes[offset] = slot + 1;
    offset += size;
  }
  return bytes;
}

function reader(bytes = syntheticDirectory()) {
  return new BlobRandomAccessReader(new Blob([bytes]), "rac1-level-data");
}

test("R&C1 level-data directory preserves 11 positional byte ranges", async () => {
  const directory = await readRac1LevelDataDirectory(reader());
  assert.equal(directory.ranges.length, 11);
  assert.equal(directory.ranges[7].present, false);
  assert.equal(directory.ranges[RAC1_LEVEL_DATA_CORE_INDEX_SLOT].slot, 2);
  assert.equal(directory.ranges[RAC1_LEVEL_DATA_CORE_DATA_SLOT].slot, 10);

  const packing = analyzeRac1LevelDataPacking(directory);
  assert.equal(packing.allOffsetsAligned40, true);
  assert.equal(packing.nonOverlapping, true);
  assert.equal(packing.firstPresentOffset, 0x80);
  assert.ok(packing.gaps.length > 0);
});

test("level-data ranges open as bounded sub-readers", async () => {
  const src = reader();
  const directory = await readRac1LevelDataDirectory(src);
  const range = openRac1LevelDataRange(src, directory, 2);
  assert.equal((await range.read(0, 1))[0], 3);
  await assert.rejects(range.read(range.size - 1, 2), RangeError);
  assert.equal(openRac1LevelDataRange(src, directory, 7), undefined);
  assert.throws(() => openRac1LevelDataRange(src, directory, 11), RangeError);
});

test("out-of-bounds or unexplained zero-size directory entries are rejected", async () => {
  const bytes = syntheticDirectory();
  const view = new DataView(bytes.buffer);
  view.setInt32(0, bytes.length - 4, true);
  view.setInt32(4, 16, true);
  await assert.rejects(readRac1LevelDataDirectory(reader(bytes)), /lies outside/);

  const bytes2 = syntheticDirectory();
  const view2 = new DataView(bytes2.buffer);
  view2.setInt32(7 * 8, 123, true);
  await assert.rejects(readRac1LevelDataDirectory(reader(bytes2)), /zero size but unexpected offset/);
});
