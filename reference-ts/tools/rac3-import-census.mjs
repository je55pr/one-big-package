#!/usr/bin/env node
import { writeFile } from "node:fs/promises";
import { resolve } from "node:path";
import { worldStats, validateWorld } from "../.build/packages/core/src/index.js";
import { importRac3World } from "../.build/packages/rac3-world/src/index.js";
import { probeUyaDiscToc } from "../.build/packages/uya-disc-toc/src/index.js";
import { LocalFileRandomAccessReader } from "./local-random-access.mjs";
import { assertUyaAuthorityTocWindow } from "./uya-retail-authority.mjs";

const args = process.argv.slice(2);
if (args.length < 1 || args.includes("--help")) {
  console.error("Usage: npm run build && node tools/rac3-import-census.mjs <UYA authority ISO> [--out FILE]");
  process.exit(2);
}
const iso = resolve(args[0]);
let out;
for (let i = 1; i < args.length; i++) {
  if (args[i] === "--out") out = resolve(args[++i]);
  else throw new Error(`Unknown argument '${args[i]}'.`);
}

const reader = await LocalFileRandomAccessReader.open(iso, "rac3-authority");
try {
  const authority = await assertUyaAuthorityTocWindow(reader);
  const toc = await probeUyaDiscToc(reader);
  const mainRows = toc.levelRows.filter((row) =>
    row.parts.filter((part) => part.publicFormatHint?.label === "level").length === 1,
  );
  const rows = [];
  for (const row of mainRows) {
    const result = await importRac3World(reader, "rac3-ntscu-original", row.index, { embedTextureImages: false });
    const world = result.world;
    const issues = validateWorld(world);
    if (issues.length) throw new Error(`RAC3 table ${row.index} validation failed: ${JSON.stringify(issues.slice(0, 4))}`);
    const modelTriangles = new Map((world.models ?? []).map((model) => [
      model.id,
      model.meshes.reduce((sum, mesh) => sum + mesh.geometry.indices.length / 3, 0),
    ]));
    let expandedMobyTriangles = 0;
    for (const instance of world.instances) {
      if (instance.modelId) expandedMobyTriangles += modelTriangles.get(instance.modelId) ?? 0;
    }
    const stats = worldStats(world);
    const item = {
      tableIndex: row.index,
      publicNativeLevelIdHint: result.publicNativeLevelIdHint,
      worldMeshCount: stats.meshCount,
      worldRenderTriangles: stats.renderTriangles,
      expandedMobyTriangles,
      totalVisibleTriangles: stats.renderTriangles + expandedMobyTriangles,
      collisionTriangles: stats.collisionTriangles,
      materialCount: world.materials.length,
      mobyModelCount: result.mobyModelCount,
      mobyInstanceCount: result.mobyInstanceCount,
      linkedMobyInstanceCount: result.linkedMobyInstanceCount,
      mobiesWithPvar: result.mobiesWithPvar,
      tieInstanceCount: result.tieInstanceCount,
      shrubInstanceCount: result.shrubInstanceCount,
      skyShellCount: result.skyShellCount,
    };
    rows.push(item);
    console.log(`RAC3 table ${row.index}: meshes=${item.worldMeshCount} visibleTris=${item.totalVisibleTriangles} mobies=${item.mobyInstanceCount} linked=${item.linkedMobyInstanceCount} pvars=${item.mobiesWithPvar}`);
  }

  const report = {
    schemaVersion: 1,
    evidenceStatus: "production RAC3 importer exercised against exact retail UYA authority; hidden-ToC/outer-range provenance caveats remain unchanged",
    authority,
    mainLevelRows: rows.length,
    tableIndices: rows.map((row) => row.tableIndex),
    totals: rows.reduce((sum, row) => ({
      worldRenderTriangles: sum.worldRenderTriangles + row.worldRenderTriangles,
      expandedMobyTriangles: sum.expandedMobyTriangles + row.expandedMobyTriangles,
      totalVisibleTriangles: sum.totalVisibleTriangles + row.totalVisibleTriangles,
      collisionTriangles: sum.collisionTriangles + row.collisionTriangles,
      mobyInstances: sum.mobyInstances + row.mobyInstanceCount,
      linkedMobyInstances: sum.linkedMobyInstances + row.linkedMobyInstanceCount,
      mobiesWithPvar: sum.mobiesWithPvar + row.mobiesWithPvar,
      tieInstances: sum.tieInstances + row.tieInstanceCount,
      shrubInstances: sum.shrubInstances + row.shrubInstanceCount,
    }), {
      worldRenderTriangles: 0, expandedMobyTriangles: 0, totalVisibleTriangles: 0,
      collisionTriangles: 0, mobyInstances: 0, linkedMobyInstances: 0,
      mobiesWithPvar: 0, tieInstances: 0, shrubInstances: 0,
    }),
    rows,
  };
  const json = JSON.stringify(report, null, 2) + "\n";
  if (out) await writeFile(out, json);
  else console.log(json);
} finally {
  await reader.close();
}
