import test from "node:test";
import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";

test("UYA retail world CLI loads built modules and exposes bounded candidate usage", () => {
  const run = spawnSync(process.execPath, ["tools/uya-world.mjs", "--help"], {
    cwd: process.cwd(),
    encoding: "utf8",
  });
  assert.equal(run.status, 2);
  assert.match(run.stderr, /tools\/uya-world\.mjs <uya\.iso> --table-index N --out <world\.json>/);
  assert.match(run.stderr, /--no-textures/);
  assert.match(run.stderr, /--no-collision/);
  assert.match(run.stderr, /--no-static/);
  assert.equal(run.stdout, "");
});
