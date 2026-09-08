import test from "node:test";
import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";

test("UYA tfrag compatibility CLI loads built modules and exposes local and HTTP usage", () => {
  const run = spawnSync(process.execPath, ["tools/uya-tfrag-compat-probe.mjs", "--help"], {
    cwd: process.cwd(),
    encoding: "utf8",
  });
  assert.equal(run.status, 2);
  assert.match(run.stderr, /--iso '<retail UYA ISO>' --table-index N/);
  assert.match(run.stderr, /OBP_UYA_HTTP_RANGE_URL=/);
  assert.match(run.stderr, /OBP_UYA_HTTP_RANGE_PARTS_JSON=/);
  assert.equal(run.stdout, "");
});
