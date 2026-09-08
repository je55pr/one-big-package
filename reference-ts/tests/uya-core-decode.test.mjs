import test from "node:test";
import assert from "node:assert/strict";
import { BlobRandomAccessReader } from "../.build/packages/importer-common/src/index.js";
import {
  decodeUyaCoreDataPublicLead,
  probeUyaRcCollisionCompatibilityPublicLead,
} from "../.build/packages/uya-core-decode/src/index.js";
import {
  readUyaCoreDataWadLzHeaderPublicLead,
  readUyaLevelCoreHeaderPublicLead,
} from "../.build/packages/uya-level-core/src/index.js";
import {
  readUyaLevelDataHeaderPublicLead,
  readUyaLevelWadHeaderPublicLead,
} from "../.build/packages/uya-level-wad/src/index.js";

function packVertex(x16, y16, z64) {
  return ((x16 & 0x3ff) | ((y16 & 0x3ff) << 10) | ((z64 & 0xfff) << 20)) >>> 0;
}

function minimalCollision() {
  const bytes = new Uint8Array(0x80);
  const view = new DataView(bytes.buffer);
  view.setInt32(0x00, 0x40, true); // mesh
  view.setInt32(0x04, 0, true); // no hero groups
  const mesh = 0x40;
  view.setInt16(mesh + 0x00, 0, true); // z coord
  view.setUint16(mesh + 0x02, 1, true); // z count
  view.setUint16(mesh + 0x04, 2, true); // z node at mesh + 8
  view.setInt16(mesh + 0x08, 0, true); // y coord
  view.setUint16(mesh + 0x0a, 1, true);
  view.setUint32(mesh + 0x0c, 0x10, true); // y node at mesh + 0x10
  view.setInt16(mesh + 0x10, 0, true); // x coord
  view.setUint16(mesh + 0x12, 1, true);
  view.setUint32(mesh + 0x14, 0x20 << 8, true); // octant at mesh + 0x20
  const oct = mesh + 0x20;
  view.setUint16(oct + 0x00, 1, true); // face count
  view.setUint8(oct + 0x02, 3); // vertices
  view.setUint8(oct + 0x03, 0); // quads
  view.setUint32(oct + 0x04, packVertex(0, 0, 0), true);
  view.setUint32(oct + 0x08, packVertex(16, 0, 0), true);
  view.setUint32(oct + 0x0c, packVertex(0, 16, 0), true);
  bytes.set([0, 1, 2, 7], oct + 0x10);
  return bytes;
}

function literalWad(payload, name = "coredata") {
  if (payload.length < 18 || payload.length > 273) throw new Error("test payload must fit one big literal packet");
  const body = new Uint8Array(2 + payload.length);
  body[0] = 0;
  body[1] = payload.length - 18;
  body.set(payload, 2);
  const wad = new Uint8Array(0x10 + body.length);
  wad.set([0x57, 0x41, 0x44], 0);
  new DataView(wad.buffer).setInt32(3, wad.length, true);
  for (let i = 0; i < name.length && i < 9; i++) wad[7 + i] = name.charCodeAt(i);
  wad.set(body, 0x10);
  return wad;
}

function candidateLevel() {
  const assets = new Uint8Array(0x100);
  assets.set(minimalCollision(), 0x80);
  const wad = literalWad(assets);
  const bytes = new Uint8Array(0x3000);
  const view = new DataView(bytes.buffer);

  // 0x60 outer header, data at sector 1 for two sectors.
  view.setInt32(0x00, 0x60, true);
  view.setInt32(0x08, 7, true);
  view.setInt32(0x10, 1, true);
  view.setInt32(0x14, 2, true);

  const data = 0x800;
  view.setInt32(data + 0x08, 0x100, true); // coreIndex
  view.setInt32(data + 0x0c, 0x100, true);
  view.setInt32(data + 0x48, 0x300, true); // coreData
  view.setInt32(data + 0x4c, 0x200, true);

  const core = data + 0x100;
  view.setInt32(core + 0x14, 0x80, true); // collision
  view.setInt32(core + 0x60, 0x100, true); // next scalar boundary
  view.setInt32(core + 0x88, wad.length, true);
  view.setInt32(core + 0x8c, assets.length, true);
  bytes.set(wad, data + 0x300);
  return { bytes, assetsLength: assets.length, wadLength: wad.length };
}

async function openHeaders(bytes) {
  const reader = new BlobRandomAccessReader(new Blob([bytes]), "uya-candidate-level");
  const outer = await readUyaLevelWadHeaderPublicLead(reader);
  const data = await readUyaLevelDataHeaderPublicLead(reader, outer);
  const core = await readUyaLevelCoreHeaderPublicLead(reader, outer, data);
  const wad = await readUyaCoreDataWadLzHeaderPublicLead(reader, outer, data);
  return { reader, outer, data, core, wad };
}

test("explicit UYA core compatibility probe decodes exact WAD block and derives collision range", async () => {
  const fixture = candidateLevel();
  const h = await openHeaders(fixture.bytes);
  const decoded = await decodeUyaCoreDataPublicLead(h.reader, h.outer, h.data, h.core, h.wad);
  assert.equal(decoded.compressedCoreData.length, fixture.wadLength);
  assert.equal(decoded.assets.length, fixture.assetsLength);
  assert.equal(decoded.compatibilityChecks.publicCompressedSizeMatchesWad, true);
  assert.equal(decoded.compatibilityChecks.publicDecompressedSizeMatchesOutput, true);
  assert.deepEqual(decoded.publicCollisionRange, { offset: 0x80, size: 0x80 });
  assert.deepEqual(decoded.sectionBoundaries, [0x80, 0x100]);
  assert.match(decoded.compressedCoreDataSha256, /^[0-9a-f]{64}$/);
  assert.match(decoded.assetsSha256, /^[0-9a-f]{64}$/);
});

test("unchanged GC collision parser accepts the synthetic public UYA collision candidate", async () => {
  const fixture = candidateLevel();
  const h = await openHeaders(fixture.bytes);
  const decoded = await decodeUyaCoreDataPublicLead(h.reader, h.outer, h.data, h.core, h.wad);
  const collision = probeUyaRcCollisionCompatibilityPublicLead(decoded);
  assert.equal(collision.byteLength, 0x80);
  assert.equal(collision.octantCount, 1);
  assert.equal(collision.vertexCount, 3);
  assert.equal(collision.triangleCount, 1);
  assert.deepEqual(collision.materialIds, [7]);
  assert.deepEqual(collision.nativeBounds, {
    min: { x: 2, y: 2, z: 2 },
    max: { x: 3, y: 3, z: 2 },
  });
});

test("size-field disagreement is reported, not coerced into compatibility", async () => {
  const fixture = candidateLevel();
  const view = new DataView(fixture.bytes.buffer);
  view.setInt32(0x800 + 0x100 + 0x8c, fixture.assetsLength + 1, true);
  const h = await openHeaders(fixture.bytes);
  const decoded = await decodeUyaCoreDataPublicLead(h.reader, h.outer, h.data, h.core, h.wad);
  assert.equal(decoded.compatibilityChecks.publicDecompressedSizeMatchesOutput, false);
  assert.match(decoded.warnings.join(" "), /decompressed-size field/);
});
