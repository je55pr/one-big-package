import test from "node:test";
import assert from "node:assert/strict";
import { buildUyaLevelCataloguePublicLead } from "../.build/packages/uya-level-catalogue/src/index.js";

const SOURCE = {
  repository: "chaoticgd/wrench",
  commit: "e67ea46e5e60ec52a0c31fcde1e3f46b20aa4cfb",
  tableOfContentsPath: "src/iso/table_of_contents.cpp",
  wadIdentifierPath: "src/iso/wad_identifier.cpp",
};

function part(slot, headerOffset, label, headerLba = 1100 + slot, sizeSectors = 10 + slot) {
  return {
    slot,
    headerLba,
    sizeSectors,
    headerOffsetInWindowBytes: headerOffset,
    headerSize: label === "level" ? 0x60 : label === "level-audio" ? 0x1818 : 0x26f0,
    rawHeaderWordAt0x04: 5000 + slot,
    publicFormatHint: { label, gameHint: label === "level" ? "gc-or-uya" : label === "level-audio" ? "uya" : "unknown", source: SOURCE },
    issues: [],
  };
}

function analysis(rows) {
  return {
    tocLba: 1001,
    windowBytes: 0x10000,
    globalHeaders: [],
    levelTableDiscovery: "structural-public-lead",
    levelTableOffsetBytes: 0x100,
    levelRows: rows,
    warnings: [],
  };
}

test("catalogue keeps table index distinct from native level ID read from main header +0x08", () => {
  const bytes = new Uint8Array(0x10000);
  const view = new DataView(bytes.buffer);
  view.setUint32(0x2008, 0x2a, true);

  const result = buildUyaLevelCataloguePublicLead(bytes, analysis([{
    index: 7,
    offsetBytes: 0x100,
    parts: [part(0, 0x1000, "level-audio"), part(1, 0x2000, "level"), part(2, 0x3000, "level-scene")],
  }]));

  assert.equal(result.entries[0].tableIndex, 7);
  assert.equal(result.entries[0].publicNativeLevelIdHint, 42);
  assert.equal(result.entries[0].publicNativeLevelIdEvidence, "main-header-word-0x08");
  assert.deepEqual(result.entries[0].parts.map((p) => p.publicKindHint), ["audio", "level", "scene"]);
});

test("Wrench index-38 no-main-WAD case remains an explicit special-case hint", () => {
  const bytes = new Uint8Array(0x10000);
  const result = buildUyaLevelCataloguePublicLead(bytes, analysis([{
    index: 38,
    offsetBytes: 0x490,
    parts: [part(0, 0x1000, "level-audio"), { slot: 1, headerLba: 0, sizeSectors: 0, issues: [] }, part(2, 0x3000, "level-scene")],
  }]));

  assert.equal(result.entries[0].publicNativeLevelIdHint, 38);
  assert.equal(result.entries[0].publicNativeLevelIdEvidence, "wrench-index-38-special-case");
});

test("ambiguous main-header signatures withhold native ID rather than guessing", () => {
  const bytes = new Uint8Array(0x10000);
  const result = buildUyaLevelCataloguePublicLead(bytes, analysis([{
    index: 5,
    offsetBytes: 0x178,
    parts: [part(0, 0x1000, "level"), part(1, 0x2000, "level"), part(2, 0x3000, "level-scene")],
  }]));

  assert.equal(result.entries[0].publicNativeLevelIdHint, undefined);
  assert.match(result.entries[0].warnings.join(" "), /Multiple \(2\) parts have the public main-level header signature/);
});

test("duplicate provisional native IDs are reported without collapsing catalogue rows", () => {
  const bytes = new Uint8Array(0x10000);
  const view = new DataView(bytes.buffer);
  view.setUint32(0x2008, 3, true);
  view.setUint32(0x4008, 3, true);

  const result = buildUyaLevelCataloguePublicLead(bytes, analysis([
    { index: 1, offsetBytes: 0x100, parts: [part(0, 0x1000, "level-audio"), part(1, 0x2000, "level"), part(2, 0x3000, "level-scene")] },
    { index: 9, offsetBytes: 0x1d8, parts: [part(0, 0x5000, "level-audio"), part(1, 0x4000, "level"), part(2, 0x6000, "level-scene")] },
  ]));

  assert.equal(result.entries.length, 2);
  assert.match(result.warnings.join(" "), /ID hint 3 occurs at multiple table indices: 1, 9/);
});
