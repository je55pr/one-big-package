import test from "node:test";
import assert from "node:assert/strict";
import { BlobRandomAccessReader } from "../.build/packages/importer-common/src/index.js";
import {
  RAC1_CORE_INDEX_HEADER_SIZE,
  readRac1CoreIndexHeader,
  readRac1CoreData,
  readRac1CoreCollision,
  readRac1CoreSky,
  readRac1CoreTfrags,
} from "../.build/packages/rac1-level-core/src/index.js";

function reader(bytes, name) {
  return new BlobRandomAccessReader(new Blob([bytes]), name);
}

function minimalCollision() {
  const mesh = 0x40;
  const bytes = new Uint8Array(0x6c);
  const view = new DataView(bytes.buffer);
  view.setInt32(0, mesh, true);
  view.setInt16(mesh + 0, 0, true);
  view.setUint16(mesh + 2, 1, true);
  view.setUint16(mesh + 4, 2, true);
  view.setInt16(mesh + 8, 0, true);
  view.setUint16(mesh + 10, 1, true);
  view.setUint32(mesh + 12, 16, true);
  view.setInt16(mesh + 16, 0, true);
  view.setUint16(mesh + 18, 1, true);
  view.setUint32(mesh + 20, 24 << 8, true);
  const oct = mesh + 24;
  view.setUint16(oct, 1, true);
  bytes[oct + 2] = 3;
  bytes[oct + 3] = 0;
  view.setUint32(oct + 4, 0, true);
  view.setUint32(oct + 8, 16, true);
  view.setUint32(oct + 12, 16 << 10, true);
  bytes[oct + 16] = 0;
  bytes[oct + 17] = 1;
  bytes[oct + 18] = 2;
  bytes[oct + 19] = 42;
  return bytes;
}

function literalWad(data) {
  if (data.length < 18 || data.length > 273) throw new Error("test literal out of range");
  const body = new Uint8Array(2 + data.length);
  body[0] = 0;
  body[1] = data.length - 18;
  body.set(data, 2);
  const out = new Uint8Array(16 + body.length);
  out[0] = 0x57; out[1] = 0x41; out[2] = 0x44;
  new DataView(out.buffer).setInt32(3, out.length, true);
  out.set(body, 16);
  return out;
}

function fixture() {
  const collision = minimalCollision();
  const skyOffset = 0x40;
  const collisionOffset = 0x80;
  const coreData = new Uint8Array(collisionOffset + collision.length);
  // Empty but structurally valid shared tfrag block occupies [0, 0x40).
  const dataView = new DataView(coreData.buffer);
  dataView.setInt32(0x00, 0x40, true);
  dataView.setInt32(0x04, 0, true);
  // Empty but structurally valid shared sky header occupies [0x40, 0x80).
  coreData[skyOffset + 0] = 10;
  coreData[skyOffset + 1] = 20;
  coreData[skyOffset + 2] = 30;
  coreData[skyOffset + 3] = 0x80;
  dataView.setInt16(skyOffset + 0x06, 0, true); // shell_count
  dataView.setInt16(skyOffset + 0x0c, 0, true); // texture_count
  coreData.set(collision, collisionOffset);
  const wad = literalWad(coreData);

  const index = new Uint8Array(RAC1_CORE_INDEX_HEADER_SIZE);
  const view = new DataView(index.buffer);
  view.setInt32(0x08, 0, true);
  view.setInt32(0x10, skyOffset, true);
  view.setInt32(0x14, collisionOffset, true);
  view.setInt32(0x60, coreData.length, true);
  view.setInt32(0x88, wad.length, true);
  view.setInt32(0x8c, coreData.length, true);
  return { index, wad, coreData, skyOffset, collisionOffset };
}

test("R&C1 core-index keeps raw words and exposes retail-validated static/sky/collision/size boundaries", async () => {
  const { index, wad, coreData, skyOffset, collisionOffset } = fixture();
  const header = await readRac1CoreIndexHeader(reader(index, "core-index"));
  assert.equal(header.rawWords.length, 0xbc / 4);
  assert.equal(header.tfragsOffset, 0);
  assert.equal(header.tfragsEndBoundary, skyOffset);
  assert.equal(header.skyOffset, skyOffset);
  assert.equal(header.skyEndBoundary, collisionOffset);
  assert.equal(header.collisionOffset, collisionOffset);
  assert.equal(header.collisionEndBoundary, coreData.length);
  assert.equal(header.assetsCompressedSize, wad.length);
  assert.equal(header.assetsDecompressedSize, coreData.length);
});

test("R&C1 core data validates compressed and decompressed native size words", async () => {
  const { index, wad, coreData } = fixture();
  const header = await readRac1CoreIndexHeader(reader(index, "core-index"));
  const decoded = await readRac1CoreData(reader(wad, "core-data"), header);
  assert.deepEqual([...decoded.data], [...coreData]);

  const bad = index.slice();
  new DataView(bad.buffer).setInt32(0x8c, coreData.length + 1, true);
  const badHeader = await readRac1CoreIndexHeader(reader(bad, "bad-index"));
  await assert.rejects(readRac1CoreData(reader(wad, "core-data"), badHeader), /decompressed size/);
});

test("R&C1 core static-terrain path uses the shared RC tfrag codec", async () => {
  const { index, wad, skyOffset } = fixture();
  const result = await readRac1CoreTfrags(reader(index, "core-index"), reader(wad, "core-data"));
  assert.equal(result.tfragsOffset, 0);
  assert.equal(result.tfragsSize, skyOffset);
  assert.equal(result.mesh.tfragCount, 0);
  assert.equal(result.mesh.indices.length, 0);
});

test("R&C1 core sky path uses the shared RC sky codec", async () => {
  const { index, wad, skyOffset, collisionOffset } = fixture();
  const result = await readRac1CoreSky(reader(index, "core-index"), reader(wad, "core-data"));
  assert.ok(result);
  assert.equal(result.skyOffset, skyOffset);
  assert.equal(result.skySize, collisionOffset - skyOffset);
  assert.deepEqual(result.sky.colour.slice(0, 3).map((v) => Math.round(v * 255)), [10, 20, 30]);
  assert.equal(result.sky.shells.length, 0);
  assert.equal(result.sky.textures.length, 0);
});

test("R&C1 core collision path reuses the shared RC octree parser", async () => {
  const { index, wad, collisionOffset } = fixture();
  const result = await readRac1CoreCollision(reader(index, "core-index"), reader(wad, "core-data"));
  assert.equal(result.mesh.meshOffset, 0x40);
  assert.equal(result.mesh.octants.length, 1);
  assert.equal(result.mesh.positions.length / 3, 3);
  assert.equal(result.mesh.triangles.length, 1);
  assert.deepEqual(result.mesh.materialIds, [42]);
  assert.equal(result.collisionOffset, collisionOffset);
});
