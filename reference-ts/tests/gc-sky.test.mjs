import test from "node:test";
import assert from "node:assert/strict";
import { readGcSky, SKY_HEADER_SIZE } from "../.build/packages/gc-sky/src/index.js";

// Full sky recovery is verified against retail Going Commando (see
// research/GC_SKY.md). These tests exercise the header / shell / cluster walk
// and the paletted-texture path on a synthetic buffer.

function buildSky() {
  const buf = new Uint8Array(0x500);
  const dv = new DataView(buf.buffer);

  // SkyHeader
  buf[0] = 10; buf[1] = 20; buf[2] = 30; buf[3] = 0x80; // colour
  dv.setInt16(0x06, 1, true); // shell_count
  dv.setInt16(0x0c, 1, true); // texture_count
  dv.setInt32(0x10, 0x40, true); // texture_defs
  dv.setInt32(0x14, 0x60, true); // texture_data
  dv.setInt32(0x20, 0x480, true); // shells[0]

  // SkyTexture def @ 0x40 (offsets relative to texture_data)
  dv.setInt32(0x40, 0x000, true); // palette_offset
  dv.setInt32(0x44, 0x400, true); // texture_offset
  dv.setInt32(0x48, 2, true); // width
  dv.setInt32(0x4c, 2, true); // height
  dv.setUint32(0x60, 0x80ff00ff, true); // palette[0]: R=ff G=00 B=ff A=80

  // Shell @ 0x480: RacGcSkyShellHeader + one SkyClusterHeader @ +0x10
  dv.setInt32(0x480, 1, true); // cluster_count
  dv.setInt32(0x484, 0, true); // flags (textured)
  const ch = 0x490;
  dv.setInt32(ch + 0x10, 0x4b0, true); // data
  dv.setInt16(ch + 0x14, 3, true); // vertex_count
  dv.setInt16(ch + 0x16, 1, true); // tri_count
  dv.setUint16(ch + 0x18, 0, true); // vertex_offset
  dv.setUint16(ch + 0x1a, 24, true); // st_offset
  dv.setUint16(ch + 0x1c, 36, true); // tri_offset

  // vertices @ 0x4b0
  const V = 0x4b0;
  dv.setInt16(V + 0x04, 1024, true); dv.setInt16(V + 0x06, 0x80, true); // v0 z=1, a=opaque
  dv.setInt16(V + 0x08, 1024, true); dv.setInt16(V + 0x0e, 0x80, true); // v1 x=1
  dv.setInt16(V + 0x12, 1024, true); dv.setInt16(V + 0x16, 0x40, true); // v2 y=1, a=half
  // texcoords @ 0x4b0 + 24
  dv.setInt16(V + 24 + 4, 4096, true); // v1 s=1
  dv.setInt16(V + 24 + 10, 4096, true); // v2 t=1
  // face @ 0x4b0 + 36
  buf[V + 36 + 0] = 0; buf[V + 36 + 1] = 1; buf[V + 36 + 2] = 2; buf[V + 36 + 3] = 0;

  return buf;
}

test("reads a shell with one textured cluster", () => {
  const sky = readGcSky(buildSky());
  assert.deepEqual(sky.colour.slice(0, 3).map((v) => Math.round(v * 255)), [10, 20, 30]);
  assert.equal(sky.textures.length, 1);
  assert.equal(sky.textures[0].width, 2);
  assert.equal(sky.shells.length, 1);

  const shell = sky.shells[0];
  assert.equal(shell.textured, true);
  assert.deepEqual([...shell.positions], [0, 0, 1, 1, 0, 0, 0, 1, 0]);
  assert.deepEqual([...shell.indices], [2, 1, 0]); // winding reversed
  assert.deepEqual([...shell.triangleTextureIds], [0]);
  assert.equal(shell.alpha[0], 1);
  assert.ok(Math.abs(shell.alpha[2] - 0x40 * 2 / 255) < 1e-6);
  assert.deepEqual([...shell.uvs.slice(2, 4)], [1, 0]);
});

test("an untextured (gouraud) shell sets textured=false and texture id -1", () => {
  const buf = buildSky();
  new DataView(buf.buffer).setInt32(0x484, 1, true); // flags bit0 => untextured
  buf[0x4b0 + 36 + 3] = 0xff; // face texture 0xff
  const shell = readGcSky(buf).shells[0];
  assert.equal(shell.textured, false);
  assert.deepEqual([...shell.triangleTextureIds], [-1]);
});

test("a buffer shorter than the header is rejected", () => {
  assert.throws(() => readGcSky(new Uint8Array(SKY_HEADER_SIZE - 1)), /shorter than the 0x40 header/);
});

test("an implausible shell count is rejected", () => {
  const buf = buildSky();
  new DataView(buf.buffer).setInt16(0x06, 99, true);
  assert.throws(() => readGcSky(buf), /shell count/);
});
