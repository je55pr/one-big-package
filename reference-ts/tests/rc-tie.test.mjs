import test from "node:test";
import assert from "node:assert/strict";
import {
  RAC1_TIE_CLASS_HEADER_SIZE,
  RAC1_TIE_CLASS_LAYOUT,
  GC_UYA_DL_TIE_CLASS_LAYOUT,
  readRcTieClass,
} from "../.build/packages/rc-tie/src/index.js";

function buildTieClass(layout) {
  const headerTable = layout.headerSize;
  const packetData = headerTable + 0x20;
  const buf = new Uint8Array(packetData + 0x80);
  const dv = new DataView(buf.buffer);
  dv.setInt32(0x00, headerTable, true);
  buf[layout.packetCountOffset] = 1;
  dv.setFloat32(layout.scaleOffset, 1, true);

  dv.setInt32(headerTable, packetData - headerTable, true);
  buf[headerTable + 0x08] = 3;
  buf[headerTable + 0x09] = 3;
  buf[packetData + 0x23] = 1;
  buf[packetData + 0x28] = 10;
  buf[packetData + 0x2c] = 3;
  buf[packetData + 0x2e] = 6;

  const vert = (i, x, y, z, s, t, ofs) => {
    const at = packetData + 0x30 + i * 0x10;
    dv.setInt16(at, x, true);
    dv.setInt16(at + 2, y, true);
    dv.setInt16(at + 4, z, true);
    dv.setUint16(at + 6, ofs, true);
    dv.setUint16(at + 8, s, true);
    dv.setUint16(at + 10, t, true);
  };
  vert(0, 0, 0, 0, 0, 0, 7);
  vert(1, 1024, 0, 0, 4096, 0, 10);
  vert(2, 0, 1024, 0, 0, 4096, 13);
  return buf;
}

test("shared RC tie packet decoder handles the R&C1 class-header generation", () => {
  const mesh = readRcTieClass(buildTieClass(RAC1_TIE_CLASS_LAYOUT), RAC1_TIE_CLASS_LAYOUT);
  assert.equal(RAC1_TIE_CLASS_HEADER_SIZE, 0x70);
  assert.equal(mesh.positions.length / 3, 3);
  assert.equal(mesh.indices.length / 3, 1);
  assert.deepEqual([...mesh.positions.slice(3, 6)], [1, 0, 0]);
  assert.deepEqual([...mesh.triangleMaterialSlots], [0]);
});

test("shared RC tie packet decoder keeps the later GC header generation", () => {
  const mesh = readRcTieClass(buildTieClass(GC_UYA_DL_TIE_CLASS_LAYOUT), GC_UYA_DL_TIE_CLASS_LAYOUT);
  assert.equal(mesh.indices.length / 3, 1);
});

test("R&C1 header is not accidentally interpreted as the GC generation", () => {
  const bytes = buildTieClass(RAC1_TIE_CLASS_LAYOUT);
  const mesh = readRcTieClass(bytes, GC_UYA_DL_TIE_CLASS_LAYOUT);
  assert.equal(mesh.indices.length, 0);
});
