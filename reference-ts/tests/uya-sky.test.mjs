import test from "node:test";
import assert from "node:assert/strict";
import { readUyaSky, UYA_SKY_HEADER_SIZE } from "../.build/packages/uya-sky/src/index.js";

function buildSky() {
  const buf = new Uint8Array(0x500);
  const dv = new DataView(buf.buffer);
  buf[0] = 12; buf[1] = 34; buf[2] = 56; buf[3] = 0x80;
  dv.setInt16(0x06, 1, true);
  dv.setInt16(0x0c, 1, true);
  dv.setInt32(0x10, 0x40, true);
  dv.setInt32(0x14, 0x60, true);
  dv.setInt32(0x20, 0x480, true);

  dv.setInt32(0x40, 0x000, true);
  dv.setInt32(0x44, 0x400, true);
  dv.setInt32(0x48, 2, true);
  dv.setInt32(0x4c, 2, true);
  dv.setUint32(0x60, 0x80ffffff, true);

  // UyaDlSkyShellHeader: s16 cluster_count, s16 flags, two Vec3s16.
  dv.setInt16(0x480, 1, true);
  dv.setInt16(0x482, 2, true); // bloom, textured
  dv.setInt16(0x484, 1, true);
  dv.setInt16(0x486, -2, true);
  dv.setInt16(0x488, 3, true);
  dv.setInt16(0x48a, 4, true);
  dv.setInt16(0x48c, -5, true);
  dv.setInt16(0x48e, 6, true);

  const ch = 0x490;
  dv.setInt32(ch + 0x10, 0x4b0, true);
  dv.setInt16(ch + 0x14, 3, true);
  dv.setInt16(ch + 0x16, 1, true);
  dv.setInt16(ch + 0x18, 0, true);
  dv.setInt16(ch + 0x1a, 24, true);
  dv.setInt16(ch + 0x1c, 36, true);

  const V = 0x4b0;
  dv.setInt16(V + 0x04, 1024, true); dv.setInt16(V + 0x06, 0x80, true);
  dv.setInt16(V + 0x08, 1024, true); dv.setInt16(V + 0x0e, 0x80, true);
  dv.setInt16(V + 0x12, 1024, true); dv.setInt16(V + 0x16, 0x40, true);
  dv.setInt16(V + 24 + 4, 4096, true);
  dv.setInt16(V + 24 + 10, 4096, true);
  buf[V + 36] = 0; buf[V + 37] = 1; buf[V + 38] = 2; buf[V + 39] = 0;
  return buf;
}

test("reads the UYA shell header without shifting the shared cluster table", () => {
  const sky = readUyaSky(buildSky(), { framerate: 60 });
  assert.equal(sky.shells.length, 1);
  assert.equal(sky.textures.length, 1);
  const shell = sky.shells[0];
  assert.equal(shell.clusterCount, 1);
  assert.equal(shell.textured, true);
  assert.equal(shell.bloom, true);
  assert.deepEqual(shell.rotationRaw, [1, -2, 3]);
  assert.deepEqual(shell.angularVelocityRaw, [4, -5, 6]);
  assert.deepEqual([...shell.positions], [0, 0, 1, 1, 0, 0, 0, 1, 0]);
  assert.deepEqual([...shell.indices], [2, 1, 0]);
  assert.deepEqual([...shell.triangleTextureIds], [0]);
  assert.ok(Math.abs(shell.rotationRadiansPerSecond[0] - 60 * 2 * Math.PI / 32768) < 1e-12);
});

test("flags bit 0 marks an untextured shell and 0xff remains no-texture", () => {
  const buf = buildSky();
  const dv = new DataView(buf.buffer);
  dv.setInt16(0x482, 3, true); // untextured + bloom
  buf[0x4b0 + 39] = 0xff;
  const shell = readUyaSky(buf).shells[0];
  assert.equal(shell.textured, false);
  assert.equal(shell.bloom, true);
  assert.deepEqual([...shell.triangleTextureIds], [-1]);
});

test("rejects a face texture outside the declared UYA sky table", () => {
  const buf = buildSky();
  buf[0x4b0 + 39] = 2;
  assert.throws(() => readUyaSky(buf), /references texture 2 outside 1/);
});

test("rejects invalid headers", () => {
  assert.throws(() => readUyaSky(new Uint8Array(UYA_SKY_HEADER_SIZE - 1)), /shorter than the 0x40 header/);
  const buf = buildSky();
  new DataView(buf.buffer).setInt16(0x06, 9, true);
  assert.throws(() => readUyaSky(buf), /shell count/);
});
