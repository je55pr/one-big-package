import { mkdir, writeFile } from "node:fs/promises";
import { basename, dirname, resolve } from "node:path";
import { LocalFileRandomAccessReader } from "./local-random-access.mjs";
import { pngDataUri } from "./lib-png.mjs";
import { importRac1WorldSlice } from "../.build/packages/rac1-world/src/index.js";
import { readRac1LevelCatalogue } from "../.build/packages/rac1-disc-index/src/index.js";
import {
  openRac1LevelDataSource,
  readRac1LevelDataDirectory,
  openRac1LevelDataRange,
  RAC1_LEVEL_DATA_CORE_INDEX_SLOT,
  RAC1_LEVEL_DATA_CORE_DATA_SLOT,
  RAC1_LEVEL_DATA_GS_RAM_SLOT,
} from "../.build/packages/rac1-level-data/src/index.js";
import { readRac1CoreIndexHeader, readRac1CoreData } from "../.build/packages/rac1-level-core/src/index.js";
import { RAC1_TEXTURES_BASE_FIELD_OFFSET, readRac1TextureTable } from "../.build/packages/rac1-level-textures/src/index.js";
import { readRac1ClassDirectory } from "../.build/packages/rac1-level-classes/src/index.js";
import { readRac1MobyBindPoseClasses } from "../.build/packages/rac1-moby/src/index.js";

const isoPath = resolve(process.argv[2] ?? "");
const levelId = Number(process.argv[3]);
const outPath = resolve(process.argv[4] ?? "");
if (!process.argv[2] || !Number.isInteger(levelId) || levelId < 0 || !process.argv[4]) {
  throw new Error("Usage: node tools/rac1-world-export.mjs <rac1-iso> <level-id> <out.world.json>");
}

