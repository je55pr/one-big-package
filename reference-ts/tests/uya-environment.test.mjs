import test from "node:test";
import assert from "node:assert/strict";
import { uyaEnvironmentFromGameplay } from "../tools/lib-uya-environment.mjs";

function gameplayFixture() {
  const bytes = new Uint8Array(0x100);
  const view = new DataView(bytes.buffer);
  const at = 0x20;
  view.setInt32(0x00, at, true);
  for (const [off, value] of [[0x00,128],[0x04,128],[0x08,128],[0x0c,125],[0x10,115],[0x14,80]]) view.setInt32(at + off, value, true);
  view.setFloat32(at + 0x18, 51200, true);
  view.setFloat32(at + 0x1c, 204800, true);
  view.setFloat32(at + 0x20, 255, true);
  view.setFloat32(at + 0x24, 140.25, true);
  view.setFloat32(at + 0x28, 60, true);
  view.setInt32(at + 0x2c, 1, true);
  view.setFloat32(at + 0x30, 1, true);
  view.setFloat32(at + 0x34, 2, true);
  view.setFloat32(at + 0x38, 3, true);
  view.setFloat32(at + 0x3c, 10, true);
  view.setFloat32(at + 0x40, 20, true);
  view.setFloat32(at + 0x44, 30, true);
  view.setFloat32(at + 0x48, 0.5, true);
  return bytes;
}

test("promotes retail-censused GC/UYA settings into OBP Y-up environment fields", () => {
  const source = { game: "rac3", buildId: "test", levelId: "table-1", assetKind: "world" };
  const { environment, summary } = uyaEnvironmentFromGameplay(gameplayFixture(), source);
  assert.deepEqual(environment.backgroundColor, [128 / 255, 128 / 255, 128 / 255]);
  assert.deepEqual(environment.fogColor, [125 / 255, 115 / 255, 80 / 255]);
  assert.equal(environment.fogNearDistance, 51200);
  assert.equal(environment.fogFarDistance, 204800);
  assert.equal(environment.deathHeight, 60);
  assert.equal(environment.isSphericalWorld, true);
  assert.deepEqual(environment.sphereCenter, { x: 1, y: 3, z: 2 });
  assert.equal(environment.source.assetKind, "level-settings");
  assert.deepEqual(summary.shipPosition, [10, 20, 30]);
  assert.equal(summary.shipRotationZ, 0.5);
});
