import test from "node:test";
import assert from "node:assert/strict";
import { BlobRandomAccessReader } from "../.build/packages/importer-common/src/index.js";
import {
  UYA_DISC_SECTOR_BYTES,
  UYA_PUBLIC_TOC_LBA_HINT,
  UYA_PUBLIC_TOC_WINDOW_BYTES_HINT,
} from "../.build/packages/uya-disc-toc/src/index.js";
import { probeUyaWorldCandidatePublicLead } from "../.build/packages/uya-world-probe/src/index.js";

const SECTOR = UYA_DISC_SECTOR_BYTES;
const TOC_LBA = UYA_PUBLIC_TOC_LBA_HINT;
const PAYLOAD_LBA = 2500;

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

function putTocHeader(view, tocBase, relativeSector, headerSize, payloadLba) {
  const at = tocBase + relativeSector * SECTOR;
  view.setInt32(at, headerSize, true);
  view.setUint32(at + 4, payloadLba, true);
}

function syntheticDisc() {
  const payloadSectors = 8;
  const bytes = new Uint8Array((PAYLOAD_LBA + payloadSectors + 1) * SECTOR);
  const view = new DataView(bytes.buffer);
  const tocBase = TOC_LBA * SECTOR;

  // Resident/global-looking headers tile to the candidate sparse level table at +0x3e0.
  view.setInt32(tocBase + 0x000, 0x048, true);
  view.setUint32(tocBase + 0x004, 0x1234, true);
  view.setInt32(tocBase + 0x048, 0x398, true);
  view.setUint32(tocBase + 0x04c, 0x2345, true);
  const table = tocBase + 0x3e0;

  // UYA public lead order is deliberately discovered by pointed header shape, not assigned by slot.
  view.setUint32(table + 0x00, TOC_LBA + 8, true); // scene-looking resident header
  view.setUint32(table + 0x04, 3, true);
  view.setUint32(table + 0x08, TOC_LBA + 2, true); // main level-looking resident header
  view.setUint32(table + 0x0c, payloadSectors, true);
  view.setUint32(table + 0x10, TOC_LBA + 16, true); // audio-looking resident header
  view.setUint32(table + 0x14, 4, true);
  putTocHeader(view, tocBase, 2, 0x60, PAYLOAD_LBA);
  putTocHeader(view, tocBase, 8, 0x26f0, 2600);
  putTocHeader(view, tocBase, 16, 0x1818, 2700);

  // Candidate 0x60 outer level header at the payload LBA.
  const level = PAYLOAD_LBA * SECTOR;
  view.setInt32(level + 0x00, 0x60, true);
  view.setInt32(level + 0x08, 7, true);
  view.setInt32(level + 0x10, 1, true); // data section at +0x800
  view.setInt32(level + 0x14, 4, true); // 0x2000 bytes

  // Candidate 0x58 data header.
  const data = level + 0x800;
  view.setInt32(data + 0x08, 0x100, true); // coreIndex
  view.setInt32(data + 0x0c, 0x400, true);
  view.setInt32(data + 0x10, 0x500, true); // gsRam
  view.setInt32(data + 0x14, 0x80, true);
  view.setInt32(data + 0x48, 0x1000, true); // coreData
  view.setInt32(data + 0x4c, 0x800, true);

  // Candidate 0xbc core header directly in coreIndex.
  const core = data + 0x100;
  view.setInt32(core + 0x08, 0x1000, true); // tfrags public hint
  view.setInt32(core + 0x14, 0x1300, true); // collision public hint
  view.setInt32(core + 0x88, 0x123, true); // public compressed-size hint
  view.setInt32(core + 0x8c, 0x456, true);

  // Candidate coreData WAD-LZ header; no compressed payload is needed for this probe.
  const wad = data + 0x1000;
  bytes.set([0x57, 0x41, 0x44], wad);
  view.setInt32(wad + 3, 0x123, true);
  bytes.set(new TextEncoder().encode("coredata\0"), wad + 7);
  return bytes;
}

test("vertical UYA public-lead probe composes ToC -> level -> data -> core -> WAD header without decompression", async () => {
  const src = new RecordingReader(new BlobRandomAccessReader(new Blob([syntheticDisc()]), "uya-synthetic.iso"));
  const result = await probeUyaWorldCandidatePublicLead(src, { tableIndex: 0, maxLevelRows: 4 });

  assert.equal(result.selectedTableIndex, 0);
  assert.equal(result.selectedMainPart.slot, 1);
  assert.equal(result.selectedMainPart.headerSize, 0x60);
  assert.equal(result.levelPayload.outer.publicLevelIdHint, 7);
  assert.equal(result.levelPayload.core.publicFields.collision, 0x1300);
  assert.equal(result.levelPayload.coreDataWadLz.compressedSize, 0x123);
  assert.equal(result.levelPayload.compatibilityChecks.coreCompressedSizeMatchesWadHeader, true);

  const level = PAYLOAD_LBA * SECTOR;
  assert.deepEqual(src.reads, [
    [TOC_LBA * SECTOR, UYA_PUBLIC_TOC_WINDOW_BYTES_HINT],
    [level + 0x000, 0x60],
    [level + 0x800, 0x58],
    [level + 0x900, 0xbc],
    [level + 0x1800, 0x10],
  ]);
});

test("vertical probe keeps sparse table index distinct from proposed native level ID", async () => {
  const bytes = syntheticDisc();
  const view = new DataView(bytes.buffer);
  const tocBase = TOC_LBA * SECTOR;
  const table = tocBase + 0x3e0;
  const row3 = table + 3 * 24;
  view.setUint32(row3 + 0x08, TOC_LBA + 24, true);
  view.setUint32(row3 + 0x0c, 8, true);
  putTocHeader(view, tocBase, 24, 0x60, PAYLOAD_LBA);

  const result = await probeUyaWorldCandidatePublicLead(
    new BlobRandomAccessReader(new Blob([bytes]), "uya-sparse.iso"),
    { tableIndex: 3, maxLevelRows: 8 },
  );
  assert.equal(result.selectedTableIndex, 3);
  assert.equal(result.levelPayload.outer.publicLevelIdHint, 7);
  assert.notEqual(result.selectedTableIndex, result.levelPayload.outer.publicLevelIdHint);
});
