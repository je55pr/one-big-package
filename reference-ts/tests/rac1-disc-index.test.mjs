import test from "node:test";
import assert from "node:assert/strict";
import { BlobRandomAccessReader } from "../.build/packages/importer-common/src/index.js";
import {
  RAC1_DISC_INDEX_LBA,
  RAC1_DISC_INDEX_SIZE,
  RAC1_DISC_SECTOR_BYTES,
  RAC1_LEVEL_TABLE_COUNT,
  RAC1_LEVEL_TABLE_OFFSET,
  RAC1_NATIVE_LEVEL_CORE_PREFIX_SIZE,
  RAC1_NATIVE_LEVEL_HEADER_SIZE,
  readRac1DiscIndex,
  readRac1LevelCatalogue,
  analyzeRac1LevelPacking,
  openRac1LevelCoreRange,
} from "../.build/packages/rac1-disc-index/src/index.js";

const SECTOR = RAC1_DISC_SECTOR_BYTES;

class RecordingReader {
  constructor(inner) {
    this.inner = inner;
    this.name = inner.name;
    this.size = inner.size;
    this.reads = [];
  }
  async read(offset, length) {
    this.reads.push([offset, length]);
    return this.inner.read(offset, length);
  }
}

function syntheticRac1Disc() {
  // The fixed index occupies LBAs 1500..1505. Put 19 tiny synthetic native
  // level groups after it: 5 header sectors + four one-sector positional ranges.
  const firstHeaderLba = 1510;
  const sectorsPerLevel = 9;
  const finalSector = firstHeaderLba + RAC1_LEVEL_TABLE_COUNT * sectorsPerLevel + 1;
  const bytes = new Uint8Array(finalSector * SECTOR);
  const view = new DataView(bytes.buffer);

  const indexBase = RAC1_DISC_INDEX_LBA * SECTOR;
  view.setInt32(indexBase + 0x00, 1, true);
  view.setInt32(indexBase + 0x04, RAC1_DISC_INDEX_SIZE, true);

  for (let i = 0; i < RAC1_LEVEL_TABLE_COUNT; i++) {
    const headerLba = firstHeaderLba + i * sectorsPerLevel;
    const tableOffset = indexBase + RAC1_LEVEL_TABLE_OFFSET + i * 8;
    view.setUint32(tableOffset, headerLba, true);
    view.setUint32(tableOffset + 4, 0x9000 + i, true); // deliberately opaque word

    const headerBase = headerLba * SECTOR;
    view.setInt32(headerBase + 0x00, i, true);
    view.setInt32(headerBase + 0x04, RAC1_NATIVE_LEVEL_HEADER_SIZE, true);
    for (let rangeSlot = 0; rangeSlot < 4; rangeSlot++) {
      const rangeLba = headerLba + 5 + rangeSlot;
      view.setUint32(headerBase + 0x08 + rangeSlot * 8, rangeLba, true);
      view.setUint32(headerBase + 0x0c + rangeSlot * 8, 1, true);
      bytes[rangeLba * SECTOR] = (i * 4 + rangeSlot + 1) & 0xff;
    }
  }

  return bytes;
}

function reader(bytes = syntheticRac1Disc()) {
  return new BlobRandomAccessReader(new Blob([bytes]), "synthetic-rac1.iso");
}

test("bounded R&C1 index/catalogue probe decodes all 19 positional native level cores", async () => {
  const src = new RecordingReader(reader());
  const catalogue = await readRac1LevelCatalogue(src);

  assert.equal(catalogue.index.version, 1);
  assert.equal(catalogue.index.declaredSize, 0x2960);
  assert.equal(catalogue.index.levelEntries.length, 19);
  assert.equal(catalogue.levels.length, 19);
  assert.deepEqual(catalogue.levels.map((level) => level.levelId), Array.from({ length: 19 }, (_, i) => i));
  assert.equal(catalogue.levels[0].tableRawSecondWord, 0x9000);
  assert.equal(catalogue.levels[18].tableRawSecondWord, 0x9012);
  assert.deepEqual(catalogue.levels[0].coreRanges.map((range) => range.fieldOffset), [0x08, 0x10, 0x18, 0x20]);

  // One 10,592-byte index read plus exactly one 40-byte prefix per level.
  assert.equal(src.reads.length, 20);
  assert.deepEqual(src.reads[0], [RAC1_DISC_INDEX_LBA * SECTOR, RAC1_DISC_INDEX_SIZE]);
  for (const read of src.reads.slice(1)) assert.equal(read[1], RAC1_NATIVE_LEVEL_CORE_PREFIX_SIZE);
  assert.equal(src.reads.reduce((sum, [, length]) => sum + length, 0), RAC1_DISC_INDEX_SIZE + 19 * RAC1_NATIVE_LEVEL_CORE_PREFIX_SIZE);
});

