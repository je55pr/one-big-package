import test from "node:test";
import assert from "node:assert/strict";
import { BlobRandomAccessReader } from "../.build/packages/importer-common/src/index.js";
import {
  RAC234_DISC_SECTOR_BYTES,
  RAC234_PUBLIC_LEAD_TOC_LBA,
  RAC234_PUBLIC_LEAD_TOC_MAX_BYTES,
  UYA_PUBLIC_LEAD_LEVEL_PARTS,
  readRac234TocProbeWindow,
  scanRac234LevelTableStructuralCandidates,
  filterUyaLevelTableCandidatesByPublicLead,
  parseRac234LevelTableEntry,
  interpretUyaLevelTableEntryPublicLead,
  parseRac234ResidentGlobalHeaders,
} from "../.build/packages/uya-disc/src/index.js";

const SECTOR = RAC234_DISC_SECTOR_BYTES;
const TOC_LBA = RAC234_PUBLIC_LEAD_TOC_LBA;

function putU32(bytes, offset, value) {
  new DataView(bytes.buffer, bytes.byteOffset + offset, 4).setUint32(0, value, true);
}

function putResidentHeader(bytes, offset, headerSize, fileLba) {
  putU32(bytes, offset, headerSize);
  putU32(bytes, offset + 4, fileLba);
}

function putPointedHeader(bytes, headerLba, headerSize, fileLba) {
  const offset = (headerLba - TOC_LBA) * SECTOR;
  putU32(bytes, offset, headerSize);
  putU32(bytes, offset + 4, fileLba);
}

function putLevelEntry(bytes, offset, parts) {
  for (let i = 0; i < 3; i++) {
    putU32(bytes, offset + i * 8, parts[i].headerLba);
    putU32(bytes, offset + i * 8 + 4, parts[i].sizeSectors);
  }
}

function syntheticUyaLeadWindow() {
  const bytes = new Uint8Array(0x18000);

  // A synthetic resident global-header prefix. These sizes are arbitrary and do not claim UYA semantics.
  putResidentHeader(bytes, 0x00, 0x48, 5000);
  putResidentHeader(bytes, 0x48, 0x40, 5100);
  const tableOffset = 0x88;

  // Two physical table entries in the public UYA lead order: audio, level, scene.
  const entries = [
    [
      { headerLba: TOC_LBA + 2, sizeSectors: 91 },
      { headerLba: TOC_LBA + 4, sizeSectors: 121 },
      { headerLba: TOC_LBA + 6, sizeSectors: 31 },
    ],
    [
      { headerLba: TOC_LBA + 8, sizeSectors: 92 },
      { headerLba: TOC_LBA + 10, sizeSectors: 122 },
      { headerLba: TOC_LBA + 12, sizeSectors: 32 },
    ],
  ];
  putLevelEntry(bytes, tableOffset, entries[0]);
  putLevelEntry(bytes, tableOffset + 0x18, entries[1]);

  for (const parts of entries) {
    putPointedHeader(bytes, parts[0].headerLba, UYA_PUBLIC_LEAD_LEVEL_PARTS[0].headerSize, 7000 + parts[0].sizeSectors);
    putPointedHeader(bytes, parts[1].headerLba, UYA_PUBLIC_LEAD_LEVEL_PARTS[1].headerSize, 8000 + parts[1].sizeSectors);
    putPointedHeader(bytes, parts[2].headerLba, UYA_PUBLIC_LEAD_LEVEL_PARTS[2].headerSize, 9000 + parts[2].sizeSectors);
  }

  return { bytes, tableOffset, entries };
}

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

test("bounded ToC probe reads only the candidate 2 MiB window", async () => {
  const tocOffset = TOC_LBA * SECTOR;
  const totalSize = tocOffset + RAC234_PUBLIC_LEAD_TOC_MAX_BYTES + 0x4000;
  const source = new RecordingReader(new BlobRandomAccessReader(new Blob([new Uint8Array(totalSize)]), "synthetic.iso"));

  const bytes = await readRac234TocProbeWindow(source);
  assert.equal(bytes.length, RAC234_PUBLIC_LEAD_TOC_MAX_BYTES);
  assert.deepEqual(source.reads, [[tocOffset, RAC234_PUBLIC_LEAD_TOC_MAX_BYTES]]);
});

test("structural scan finds a synthetic six-part level table without assigning semantics", () => {
  const { bytes, tableOffset } = syntheticUyaLeadWindow();
  const candidates = scanRac234LevelTableStructuralCandidates(bytes);
  const candidate = candidates.find((value) => value.tableOffset === tableOffset);
  assert.ok(candidate, "expected structural candidate at synthetic table offset");
  assert.equal(candidate.firstTwoEntries[0].parts[0].headerLba, TOC_LBA + 2);
  assert.equal(candidate.firstTwoEntries[0].parts[1].headerLba, TOC_LBA + 4);
  assert.equal(candidate.firstTwoEntries[0].parts[2].headerLba, TOC_LBA + 6);
});

test("public UYA lead filter requires audio/level/scene header-size sequence twice", () => {
  const { bytes, tableOffset } = syntheticUyaLeadWindow();
  const candidates = scanRac234LevelTableStructuralCandidates(bytes);
  const matches = filterUyaLevelTableCandidatesByPublicLead(candidates);
  assert.deepEqual(matches.map((value) => value.tableOffset), [tableOffset]);

  // Break one pointed header. Structural sanity remains valid, but the public UYA interpretation no longer matches.
  putPointedHeader(bytes, TOC_LBA + 10, 0x68, 8122);
  const changed = filterUyaLevelTableCandidatesByPublicLead(scanRac234LevelTableStructuralCandidates(bytes));
  assert.equal(changed.some((value) => value.tableOffset === tableOffset), false);
});

test("physical entry parsing and provisional UYA interpretation stay separate", () => {
  const { bytes, tableOffset } = syntheticUyaLeadWindow();
  const physical = parseRac234LevelTableEntry(bytes, tableOffset, 0);
  assert.deepEqual(physical.parts.map((part) => part.sizeSectors), [91, 121, 31]);

  const uya = interpretUyaLevelTableEntryPublicLead(physical);
  assert.equal(uya.audio.sizeSectors, 91);
  assert.equal(uya.level.sizeSectors, 121);
  assert.equal(uya.scene.sizeSectors, 31);
});

test("resident global-header prefix is parsed positionally up to a selected table offset", () => {
  const { bytes, tableOffset } = syntheticUyaLeadWindow();
  assert.deepEqual(parseRac234ResidentGlobalHeaders(bytes, tableOffset), [
    { offset: 0x00, headerSize: 0x48, fileLba: 5000 },
    { offset: 0x48, headerSize: 0x40, fileLba: 5100 },
  ]);
});

test("invalid pointed headers are rejected as structural candidates", () => {
  const { bytes, tableOffset } = syntheticUyaLeadWindow();
  putPointedHeader(bytes, TOC_LBA + 2, 0x10000, 7091);
  const candidates = scanRac234LevelTableStructuralCandidates(bytes);
  assert.equal(candidates.some((value) => value.tableOffset === tableOffset), false);
});

test("level entry and resident stream bounds failures are explicit", () => {
  const bytes = new Uint8Array(0x40);
  assert.throws(() => parseRac234LevelTableEntry(bytes, 0x30, 0), RangeError);

  putResidentHeader(bytes, 0, 0x30, 1234);
  assert.throws(() => parseRac234ResidentGlobalHeaders(bytes, 0x20), /overlaps level table/);
});
