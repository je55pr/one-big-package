import test from "node:test";
import assert from "node:assert/strict";
import { validateWorld, worldStats } from "../.build/packages/core/src/index.js";
import { rac1SyntheticWorld, rac2SyntheticWorld } from "../.build/packages/fixtures/src/index.js";
import { BlobRandomAccessReader, ConcatenatedRandomAccessReader } from "../.build/packages/importer-common/src/index.js";
import { MemoryAssetStore, normalizeAssetKey } from "../.build/packages/storage/src/index.js";

test("synthetic worlds validate against OBP schema", () => {
  assert.deepEqual(validateWorld(rac1SyntheticWorld), []);
  assert.deepEqual(validateWorld(rac2SyntheticWorld), []);
});

test("geometry validation checks per-vertex array lengths", () => {
  const base = {
    schemaVersion: 1,
    id: "t",
    displayName: "t",
    source: { game: "synthetic", buildId: "t" },
    bounds: { min: { x: 0, y: 0, z: 0 }, max: { x: 1, y: 1, z: 1 } },
    materials: [],
    collisionMeshes: [],
    instances: [],
    splines: [],
    volumes: [],
    spawnPoints: [],
  };
  const mesh = (geometry) => ({ ...base, meshes: [{ id: "m", name: "m", geometry, source: base.source }] });
  const tri = { positions: [0, 0, 0, 1, 0, 0, 0, 1, 0], indices: [0, 1, 2] };

  assert.deepEqual(validateWorld(mesh({ ...tri, alpha: [1, 1, 1] })), []);
  assert.equal(validateWorld(mesh({ ...tri, alpha: [1, 1] })).length, 1);
  assert.equal(validateWorld(mesh({ ...tri, uvs: [0, 0, 1, 0] })).length, 1);
  assert.equal(validateWorld(mesh({ ...tri, colors: [1, 1, 1] })).length, 1);
});

test("model assets are local geometry and instance links must resolve", () => {
  const source = { game: "synthetic", buildId: "model-test" };
  const tri = { positions: [0, 0, 0, 1, 0, 0, 0, 1, 0], indices: [0, 1, 2] };
  const world = {
    schemaVersion: 1,
    id: "model-world",
    displayName: "model world",
    source,
    bounds: { min: { x: 0, y: 0, z: 0 }, max: { x: 1, y: 1, z: 1 } },
    materials: [],
    meshes: [],
    models: [{ id: "model-a", name: "model a", meshes: [{ id: "model-a-mesh", name: "mesh", geometry: tri, source }], source }],
    collisionMeshes: [],
    instances: [{
      id: "instance-a",
      name: "instance a",
      sourceClass: 7,
      modelId: "model-a",
      transform: { position: { x: 0, y: 0, z: 0 }, rotationEuler: { x: 0, y: 0, z: 0 }, scale: { x: 1, y: 1, z: 1 } },
      properties: {},
      source,
    }],
    splines: [],
    volumes: [],
    spawnPoints: [],
  };

  assert.deepEqual(validateWorld(world), []);
  assert.deepEqual(worldStats(world), {
    meshCount: 0,
    renderTriangles: 0,
    collisionMeshCount: 0,
    collisionTriangles: 0,
    instanceCount: 1,
    splineCount: 0,
    volumeCount: 0,
    spawnPointCount: 0,
  });
  const broken = structuredClone(world);
  broken.instances[0].modelId = "missing-model";
  assert.match(validateWorld(broken)[0].message, /does not exist/);
});

test("fixture stats are deterministic", () => {
  assert.deepEqual(worldStats(rac1SyntheticWorld), {
    meshCount: 2,
    renderTriangles: 4,
    collisionMeshCount: 1,
    collisionTriangles: 4,
    instanceCount: 1,
    splineCount: 0,
    volumeCount: 0,
    spawnPointCount: 1,
  });
});

test("R&C1 and R&C2 fixtures use the same world representation", () => {
  assert.equal(rac1SyntheticWorld.schemaVersion, rac2SyntheticWorld.schemaVersion);
  assert.equal(rac1SyntheticWorld.meshes.length, rac2SyntheticWorld.meshes.length);
  assert.notEqual(rac1SyntheticWorld.source.game, rac2SyntheticWorld.source.game);
});

test("blob random-access reader only returns requested ranges", async () => {
  const reader = new BlobRandomAccessReader(new Blob([new Uint8Array([10, 20, 30, 40, 50])]), "disc.iso");
  assert.equal(reader.name, "disc.iso");
  assert.equal(reader.size, 5);
  assert.deepEqual([...(await reader.read(1, 3))], [20, 30, 40]);
  assert.deepEqual([...(await reader.read(5, 0))], []);
  await assert.rejects(reader.read(4, 2), RangeError);
});

test("concatenated random-access reader spans split files without whole-part reads", async () => {
  class RecordingReader {
    constructor(name, bytes) {
      this.name = name;
      this.bytes = Uint8Array.from(bytes);
      this.size = this.bytes.length;
      this.reads = [];
    }

    async read(offset, length) {
      this.reads.push([offset, length]);
      return this.bytes.slice(offset, offset + length);
    }
  }

  const part1 = new RecordingReader("disc.iso.001", [1, 2, 3]);
  const part2 = new RecordingReader("disc.iso.002", [4, 5]);
  const part3 = new RecordingReader("disc.iso.003", [6, 7, 8, 9]);
  const reader = new ConcatenatedRandomAccessReader([part1, part2, part3], "disc.iso");

  assert.equal(reader.name, "disc.iso");
  assert.equal(reader.size, 9);
  assert.deepEqual([...(await reader.read(2, 4))], [3, 4, 5, 6]);
  assert.deepEqual(part1.reads, [[2, 1]]);
  assert.deepEqual(part2.reads, [[0, 2]]);
  assert.deepEqual(part3.reads, [[0, 1]]);
  assert.deepEqual([...(await reader.read(9, 0))], []);
  await assert.rejects(reader.read(8, 2), RangeError);
});

test("memory asset store preserves immutable byte snapshots", async () => {
  const store = new MemoryAssetStore();
  const original = new Uint8Array([1, 2, 3, 4, 5]);
  await store.put("worlds/rac1/test.bin", original);
  original[0] = 99;

  const reader = await store.open("worlds/rac1/test.bin");
  assert.ok(reader);
  assert.equal(reader.size, 5);
  assert.deepEqual([...(await reader.read(1, 3))], [2, 3, 4]);
  await assert.rejects(reader.read(4, 2), RangeError);

  const loaded = await store.get("worlds/rac1/test.bin");
  assert.deepEqual([...loaded], [1, 2, 3, 4, 5]);
  loaded[1] = 88;
  assert.deepEqual([...(await store.get("worlds/rac1/test.bin"))], [1, 2, 3, 4, 5]);
});

test("asset keys reject path traversal", () => {
  assert.throws(() => normalizeAssetKey("worlds/../secret"));
  assert.equal(normalizeAssetKey("/worlds\\rac2\\maktar.pack/"), "worlds/rac2/maktar.pack");
});
