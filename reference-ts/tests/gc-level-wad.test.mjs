import test from "node:test";
import assert from "node:assert/strict";
import { BlobRandomAccessReader } from "../.build/packages/importer-common/src/index.js";
import {
  GC_LEVEL_WAD_HEADER_SIZE,
  GC_LEVEL_WAD_SECTOR_BYTES,
  readGcLevelWadHeader,
  openGcLevelWadLump,
  analyzeGcLevelWadTiling,
} from "../.build/packages/gc-level-wad/src/index.js";

const SECTOR = GC_LEVEL_WAD_SECTOR_BYTES;

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

/**
 * Build a synthetic GC-style LEVEL WAD: a 0x60 header followed by `lumps`
 * ({slot, offsetSectors, sizeSectors}) laid out at their sector offsets. Total
 * size defaults to the sector-rounded end of the furthest lump minus `tailTrim`
 * bytes (mimicking the retail sub-sector tail).
 */
function syntheticGcLevelWad({ levelId = 3, unknown0x04 = 0, unknown0x0c = 0, headerSize = 0x60, lumps = [], tailTrim = 400, totalBytes } = {}) {
  const table = new Array(10).fill(null).map(() => ({ offsetSectors: 0, sizeSectors: 0 }));
  let maxEnd = SECTOR;
  for (const lump of lumps) {
    table[lump.slot] = { offsetSectors: lump.offsetSectors, sizeSectors: lump.sizeSectors };
    maxEnd = Math.max(maxEnd, (lump.offsetSectors + lump.sizeSectors) * SECTOR);
  }
  const size = totalBytes ?? Math.max(GC_LEVEL_WAD_HEADER_SIZE, maxEnd - tailTrim);
  const bytes = new Uint8Array(size);
  const view = new DataView(bytes.buffer);
  view.setUint32(0x00, headerSize, true);
  view.setUint32(0x04, unknown0x04, true);
  view.setUint32(0x08, levelId, true);
  view.setUint32(0x0c, unknown0x0c, true);
  table.forEach((entry, slot) => {
    view.setUint32(0x10 + slot * 8, entry.offsetSectors, true);
    view.setUint32(0x10 + slot * 8 + 4, entry.sizeSectors, true);
    if (entry.offsetSectors) {
      const at = entry.offsetSectors * SECTOR;
      for (let i = at; i < Math.min(size, at + 16); i++) bytes[i] = (slot + 1) & 0xff;
    }
  });
  return bytes;
}

function reader(bytes) {
  return new BlobRandomAccessReader(new Blob([bytes]), "LEVEL.WAD");
}

// Retail LEVEL1.WAD lump geometry (research/GC_LEVEL_WAD.md).
const LEVEL1_LUMPS = [
  { slot: 0, offsetSectors: 636, sizeSectors: 7700 },
  { slot: 1, offsetSectors: 1, sizeSectors: 635 },
  { slot: 2, offsetSectors: 8336, sizeSectors: 396 },
  { slot: 3, offsetSectors: 8732, sizeSectors: 9 },
  { slot: 4, offsetSectors: 8741, sizeSectors: 1519 },
  { slot: 5, offsetSectors: 10260, sizeSectors: 116 },
  { slot: 7, offsetSectors: 10376, sizeSectors: 157 },
  { slot: 8, offsetSectors: 10533, sizeSectors: 156 },
];

test("valid synthetic GC level WAD header parses fields and positional lumps", async () => {
  const bytes = syntheticGcLevelWad({ levelId: 30, unknown0x0c: 4, lumps: LEVEL1_LUMPS, tailTrim: 684 });
  const header = await readGcLevelWadHeader(reader(bytes));

  assert.equal(header.headerSize, 0x60);
  assert.equal(header.unknown0x04, 0);
  assert.equal(header.levelId, 30);
  assert.equal(header.unknown0x0c, 4);
  assert.equal(header.lumps.length, 10);

  assert.deepEqual(header.lumps.filter((l) => l.present).map((l) => l.slot), [0, 1, 2, 3, 4, 5, 7, 8]);
  assert.equal(header.lumps[6].present, false);
  assert.equal(header.lumps[9].present, false);

  const slot0 = header.lumps[0];
  assert.equal(slot0.offsetBytes, 636 * SECTOR);
  assert.equal(slot0.sizeBytes, 7700 * SECTOR);
});

test("present lumps open as bounded sub-readers; absent lumps return undefined", async () => {
  const bytes = syntheticGcLevelWad({ lumps: LEVEL1_LUMPS, tailTrim: 684 });
  const src = new RecordingReader(reader(bytes));
  const header = await readGcLevelWadHeader(src);

  assert.equal(openGcLevelWadLump(src, header, 6), undefined);

  const slot1 = openGcLevelWadLump(src, header, 1);
  assert.equal(slot1.size, 635 * SECTOR);
  const head = await slot1.read(0, 16);
  assert.equal(head[0], 2); // slot 1 marker byte

  // Only the header plus the one explicit lump read were requested.
  assert.deepEqual(src.reads, [
    [0, GC_LEVEL_WAD_HEADER_SIZE],
    [1 * SECTOR, 16],
  ]);
});

