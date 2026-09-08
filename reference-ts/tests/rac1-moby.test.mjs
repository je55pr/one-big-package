import test from "node:test";
import assert from "node:assert/strict";
import {
  RAC1_MOBY_CLASS_HEADER_SIZE,
  RAC1_MOBY_VERTEX_HEADER_SIZE,
  readRac1MobyBindPoseClass,
  readRac1MobyBindPoseClasses,
  readRac1RigidMobyClass,
  readRac1RigidMobyClasses,
} from "../.build/packages/rac1-moby/src/index.js";
import { VIF_UNPACK } from "../.build/packages/ps2-vif/src/index.js";

const cat = (...parts) => {
  const total = parts.reduce((n, part) => n + part.length, 0);
  const out = new Uint8Array(total);
  let at = 0;
  for (const part of parts) { out.set(part, at); at += part.length; }
  return out;
};
const u32 = (value) => new Uint8Array([value & 0xff, (value >>> 8) & 0xff, (value >>> 16) & 0xff, (value >>> 24) & 0xff]);
function unpack(vnvl, count, data) {
  const raw = ((0x60 | vnvl) << 24) | ((count & 0xff) << 16);
  const padded = new Uint8Array(Math.ceil(data.length / 4) * 4);
  padded.set(data);
  return cat(u32(raw >>> 0), padded);
}

function buildRigidClass({ jointCount = 0 } = {}) {
  const packetTable = 0x80;
  const vifOffset = 0x90;
  const st = new Uint8Array(12);
  const stv = new DataView(st.buffer);
  stv.setInt16(4, 4096, true);
  stv.setInt16(10, 4096, true);
  // Header + restart-coded [0,1,2] + 3 pipeline drain indices + terminator.
  const idx = Uint8Array.from([0, 0, 0, 0, 0x81, 0x82, 0x03, 0x01, 0x01, 0x01, 0x00, 0x00]);
  const vif = cat(unpack(VIF_UNPACK.V2_16, 3, st), unpack(VIF_UNPACK.V4_8, 3, idx));
  const vertexOffset = vifOffset + vif.length;
  const vertexBytes = 0x90;
  const out = new Uint8Array(vertexOffset + vertexBytes);
  out.set(vif, vifOffset);
  const view = new DataView(out.buffer);

  view.setInt32(0x00, packetTable, true);
  out[0x04] = 1; // high LOD packets
  out[0x08] = jointCount;
  view.setFloat32(0x24, 1, true);
  view.setFloat32(0x3c, 1, true); // raw radius -> 1/1024 after class scale

  view.setUint32(packetTable + 0x00, vifOffset, true);
  view.setUint16(packetTable + 0x04, vif.length / 0x10, true);
  view.setUint32(packetTable + 0x08, vertexOffset, true);
  out[packetTable + 0x0c] = vertexBytes / 0x10;
  out[packetTable + 0x0d] = 2; // floor((0xf + 3*6)/0x10)
  out[packetTable + 0x0e] = 1; // floor((3 + 3)/4)
  out[packetTable + 0x0f] = 3;

  // RAC1 8*u32 vertex header.
  view.setUint32(vertexOffset + 0x00, 0, true); // matrix transfers
  view.setUint32(vertexOffset + 0x04, 0, true); // two-way
  view.setUint32(vertexOffset + 0x08, 0, true); // three-way
  view.setUint32(vertexOffset + 0x0c, 3, true); // main
  view.setUint32(vertexOffset + 0x10, 0, true); // dupes
  view.setUint32(vertexOffset + 0x14, 3, true); // transfer total
  view.setUint32(vertexOffset + 0x18, RAC1_MOBY_VERTEX_HEADER_SIZE, true);
  view.setUint32(vertexOffset + 0x1c, vertexBytes, true);

  const v = vertexOffset + RAC1_MOBY_VERTEX_HEADER_SIZE;
  // Native positions. The real vertex indices are carried by the pipeline epilogue below.
  view.setInt16(v + 0x0a, 0, true); view.setInt16(v + 0x0c, 0, true); view.setInt16(v + 0x0e, 0, true);
  view.setInt16(v + 0x10 + 0x0a, 1024, true); view.setInt16(v + 0x10 + 0x0c, 0, true); view.setInt16(v + 0x10 + 0x0e, 0, true);
  view.setInt16(v + 0x20 + 0x0a, 0, true); view.setInt16(v + 0x20 + 0x0c, 1024, true); view.setInt16(v + 0x20 + 0x0e, 0, true);
  // For 3 in-file vertices the final pipeline vertex sits at relative 0x80 and stores the three pending indices.
  const epilogue = vertexOffset + 0x80;
  view.setUint16(epilogue + 0x04, 0, true);
  view.setUint16(epilogue + 0x06, 1, true);
  view.setUint16(epilogue + 0x08, 2, true);
  return out;
}

