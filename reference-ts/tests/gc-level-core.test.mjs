import test from "node:test";
import assert from "node:assert/strict";
import { BlobRandomAccessReader } from "../.build/packages/importer-common/src/index.js";
import { openGcLevelCore, gcLevelCoreCollision, gcLevelCoreSectionRange, GC_LEVEL_CORE_HEADER_SIZE } from "../.build/packages/gc-level-core/src/index.js";
import { readRcCollision } from "../.build/packages/rc-collision/src/index.js";

/** Wrap `payload` as a WAD-LZ container holding it in one big-literal packet (payload must be 18..273 bytes). */
function wadLiteral(payload) {
  if (payload.length < 18 || payload.length > 273) throw new Error("wadLiteral payload must be 18..273 bytes");
  const body = [0x00, payload.length - 18, ...payload];
  const size = 16 + body.length;
  const header = new Uint8Array(16);
  header[0] = 0x57; header[1] = 0x41; header[2] = 0x44;
  header[3] = size & 0xff; header[4] = (size >> 8) & 0xff; header[5] = (size >> 16) & 0xff; header[6] = (size >> 24) & 0xff;
  return new Uint8Array([...header, ...body]);
}

/** Empty-octree collision blob: CollisionHeader { meshOffset:8, heroGroups:0 } + a zero mesh root. */
const EMPTY_COLLISION = new Uint8Array([8, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0]);

/**
 * Build a synthetic GC level `data` lump:
 * GcUyaLevelDataHeader -> coreIndex (LevelCoreHeader) -> coreData (WAD-LZ asset blob).
 * `assetBlob` is the decompressed asset blob; `core` overrides LevelCoreHeader fields.
 */
function buildDataLump(assetBlob, core = {}) {
  const dataHeader = new Uint8Array(0x58);
  const dv = new DataView(dataHeader.buffer);
  const setRange = (at, offset, size) => { dv.setInt32(at, offset, true); dv.setInt32(at + 4, size, true); };

  const index = new Uint8Array(GC_LEVEL_CORE_HEADER_SIZE);
  const iv = new DataView(index.buffer);
  iv.setInt32(0x14, core.collision ?? 0, true);
  iv.setInt32(0x08, core.tfrags ?? 0, true);
  iv.setInt32(0x60, core.texturesBaseOffset ?? 0, true);
  iv.setInt32(0x8c, core.assetsDecompressedSize ?? assetBlob.length, true);
  iv.setInt32(0x88, core.assetsCompressedSize ?? 0, true);

  const coreData = wadLiteral(assetBlob);
  setRange(0x00, 0, 0);                       // overlay
  setRange(0x08, 0x58, index.length);         // coreIndex
  setRange(0x10, 0, 0);                       // gsRam
  setRange(0x18, 0, 0);                       // hudHeader
  for (let i = 0; i < 5; i++) setRange(0x20 + i * 8, 0, 0); // hudBanks
  setRange(0x48, 0x58 + index.length, coreData.length);     // coreData
  setRange(0x50, 0, 0);                       // transitionTextures

  return new Uint8Array([...dataHeader, ...index, ...coreData]);
}

function reader(bytes) {
  return new BlobRandomAccessReader(new Blob([bytes]), "LEVEL.WAD#lump0");
}

test("parses the data header, decompresses the asset blob, and locates the collision section", async () => {
  const asset = new Uint8Array(40);
  asset.set(EMPTY_COLLISION, 16); // collision at offset 16
  const lump = buildDataLump(asset, { collision: 16, tfrags: 4, assetsDecompressedSize: 40 });

  const core = await openGcLevelCore(reader(lump));
  assert.equal(core.assets.length, 40);
  assert.equal(core.coreHeader.collision, 16);
  assert.equal(core.coreHeader.tfrags, 4);
  assert.equal(core.dataHeader.coreIndex.offset, 0x58);

  // boundaries: tfrags 4, collision 16, assetsDecompressedSize 40 -> section [16, 40)
  assert.deepEqual(gcLevelCoreSectionRange(core, 16), { offset: 16, size: 24 });
  assert.deepEqual(gcLevelCoreSectionRange(core, 4), { offset: 4, size: 12 });

  const collision = gcLevelCoreCollision(core);
  assert.equal(collision.length, 24);
  const mesh = readRcCollision(collision.subarray(0, 12));
  assert.equal(mesh.octants.length, 0); // empty octree fixture
});

test("rejects a decompressed size that disagrees with the header", async () => {
  const asset = new Uint8Array(40);
  const lump = buildDataLump(asset, { collision: 16, assetsDecompressedSize: 999 });
  await assert.rejects(openGcLevelCore(reader(lump)), /decompressed asset blob is 40 bytes but the header declares 999/);
});

test("rejects a coreData range outside the data lump", async () => {
  const asset = new Uint8Array(40);
  const lump = buildDataLump(asset, { collision: 16 });
  new DataView(lump.buffer).setInt32(0x48, lump.length + 1000, true); // coreData.offset past the end
  await assert.rejects(openGcLevelCore(reader(lump)), /coreData range/);
});

test("gcLevelCoreCollision returns null when there is no collision section", async () => {
  const asset = new Uint8Array(40);
  const core = await openGcLevelCore(reader(buildDataLump(asset, { collision: 0, assetsDecompressedSize: 40 })));
  assert.equal(gcLevelCoreCollision(core), null);
  assert.equal(gcLevelCoreSectionRange(core, 0), null);
});

test("class-table offsets contribute section boundaries", async () => {
  const asset = new Uint8Array(64);
  asset.set(EMPTY_COLLISION, 8);
  // one moby class entry whose offset_in_asset_wad is 24 -> caps the collision section at [8, 24)
  const lump = buildDataLump(asset, { collision: 8, assetsDecompressedSize: 64 });
  const iv = new DataView(lump.buffer, 0x58, GC_LEVEL_CORE_HEADER_SIZE);
  // moby_classes ArrayRange @ 0x18 { count, offset }; put the table at index offset 0x40, one entry
  iv.setInt32(0x18, 1, true);
  iv.setInt32(0x1c, 0x40, true);
  iv.setInt32(0x40, 24, true); // entry[0].offset_in_asset_wad

  const core = await openGcLevelCore(reader(lump));
  assert.deepEqual(gcLevelCoreSectionRange(core, 8), { offset: 8, size: 16 });
});
