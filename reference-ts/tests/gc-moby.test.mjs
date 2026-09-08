import test from "node:test";
import assert from "node:assert/strict";
import { readGcMobyClass, readGcMobyClasses, debugGcMobyClassPackets, MOBY_CLASS_HEADER_SIZE } from "../.build/packages/gc-moby/src/index.js";

// Full moby geometry recovery (VIF list + skinned vertex table + Insomniac strip
// index buffer) is verified against retail Going Commando — see
// research/GC_MOBY.md. These tests cover the header / table plumbing and graceful
// handling of malformed input.

test("reads scale and packet count from the class header", () => {
  const buf = new Uint8Array(MOBY_CLASS_HEADER_SIZE + 0x40);
  const dv = new DataView(buf.buffer);
  dv.setInt32(0x00, MOBY_CLASS_HEADER_SIZE, true); // packetTableOffset -> just past the header
  buf[0x04] = 0; // highLodCount 0 -> empty mesh
  dv.setFloat32(0x24, 0.25, true); // scale

  const mesh = readGcMobyClass(buf);
  assert.equal(mesh.scale, 0.25);
  assert.equal(mesh.indices.length, 0);
  assert.equal(mesh.positions.length, 0);
  assert.equal(mesh.skinned, false);
  assert.equal(mesh.skinningApplied, false);
});

test("a class with a joint count but an out-of-range skeleton offset does not skin or crash", () => {
  const buf = new Uint8Array(MOBY_CLASS_HEADER_SIZE + 0x40);
  const dv = new DataView(buf.buffer);
  dv.setInt32(0x00, MOBY_CLASS_HEADER_SIZE, true);
  buf[0x04] = 0; // highLodCount 0
  buf[0x08] = 20; // jointCount
  dv.setInt32(0x14, 0x100000, true); // skeleton offset past the end
  dv.setInt32(0x18, 0x100000, true); // common_trans offset past the end
  dv.setFloat32(0x24, 1, true);

  const mesh = readGcMobyClass(buf);
  assert.equal(mesh.skinningApplied, false);
  assert.equal(mesh.indices.length, 0);
});

test("debugGcMobyClassPackets returns no packets for a geometry-free class", () => {
  const buf = new Uint8Array(MOBY_CLASS_HEADER_SIZE + 0x40);
  const dv = new DataView(buf.buffer);
  dv.setInt32(0x00, MOBY_CLASS_HEADER_SIZE, true);
  buf[0x04] = 0; // highLodCount 0
  dv.setFloat32(0x24, 1, true);
  assert.deepEqual(debugGcMobyClassPackets(buf), []);
});

test("truncated moby class buffer is rejected", () => {
  assert.throws(() => readGcMobyClass(new Uint8Array(0x20)), /shorter than the 0x48 header/);
});

test("a packet with an out-of-range table entry is skipped, not fatal", () => {
  const buf = new Uint8Array(MOBY_CLASS_HEADER_SIZE + 0x40);
  const dv = new DataView(buf.buffer);
  dv.setInt32(0x00, 0x10000, true); // packetTableOffset past the end
  buf[0x04] = 3;
  dv.setFloat32(0x24, 1, true);
  assert.doesNotThrow(() => readGcMobyClass(buf));
  assert.equal(readGcMobyClass(buf).indices.length, 0);
});

test("readGcMobyClasses walks the MobyClassEntry table and maps textures", () => {
  const classHeader = new Uint8Array(MOBY_CLASS_HEADER_SIZE + 0x10);
  new DataView(classHeader.buffer).setInt32(0x00, MOBY_CLASS_HEADER_SIZE, true);
  classHeader[0x04] = 0;
  new DataView(classHeader.buffer).setFloat32(0x24, 0.5, true);

  const assets = new Uint8Array(0x800 + classHeader.length);
  const classOffset = 0x200;
  assets.set(classHeader, classOffset);

  const index = new Uint8Array(0x200);
  const iv = new DataView(index.buffer);
  const tableOffset = 0x40;
  iv.setInt32(tableOffset, classOffset, true); // offset_in_asset_wad
  iv.setInt32(tableOffset + 4, 4853, true); // o_class
  index[tableOffset + 0x10] = 9; // textures[0]

  const core = {
    coreHeader: { mobyClasses: { count: 1, offset: tableOffset } },
    index,
    assets,
    sectionBoundaries: [classOffset, assets.length],
  };

  const cls = readGcMobyClasses(core).get(4853);
  assert.ok(cls);
  assert.equal(cls.mesh.scale, 0.5);
  assert.equal(cls.oClass, 4853);
});
