import { mkdir, writeFile } from "node:fs/promises";
import { basename, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { LocalFileRandomAccessReader } from "./local-random-access.mjs";
import { readRac1LevelCatalogue, openRac1LevelCoreRange } from "../.build/packages/rac1-disc-index/src/index.js";
import {
  openRac1LevelDataSource,
  readRac1LevelDataDirectory,
  openRac1LevelDataRange,
  RAC1_LEVEL_DATA_CORE_INDEX_SLOT,
  RAC1_LEVEL_DATA_CORE_DATA_SLOT,
} from "../.build/packages/rac1-level-data/src/index.js";
import { readRac1CoreIndexHeader, readRac1CoreData } from "../.build/packages/rac1-level-core/src/index.js";
import { readRac1ClassDirectory } from "../.build/packages/rac1-level-classes/src/index.js";
import { readRac1MobyBindPoseClasses } from "../.build/packages/rac1-moby/src/index.js";
import { parseRac1GameplayInstances, RAC1_GAMEPLAY_RANGE_SLOT } from "../.build/packages/rac1-instances/src/index.js";
import { readWadLz } from "../.build/packages/wad-lz/src/index.js";

export async function censusRac1MobyLinkage(reader) {
  const catalogue = await readRac1LevelCatalogue(reader);
  const levels = [];
  const totals = makeCounts();
  const globalClassIds = makeClassSets();

  for (const level of catalogue.levels) {
    const levelData = openRac1LevelDataSource(reader, level);
    const directory = await readRac1LevelDataDirectory(levelData);
    const coreIndexReader = openRac1LevelDataRange(levelData, directory, RAC1_LEVEL_DATA_CORE_INDEX_SLOT);
    const coreDataReader = openRac1LevelDataRange(levelData, directory, RAC1_LEVEL_DATA_CORE_DATA_SLOT);
    if (!coreIndexReader || !coreDataReader) throw new Error(`R&C1 level ${level.levelId} is missing core index/data.`);
    const coreHeader = await readRac1CoreIndexHeader(coreIndexReader);
    const index = await coreIndexReader.read(0, coreIndexReader.size);
    const coreData = await readRac1CoreData(coreDataReader, coreHeader);
    const classDirectory = readRac1ClassDirectory(index, coreData.data.length);
    const classes = readRac1MobyBindPoseClasses(index, coreData.data);
    const animated = new Set(classes.animatedClassIds);
    const entriesByClass = new Map();
    for (const entry of classDirectory.moby.entries) {
      const list = entriesByClass.get(entry.oClass) ?? [];
      list.push(entry);
      entriesByClass.set(entry.oClass, list);
    }

    const gameplayReader = openRac1LevelCoreRange(reader, level, RAC1_GAMEPLAY_RANGE_SLOT);
    if (!gameplayReader) throw new Error(`R&C1 level ${level.levelId} is missing gameplay range ${RAC1_GAMEPLAY_RANGE_SLOT}.`);
    const gameplayDecoded = await readWadLz(gameplayReader, 0, { maxOutputBytes: 64 * 1024 * 1024 });
    const gameplay = parseRac1GameplayInstances(gameplayDecoded.data);

    const counts = makeCounts();
    const classIds = makeClassSets();
    for (const instance of gameplay.mobyInstances) {
      counts.placementCount++;
      const oClass = instance.oClass;
      const cls = classes.classes.get(oClass);
      const entries = entriesByClass.get(oClass) ?? [];

      if (cls) {
        if (cls.mesh.positions.length === 0) {
          counts.geometryFreePayload++;
          classIds.geometryFreePayload.add(oClass);
          continue;
        }
        counts.linkedGeometry++;
        classIds.linkedGeometry.add(oClass);
        if (animated.has(oClass)) {
          counts.linkedAnimated++;
          classIds.linkedAnimated.add(oClass);
        } else {
          counts.linkedRigid++;
          classIds.linkedRigid.add(oClass);
        }
        continue;
      }

      if (entries.length === 0) {
        counts.absentClassTableEntry++;
        classIds.absentClassTableEntry.add(oClass);
      } else if (entries.every((entry) => entry.assetOffset === 0)) {
        counts.zeroAssetTableEntry++;
        classIds.zeroAssetTableEntry.add(oClass);
      } else {
        counts.payloadDecodeGap++;
        classIds.payloadDecodeGap.add(oClass);
      }
    }

    counts.unlinked = counts.geometryFreePayload + counts.zeroAssetTableEntry + counts.absentClassTableEntry + counts.payloadDecodeGap;
    assertAccounting(level.levelId, counts);
    addCounts(totals, counts);
    mergeClassSets(globalClassIds, classIds);
    const report = { levelId: level.levelId, ...counts, classIds: serializeClassSets(classIds) };
    levels.push(report);
    console.log(`MOBY_LINKAGE_LEVEL level=${level.levelId} placements=${counts.placementCount} linked=${counts.linkedGeometry} rigid=${counts.linkedRigid} animated=${counts.linkedAnimated} unlinked=${counts.unlinked} geometryFree=${counts.geometryFreePayload} zeroAsset=${counts.zeroAssetTableEntry} absentClass=${counts.absentClassTableEntry} decodeGap=${counts.payloadDecodeGap}`);
  }

  totals.unlinked = totals.geometryFreePayload + totals.zeroAssetTableEntry + totals.absentClassTableEntry + totals.payloadDecodeGap;
  assertAccounting("all", totals);
  if (totals.payloadDecodeGap !== 0) throw new Error(`R&C1 Moby linkage census found ${totals.payloadDecodeGap} nonzero class payload placements without decoded models.`);

  return {
    generator: "tools/rac1-moby-linkage-census.mjs",
    interpretation: "A placement is model-linked exactly when its level-local Moby class has a decoded non-empty bind/rest-pose surface. Unlinked placements are partitioned by native table/payload reason; payloadDecodeGap is a hard failure.",
    totals: { ...totals, classIds: serializeClassSets(globalClassIds) },
    levels,
  };
}

function makeCounts() {
  return {
    placementCount: 0,
    linkedGeometry: 0,
    linkedRigid: 0,
    linkedAnimated: 0,
    unlinked: 0,
    geometryFreePayload: 0,
    zeroAssetTableEntry: 0,
    absentClassTableEntry: 0,
    payloadDecodeGap: 0,
  };
}

function makeClassSets() {
  return {
    linkedGeometry: new Set(),
    linkedRigid: new Set(),
    linkedAnimated: new Set(),
    geometryFreePayload: new Set(),
    zeroAssetTableEntry: new Set(),
    absentClassTableEntry: new Set(),
    payloadDecodeGap: new Set(),
  };
}

function addCounts(target, source) {
  for (const key of Object.keys(target)) target[key] += source[key];
}
function mergeClassSets(target, source) {
  for (const key of Object.keys(target)) for (const value of source[key]) target[key].add(value);
}
function serializeClassSets(sets) {
  return Object.fromEntries(Object.entries(sets).map(([key, values]) => [key, [...values].sort((a, b) => a - b)]));
}
function assertAccounting(levelId, counts) {
  if (counts.linkedGeometry + counts.unlinked !== counts.placementCount) {
    throw new Error(`R&C1 Moby linkage accounting failed for ${levelId}: ${counts.linkedGeometry}+${counts.unlinked} != ${counts.placementCount}.`);
  }
  if (counts.linkedRigid + counts.linkedAnimated !== counts.linkedGeometry) {
    throw new Error(`R&C1 Moby linked rigid/animated accounting failed for ${levelId}.`);
  }
}

const isCli = process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url);
if (isCli) {
  const isoPath = resolve(process.argv[2] ?? "");
  const outDir = resolve(process.argv[3] ?? "research/generated");
  if (!process.argv[2]) throw new Error("Usage: node tools/rac1-moby-linkage-census.mjs <rac1-iso> [out-dir]");
  const reader = await LocalFileRandomAccessReader.open(isoPath, basename(isoPath));
  try {
    const report = await censusRac1MobyLinkage(reader);
    await mkdir(outDir, { recursive: true });
    const path = resolve(outDir, "rac1-local.moby-linkage-census.json");
    await writeFile(path, JSON.stringify(report, null, 2) + "\n");
    const t = report.totals;
    console.log(`MOBY_LINKAGE_CENSUS placements=${t.placementCount} linked=${t.linkedGeometry} rigid=${t.linkedRigid} animated=${t.linkedAnimated} unlinked=${t.unlinked} geometryFree=${t.geometryFreePayload} zeroAsset=${t.zeroAssetTableEntry} absentClass=${t.absentClassTableEntry} decodeGap=${t.payloadDecodeGap}`);
    console.log(`MOBY_LINKAGE_CLASS_IDS ${JSON.stringify(t.classIds)}`);
    console.log(`wrote ${path}`);
  } finally {
    await reader.close();
  }
}
