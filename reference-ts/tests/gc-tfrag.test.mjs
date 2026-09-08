import test from "node:test";
import assert from "node:assert/strict";
import { readGcTfrags, toObpTfragMeshes, TFRAG_HEADER_SIZE } from "../.build/packages/gc-tfrag/src/index.js";
import { VIF_UNPACK } from "../.build/packages/ps2-vif/src/index.js";
import { validateWorld } from "../.build/packages/core/src/index.js";

const cat = (...parts) => {
  const total = parts.reduce((n, p) => n + p.length, 0);
  const out = new Uint8Array(total);
  let o = 0;
  for (const p of parts) { out.set(p, o); o += p.length; }
  return out;
};
const u32 = (v) => new Uint8Array([v & 0xff, (v >> 8) & 0xff, (v >> 16) & 0xff, (v >>> 24) & 0xff]);
const s16arr = (values) => {
  const b = new Uint8Array(values.length * 2);
  const d = new DataView(b.buffer);
  values.forEach((v, i) => d.setInt16(i * 2, v, true));
  return b;
};

/** One VIF UNPACK packet: code word + data padded to a word. */
function unpackPacket(vnvl, num, addr, data) {
  const cmd = 0x60 | (vnvl & 0x0f);
  const code = ((cmd & 0x7f) << 24) | ((num & 0xff) << 16) | (addr & 0x3ff);
  const pad = (4 - (data.length % 4)) % 4;
  return cat(u32(code), data, new Uint8Array(pad));
}
/** A STROW packet (code + 4 register words). */
function strowPacket(r0, r1, r2, r3) {
  return cat(u32(0x30 << 24), u32(r0), u32(r1), u32(r2), u32(r3));
}
const nop = () => u32(0);

/**
 * Build a one-tfrag blob whose LOD 0 is a single 4-vertex triangle strip
 * (2 triangles). Positions are common; the strip indexes vertex_infos 0..3.
 *
 * Data-region layout: [pad to sharedOfs] [common (padded to 0x10)] [lod0] [rgbas].
 * With lod1Size = commonLen/0x10, lod2Size = 1, commonSize = 0 the reader's LOD 0
 * buffer math lands exactly on `lod0`, and the LOD 01 buffer is empty.
 */
function buildOneTfrag({ base = [4096, 8192, 12288], positions, uvs, rgba } = {}) {
  positions ??= [[0, 0, 0], [16, 0, 0], [0, 16, 0], [16, 16, 0]];
  uvs ??= [[0, 0], [4096, 0], [0, 4096], [4096, 4096]];
  rgba ??= positions.map(() => [128, 200, 64, 255]);

  const vertexInfo = positions.map((_, i) => s16arr([uvs[i][0], uvs[i][1], 0, i * 2])); // {s,t,parent,vertex}
  let common = cat(
    nop(), nop(), nop(), nop(), nop(),
    strowPacket(base[0], base[1], base[2], 0),
    unpackPacket(VIF_UNPACK.V4_16, 1, 0, s16arr([0, 0, 0, 0])),
    unpackPacket(VIF_UNPACK.V4_32, 5, 0, new Uint8Array(0x50)), // one texture primitive (5 qwords), texId 0 (data_lo @ 0)
    unpackPacket(VIF_UNPACK.V4_16, positions.length, 10, cat(...vertexInfo)),
    unpackPacket(VIF_UNPACK.V3_16, positions.length, 20, cat(...positions.map((p) => s16arr(p)))),
  );
  while (common.length % 0x10 !== 0) common = cat(common, nop());

  const lod0 = cat(
    // strips: one strip. First byte 132 = -124 as s8 -> "set ad_gif (offset 0 -> gif 0), real count 132-128+128"...
    // vertexCountAndFlag 132 -> s8 -124 -> <=0 branch: ad_gif_offset 0 -> activeAdGif 0; then += 128 -> vc 4.
    unpackPacket(VIF_UNPACK.V4_8, 1, 100, new Uint8Array([132, 0, 0, 0])),
    unpackPacket(VIF_UNPACK.V4_8, 1, 90, new Uint8Array([0, 1, 2, 3])), // indices
  );
  const rgbaBytes = new Uint8Array(rgba.flat());

  const sharedOfs = 0x10;
  const lod1Size = common.length / 0x10;
  const lod1Ofs = sharedOfs + common.length;
  const lod0Ofs = lod1Ofs;
  const rgbaOfs = lod1Ofs + lod0.length;

  const dataRegion = cat(new Uint8Array(sharedOfs), common, lod0, rgbaBytes);

  const header = new Uint8Array(TFRAG_HEADER_SIZE);
  const hv = new DataView(header.buffer);
  // data region sits right after the 1-entry header table: (0x40 tfragsHeader + 0x40 table) - 0x40 tableOffset
  hv.setInt32(0x10, TFRAG_HEADER_SIZE, true); // data (offset from tableOffset)
  hv.setUint16(0x16, sharedOfs, true);
  hv.setUint16(0x18, lod1Ofs, true);
  hv.setUint16(0x1a, lod0Ofs, true);
  hv.setUint16(0x1e, rgbaOfs, true);
  header[0x20] = 0; // commonSize
  header[0x21] = 1; // lod2Size
  header[0x22] = lod1Size;
  header[0x29] = Math.ceil(rgba.length / 4); // rgbaSize
  header[0x3c] = positions.length;
  header[0x3d] = 2;

  const tfragsHeader = new Uint8Array(0x40);
  const th = new DataView(tfragsHeader.buffer);
  th.setInt32(0, 0x40, true); // tableOffset
  th.setInt32(4, 1, true); // tfragCount

  return cat(tfragsHeader, header, dataRegion);
}

