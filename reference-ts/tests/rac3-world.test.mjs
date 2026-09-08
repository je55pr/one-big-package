import test from "node:test";
import assert from "node:assert/strict";
import { matrixFromPosRotScale, transformPoint } from "../.build/packages/gc-instances/src/index.js";
import { rac3MobyRotationToObpEuler, rac3PointToObp } from "../.build/packages/rac3-world/src/index.js";
import { trs } from "../.build/apps/viewer/src/math.js";

function apply(matrix, point) {
  return [
    matrix[0] * point[0] + matrix[4] * point[1] + matrix[8] * point[2] + matrix[12],
    matrix[1] * point[0] + matrix[5] * point[1] + matrix[9] * point[2] + matrix[13],
    matrix[2] * point[0] + matrix[6] * point[1] + matrix[10] * point[2] + matrix[14],
  ];
}

test("RAC3 Moby Euler conversion preserves the native transform after Z-up to Y-up basis change", () => {
  const rotations = [
    [0, 0, 0],
    [0.37, -0.61, 1.12],
    [-1.4, 0.9, -2.2],
    [2.1, -1.2, 0.4],
  ];
  const position = [3, -4, 5];
  const local = [2, -3, 4];
  const scale = 1.7;

  for (const rotation of rotations) {
    const native = transformPoint(matrixFromPosRotScale(position, rotation, scale), ...local);
    const expected = rac3PointToObp(native);
    const obpPosition = rac3PointToObp(position);
    const obpLocal = rac3PointToObp(local);
    const obpRotation = rac3MobyRotationToObpEuler(rotation);
    const actual = apply(trs(obpPosition, obpRotation, [scale, scale, scale]), obpLocal);
    for (let axis = 0; axis < 3; axis++) {
      assert.ok(Math.abs(actual[axis] - expected[axis]) < 1e-5, `${rotation}: axis ${axis} ${actual[axis]} != ${expected[axis]}`);
    }
  }
});
