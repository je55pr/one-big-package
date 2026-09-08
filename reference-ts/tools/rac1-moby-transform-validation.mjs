import { mkdir, writeFile } from "node:fs/promises";
import { basename, resolve } from "node:path";
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
import {
  parseRac1GameplayInstances,
  RAC1_GAMEPLAY_RANGE_SLOT,
  transformRac1MobyPoint,
} from "../.build/packages/rac1-instances/src/index.js";
import { readRac1MobyBindPoseClasses } from "../.build/packages/rac1-moby/src/index.js";
import { readRcTfrags } from "../.build/packages/rc-tfrag/src/index.js";
import { readRcCollision, obpCollisionBounds } from "../.build/packages/rc-collision/src/index.js";
import { readWadLz } from "../.build/packages/wad-lz/src/index.js";

const isoPath = resolve(process.argv[2] ?? "");
const outDir = resolve(process.argv[3] ?? "research/generated");
if (!process.argv[2]) throw new Error("Usage: node tools/rac1-moby-transform-validation.mjs <rac1-iso> [out-dir]");

const reader = await LocalFileRandomAccessReader.open(isoPath, basename(isoPath));
try {
  const catalogue = await readRac1LevelCatalogue(reader);
  const levels = [];
  const totals = {
    levelCount: 0,
    placementCount: 0,
    rigidPlacementCount: 0,
    animatedPlacementCount: 0,
    geometryFreePlacementCount: 0,
    missingPayloadPlacementCount: 0,
    rigidClassCountReferenced: 0,
    transformedVertexCount: 0,
    nonFiniteTransformedVertexCount: 0,
    negativeScalePlacementCount: 0,
    zeroScalePlacementCount: 0,
    centerInsideStaticBoundsCount: 0,
    geometryIntersectsStaticBoundsCount: 0,
    maxSphereExcess: -Infinity,
    maxSphereRelativeExcess: -Infinity,
    worstSphereObservation: null,
    maxAbsScale: 0,
  };
  const referencedRigidClasses = new Set();

  for (const level of catalogue.levels) {
    const levelData = openRac1LevelDataSource(reader, level);
    const directory = await readRac1LevelDataDirectory(levelData);
    const coreIndexReader = openRac1LevelDataRange(levelData, directory, RAC1_LEVEL_DATA_CORE_INDEX_SLOT);
    const coreDataReader = openRac1LevelDataRange(levelData, directory, RAC1_LEVEL_DATA_CORE_DATA_SLOT);
    if (!coreIndexReader || !coreDataReader) throw new Error(`R&C1 level ${level.levelId} is missing core index/data.`);
    const coreHeader = await readRac1CoreIndexHeader(coreIndexReader);
    const index = await coreIndexReader.read(0, coreIndexReader.size);
    const coreData = await readRac1CoreData(coreDataReader, coreHeader);
    const classes = readRac1MobyBindPoseClasses(index, coreData.data);
    const animated = new Set(classes.animatedClassIds);
    const geometryFree = new Set(classes.geometryFreeClassIds);

    const gameplayReader = openRac1LevelCoreRange(reader, level, RAC1_GAMEPLAY_RANGE_SLOT);
    if (!gameplayReader) throw new Error(`R&C1 level ${level.levelId} is missing gameplay range ${RAC1_GAMEPLAY_RANGE_SLOT}.`);
    const gameplayDecoded = await readWadLz(gameplayReader, 0, { maxOutputBytes: 64 * 1024 * 1024 });
    const gameplay = parseRac1GameplayInstances(gameplayDecoded.data);

    const tfrags = readRcTfrags(coreData.data.subarray(coreHeader.tfragsOffset, coreHeader.tfragsEndBoundary));
    const collision = readRcCollision(coreData.data.subarray(coreHeader.collisionOffset, coreHeader.collisionEndBoundary));
    const collisionBounds = obpCollisionBounds(collision);
    const staticBounds = unionBounds(
      { min: { x: tfrags.bounds.min[0], y: tfrags.bounds.min[1], z: tfrags.bounds.min[2] }, max: { x: tfrags.bounds.max[0], y: tfrags.bounds.max[1], z: tfrags.bounds.max[2] } },
      collisionBounds,
    );

    const report = {
      levelId: level.levelId,
      placements: gameplay.mobyInstances.length,
      rigidPlacements: 0,
      animatedPlacements: 0,
      geometryFreePlacements: 0,
      missingPayloadPlacements: 0,
      transformedVertices: 0,
      nonFiniteTransformedVertices: 0,
      negativeScalePlacements: 0,
      zeroScalePlacements: 0,
      centerInsideStaticBounds: 0,
      geometryIntersectsStaticBounds: 0,
      maxSphereExcess: -Infinity,
      maxSphereRelativeExcess: -Infinity,
      worstSphereObservation: null,
      maxAbsScale: 0,
      placedRigidBounds: null,
      staticBounds,
    };
    let placedMin = { x: Infinity, y: Infinity, z: Infinity };
    let placedMax = { x: -Infinity, y: -Infinity, z: -Infinity };

    for (const instance of gameplay.mobyInstances) {
      const cls = classes.classes.get(instance.oClass);
      if (!cls) {
        report.missingPayloadPlacements++;
        continue;
      }
      if (animated.has(instance.oClass)) {
        report.animatedPlacements++;
        continue;
      }
      if (geometryFree.has(instance.oClass) || cls.mesh.positions.length === 0) {
        report.geometryFreePlacements++;
        continue;
      }

      report.rigidPlacements++;
      referencedRigidClasses.add(instance.oClass);
      const absScale = Math.abs(instance.scale);
      report.maxAbsScale = Math.max(report.maxAbsScale, absScale);
      if (instance.scale < 0) report.negativeScalePlacements++;
      if (instance.scale === 0) report.zeroScalePlacements++;

      const centerObp = nativeToObp(instance.position);
      if (pointInsideBounds(centerObp, staticBounds)) report.centerInsideStaticBounds++;

      const sourceEntry = cls.sourceEntry;
      const classAt = sourceEntry.assetOffset;
      if (classAt <= 0 || classAt + 0x40 > coreData.data.length) {
        throw new Error(`R&C1 level ${level.levelId} Moby class ${instance.oClass} sphere header is out of range.`);
      }
      const cv = new DataView(coreData.data.buffer, coreData.data.byteOffset, coreData.data.byteLength);
      const classScale = cls.mesh.scale / 1024;
      const localSphereCenter = [
        cv.getFloat32(classAt + 0x30, true) * classScale,
        cv.getFloat32(classAt + 0x34, true) * classScale,
        cv.getFloat32(classAt + 0x38, true) * classScale,
      ];
      const localSphereRadius = Math.abs(cv.getFloat32(classAt + 0x3c, true) * classScale);
      const sphereCenterNative = transformRac1MobyPoint(
        instance,
        localSphereCenter[0], localSphereCenter[1], localSphereCenter[2],
      );
      const sphereRadius = localSphereRadius * absScale;

      let instanceMin = { x: Infinity, y: Infinity, z: Infinity };
      let instanceMax = { x: -Infinity, y: -Infinity, z: -Infinity };
      for (let v = 0; v < cls.mesh.positions.length; v += 3) {
        const local = [cls.mesh.positions[v], cls.mesh.positions[v + 1], cls.mesh.positions[v + 2]];
        const native = transformRac1MobyPoint(instance, local[0], local[1], local[2]);
        const obp = nativeToObp(native);
        report.transformedVertices++;
        if (!obp.every(Number.isFinite)) {
          report.nonFiniteTransformedVertices++;
          continue;
        }
        includePoint(instanceMin, instanceMax, obp);
        includePoint(placedMin, placedMax, obp);
        const sphereDistance = Math.hypot(
          native[0] - sphereCenterNative[0],
          native[1] - sphereCenterNative[1],
          native[2] - sphereCenterNative[2],
        );
        const sphereExcess = sphereDistance - sphereRadius;
        const sphereRelativeExcess = sphereRadius > 0 ? sphereExcess / sphereRadius : sphereExcess > 0 ? Infinity : 0;
        if (sphereExcess > report.maxSphereExcess) {
          report.maxSphereExcess = sphereExcess;
          report.maxSphereRelativeExcess = sphereRelativeExcess;
          report.worstSphereObservation = {
            instanceIndex: instance.index,
            oClass: instance.oClass,
            instanceScale: instance.scale,
            classScale: cls.mesh.scale,
            localSphereCenter,
            localSphereRadius,
            sphereRadius,
            sphereDistance,
            sphereExcess,
            sphereRelativeExcess,
            localVertex: local,
          };
        }
      }
      if (Number.isFinite(instanceMin.x) && boundsIntersect({ min: instanceMin, max: instanceMax }, staticBounds)) {
        report.geometryIntersectsStaticBounds++;
      }
    }

    if (Number.isFinite(placedMin.x)) report.placedRigidBounds = { min: placedMin, max: placedMax };
    if (!Number.isFinite(report.maxSphereExcess)) report.maxSphereExcess = 0;
    if (!Number.isFinite(report.maxSphereRelativeExcess)) report.maxSphereRelativeExcess = 0;
    levels.push(report);

    totals.levelCount++;
    totals.placementCount += report.placements;
    totals.rigidPlacementCount += report.rigidPlacements;
    totals.animatedPlacementCount += report.animatedPlacements;
    totals.geometryFreePlacementCount += report.geometryFreePlacements;
    totals.missingPayloadPlacementCount += report.missingPayloadPlacements;
    totals.transformedVertexCount += report.transformedVertices;
    totals.nonFiniteTransformedVertexCount += report.nonFiniteTransformedVertices;
    totals.negativeScalePlacementCount += report.negativeScalePlacements;
    totals.zeroScalePlacementCount += report.zeroScalePlacements;
    totals.centerInsideStaticBoundsCount += report.centerInsideStaticBounds;
    totals.geometryIntersectsStaticBoundsCount += report.geometryIntersectsStaticBounds;
    if (report.maxSphereExcess > totals.maxSphereExcess) {
      totals.maxSphereExcess = report.maxSphereExcess;
      totals.maxSphereRelativeExcess = report.maxSphereRelativeExcess;
      totals.worstSphereObservation = report.worstSphereObservation ? { levelId: level.levelId, ...report.worstSphereObservation } : null;
    }
    totals.maxAbsScale = Math.max(totals.maxAbsScale, report.maxAbsScale);

    console.log(
      `MOBY_TRANSFORM_LEVEL level=${report.levelId} placements=${report.placements} rigid=${report.rigidPlacements} animated=${report.animatedPlacements} geometryFree=${report.geometryFreePlacements} missingPayload=${report.missingPayloadPlacements} vertices=${report.transformedVertices} nonFinite=${report.nonFiniteTransformedVertices} negativeScale=${report.negativeScalePlacements} zeroScale=${report.zeroScalePlacements} centerInsideStatic=${report.centerInsideStaticBounds} geometryIntersectsStatic=${report.geometryIntersectsStaticBounds} maxSphereExcess=${report.maxSphereExcess} maxSphereRelativeExcess=${report.maxSphereRelativeExcess} maxAbsScale=${report.maxAbsScale}`,
    );
  }

  totals.rigidClassCountReferenced = referencedRigidClasses.size;
  if (!Number.isFinite(totals.maxSphereExcess)) totals.maxSphereExcess = 0;
  if (!Number.isFinite(totals.maxSphereRelativeExcess)) totals.maxSphereRelativeExcess = 0;
  if (totals.nonFiniteTransformedVertexCount !== 0) {
    throw new Error(`Candidate R&C1 Moby transform produced ${totals.nonFiniteTransformedVertexCount} non-finite vertices.`);
  }

  const output = {
    generator: "tools/rac1-moby-transform-validation.mjs",
    authority: basename(isoPath),
    transform: "production transformRac1MobyPoint: T * S * Rz * Ry * Rx (native column-vector convention), then RC Z-up -> OBP Y-up",
    interpretation: "The all-level retail census invokes the same promoted transform helper used by the importer, so archaeology and production placement math cannot silently diverge.",
    sphereObservation: "Native class-sphere containment is recorded as a diagnostic, not an Euler-order discriminator: uniform scale and rotation preserve sphere distance, and tiny quantization excesses are retained rather than hidden by a tolerance.",
    totals,
    levels,
  };
  await mkdir(outDir, { recursive: true });
  const path = resolve(outDir, "rac1-local.moby-transform-validation.json");
  await writeFile(path, JSON.stringify(output, null, 2) + "\n");
  console.log(`MOBY_TRANSFORM_VALIDATION levels=${totals.levelCount} placements=${totals.placementCount} rigid=${totals.rigidPlacementCount} animated=${totals.animatedPlacementCount} geometryFree=${totals.geometryFreePlacementCount} missingPayload=${totals.missingPayloadPlacementCount} rigidClasses=${totals.rigidClassCountReferenced} vertices=${totals.transformedVertexCount} nonFinite=${totals.nonFiniteTransformedVertexCount} negativeScale=${totals.negativeScalePlacementCount} zeroScale=${totals.zeroScalePlacementCount} centerInsideStatic=${totals.centerInsideStaticBoundsCount} geometryIntersectsStatic=${totals.geometryIntersectsStaticBoundsCount} maxSphereExcess=${totals.maxSphereExcess} maxSphereRelativeExcess=${totals.maxSphereRelativeExcess} maxAbsScale=${totals.maxAbsScale}`);
  if (totals.worstSphereObservation) console.log(`MOBY_TRANSFORM_WORST_SPHERE ${JSON.stringify(totals.worstSphereObservation)}`);
  console.log(`wrote ${path}`);
} finally {
  await reader.close();
}

function nativeToObp(p) { return [p[0], p[2], p[1]]; }
function includePoint(min, max, p) {
  min.x = Math.min(min.x, p[0]); min.y = Math.min(min.y, p[1]); min.z = Math.min(min.z, p[2]);
  max.x = Math.max(max.x, p[0]); max.y = Math.max(max.y, p[1]); max.z = Math.max(max.z, p[2]);
}
function pointInsideBounds(p, b) {
  return p[0] >= b.min.x && p[0] <= b.max.x && p[1] >= b.min.y && p[1] <= b.max.y && p[2] >= b.min.z && p[2] <= b.max.z;
}
function boundsIntersect(a, b) {
  return a.min.x <= b.max.x && a.max.x >= b.min.x && a.min.y <= b.max.y && a.max.y >= b.min.y && a.min.z <= b.max.z && a.max.z >= b.min.z;
}
function unionBounds(a, b) {
  return {
    min: { x: Math.min(a.min.x, b.min.x), y: Math.min(a.min.y, b.min.y), z: Math.min(a.min.z, b.min.z) },
    max: { x: Math.max(a.max.x, b.max.x), y: Math.max(a.max.y, b.max.y), z: Math.max(a.max.z, b.max.z) },
  };
}
