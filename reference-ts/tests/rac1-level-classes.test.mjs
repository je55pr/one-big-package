import test from "node:test";
import assert from "node:assert/strict";
import { VIF_UNPACK } from "../.build/packages/ps2-vif/src/index.js";
import { RAC1_TIE_CLASS_LAYOUT } from "../.build/packages/rc-tie/src/index.js";
import { readRac1ClassDirectory, readRac1StaticClasses } from "../.build/packages/rac1-level-classes/src/index.js";

const cat = (...parts) => {
  const out = new Uint8Array(parts.reduce((sum, part) => sum + part.length, 0));
  let at = 0;
  for (const part of parts) { out.set(part, at); at += part.length; }
  return out;
};
const u32 = (v) => new Uint8Array([v & 0xff, (v >> 8) & 0xff, (v >> 16) & 0xff, (v >>> 24) & 0xff]);
const s16buf = (values) => {
  const out = new Uint8Array(values.length * 2);
  const view = new DataView(out.buffer);
  values.forEach((value, i) => view.setInt16(i * 2, value, true));
  return out;
};
function unpackPacket(vnvl, num, data) {
  const cmd = 0x60 | (vnvl & 0x0f);
  const code = ((cmd & 0x7f) << 24) | ((num & 0xff) << 16);
  return cat(u32(code), data, new Uint8Array((4 - (data.length % 4)) % 4));
}

function buildRac1TieClass() {
  const layout = RAC1_TIE_CLASS_LAYOUT;
  const table = layout.headerSize;
  const packet = table + 0x20;
  const bytes = new Uint8Array(packet + 0x80);
  const view = new DataView(bytes.buffer);
  view.setInt32(0, table, true);
  bytes[layout.packetCountOffset] = 1;
  view.setFloat32(layout.scaleOffset, 1, true);
  view.setInt32(table, packet - table, true);
  bytes[table + 8] = 3;
  bytes[table + 9] = 3;
  bytes[packet + 0x23] = 1;
  bytes[packet + 0x28] = 10;
  bytes[packet + 0x2e] = 6;
  const vert = (i, x, y, ofs) => {
    const at = packet + 0x30 + i * 0x10;
    view.setInt16(at, x, true); view.setInt16(at + 2, y, true);
    view.setUint16(at + 6, ofs, true);
  };
  vert(0, 0, 0, 7); vert(1, 1024, 0, 10); vert(2, 0, 1024, 13);
  return bytes;
}

function buildShrubClass(oClass) {
  const gifTag = new Uint8Array(0x10);
  new DataView(gifTag.buffer).setUint32(4, 1 << 17, true);
  const header = cat(u32(0), u32(1), u32(3), u32(0), gifTag);
  const part1 = cat(s16buf([0, 0, 0, 1]), s16buf([1024, 0, 0, 4]), s16buf([0, 1024, 0, 7]));
  const part2 = cat(s16buf([0, 0, 0, 0]), s16buf([4096, 0, 0, 0]), s16buf([0, 4096, 0, 0]));
  const packet = cat(
    unpackPacket(VIF_UNPACK.V4_32, header.length / 16, header),
    unpackPacket(VIF_UNPACK.V4_16, 3, part1),
    unpackPacket(VIF_UNPACK.V4_16, 3, part2),
  );
  const classHeader = new Uint8Array(0x40);
  const view = new DataView(classHeader.buffer);
  view.setFloat32(0x20, 1, true);
  view.setInt16(0x24, oClass, true);
  view.setInt16(0x28, 1, true);
  return cat(classHeader, u32(0x48), u32(packet.length), packet);
}

function fixture() {
  const tieClass = buildRac1TieClass();
  const shrubClass = buildShrubClass(200);
  const assets = new Uint8Array(0x800);
  assets.set(tieClass, 0x100);
  assets.set(shrubClass, 0x500);

  const index = new Uint8Array(0x200);
  const view = new DataView(index.buffer);
  view.setInt32(0x18, 0, true); view.setInt32(0x1c, 0, true);
  view.setInt32(0x20, 1, true); view.setInt32(0x24, 0xc0, true);
  view.setInt32(0x28, 1, true); view.setInt32(0x2c, 0xe0, true);
  view.setInt32(0xc0, 0x100, true); view.setInt32(0xc4, 100, true); index[0xd0] = 7;
  view.setInt32(0xe0, 0x500, true); view.setInt32(0xe4, 200, true); index[0xf0] = 9;
  return { index, assets };
}

test("R&C1 class directory preserves all three native tables and unknown words", () => {
  const { index, assets } = fixture();
  const directory = readRac1ClassDirectory(index, assets.length);
  assert.equal(directory.moby.count, 0);
  assert.equal(directory.tie.count, 1);
  assert.equal(directory.shrub.count, 1);
  assert.equal(directory.tie.entries[0].oClass, 100);
  assert.deepEqual(directory.tie.entries[0].textureIds.slice(0, 2), [7, 0]);
  assert.equal(directory.shrub.entries[0].rawTailWords.length, 4);
});

test("R&C1 static class decode uses RAC1 tie header and shared shrub codec", () => {
  const { index, assets } = fixture();
  const result = readRac1StaticClasses(index, assets);
  const tie = result.ties.get(100);
  const shrub = result.shrubs.get(200);
  assert.ok(tie);
  assert.ok(shrub);
  assert.equal(tie.mesh.indices.length / 3, 1);
  assert.equal(shrub.mesh.indices.length / 3, 1);
  assert.deepEqual([...tie.triangleTextureIds], [7]);
  assert.deepEqual([...shrub.triangleTextureIds], [9]);
});

test("R&C1 shrub table/payload class-id disagreement is rejected", () => {
  const { index, assets } = fixture();
  new DataView(assets.buffer).setInt16(0x500 + 0x24, 201, true);
  assert.throws(() => readRac1StaticClasses(index, assets), /disagrees with payload class/);
});