const reader = await LocalFileRandomAccessReader.open(isoPath, basename(isoPath));
try {
  // Production importer is the source of world geometry/placements/models.
  const observation = await importRac1WorldSlice(reader, "rac1-ntscu-original", levelId);
  const world = observation.world;

  // Capture-only enrichment: recover the same decoded native texture tables and
  // attach lossless PNG data URIs to the neutral materials. This deliberately
  // lives outside rac1-world so Node/zlib never becomes a production importer
  // dependency and no retail pixels are committed to source control.
  const catalogue = await readRac1LevelCatalogue(reader);
  const level = catalogue.levels.find((candidate) => candidate.levelId === levelId);
  if (!level) throw new Error(`R&C1 level ${levelId} not found in native catalogue.`);
  const levelData = openRac1LevelDataSource(reader, level);
  const directory = await readRac1LevelDataDirectory(levelData);
  const indexReader = openRac1LevelDataRange(levelData, directory, RAC1_LEVEL_DATA_CORE_INDEX_SLOT);
  const dataReader = openRac1LevelDataRange(levelData, directory, RAC1_LEVEL_DATA_CORE_DATA_SLOT);
  const gsReader = openRac1LevelDataRange(levelData, directory, RAC1_LEVEL_DATA_GS_RAM_SLOT);
  if (!indexReader || !dataReader || !gsReader) throw new Error(`R&C1 level ${levelId} is missing capture texture inputs.`);
  const coreHeader = await readRac1CoreIndexHeader(indexReader);
  const index = await indexReader.read(0, indexReader.size);
  const coreData = await readRac1CoreData(dataReader, coreHeader);
  const gsRam = await gsReader.read(0, gsReader.size);
  const iv = new DataView(index.buffer, index.byteOffset, index.byteLength);
  const texturesBaseOffset = iv.getInt32(RAC1_TEXTURES_BASE_FIELD_OFFSET, true);

  const imageByMaterial = new Map();
  for (const kind of ["tfrag", "moby", "tie", "shrub"]) {
    const table = readRac1TextureTable(index, coreData.data, gsRam, texturesBaseOffset, kind);
    for (const texture of table.textures) {
      imageByMaterial.set(
        `rac1-${levelId}-${kind}-tex${texture.index}`,
        pngDataUri(texture.width, texture.height, texture.rgba),
      );
    }
  }

  const materialIds = new Set(world.materials.map((material) => material.id));
  for (const [id] of imageByMaterial) {
    if (!materialIds.has(id)) throw new Error(`Capture texture '${id}' has no neutral world material.`);
  }
  world.materials = world.materials.map((material) => ({
    ...material,
    ...(imageByMaterial.has(material.id) ? { image: imageByMaterial.get(material.id) } : {}),
  }));

  // Independent linkage accounting. This is deliberately separate from rigid vs
  // animated transform categories: animated classes can be valid reusable bind-pose
  // models, while geometry-free animated classes must remain unlinked.
  const classDirectory = readRac1ClassDirectory(index, coreData.data.length);
  const mobyClasses = readRac1MobyBindPoseClasses(index, coreData.data);
  const entriesByClass = new Map();
  for (const entry of classDirectory.moby.entries) {
    const list = entriesByClass.get(entry.oClass) ?? [];
    list.push(entry);
    entriesByClass.set(entry.oClass, list);
  }
  const unlinkedReasons = { zeroAssetTableEntry: 0, geometryFreePayload: 0, absentClassTableEntry: 0, payloadDecodeGap: 0 };
  const unlinkedClassIds = { zeroAssetTableEntry: new Set(), geometryFreePayload: new Set(), absentClassTableEntry: new Set(), payloadDecodeGap: new Set() };
  let linked = 0;
  for (const instance of world.instances) {
    if (instance.modelId !== undefined) {
      linked++;
      continue;
    }
    const oClass = Number(instance.sourceClass);
    if (!Number.isInteger(oClass)) throw new Error(`R&C1 level ${levelId} instance '${instance.id}' has non-numeric sourceClass '${instance.sourceClass}'.`);
    const cls = mobyClasses.classes.get(oClass);
    const entries = entriesByClass.get(oClass) ?? [];
    let reason;
    if (cls && cls.mesh.positions.length === 0) reason = "geometryFreePayload";
    else if (entries.length > 0 && entries.every((entry) => entry.assetOffset === 0)) reason = "zeroAssetTableEntry";
    else if (entries.length === 0) reason = "absentClassTableEntry";
    else reason = "payloadDecodeGap";
    unlinkedReasons[reason]++;
    unlinkedClassIds[reason].add(oClass);
  }
  const classifiedUnlinked = Object.values(unlinkedReasons).reduce((sum, count) => sum + count, 0);
  if (linked + classifiedUnlinked !== world.instances.length) {
    throw new Error(`R&C1 level ${levelId} linkage accounting ${linked}+${classifiedUnlinked} != ${world.instances.length}.`);
  }
  if (unlinkedReasons.payloadDecodeGap !== 0) {
    throw new Error(`R&C1 level ${levelId} has ${unlinkedReasons.payloadDecodeGap} placements whose nonzero class payload failed to produce a model.`);
  }

  const metadata = {
    levelId,
    worldId: world.id,
    displayName: world.displayName,
    worldMeshes: world.meshes.length,
    models: world.models?.length ?? 0,
    instances: world.instances.length,
    linkedInstances: linked,
    unlinkedInstances: classifiedUnlinked,
    unlinkedReasons,
    unlinkedClassIds: Object.fromEntries(Object.entries(unlinkedClassIds).map(([reason, ids]) => [reason, [...ids].sort((a, b) => a - b)])),
    materials: world.materials.length,
    texturedMaterials: world.materials.filter((material) => material.image).length,
    bounds: world.bounds,
  };

  await mkdir(dirname(outPath), { recursive: true });
  await writeFile(outPath, JSON.stringify(world, typedArrayReplacer) + "\n");
  await writeFile(outPath.replace(/\.world\.json$/i, ".world-meta.json"), JSON.stringify(metadata, null, 2) + "\n");
  console.log(`RAC1_WORLD_EXPORT ${JSON.stringify(metadata)}`);
  console.log(outPath);
} finally {
  await reader.close();
}

function typedArrayReplacer(_key, value) {
  if (ArrayBuffer.isView(value) && !(value instanceof DataView)) return Array.from(value);
  return value;
}