test("R&C1 rigid Moby decodes the 0x20 u32 vertex header and shared packet strip", () => {
  const mesh = readRac1RigidMobyClass(buildRigidClass());
  assert.equal(mesh.highLodPacketCount, 1);
  assert.equal(mesh.positions.length / 3, 3);
  assert.equal(mesh.indices.length / 3, 1);
  assert.deepEqual([...mesh.positions], [0, 0, 0, 1, 0, 0, 0, 1, 0]);
  assert.deepEqual([...mesh.triangleMaterialSlots], [0]);
  assert.ok(Math.abs(mesh.uvs[2] - 1) < 1e-6);
});

test("R&C1 rigid decoder refuses animated classes instead of approximating skinning", () => {
  assert.throws(() => readRac1RigidMobyClass(buildRigidClass({ jointCount: 4 })), /rigid decoder requires jointCount=0/);
});

test("R&C1 bind-pose decoder preserves the base surface of animated classes without applying animation", () => {
  const mesh = readRac1MobyBindPoseClass(buildRigidClass({ jointCount: 4 }));
  assert.equal(mesh.highLodPacketCount, 1);
  assert.deepEqual([...mesh.positions], [0, 0, 0, 1, 0, 0, 0, 1, 0]);
  assert.deepEqual([...mesh.indices], [1, 0, 2]);
});

test("R&C1 rigid class directory maps class-local texture slots and preserves animated ids", () => {
  const rigid = buildRigidClass();
  const animated = buildRigidClass({ jointCount: 2 });
  const assets = new Uint8Array(0x100 + rigid.length + 0x40 + animated.length);
  const rigidOffset = 0x100;
  const animatedOffset = rigidOffset + rigid.length + 0x40;
  assets.set(rigid, rigidOffset);
  assets.set(animated, animatedOffset);

  const index = new Uint8Array(0x200);
  const view = new DataView(index.buffer);
  const table = 0x80;
  view.setInt32(0x18, 2, true); view.setInt32(0x1c, table, true);
  // empty tie/shrub tables
  view.setInt32(0x20, 0, true); view.setInt32(0x24, 0, true);
  view.setInt32(0x28, 0, true); view.setInt32(0x2c, 0, true);
  view.setInt32(table + 0x00, rigidOffset, true); view.setInt32(table + 0x04, 123, true); index[table + 0x10] = 7;
  view.setInt32(table + 0x20, animatedOffset, true); view.setInt32(table + 0x24, 456, true);

  const all = readRac1MobyBindPoseClasses(index, assets);
  assert.equal(all.classes.size, 2);
  assert.deepEqual(all.animatedClassIds, [456]);
  assert.deepEqual(all.geometryFreeClassIds, []);
  assert.equal(all.classes.get(456)?.mesh.indices.length / 3, 1);

  const result = readRac1RigidMobyClasses(index, assets);
  assert.equal(result.classes.size, 1);
  assert.deepEqual(result.skippedAnimatedClassIds, [456]);
  const cls = result.classes.get(123);
  assert.ok(cls);
  assert.deepEqual([...cls.triangleTextureIds], [7]);
  assert.deepEqual(cls.textureIds, [7]);
});

test("R&C1 Moby class shorter than the shared 0x48 header is rejected", () => {
  const short = new Uint8Array(RAC1_MOBY_CLASS_HEADER_SIZE - 1);
  assert.throws(() => readRac1MobyBindPoseClass(short), /shorter than the 0x48 header/);
  assert.throws(() => readRac1RigidMobyClass(short), /shorter than the 0x48 header/);
});
