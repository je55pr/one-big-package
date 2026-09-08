import test from "node:test";
import assert from "node:assert/strict";
import { trs } from "../.build/apps/viewer/src/math.js";

function point(matrix, [x, y, z]) {
  return [
    matrix[0] * x + matrix[4] * y + matrix[8] * z + matrix[12],
    matrix[1] * x + matrix[5] * y + matrix[9] * z + matrix[13],
    matrix[2] * x + matrix[6] * y + matrix[10] * z + matrix[14],
  ];
}

test("viewer TRS follows neutral T * Rz * Ry * Rx * S convention", () => {
  const matrix = trs([10, 20, 30], [Math.PI / 2, 0, 0], [2, 3, 4]);
  const transformed = point(matrix, [0, 1, 0]);
  assert.ok(Math.abs(transformed[0] - 10) < 1e-6);
  assert.ok(Math.abs(transformed[1] - 20) < 1e-6);
  assert.ok(Math.abs(transformed[2] - 33) < 1e-6);
});
