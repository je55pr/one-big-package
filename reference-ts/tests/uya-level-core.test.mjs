import test from "node:test";
import assert from "node:assert/strict";
import { BlobRandomAccessReader } from "../.build/packages/importer-common/src/index.js";
import {
  readUyaLevelDataHeaderPublicLead,
  readUyaLevelWadHeaderPublicLead,
} from "../.build/packages/uya-level-wad/src/index.js";
import {
  UYA_PUBLIC_LEAD_LEVEL_CORE_HEADER_BYTES,
  readUyaCoreDataWadLzHeaderPublicLead,
  readUyaLevelCoreHeaderPublicLead,
} from "../.build/packages/uya-level-core/src/index.js";

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

function buildCandidateLevel() {
  const bytes = new Uint8Array(0x4000);
  const view = new DataView(bytes.buffer);

  // Public 0x60 GcUyaLevelWadHeader lead. Only the data range is needed here.
  view.setInt32(0x00, 0x60, true);
  view.setInt32(0x08, 7, true);
  view.setInt32(0x10, 1, true); // data offset sectors => 0x800
  view.setInt32(0x14, 4, true); // data size sectors => 0x2000

  // Public 0x58 GcUyaLevelDataHeader lead at the start of the data range.
  const data = 0x800;
  view.setInt32(data + 0x08, 0x100, true); // coreIndex offset within data
  view.setInt32(data + 0x0c, 0x400, true); // coreIndex size
  view.setInt32(data + 0x10, 0x500, true); // gsRam
  view.setInt32(data + 0x14, 0x80, true);
  view.setInt32(data + 0x48, 0x1000, true); // compressed coreData
  view.setInt32(data + 0x4c, 0x800, true);

  // Candidate public LevelCoreHeader at data + coreIndex = 0x900.
  // Pinned public ArrayRange is { count, offset }, matching the retail-backed GC parser.
  const core = 0x900;
  view.setInt32(core + 0x00, 3, true); // gsRam array count
  view.setInt32(core + 0x04, 0x200, true); // offset
  view.setInt32(core + 0x08, 0x1000, true); // tfrags
  view.setInt32(core + 0x0c, 0x1100, true); // occlusion
  view.setInt32(core + 0x10, 0x1200, true); // sky
  view.setInt32(core + 0x14, 0x1300, true); // collision
  view.setInt32(core + 0x18, 9, true); // mobyClasses count
  view.setInt32(core + 0x1c, 0x240, true); // mobyClasses offset
  view.setInt32(core + 0x60, 0x2000, true); // texturesBaseOffset
  view.setInt32(core + 0x84, 5, true); // moby GS stash count
  view.setInt32(core + 0x88, 0x3456, true);
  view.setInt32(core + 0x8c, 0x789a, true);
  view.setUint32(core + 0xb8, 0xfedcba98, true); // preserve exact raw word even when signed public view differs

  // Proposed coreData WAD-LZ header at data + 0x1000 = 0x1800.
  const wad = data + 0x1000;
  bytes.set([0x57, 0x41, 0x44], wad);
  view.setInt32(wad + 3, 0x123, true);
  bytes.set(new TextEncoder().encode("core\0"), wad + 7);
  return bytes;
}

test("bounded UYA public-lead path reads only fixed headers and preserves raw core words", async () => {
  const src = new RecordingReader(new BlobRandomAccessReader(new Blob([buildCandidateLevel()]), "candidate-level.wad"));
  const outer = await readUyaLevelWadHeaderPublicLead(src);
  const data = await readUyaLevelDataHeaderPublicLead(src, outer);
  const core = await readUyaLevelCoreHeaderPublicLead(src, outer, data);
  const wad = await readUyaCoreDataWadLzHeaderPublicLead(src, outer, data);

  assert.deepEqual(src.reads, [
    [0x000, 0x60],
    [0x800, 0x58],
    [0x900, 0xbc],
    [0x1800, 0x10],
  ]);
  assert.equal(core.offsetInLevelBytes, 0x900);
  assert.equal(core.headerBytes, UYA_PUBLIC_LEAD_LEVEL_CORE_HEADER_BYTES);
  assert.equal(core.rawWordsU32.length, 0xbc / 4);
  assert.equal(core.rawWordsU32[0xb8 / 4], 0xfedcba98);
  assert.deepEqual(core.publicFields.gsRam, { count: 3, offset: 0x200 });
  assert.equal(core.publicFields.tfrags, 0x1000);
  assert.equal(core.publicFields.collision, 0x1300);
  assert.deepEqual(core.publicFields.mobyClasses, { count: 9, offset: 0x240 });
  assert.equal(core.publicFields.texturesBaseOffset, 0x2000);
  assert.equal(core.publicFields.mobyGsStashCountRac23Dl, 5);
  assert.equal(core.publicFields.assetsCompressedSize, 0x3456);
  assert.equal(core.publicFields.assetsDecompressedSize, 0x789a);
  assert.equal(core.publicFields.occlusionRad2Offset, -0x01234568);
  assert.equal(core.publicSource.commit, "e67ea46e5e60ec52a0c31fcde1e3f46b20aa4cfb");

  assert.equal(wad.offsetInLevelBytes, 0x1800);
  assert.equal(wad.containingRangeBytes, 0x800);
  assert.equal(wad.compressedSize, 0x123);
  assert.equal(wad.name, "core");
});

test("core-header probe rejects a coreIndex smaller than the public 0xbc lead without an extra read", async () => {
  const bytes = buildCandidateLevel();
  const view = new DataView(bytes.buffer);
  view.setInt32(0x800 + 0x0c, 0xbb, true);
  const src = new RecordingReader(new BlobRandomAccessReader(new Blob([bytes]), "short-core-index.wad"));
  const outer = await readUyaLevelWadHeaderPublicLead(src);
  const data = await readUyaLevelDataHeaderPublicLead(src, outer);
  await assert.rejects(readUyaLevelCoreHeaderPublicLead(src, outer, data), /smaller than the public 0xbc/);
  assert.deepEqual(src.reads, [
    [0x000, 0x60],
    [0x800, 0x58],
  ]);
});

test("coreData WAD-LZ lead rejects a declared compressed block larger than its containing range", async () => {
  const bytes = buildCandidateLevel();
  const view = new DataView(bytes.buffer);
  view.setInt32(0x1800 + 3, 0x801, true);
  const src = new RecordingReader(new BlobRandomAccessReader(new Blob([bytes]), "oversize-core-data.wad"));
  const outer = await readUyaLevelWadHeaderPublicLead(src);
  const data = await readUyaLevelDataHeaderPublicLead(src, outer);
  await assert.rejects(readUyaCoreDataWadLzHeaderPublicLead(src, outer, data), /exceeds the public coreData range/);
  assert.deepEqual(src.reads, [
    [0x000, 0x60],
    [0x800, 0x58],
    [0x1800, 0x10],
  ]);
});
