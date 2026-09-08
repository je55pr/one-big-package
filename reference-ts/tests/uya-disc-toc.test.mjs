import test from "node:test";
import assert from "node:assert/strict";
import { BlobRandomAccessReader } from "../.build/packages/importer-common/src/index.js";
import {
  UYA_DISC_SECTOR_BYTES,
  UYA_PUBLIC_TOC_LBA_HINT,
  analyzeUyaTocWindow,
  probeUyaDiscToc,
  publicWrenchFormatHint,
} from "../.build/packages/uya-disc-toc/src/index.js";

const SECTOR = UYA_DISC_SECTOR_BYTES;
const TOC_LBA = UYA_PUBLIC_TOC_LBA_HINT;

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

function putHeader(view, relativeSector, headerSize, rawWordAt04) {
  const offset = relativeSector * SECTOR;
  view.setInt32(offset, headerSize, true);
  view.setUint32(offset + 4, rawWordAt04, true);
}

function syntheticTocWindow() {
  const bytes = new Uint8Array(0x20000);
  const view = new DataView(bytes.buffer);

  // Two resident/global-looking headers. Their sizes place the candidate level table at 0x3e0.
  view.setInt32(0x000, 0x048, true);
  view.setUint32(0x004, 0x1234, true);
  view.setInt32(0x048, 0x398, true);
  view.setUint32(0x04c, 0x2345, true);
  const table = 0x3e0;

  // Preserve the raw 3-slot order rather than assuming level/audio/scene ordering.
  const row0 = table;
  view.setUint32(row0 + 0x00, TOC_LBA + 8, true); // public scene-size hint
  view.setUint32(row0 + 0x04, 11, true);
  view.setUint32(row0 + 0x08, TOC_LBA + 2, true); // public level-size hint
  view.setUint32(row0 + 0x0c, 22, true);
  view.setUint32(row0 + 0x10, TOC_LBA + 16, true); // public UYA audio-size hint
  view.setUint32(row0 + 0x14, 33, true);

  // Leave rows 1-2 empty so sparse native indices remain observable.
  const row3 = table + 3 * 24;
  view.setUint32(row3 + 0x00, TOC_LBA + 24, true);
  view.setUint32(row3 + 0x04, 44, true);

  putHeader(view, 2, 0x60, 0x5000);
  putHeader(view, 8, 0x26f0, 0x6000);
  putHeader(view, 16, 0x1818, 0x7000);
  putHeader(view, 24, 0x77, 0x8000);
  return bytes;
}

test("synthetic UYA TOC window preserves raw globals, sparse row indices and slot order", () => {
  const result = analyzeUyaTocWindow(syntheticTocWindow(), { tocLba: TOC_LBA, maxLevelRows: 8 });
  assert.deepEqual(result.globalHeaders, [
    { index: 0, offsetBytes: 0, headerSize: 0x48, rawWordAt0x04: 0x1234 },
    { index: 1, offsetBytes: 0x48, headerSize: 0x398, rawWordAt0x04: 0x2345 },
  ]);
  assert.equal(result.levelTableDiscovery, "fallback-leading-header-chain");
  assert.equal(result.levelTableOffsetBytes, 0x3e0);
  assert.deepEqual(result.levelRows.map((row) => row.index), [0, 3]);

  const row0 = result.levelRows[0];
  assert.equal(row0.parts[0].headerSize, 0x26f0);
  assert.equal(row0.parts[0].publicFormatHint.label, "level-scene");
  assert.equal(row0.parts[1].headerSize, 0x60);
  assert.equal(row0.parts[1].publicFormatHint.label, "level");
  assert.equal(row0.parts[2].headerSize, 0x1818);
  assert.equal(row0.parts[2].publicFormatHint.label, "level-audio");
  assert.equal(row0.parts[1].rawHeaderWordAt0x04, 0x5000);
  assert.match(result.warnings.join(" "), /No level-table offset matched/);
});

test("disc probe performs exactly one bounded read at the candidate LBA", async () => {
  const toc = syntheticTocWindow();
  const prefix = new Uint8Array(TOC_LBA * SECTOR);
  const src = new RecordingReader(new BlobRandomAccessReader(new Blob([prefix, toc]), "uya.iso.001"));
  const result = await probeUyaDiscToc(src, { windowBytes: toc.length, maxLevelRows: 8 });
  assert.equal(result.readOffsetBytes, TOC_LBA * SECTOR);
  assert.equal(result.windowBytes, toc.length);
  assert.deepEqual(src.reads, [[TOC_LBA * SECTOR, toc.length]]);
});

test("invalid pointed headers are retained as raw evidence with issues instead of forced types", () => {
  const bytes = syntheticTocWindow();
  const view = new DataView(bytes.buffer);
  view.setInt32(2 * SECTOR, 0x7fffffff, true);
  const result = analyzeUyaTocWindow(bytes, { tocLba: TOC_LBA, maxLevelRows: 4 });
  const part = result.levelRows[0].parts[1];
  assert.equal(part.headerSize, 0x7fffffff);
  assert.equal(part.publicFormatHint, undefined);
  assert.match(part.issues.join(" "), /outside the conservative/);
});

test("public Wrench labels remain explicit hints and unknown sizes remain unknown", () => {
  const hint = publicWrenchFormatHint(0x1818);
  assert.equal(hint.label, "level-audio");
  assert.equal(hint.gameHint, "uya");
  assert.equal(hint.source.commit, "e67ea46e5e60ec52a0c31fcde1e3f46b20aa4cfb");
  assert.equal(publicWrenchFormatHint(0x1234), undefined);
});

test("truncated source before candidate LBA is rejected without reads", async () => {
  const src = new RecordingReader(new BlobRandomAccessReader(new Blob([new Uint8Array(100)]), "tiny.bin"));
  await assert.rejects(probeUyaDiscToc(src), /begins past/);
  assert.deepEqual(src.reads, []);
});
