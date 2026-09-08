import test from "node:test";
import assert from "node:assert/strict";
import { buildUyaGlobalCataloguePublicLead, UYA_PUBLIC_GLOBAL_ORDER } from "../.build/packages/uya-global-catalogue/src/index.js";

function analysisWithSizes(sizes) {
  let offset = 0;
  const globalHeaders = sizes.map((headerSize, index) => {
    const entry = { index, offsetBytes: offset, headerSize, rawWordAt0x04: 5000 + index };
    offset += headerSize;
    return entry;
  });
  return {
    tocLba: 1001,
    windowBytes: 0x200000,
    globalHeaders,
    levelTableDiscovery: "structural-public-lead",
    levelTableOffsetBytes: offset,
    levelRows: [],
    warnings: [],
  };
}

test("public UYA global catalogue keeps raw headers and independently checks pinned order", () => {
  const result = buildUyaGlobalCataloguePublicLead(analysisWithSizes([
    0x0648, // mpeg
    0x0048, // misc
    0x0bf0, // bonus
    0x0c30, // space
    0x0398, // armor
    0x2340, // audio
    0x03c8, // gadget
    0x2ab0, // hud
  ]));
  assert.deepEqual(result.publicExpectedOrder, UYA_PUBLIC_GLOBAL_ORDER);
  assert.equal(result.exactExpectedCount, true);
  assert.equal(result.allClassifiedEntriesAgreeWithExpectedOrder, true);
  assert.equal(result.entries[0].rawWordAt0x04, 5000);
  assert.equal(result.entries[0].expectedPublicKindAtIndex, "mpeg");
  assert.equal(result.entries[0].headerSizePublicHint.label, "mpeg");
  assert.equal(result.warnings.length, 0);
});

test("order disagreement is reported rather than coercing the raw census", () => {
  const result = buildUyaGlobalCataloguePublicLead(analysisWithSizes([0x0048, 0x0648]));
  assert.equal(result.exactExpectedCount, false);
  assert.equal(result.allClassifiedEntriesAgreeWithExpectedOrder, false);
  assert.equal(result.entries[0].expectedPublicKindAtIndex, "mpeg");
  assert.equal(result.entries[0].headerSizePublicHint.label, "misc");
  assert.match(result.warnings.join(" "), /conflicts/);
  assert.match(result.warnings.join(" "), /expects 8/);
});

test("unknown header size remains raw and unclassified", () => {
  const result = buildUyaGlobalCataloguePublicLead(analysisWithSizes([0x1234]));
  assert.equal(result.entries[0].headerSize, 0x1234);
  assert.equal(result.entries[0].headerSizePublicHint, undefined);
  assert.match(result.warnings.join(" "), /no pinned UYA public format hint/);
});
