import test from "node:test";
import assert from "node:assert/strict";
import {
  BlobRandomAccessReader,
  MappedRandomAccessReader,
} from "../.build/packages/importer-common/src/index.js";

function blob(bytes, name) {
  return new BlobRandomAccessReader(new Blob([Uint8Array.from(bytes)]), name);
}

test("mapped reader exposes exact logical extents without shifting holes", async () => {
  const reader = new MappedRandomAccessReader([
    { start: 0, reader: blob([10, 11, 12, 13], "part1") },
    { start: 8, reader: blob([20, 21, 22], "part3") },
  ], 16, "sparse-disc");

  assert.deepEqual([...await reader.read(1, 2)], [11, 12]);
  assert.deepEqual([...await reader.read(8, 3)], [20, 21, 22]);
  await assert.rejects(reader.read(4, 1), /Unmapped read/);
  await assert.rejects(reader.read(3, 6), /Unmapped read/);
});

test("mapped reader may cross only directly adjacent extents", async () => {
  const reader = new MappedRandomAccessReader([
    { start: 2, reader: blob([1, 2], "left") },
    { start: 4, reader: blob([3, 4, 5], "right") },
  ], 8, "adjacent");
  assert.deepEqual([...await reader.read(3, 4)], [2, 3, 4, 5]);
});

test("mapped reader supports bounded slices of an underlying reader", async () => {
  const source = blob([90, 91, 92, 93, 94], "source");
  const reader = new MappedRandomAccessReader([
    { start: 10, reader: source, readerOffset: 1, length: 3 },
  ], 20, "slice");
  assert.deepEqual([...await reader.read(10, 3)], [91, 92, 93]);
});

test("mapped reader rejects overlapping and out-of-range extents", () => {
  const a = blob([1, 2, 3], "a");
  const b = blob([4, 5], "b");
  assert.throws(() => new MappedRandomAccessReader([
    { start: 1, reader: a },
    { start: 3, reader: b },
  ], 10), /overlap/);
  assert.throws(() => new MappedRandomAccessReader([
    { start: 9, reader: b },
  ], 10), /outside logical source size/);
  assert.throws(() => new MappedRandomAccessReader([
    { start: 0, reader: a, readerOffset: 2, length: 2 },
  ], 10), /Invalid mapped length/);
});
