import test from "node:test";
import assert from "node:assert/strict";
import {
  classifyUyaLevelOverlayAddressPublicLead,
  parseUyaLevelOverlayPublicLead,
} from "../.build/packages/uya-level-overlay/src/index.js";

function buildOverlay({ duplicate = false, terminator = true } = {}) {
  const payloadSizes = [4, 4, 4, 48, 4, 4, 16];
  const total = payloadSizes.reduce((sum, size) => sum + 16 + size, 0);
  const bytes = new Uint8Array(total);
  const view = new DataView(bytes.buffer);
  let at = 0;
  let vtblData = -1;
  for (let i = 0; i < payloadSizes.length; i++) {
    const dest = 0x200000 + i * 0x1000;
    view.setUint32(at, dest, true);
    view.setUint32(at + 4, payloadSizes[i], true);
    view.setUint32(at + 8, i === 1 ? 8 : 1, true);
    view.setUint32(at + 12, 0x123456, true);
    if (i === 3) vtblData = at + 16;
    at += 16 + payloadSizes[i];
  }
  view.setInt32(vtblData, 500, true);
  view.setUint32(vtblData + 4, 0x206004, true);
  view.setUint32(vtblData + 8, 0x203024, true);
  view.setInt32(vtblData + 12, duplicate ? 500 : 501, true);
  view.setUint32(vtblData + 16, 0x206008, true);
  view.setUint32(vtblData + 20, 0x203030, true);
  view.setInt32(vtblData + 24, terminator ? -1 : 502, true);
  view.setUint32(vtblData + 28, 0, true);
  view.setUint32(vtblData + 32, 0x20303c, true);
  if (!terminator) {
    view.setInt32(vtblData + 36, 503, true);
    view.setUint32(vtblData + 40, 0x20600c, true);
    view.setUint32(vtblData + 44, 0x203048, true);
  }
  return bytes;
}

test("UYA public overlay lead parses seven sections and the terminated Moby dispatch table", () => {
  const parsed = parseUyaLevelOverlayPublicLead(buildOverlay());
  assert.equal(parsed.sections.length, 7);
  assert.deepEqual(parsed.sections.map((section) => section.publicLabel), [".lit", ".bss", ".data", "lvl.vtbl", "lvl.camvtbl", "lvl.sndvtbl", ".text"]);
  assert.equal(parsed.mobyDispatch.records.length, 2);
  assert.deepEqual(parsed.mobyDispatch.records.map((record) => record.oClassGcCompatibility), [500, 501]);
  assert.equal(parsed.mobyDispatch.records[0].recordAddressU32, 0x203000);
  assert.equal(parsed.mobyDispatch.terminatorOffset, 24);
  assert.equal(classifyUyaLevelOverlayAddressPublicLead(parsed, 0x206004), ".text");
  assert.equal(classifyUyaLevelOverlayAddressPublicLead(parsed, 0x203024), "lvl.vtbl");
  assert.equal(classifyUyaLevelOverlayAddressPublicLead(parsed, 0), "zero");
  assert.equal(classifyUyaLevelOverlayAddressPublicLead(parsed, 0x500000), "external");
});

test("UYA public Moby dispatch lead rejects duplicate classes", () => {
  assert.throws(() => parseUyaLevelOverlayPublicLead(buildOverlay({ duplicate: true })), /duplicate class 500/i);
});

test("UYA public Moby dispatch lead requires the native -1 terminator", () => {
  assert.throws(() => parseUyaLevelOverlayPublicLead(buildOverlay({ terminator: false })), /no -1 class terminator/i);
});