test("decodes a one-tfrag blob into world-space geometry (Y-up), UVs and vertex colours", () => {
  const blob = buildOneTfrag({ base: [409600, 0, 819200] }); // /1024 -> x 400, z 800 (native)
  const mesh = readGcTfrags(blob);

  assert.equal(mesh.tfragCount, 1);
  assert.equal(mesh.positions.length / 3, 4);
  assert.equal(mesh.indices.length / 3, 2); // 4-vertex strip -> 2 triangles

  // native (x, y, z) -> OBP (x, z, y): base (400, 0, 800) native + vertex 0 (0,0,0)
  assert.deepEqual([mesh.positions[0], mesh.positions[1], mesh.positions[2]], [400, 800, 0]);
  // vertex 1 is +16/1024 in native x
  assert.ok(Math.abs(mesh.positions[3] - (400 + 16 / 1024)) < 1e-6);

  // uv of vertex 1 = (4096/4096, 0) = (1, 0)
  assert.ok(Math.abs(mesh.uvs[2] - 1) < 1e-6);
  // colour of vertex 0 = (128,200,64)/255
  assert.ok(Math.abs(mesh.colors[0] - 128 / 255) < 1e-6);
  assert.ok(Math.abs(mesh.colors[1] - 200 / 255) < 1e-6);

  assert.deepEqual([...mesh.textureIds], [0]);
});

test("toObpTfragMeshes groups by texture id and produces a valid world", () => {
  const blob = buildOneTfrag();
  const mesh = readGcTfrags(blob);
  const source = { game: "rac2", buildId: "rac2-ntscu-v1.01", levelId: "1" };
  const meshes = toObpTfragMeshes(mesh, source, "rac2-1-t0");

  assert.equal(meshes.length, 1);
  assert.equal(meshes[0].id, "rac2-1-t0-tex0");
  assert.equal(meshes[0].geometry.colors.length, meshes[0].geometry.positions.length);
  assert.equal(meshes[0].geometry.uvs.length, (meshes[0].geometry.positions.length / 3) * 2);

  const world = {
    schemaVersion: 1, id: "w", displayName: "w", source,
    bounds: { min: { x: mesh.bounds.min[0], y: mesh.bounds.min[1], z: mesh.bounds.min[2] }, max: { x: mesh.bounds.max[0], y: mesh.bounds.max[1], z: mesh.bounds.max[2] } },
    materials: [], meshes, collisionMeshes: [], instances: [], splines: [], volumes: [], spawnPoints: [],
  };
  assert.deepEqual(validateWorld(world), []);
});

test("rejects a bad tfrags header", () => {
  assert.throws(() => readGcTfrags(new Uint8Array(8)), /shorter than/);
  const bad = new Uint8Array(0x40);
  new DataView(bad.buffer).setInt32(0, 0x999999, true);
  assert.throws(() => readGcTfrags(bad), /tableOffset/);
});
