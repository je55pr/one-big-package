import test from "node:test";
import assert from "node:assert/strict";
import { parseGcGameplayMobyPvars } from "../.build/packages/gc-pvars/src/index.js";

function fixture() {
  const data = new Uint8Array(0x600);
  const v = new DataView(data.buffer);

  const classBlock = 0x100;
  v.setInt32(0x48, classBlock, true);
  v.setInt32(classBlock, 2, true);
  v.setInt32(classBlock + 4, 500, true);
  v.setInt32(classBlock + 8, 501, true);

  const mobyBlock = 0x140;
  v.setInt32(0x4c, mobyBlock, true);
  v.setInt32(mobyBlock, 2, true);
  v.setInt32(mobyBlock + 4, 7, true); // spawnable count is not used by this slice

  const a = mobyBlock + 0x10;
  v.setInt32(a, 0x88, true);
  v.setInt32(a + 0x10, 0x1234, true);
  v.setInt32(a + 0x14, 0x55, true);
  v.setInt32(a + 0x28, 500, true);
  v.setInt32(a + 0x68, 1, true);
  v.setInt32(a + 0x70, 0x20, true);

  const b = a + 0x88;
  v.setInt32(b, 0x88, true);
  v.setInt32(b + 0x28, 501, true);
  v.setInt32(b + 0x68, -1, true);

  const pvarTable = 0x300;
  const pvarData = 0x340;
  v.setInt32(0x5c, pvarTable, true);
  v.setInt32(0x60, pvarData, true);
  v.setInt32(pvarTable + 8, 0x10, true); // index 1: offset
  v.setInt32(pvarTable + 12, 0x20, true); // index 1: size
  for (let i = 0; i < 0x20; i++) data[pvarData + 0x10 + i] = i;

  const links = 0x400;
  v.setInt32(0x58, links, true);
  v.setInt32(links, 1, true);
  v.setUint32(links + 4, 0x0c, true);
  v.setInt32(links + 8, -1, true);
  v.setInt32(links + 12, -1, true);

  const pointers = 0x440;
  v.setInt32(0x64, pointers, true);
  v.setInt32(pointers, 1, true);
  v.setUint32(pointers + 4, 0x10, true);
  v.setInt32(pointers + 8, -1, true);
  v.setInt32(pointers + 12, -1, true);

  return data;
}

test("parses GC static Moby PVar indices, table entries and fixups", () => {
  const data = fixture();
  const parsed = parseGcGameplayMobyPvars(data);
  assert.deepEqual(parsed.mobyClasses, [500, 501]);
  assert.equal(parsed.mobies.length, 2);
  assert.deepEqual(parsed.mobies[0], {
    index: 0,
    oClass: 500,
    pvarIndex: 1,
    modeBits: 0x20,
    uid: 0x1234,
    raw0x14: 0x55,
    pvar: { index: 1, offset: 0x10, size: 0x20, dataOffset: 0x350 },
  });
  assert.equal(parsed.mobies[1].pvar, null);
  assert.deepEqual(parsed.mobyPvars, [{ index: 1, offset: 0x10, size: 0x20, dataOffset: 0x350 }]);
  assert.deepEqual(parsed.pvarMobyLinks, [{ pvarIndex: 1, offset: 0x0c }]);
  assert.deepEqual(parsed.pvarRelativePointers, [{ pvarIndex: 1, offset: 0x10 }]);
});

test("rejects an out-of-range PVar table entry", () => {
  const data = fixture();
  const v = new DataView(data.buffer);
  v.setInt32(0x300 + 8, 0x1000, true);
  assert.throws(() => parseGcGameplayMobyPvars(data), /PVar 1 data range/);
});
