import test from "node:test";
import assert from "node:assert/strict";
import {
  censusUyaTfragBlobGcPrerequisites,
  probeUyaGcTfragCompatibilityPublicLead,
} from "../.build/packages/uya-tfrag-compat/src/index.js";
import { TFRAG_HEADER_SIZE } from "../.build/packages/gc-tfrag/src/index.js";
import { VIF_UNPACK } from "../.build/packages/ps2-vif/src/index.js";

const cat = (...parts) => {
  const total = parts.reduce((n, p) => n + p.length, 0);
  const out = new Uint8Array(total);
  let offset = 0;
  for (const part of parts) { out.set(part, offset); offset += part.length; }
  return out;
};
const u32 = (v) => new Uint8Array([v & 0xff, (v >> 8) & 0xff, (v >> 16) & 0xff, (v >>> 24) & 0xff]);
const s16arr = (values) => {
  const bytes = new Uint8Array(values.length * 2);
  const view = new DataView(bytes.buffer);
  values.forEach((value, i) => view.setInt16(i * 2, value, true));
  return bytes;
};
function unpackPacket(vnvl, num, addr, data) {
  const cmd = 0x60 | (vnvl & 0x0f);
  const code = ((cmd & 0x7f) << 24) | ((num & 0xff) << 16) | (addr & 0x3ff);
  return cat(u32(code), data, new Uint8Array((4 - (data.length % 4)) % 4));
}
function strowPacket(r0, r1, r2, r3) {
  return cat(u32(0x30 << 24), u32(r0), u32(r1), u32(r2), u32(r3));
}
const nop = () => u32(0);

function buildOneTfrag() {
  const positions = [[0, 0, 0], [16, 0, 0], [0, 16, 0], [16, 16, 0]];
  const uvs = [[0, 0], [4096, 0], [0, 4096], [4096, 4096]];
  const vertexInfo = positions.map((_, i) => s16arr([uvs[i][0], uvs[i][1], 0, i * 2]));
  let common = cat(
    nop(), nop(), nop(), nop(), nop(),
    strowPacket(409600, 0, 819200, 0),
    unpackPacket(VIF_UNPACK.V4_16, 1, 0, s16arr([0, 0, 0, 0])),
    unpackPacket(VIF_UNPACK.V4_32, 5, 0, new Uint8Array(0x50)),
    unpackPacket(VIF_UNPACK.V4_16, positions.length, 10, cat(...vertexInfo)),
    unpackPacket(VIF_UNPACK.V3_16, positions.length, 20, cat(...positions.map((p) => s16arr(p)))),
  );
  while (common.length % 0x10 !== 0) common = cat(common, nop());
  const lod0 = cat(
    unpackPacket(VIF_UNPACK.V4_8, 1, 100, new Uint8Array([132, 0, 0, 0])),
    unpackPacket(VIF_UNPACK.V4_8, 1, 90, new Uint8Array([0, 1, 2, 3])),
  );
  const rgbas = new Uint8Array(positions.flatMap(() => [128, 200, 64, 255]));
  const sharedOfs = 0x10;
  const lod1Size = common.length / 0x10;
  const lod1Ofs = sharedOfs + common.length;
  const rgbaOfs = lod1Ofs + lod0.length;
  const dataRegion = cat(new Uint8Array(sharedOfs), common, lod0, rgbas);

  const header = new Uint8Array(TFRAG_HEADER_SIZE);
  const hv = new DataView(header.buffer);
  hv.setInt32(0x10, TFRAG_HEADER_SIZE, true);
  hv.setUint16(0x16, sharedOfs, true);
  hv.setUint16(0x18, lod1Ofs, true);
  hv.setUint16(0x1a, lod1Ofs, true);
  hv.setUint16(0x1e, rgbaOfs, true);
  header[0x20] = 0;
  header[0x21] = 1;
  header[0x22] = lod1Size;
  header[0x29] = 1;

  const root = new Uint8Array(0x40);
  const rv = new DataView(root.buffer);
  rv.setInt32(0, 0x40, true);
  rv.setInt32(4, 1, true);
  return cat(root, header, dataRegion);
}

function decodedFor(blob) {
  return { assets: blob, sectionBoundaries: [blob.length] };
}

test("UYA tfrag compatibility wrapper accepts offset-zero sections and observes unchanged GC output", () => {
  const blob = buildOneTfrag();
  const result = probeUyaGcTfragCompatibilityPublicLead(decodedFor(blob), 0, { publicTfragTextureCount: 1 });
  assert.deepEqual(result.publicRange, { offset: 0, size: blob.length });
  assert.equal(result.prerequisiteCensus.tableOffset, 0x40);
  assert.equal(result.prerequisiteCensus.declaredTfragCount, 1);
  assert.equal(result.prerequisiteCensus.parserCandidateTfragCount, 1);
  assert.equal(result.prerequisiteCensus.skippedByDataStart, 0);
  assert.equal(result.prerequisiteCensus.skippedByCommonUnpacks, 0);
  assert.equal(result.prerequisiteCensus.skippedByStrow, 0);
  assert.equal(result.prerequisiteCensus.skippedByLod0Streams, 0);
  assert.equal(result.declaredTfragCount, 1);
  assert.equal(result.vertexCount, 4);
  assert.equal(result.triangleCount, 2);
  assert.deepEqual(result.textureIds, [0]);
  assert.deepEqual(result.textureIdsOutsidePublicTable, []);
  assert.equal(result.nonFinitePositionCount, 0);
  assert.match(result.tfragsSha256, /^[0-9a-f]{64}$/);
});

test("UYA tfrag prerequisite census makes tolerant GC per-fragment skipping explicit", () => {
  const blob = new Uint8Array(0x80);
  const view = new DataView(blob.buffer);
  view.setInt32(0, 0x40, true);
  view.setInt32(4, 1, true);
  view.setInt32(0x40 + 0x10, 0x1000, true); // dataStart is outside the section.
  const census = censusUyaTfragBlobGcPrerequisites(blob);
  assert.equal(census.declaredTfragCount, 1);
  assert.equal(census.parserCandidateTfragCount, 0);
  assert.equal(census.skippedByDataStart, 1);

  const result = probeUyaGcTfragCompatibilityPublicLead(decodedFor(blob), 0);
  assert.equal(result.declaredTfragCount, 1);
  assert.equal(result.vertexCount, 0);
  assert.equal(result.triangleCount, 0);
});

test("UYA tfrag compatibility reports texture ids outside the public candidate table without rejecting geometry", () => {
  const blob = buildOneTfrag();
  const result = probeUyaGcTfragCompatibilityPublicLead(decodedFor(blob), 0, { publicTfragTextureCount: 0 });
  assert.deepEqual(result.textureIds, [0]);
  assert.deepEqual(result.textureIdsOutsidePublicTable, [0]);
});
