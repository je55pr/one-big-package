import test from "node:test";
import assert from "node:assert/strict";
import {
  censusUyaGameplayPvarPrerequisites,
  probeUyaGcPvarCompatibility,
} from "../.build/packages/uya-pvars-compat/src/index.js";

function fixture() {
  const data = new Uint8Array(0x700);
  const v = new DataView(data.buffer);
  const classBlock = 0x100;
  v.setInt32(0x48, classBlock, true);
  v.setInt32(classBlock, 2, true);
  v.setInt32(classBlock + 4, 500, true);
  v.setInt32(classBlock + 8, 501, true);

  const mobyBlock = 0x140;
  v.setInt32(0x4c, mobyBlock, true);
  v.setInt32(mobyBlock, 2, true);
  const a = mobyBlock + 0x10;
  v.setInt32(a, 0x88, true);
  v.setInt32(a + 0x28, 500, true);
  v.setInt32(a + 0x68, 1, true);
  const b = a + 0x88;
  v.setInt32(b, 0x88, true);
  v.setInt32(b + 0x28, 501, true);
  v.setInt32(b + 0x68, -1, true);
  const pvarTable = 0x300;
  const pvarData = 0x380;
  v.setInt32(0x5c, pvarTable, true);
  v.setInt32(0x60, pvarData, true);
  v.setInt32(pvarTable + 1 * 8, 0x10, true);
  v.setInt32(pvarTable + 1 * 8 + 4, 0x20, true);
  v.setInt32(pvarTable + 2 * 8, 0x40, true);
  v.setInt32(pvarTable + 2 * 8 + 4, 0x10, true);

  const links = 0x450;
  v.setInt32(0x58, links, true);
  v.setInt32(links, 1, true);
  v.setUint32(links + 4, 0x0c, true);
  v.setInt32(links + 8, 2, true);
  v.setUint32(links + 12, 0x04, true);
  v.setInt32(links + 16, -1, true);

  const relatives = 0x490;
  v.setInt32(0x64, relatives, true);
  v.setInt32(relatives, 1, true);
  v.setUint32(relatives + 4, 0x10, true);
  v.setInt32(relatives + 8, -1, true);
  return data;
}
test("independent UYA PVar census validates all Moby/fixup references before unchanged GC parsing", () => {
  const result = probeUyaGcPvarCompatibility(fixture());
  const census = result.prerequisiteCensus;
  assert.equal(census.classCount, 2);
  assert.equal(census.mobyCount, 2);
  assert.equal(census.mobiesWithPvar, 1);
  assert.equal(census.mobyReferencedPvarCount, 1);
  assert.equal(census.allReferencedPvarCount, 2);
  assert.equal(census.pvarMobyLinks.length, 2);
  assert.equal(census.pvarRelativePointers.length, 1);
  assert.deepEqual(census.referencedPvars.map((p) => p.index), [1, 2]);
  assert.equal(result.parser.mobyPvars.length, 1);
});

test("independent census rejects a referenced PVar data span outside gameplay", () => {
  const data = fixture();
  const v = new DataView(data.buffer);
  v.setInt32(0x300 + 2 * 8, 0x1000, true);
  assert.throws(() => censusUyaGameplayPvarPrerequisites(data), /PVar 2 data range/);
});

test("independent census rejects a fixup that cannot address a 4-byte field inside its PVar", () => {
  const data = fixture();
  const v = new DataView(data.buffer);
  v.setUint32(0x450 + 12, 0x0e, true);
  assert.throws(() => censusUyaGameplayPvarPrerequisites(data), /does not fit a 4-byte field/);
});
