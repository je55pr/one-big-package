import test from "node:test";
import assert from "node:assert/strict";
import { readGcTieClass, readGcTieClasses, TIE_CLASS_HEADER_SIZE } from "../.build/packages/gc-tie/src/index.js";

/**
 * Minimal one-packet tie class: a single 3-vertex strip.
 * Layout: [0x80 header][packet table @ 0x80][packet GS data @ 0xA0].
 */
function buildTieClass() {
  const headerTable = 0x80;
  const packetData = 0xa0;

  const buf = new Uint8Array(0x200);
  const dv = new DataView(buf.buffer);
  dv.setInt32(0x00, headerTable, true); // packets[0]
  buf[0x0c] = 1; // packetCount[0]
  dv.setFloat32(0x40, 1.0, true); // scale (so x/1024 -> world units)

  // TiePacketHeader @ headerTable
  dv.setInt32(headerTable + 0x00, packetData - headerTable, true); // data (relative to packets[0])
  buf[headerTable + 0x08] = (packetData + 0x30 - packetData) / 0x10; // vertOfs -> 3 -> 0x30
  buf[headerTable + 0x09] = 3; // vertSize -> 0x30

  // packet GS data
  // 0x00 adGifDestOffsets[4], 0x10 adGifSrcOffsets[4] -> all zero -> material slot 0
  buf[packetData + 0x23] = 1; // stripCount
  buf[packetData + 0x28] = 3 * 2 + 4; // dinkyVerticesSizePlusFour -> dinkyCount 3
  // TieStrip @ +0x2c : {vertexCount, pad, gifTagOffset, winding}
  buf[packetData + 0x2c + 0] = 3;
  buf[packetData + 0x2c + 2] = 6; // gifTagOffset must equal the initial nextOffset (6)
  buf[packetData + 0x2c + 3] = 0; // winding

  // 3 dinky vertices @ +0x30, gs_packet_write_ofs 7 / 10 / 13
  const vert = (i, x, y, z, s, t, ofs) => {
    const o = packetData + 0x30 + i * 0x10;
    dv.setInt16(o + 0, x, true);
    dv.setInt16(o + 2, y, true);
    dv.setInt16(o + 4, z, true);
    dv.setUint16(o + 6, ofs, true);
    dv.setUint16(o + 8, s, true);
    dv.setUint16(o + 10, t, true);
    dv.setUint16(o + 14, 0, true); // gs_ofs_2
  };
  vert(0, 0, 0, 0, 0, 0, 7);
  vert(1, 1024, 0, 0, 4096, 0, 10);
  vert(2, 0, 1024, 0, 0, 4096, 13);

  return buf;
}

test("decodes a one-strip tie class", () => {
  const mesh = readGcTieClass(buildTieClass());
  assert.equal(mesh.positions.length / 3, 3);
  assert.equal(mesh.indices.length / 3, 1);
  assert.equal(mesh.scale, 1);

  // vertex 1 is (1024, 0, 0) * (scale/1024) = (1, 0, 0)
  assert.deepEqual([...mesh.positions.slice(3, 6)], [1, 0, 0]);
  // uv of vertex 1 = (4096/4096, 0) = (1, 0)
  assert.ok(Math.abs(mesh.uvs[2] - 1) < 1e-6);
  assert.deepEqual([...mesh.triangleMaterialSlots], [0]);
});

test("truncated tie class buffer is rejected", () => {
  assert.throws(() => readGcTieClass(new Uint8Array(0x20)), /shorter than the 0x80 header/);
  assert.equal(TIE_CLASS_HEADER_SIZE, 0x80);
});

test("readGcTieClasses maps class-local slots to level texture ids", () => {
  const classBuf = buildTieClass();
  const assets = new Uint8Array(0x1000 + classBuf.length);
  const classOffset = 0x400;
  assets.set(classBuf, classOffset);

  const index = new Uint8Array(0x200);
  const iv = new DataView(index.buffer);
  const tableOffset = 0x40;
  iv.setInt32(tableOffset + 0x00, classOffset, true); // offsetInAssetWad
  iv.setInt32(tableOffset + 0x04, 2262, true); // oClass
  index[tableOffset + 0x10] = 42; // textures[0] -> level tie texture 42

  const core = {
    coreHeader: { tieClasses: { count: 1, offset: tableOffset } },
    index,
    assets,
    sectionBoundaries: [classOffset, assets.length],
  };

  const classes = readGcTieClasses(core);
  const cls = classes.get(2262);
  assert.ok(cls);
  assert.deepEqual([...cls.triangleTextureIds], [42]);
  assert.deepEqual(cls.textureIds, [42]);
});