test("raw second level-table word is preserved without assigning semantics", async () => {
  const bytes = syntheticRac1Disc();
  const view = new DataView(bytes.buffer);
  const secondWord = RAC1_DISC_INDEX_LBA * SECTOR + RAC1_LEVEL_TABLE_OFFSET + 4;
  view.setUint32(secondWord, 0xffffffff, true);
  const index = await readRac1DiscIndex(reader(bytes));
  assert.equal(index.levelEntries[0].rawSecondWord, 0xffffffff);
});

test("packing analysis describes the retail-style contiguous invariant without making it parser authority", async () => {
  const bytes = syntheticRac1Disc();
  const catalogue = await readRac1LevelCatalogue(reader(bytes));
  const packing = analyzeRac1LevelPacking(catalogue);
  assert.equal(packing.length, 19);
  assert.ok(packing.every((entry) => entry.rangesContiguousAfterHeader));
  assert.ok(packing.slice(0, -1).every((entry) => entry.nextHeaderBeginsAtCoreEnd === true));
  assert.equal(packing.at(-1).nextHeaderBeginsAtCoreEnd, null);

  // Break one positional range while keeping it in-bounds. Parsing still works;
  // the research helper, not the codec, reports the difference.
  const firstHeaderLba = 1510;
  const headerBase = firstHeaderLba * SECTOR;
  new DataView(bytes.buffer).setUint32(headerBase + 0x10, firstHeaderLba + 7, true);
  const changed = analyzeRac1LevelPacking(await readRac1LevelCatalogue(reader(bytes)));
  assert.equal(changed[0].rangesContiguousAfterHeader, false);
});

test("decoded core range opens as a bounded sub-reader", async () => {
  const src = reader();
  const catalogue = await readRac1LevelCatalogue(src);
  const level = catalogue.levels[3];
  const range = openRac1LevelCoreRange(src, level, 2);
  assert.equal(range.size, SECTOR);
  assert.equal((await range.read(0, 1))[0], 3 * 4 + 2 + 1);
  await assert.rejects(range.read(SECTOR - 1, 2), RangeError);
  assert.throws(() => openRac1LevelCoreRange(src, level, 4), RangeError);
});

test("unknown disc-index version and size are rejected rather than guessed", async () => {
  const bytes = syntheticRac1Disc();
  const view = new DataView(bytes.buffer);
  const base = RAC1_DISC_INDEX_LBA * SECTOR;

  view.setInt32(base, 2, true);
  await assert.rejects(readRac1DiscIndex(reader(bytes)), /version 2/);

  view.setInt32(base, 1, true);
  view.setInt32(base + 4, 0x3000, true);
  await assert.rejects(readRac1DiscIndex(reader(bytes)), /declares size 0x3000/);
});

test("unexpected native level-header size is rejected", async () => {
  const bytes = syntheticRac1Disc();
  const firstHeaderLba = 1510;
  new DataView(bytes.buffer).setInt32(firstHeaderLba * SECTOR + 4, 0x999, true);
  await assert.rejects(readRac1LevelCatalogue(reader(bytes)), /header size 0x999/);
});

test("a native core range outside the source is rejected", async () => {
  const bytes = syntheticRac1Disc();
  const firstHeaderLba = 1510;
  new DataView(bytes.buffer).setUint32(firstHeaderLba * SECTOR + 0x08, 0xffffff00, true);
  await assert.rejects(readRac1LevelCatalogue(reader(bytes)), /lies outside/);
});
