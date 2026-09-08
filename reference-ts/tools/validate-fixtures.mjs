import { validateWorld, worldStats } from "../.build/packages/core/src/index.js";
import { syntheticWorlds } from "../.build/packages/fixtures/src/index.js";

let failed = false;
for (const world of syntheticWorlds) {
  const issues = validateWorld(world);
  if (issues.length) {
    failed = true;
    console.error(`${world.id}: invalid`);
    for (const issue of issues) console.error(`  ${issue.path}: ${issue.message}`);
  } else {
    console.log(`${world.id}: ok`, worldStats(world));
  }
}
if (failed) process.exitCode = 1;
