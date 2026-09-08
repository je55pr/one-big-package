import test from "node:test";
import assert from "node:assert/strict";
import { assembleRac1WorldSlice } from "../.build/packages/rac1-world/src/index.js";
import { validateWorld, worldStats } from "../.build/packages/core/src/index.js";

function texture(index, rgba = [128, 64, 32, 255]) {
  return { index, width: 1, height: 1, type: 7, paletteSlot: 3, dataOffset: 0x20 + index, raw0x0c: -1, raw0x0e: -1, rgba: new Uint8Array(rgba) };
}

function decodedFixture() {
  return {
    buildId: "rac1-ntscu-original",
    level: { tableSlot: 0, tableRawSecondWord: 19462, headerLba: 1885903, levelId: 0 },
    texturesBaseOffset: 0x400,
    textures: [
      texture(0),
      { ...texture(1, [1, 2, 3, 128]), type: 9, paletteSlot: 4, raw0x0c: 5 },
    ],
    tfrags: {
      positions: Float64Array.from([10, 20, 30, 11, 20, 30, 10, 21, 30]),
      uvs: Float32Array.from([0, 0, 1, 0, 0, 1]),
      colors: Float32Array.from([1, 1, 1, 1, 1, 1, 1, 1, 1]),
      indices: Uint32Array.from([0, 1, 2]),
      triangleTextureIds: Int32Array.from([0]),
      tfragCount: 1,
      bounds: { min: [10, 20, 30], max: [11, 21, 30] },
      textureIds: [0],
    },
    collision: {
      meshOffset: 0x40,
      heroGroupsOffset: 0,
      heroGroupCount: 0,
      octants: [],
      positions: Float64Array.from([9, 19, 39, 12, 19, 39, 9, 22, 41]),
      triangles: [{ a: 0, b: 1, c: 2, materialId: 31, octantIndex: 0, fromQuad: false }],
      bounds: { min: { x: 9, y: 19, z: 39 }, max: { x: 12, y: 22, z: 41 } },
      materialIds: [31],
    },
  };
}

function staticMesh(textureId) {
  return {
    positions: Float64Array.from([0, 0, 0, 1, 0, 0, 0, 1, 0]),
    uvs: Float32Array.from([0, 0, 1, 0, 0, 1]),
    indices: Uint32Array.from([0, 1, 2]),
    triangleMaterialSlots: Int32Array.from([0]),
    scale: 1,
    triangleTextureIds: Int32Array.from([textureId]),
  };
}

function matrix(tx, ty, tz) {
  return [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, tx, ty, tz, 0.01];
}

test("R&C1 decoded terrain/collision/textures assemble into one neutral valid OBPWorld", () => {
  const world = assembleRac1WorldSlice(decodedFixture());
  assert.deepEqual(validateWorld(world), []);
  assert.equal(world.source.game, "rac1");
  assert.equal(world.source.levelId, "0");
  assert.equal(world.materials.length, 2);
  assert.equal(world.meshes.length, 1);
  assert.equal(world.meshes[0].materialId, "rac1-0-tfrag-tex0");
  assert.equal(world.collisionMeshes.length, 1);
  assert.deepEqual(world.collisionMeshes[0].triangleMaterialIds, [31]);
  assert.deepEqual(world.bounds, {
    min: { x: 9, y: 20, z: 19 },
    max: { x: 12, y: 41, z: 30 },
  });
  assert.deepEqual(worldStats(world), {
    meshCount: 1,
    renderTriangles: 1,
    collisionMeshCount: 1,
    collisionTriangles: 1,
    instanceCount: 0,
    splineCount: 0,
    volumeCount: 0,
    spawnPointCount: 0,
  });
});