test("tail lump view is clamped to the real source size", async () => {
  const bytes = syntheticGcLevelWad({ lumps: LEVEL1_LUMPS, tailTrim: 684 });
  const src = reader(bytes);
  const header = await readGcLevelWadHeader(src);
  const tail = header.lumps[8];
  assert.equal(tail.sizeBytes, 156 * SECTOR);
  assert.equal(tail.availableBytes, src.size - tail.offsetBytes);
  assert.ok(tail.availableBytes < tail.sizeBytes);

  const view = openGcLevelWadLump(src, header, 8);
  assert.equal(view.size, tail.availableBytes);
  await assert.rejects(view.read(view.size - 1, 2), RangeError);
});

test("analyzeGcLevelWadTiling confirms the retail contiguous layout", async () => {
  const bytes = syntheticGcLevelWad({ lumps: LEVEL1_LUMPS, tailTrim: 684 });
  const header = await readGcLevelWadHeader(reader(bytes));
  const tiling = analyzeGcLevelWadTiling(header);
  assert.equal(tiling.contiguousFromSectorOne, true);
  assert.deepEqual(tiling.gaps, []);
  assert.deepEqual(tiling.overlaps, []);
  assert.ok(tiling.tailOverrunBytes > 0 && tiling.tailOverrunBytes < SECTOR);
});

test("analyzeGcLevelWadTiling reports gaps without the parser rejecting them", async () => {
  const bytes = syntheticGcLevelWad({
    lumps: [
      { slot: 1, offsetSectors: 1, sizeSectors: 10 },
      { slot: 0, offsetSectors: 20, sizeSectors: 10 },
    ],
    tailTrim: 0,
  });
  const header = await readGcLevelWadHeader(reader(bytes));
  const tiling = analyzeGcLevelWadTiling(header);
  assert.equal(tiling.contiguousFromSectorOne, false);
  assert.deepEqual(tiling.gaps, [{ afterSlot: 1, gapSectors: 9 }]);
});

test("truncated header is rejected", async () => {
  await assert.rejects(readGcLevelWadHeader(reader(new Uint8Array(0x40))), /smaller than the 96-byte header/);
});

test("non-0x60 header size is rejected (guards against other WAD families / variants)", async () => {
  const bytes = syntheticGcLevelWad({ headerSize: 0x68, lumps: LEVEL1_LUMPS });
  await assert.rejects(readGcLevelWadHeader(reader(bytes)), /header size 0x68 is not 0x60/);
});

test("a lump offset inside the header is rejected", async () => {
  const bytes = syntheticGcLevelWad({ lumps: [{ slot: 0, offsetSectors: 0, sizeSectors: 0 }] });
  const view = new DataView(bytes.buffer);
  view.setUint32(0x10, 0, true); // offsetSectors 0
  view.setUint32(0x14, 4, true); // sizeSectors 4 -> "size but no offset"
  await assert.rejects(readGcLevelWadHeader(reader(bytes)), /size but no offset/);
});

test("a lump starting past the source is rejected", async () => {
  const bytes = syntheticGcLevelWad({
    lumps: [{ slot: 1, offsetSectors: 1, sizeSectors: 4 }],
    totalBytes: 8 * SECTOR,
  });
  const view = new DataView(bytes.buffer);
  view.setUint32(0x10, 99, true); // slot 0 offset 99 sectors, well past the 8-sector file
  view.setUint32(0x14, 1, true);
  await assert.rejects(readGcLevelWadHeader(reader(bytes)), /lies past the/);
});

test("overflowing sector fields are rejected", async () => {
  const bytes = syntheticGcLevelWad({ lumps: [{ slot: 1, offsetSectors: 1, sizeSectors: 4 }], totalBytes: 8 * SECTOR });
  const view = new DataView(bytes.buffer);
  view.setUint32(0x10, 0xffffffff, true);
  view.setUint32(0x14, 0xffffffff, true);
  await assert.rejects(readGcLevelWadHeader(reader(bytes)), /overflow|lies past/);
});

test("openGcLevelWadLump rejects an out-of-range slot", async () => {
  const bytes = syntheticGcLevelWad({ lumps: LEVEL1_LUMPS, tailTrim: 684 });
  const src = reader(bytes);
  const header = await readGcLevelWadHeader(src);
  assert.throws(() => openGcLevelWadLump(src, header, 10), RangeError);
  assert.throws(() => openGcLevelWadLump(src, header, -1), RangeError);
});
