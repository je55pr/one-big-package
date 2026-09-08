import test from "node:test";
import assert from "node:assert/strict";
import {
  RAC1_MOBY_INSTANCE_SIZE,
  RAC1_SHRUB_INSTANCE_SIZE,
  RAC1_TIE_INSTANCE_SIZE,
  parseRac1GameplayInstances,
  rac1MobyRotationToObpEuler,
  transformRac1InstancePoint,
  transformRac1MobyPoint,
} from "../.build/packages/rac1-instances/src/index.js";

function fixture() {
  const tieBlock = 0x100;
  const shrubBlock = tieBlock + 0x10 + RAC1_TIE_INSTANCE_SIZE;
  const mobyBlock = shrubBlock + 0x10 + RAC1_SHRUB_INSTANCE_SIZE;
  const bytes = new Uint8Array(mobyBlock + 0x10 + RAC1_MOBY_INSTANCE_SIZE);
  const view = new DataView(bytes.buffer);
  view.setInt32(0x34, tieBlock, true);
  view.setInt32(0x3c, shrubBlock, true);
  view.setInt32(0x44, mobyBlock, true);

  const writeMatrixInstance = (block, oClass, size, tx, ty, tz) => {
    view.setInt32(block, 1, true);
    const at = block + 0x10;
    view.setInt32(at, oClass, true);
    const matrix = [1, 0, 0, 0, 0, 2, 0, 0, 0, 0, 3, 0, tx, ty, tz, 0.01];
    matrix.forEach((value, i) => view.setFloat32(at + 0x10 + i * 4, value, true));
    assert.ok(at + size <= bytes.length);
  };
  writeMatrixInstance(tieBlock, 100, RAC1_TIE_INSTANCE_SIZE, 10, 20, 30);
  view.setInt32(tieBlock + 0x10 + 0x54, 1234, true);
  writeMatrixInstance(shrubBlock, 200, RAC1_SHRUB_INSTANCE_SIZE, 40, 50, 60);

  view.setInt32(mobyBlock, 1, true);
  view.setInt32(mobyBlock + 4, 256, true);
  const moby = mobyBlock + 0x10;
  view.setInt32(moby, RAC1_MOBY_INSTANCE_SIZE, true);
  view.setInt32(moby + 0x18, 300, true);
  view.setFloat32(moby + 0x1c, 1.5, true);
  view.setFloat32(moby + 0x30, 1, true);
  view.setFloat32(moby + 0x34, 2, true);
  view.setFloat32(moby + 0x38, 3, true);
  view.setFloat32(moby + 0x3c, 0.1, true);
  view.setFloat32(moby + 0x40, 0.2, true);
  view.setFloat32(moby + 0x44, 0.3, true);
  return bytes;
}

test("R&C1 gameplay placement parser decodes tie, shrub and moby generations", () => {
  const parsed = parseRac1GameplayInstances(fixture());
  assert.equal(parsed.tieInstances.length, 1);
  assert.equal(parsed.tieInstances[0].oClass, 100);
  assert.equal(parsed.tieInstances[0].uid, 1234);
  assert.deepEqual(parsed.tieInstances[0].matrix.slice(12, 15), [10, 20, 30]);
  assert.equal(parsed.shrubInstances.length, 1);
  assert.equal(parsed.shrubInstances[0].oClass, 200);
  assert.equal(parsed.mobyInstances.length, 1);
  assert.equal(parsed.mobyInstances[0].oClass, 300);
  assert.equal(parsed.mobyInstances[0].scale, 1.5);
  assert.deepEqual(parsed.mobyInstances[0].position, [1, 2, 3]);
  assert.equal(parsed.spawnableMobyCount, 256);
});

test("native instance point transform ignores the non-affine stored bottom-right word", () => {
  const matrix = [1, 0, 0, 0, 0, 2, 0, 0, 0, 0, 3, 0, 10, 20, 30, 0.01];
  assert.deepEqual(transformRac1InstancePoint(matrix, 2, 3, 4), [12, 26, 42]);
});

test("R&C1 Moby point transform applies uniform scale and translation", () => {
  const instance = { position: [10, 20, 30], rotation: [0, 0, 0], scale: 2 };
  assert.deepEqual(transformRac1MobyPoint(instance, 1, 2, 3), [12, 24, 36]);
});

test("R&C1 Moby point transform composes native Rx before Ry before Rz", () => {
  const instance = { position: [0, 0, 0], rotation: [Math.PI / 2, Math.PI / 2, 0], scale: 1 };
  const actual = transformRac1MobyPoint(instance, 1, 0, 0);
  assert.ok(Math.abs(actual[0]) < 1e-12);
  assert.ok(Math.abs(actual[1]) < 1e-12);
  assert.ok(Math.abs(actual[2] + 1) < 1e-12);
});

test("R&C1 Moby OBP Euler conversion is equivalent to native transform then Y/Z swap", () => {
  const nativeRotation = [0.37, -0.61, 1.12];
  const obpRotation = rac1MobyRotationToObpEuler(nativeRotation);
  const nativeInstance = { position: [0, 0, 0], rotation: nativeRotation, scale: 1 };
  const localNative = [1.7, -2.1, 0.6];
  const nativeWorld = transformRac1MobyPoint(nativeInstance, ...localNative);
  const expectedObp = [nativeWorld[0], nativeWorld[2], nativeWorld[1]];
  const localObp = [localNative[0], localNative[2], localNative[1]];
  const actualObp = applyEuler(localObp, obpRotation);
  for (let i = 0; i < 3; i++) assert.ok(Math.abs(actualObp[i] - expectedObp[i]) < 1e-12);
});

test("R&C1 moby size word is validated", () => {
  const bytes = fixture();
  const view = new DataView(bytes.buffer);
  const mobyBlock = view.getInt32(0x44, true);
  view.setInt32(mobyBlock + 0x10, 0x88, true);
  assert.throws(() => parseRac1GameplayInstances(bytes), /expected 0x78/);
});

function applyEuler([x, y, z], [rx, ry, rz]) {
  const sx = Math.sin(rx), cx = Math.cos(rx);
  const sy = Math.sin(ry), cy = Math.cos(ry);
  const sz = Math.sin(rz), cz = Math.cos(rz);
  const x1 = x, y1 = cx * y - sx * z, z1 = sx * y + cx * z;
  const x2 = cy * x1 + sy * z1, y2 = y1, z2 = -sy * x1 + cy * z1;
  return [cz * x2 - sz * y2, sz * x2 + cz * y2, z2];
}