test("R&C1 world bakes static placements, links class-local Moby models, and maps native environment", () => {
  const fixture = decodedFixture();
  fixture.mobyTextures = [texture(0)];
  fixture.tieTextures = [texture(0)];
  fixture.shrubTextures = [texture(0)];
  fixture.staticClasses = {
    directory: { moby: {}, tie: {}, shrub: {} },
    ties: new Map([[100, { oClass: 100, assetOffset: 0x100, mesh: staticMesh(0), triangleTextureIds: Int32Array.from([0]), textureIds: [0], sourceEntry: {} }]]),
    shrubs: new Map([[200, { oClass: 200, assetOffset: 0x200, mesh: staticMesh(0), triangleTextureIds: Int32Array.from([0]), textureIds: [0], sourceEntry: {} }]]),
  };
  fixture.mobyClasses = {
    classes: new Map([[300, {
      oClass: 300,
      assetOffset: 0x300,
      mesh: { ...staticMesh(0), highLodPacketCount: 1, boundingRadius: 1 },
      triangleTextureIds: Int32Array.from([0]),
      textureIds: [0],
      sourceEntry: { assetOffset: 0x300 },
    }]]),
    animatedClassIds: [],
    geometryFreeClassIds: [],
  };
  fixture.gameplay = {
    tieInstances: [{ index: 0, oClass: 100, matrix: matrix(100, 200, 300), raw0x04: 0, raw0x08: 0, raw0x0c: 0, uid: 1 }],
    shrubInstances: [{ index: 0, oClass: 200, matrix: matrix(400, 500, 600), raw0x04: 0, raw0x08: 0, raw0x0c: 0 }],
    mobyInstances: [{ index: 0, oClass: 300, scale: 2, position: [7, 8, 9], rotation: [0.37, -0.61, 1.12], spawnableMobyCount: 256 }],
    spawnableMobyCount: 256,
    decompressedSize: 0x1000,
  };
  fixture.settings = {
    backgroundColour: [71 / 255, 66 / 255, 58 / 255],
    fogColour: [47 / 255, 26 / 255, 15 / 255],
    fogNearDistance: 0,
    fogFarDistance: 240640,
    fogNearIntensity: 255,
    fogFarIntensity: 53.55,
    deathHeight: 27,
    shipPosition: [20, 20, 20],
    shipRotationZ: 0,
    shipPath: -1,
    shipCameraCuboidStart: 0,
    shipCameraCuboidEnd: 0,
    rawPadWords: [0, 0],
    blockOffset: 0xb0,
  };
  fixture.sky = { colour: [0.1, 0.2, 0.3, 1], clearScreen: true, maximumSpriteCount: 0, shells: [], textures: [] };

  const world = assembleRac1WorldSlice(fixture);
  assert.deepEqual(validateWorld(world), []);
  assert.equal(world.materials.length, 5);
  assert.equal(world.meshes.length, 3);
  const tie = world.meshes.find((mesh) => mesh.id === "rac1-0-tie-tex0");
  const shrub = world.meshes.find((mesh) => mesh.id === "rac1-0-shrub-tex0");
  assert.ok(tie);
  assert.ok(shrub);
  assert.deepEqual(tie.geometry.positions.slice(0, 3), [100, 300, 200]);
  assert.deepEqual(shrub.geometry.positions.slice(0, 3), [400, 600, 500]);

  assert.equal(world.models.length, 1);
  const model = world.models[0];
  assert.equal(model.id, "rac1-0-moby-class300");
  assert.equal(model.meshes.length, 1);
  assert.equal(model.meshes[0].materialId, "rac1-0-moby-tex0");
  assert.deepEqual(model.meshes[0].geometry.positions, [0, 0, 0, 1, 0, 0, 0, 0, 1]);

  assert.equal(world.instances.length, 1);
  const moby = world.instances[0];
  assert.equal(moby.sourceClass, 300);
  assert.equal(moby.modelId, "rac1-0-moby-class300");
  assert.deepEqual(moby.transform.position, { x: 7, y: 9, z: 8 });
  assert.deepEqual(moby.transform.scale, { x: 2, y: 2, z: 2 });
  assert.deepEqual(moby.properties.nativeRotation, [0.37, -0.61, 1.12]);
  assert.equal(moby.properties.classGeometryBinding, "neutral-model");
  assert.ok(Object.values(moby.transform.rotationEuler).every(Number.isFinite));

  assert.deepEqual(world.environment.skyColor, [0.1, 0.2, 0.3, 1]);
  assert.deepEqual(world.environment.backgroundColor.map((v) => Math.round(v * 255)), [71, 66, 58]);
  assert.deepEqual(world.environment.fogColor.map((v) => Math.round(v * 255)), [47, 26, 15]);
  assert.equal(world.environment.fogFarDistance, 240640);
  assert.equal(world.environment.deathHeight, 27);
  assert.equal(world.environment.isSphericalWorld, undefined);
  assert.deepEqual(world.bounds.max, { x: 401, y: 600, z: 501 });
  assert.equal(worldStats(world).renderTriangles, 3);
  assert.equal(worldStats(world).instanceCount, 1);
});

test("R&C1 Moby placements without a geometry-bearing class remain valid unlinked instances", () => {
  const fixture = decodedFixture();
  fixture.gameplay = {
    tieInstances: [],
    shrubInstances: [],
    mobyInstances: [{ index: 0, oClass: 999, scale: 1, position: [1, 2, 3], rotation: [0, 0, 0], spawnableMobyCount: 1 }],
    spawnableMobyCount: 1,
    decompressedSize: 0x100,
  };
  const world = assembleRac1WorldSlice(fixture);
  assert.deepEqual(validateWorld(world), []);
  assert.equal(world.instances[0].modelId, undefined);
  assert.equal(world.instances[0].properties.classGeometryBinding, "unavailable");
});

test("R&C1 world assembly refuses a terrain texture reference missing from the native table", () => {
  const fixture = decodedFixture();
  fixture.tfrags.triangleTextureIds = Int32Array.from([7]);
  fixture.tfrags.textureIds = [7];
  assert.throws(() => assembleRac1WorldSlice(fixture), /missing native texture material/);
});
