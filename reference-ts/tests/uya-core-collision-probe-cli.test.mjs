import test from "node:test";
import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";

test("UYA core/collision compatibility CLI loads built modules and exposes usage", () => {
  const run = spawnSync(process.execPath, ["tools/uya-core-collision-probe.mjs", "--help"], {
    cwd: process.cwd(),
    encoding: "utf8",
  });
  assert.equal(run.status, 2);
  assert.match(run.stderr, /Usage:\s+npm run build && node tools\/uya-core-collision-probe\.mjs/);
  assert.match(run.stderr, /OBP_UYA_HTTP_RANGE_URL/);
  assert.match(run.stderr, /OBP_UYA_HTTP_RANGE_PARTS_JSON/);
  assert.match(run.stderr, /identity hash/i);
  assert.equal(run.stdout, "");
});
