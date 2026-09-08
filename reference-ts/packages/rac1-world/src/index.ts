import { OBP_SCHEMA_VERSION } from "../../core/src/index.js";
import type { OBPBounds, OBPInstance, OBPMaterial, OBPMesh, OBPSourceRef, OBPWorld } from "../../core/src/index.js";
import type { RandomAccessReader } from "../../importer-common/src/index.js";
import { openRac1LevelCoreRange, readRac1LevelCatalogue } from "../../rac1-disc-index/src/index.js";
import type { Rac1NativeLevelCore } from "../../rac1-disc-index/src/index.js";
import {
  RAC1_GAMEPLAY_RANGE_SLOT,
  parseRac1GameplayInstances,
  rac1MobyRotationToObpEuler,
  transformRac1InstancePoint,
} from "../../rac1-instances/src/index.js";
import type { Rac1GameplayInstances, Rac1MatrixInstance } from "../../rac1-instances/src/index.js";
import { readRac1StaticClasses } from "../../rac1-level-classes/src/index.js";
import type { Rac1ShrubClass, Rac1StaticClasses, Rac1TieClass } from "../../rac1-level-classes/src/index.js";
import {
  RAC1_LEVEL_DATA_CORE_DATA_SLOT,
  RAC1_LEVEL_DATA_CORE_INDEX_SLOT,
  RAC1_LEVEL_DATA_GS_RAM_SLOT,
  openRac1LevelDataRange,
  openRac1LevelDataSource,
  readRac1LevelDataDirectory,
} from "../../rac1-level-data/src/index.js";
import { readRac1CoreData, readRac1CoreIndexHeader } from "../../rac1-level-core/src/index.js";
import { parseRac1LevelSettings } from "../../rac1-level-settings/src/index.js";
import type { Rac1LevelSettings } from "../../rac1-level-settings/src/index.js";
import { RAC1_TEXTURES_BASE_FIELD_OFFSET, readRac1TextureTable } from "../../rac1-level-textures/src/index.js";
import { readRac1MobyBindPoseClasses } from "../../rac1-moby/src/index.js";
import type { Rac1MobyBindPoseClasses } from "../../rac1-moby/src/index.js";
import { obpCollisionBounds, readRcCollision, toObpCollisionMesh } from "../../rc-collision/src/index.js";
import type { RcCollisionMesh } from "../../rc-collision/src/index.js";
import type { RcLevelTexture } from "../../rc-level-textures/src/index.js";
import { readRcSky } from "../../rc-sky/src/index.js";
import type { RcSky } from "../../rc-sky/src/index.js";
import { readRcTfrags, toObpRcTfragMeshes } from "../../rc-tfrag/src/index.js";
import type { RcTfragMesh } from "../../rc-tfrag/src/index.js";
import { readWadLz } from "../../wad-lz/src/index.js";
import { buildRac1MobyModels } from "./moby-models.js";

/**
 * Evidence-backed neutral R&C1 world slice.
 *
 * The native path covers terrain, collision, level texture tables, sky colour,
 * fog/background/death-height settings, baked placement of Tie/Shrub static
 * geometry, and neutral Moby class-model / placement relations. Moby models stay
 * class-local and are rendered only through OBPInstance transforms; they are not
 * fabricated as world-space meshes at the origin.
 */

export interface Rac1DecodedWorldSlice {
  readonly buildId: string;
  readonly level: Pick<Rac1NativeLevelCore, "tableSlot" | "tableRawSecondWord" | "headerLba" | "levelId">;
  readonly tfrags: RcTfragMesh;
  readonly collision: RcCollisionMesh;
  /** Native terrain texture table; legacy field name retained. */
  readonly textures: readonly RcLevelTexture[];
  readonly mobyTextures?: readonly RcLevelTexture[];
  readonly tieTextures?: readonly RcLevelTexture[];
  readonly shrubTextures?: readonly RcLevelTexture[];
  readonly texturesBaseOffset: number;
  readonly staticClasses?: Rac1StaticClasses;
  readonly mobyClasses?: Rac1MobyBindPoseClasses;
  readonly gameplay?: Rac1GameplayInstances;
  readonly settings?: Rac1LevelSettings;
  readonly sky?: RcSky | null;
}

export interface Rac1WorldImportObservation {
  readonly world: OBPWorld;
  readonly level: Rac1NativeLevelCore;
  readonly coreDataCompressedSize: number;
  readonly coreDataDecompressedSize: number;
  readonly gameplayCompressedSize: number;
  readonly gameplayDecompressedSize: number;
  readonly tfragsOffset: number;
  readonly collisionOffset: number;
  readonly texturesBaseOffset: number;
  readonly tieInstanceCount: number;
  readonly shrubInstanceCount: number;
  readonly mobyInstanceCount: number;
  readonly mobyModelCount: number;
  readonly mobyLinkedInstanceCount: number;
  readonly mobyUnlinkedInstanceCount: number;
}

type StaticClass = Rac1TieClass | Rac1ShrubClass;

/** Assemble already-decoded, retail-derived native structures into neutral OBP concepts. */
export function assembleRac1WorldSlice(input: Rac1DecodedWorldSlice): OBPWorld {
  const { level, tfrags, collision } = input;
  const levelId = String(level.levelId);
  const gameplay = input.gameplay;
  const staticClasses = input.staticClasses;
  const mobyClasses = input.mobyClasses;
  const mobyTextures = input.mobyTextures ?? [];
  const tieTextures = input.tieTextures ?? [];
  const shrubTextures = input.shrubTextures ?? [];

  const hasStaticPlacements = !!staticClasses || tieTextures.length > 0 || shrubTextures.length > 0 ||
    !!gameplay?.tieInstances.length || !!gameplay?.shrubInstances.length;
  if (hasStaticPlacements && (!gameplay || !staticClasses)) {
    throw new Error("R&C1 placed static geometry requires both decoded gameplay placements and native static class libraries.");
  }

  const worldSource: OBPSourceRef = {
    game: "rac1",
    buildId: input.buildId,
    levelId,
    assetKind: "world",
    originalId: level.levelId,
    originalOffset: level.headerLba * 0x800,
    notes: [
      `R&C1 native table slot ${level.tableSlot}`,
      `native level header LBA ${level.headerLba}`,
      `raw level-table second word ${level.tableRawSecondWord}`,
      "world slice contains retail-validated terrain, texture tables, collision, sky colour, level settings, baked Tie/Shrub geometry, neutral Moby placements, and reusable Moby class models when decoded",
      ...(gameplay ? [`parsed native placements: tie=${gameplay.tieInstances.length} shrub=${gameplay.shrubInstances.length} moby=${gameplay.mobyInstances.length}`] : []),
      ...(mobyClasses ? [`decoded native Moby class models: ${[...mobyClasses.classes.values()].filter((cls) => cls.mesh.indices.length > 0).length}`] : []),
    ],
  };

  const materials: OBPMaterial[] = [];
  const addTextureMaterials = (kind: "tfrag" | "moby" | "tie" | "shrub", textures: readonly RcLevelTexture[]): string => {
    const prefix = `rac1-${levelId}-${kind}`;
    for (const texture of textures) {
      materials.push({
        id: `${prefix}-tex${texture.index}`,
        name: `R&C1 native ${kind} texture ${texture.index}`,
        debugRgba: averageTextureRgba(texture.rgba),
        source: {
          game: "rac1",
          buildId: input.buildId,
          levelId,
          assetKind: "texture",
          originalId: `${kind}-${texture.index}`,
          originalOffset: input.texturesBaseOffset + texture.dataOffset,
          notes: [
            `native TextureEntry ${texture.width}x${texture.height}`,
            `type=${texture.type} paletteSlot=${texture.paletteSlot}`,
            `raw0x0c=${texture.raw0x0c} raw0x0e=${texture.raw0x0e}`,
            "debugRgba is an average preview; decoded retail pixels are not embedded in this browser-neutral slice",
          ],
        },
      });
    }
    return prefix;
  };

  const tfragMaterialPrefix = addTextureMaterials("tfrag", input.textures);
  const mobyMaterialPrefix = addTextureMaterials("moby", mobyTextures);
  const tieMaterialPrefix = addTextureMaterials("tie", tieTextures);
  const shrubMaterialPrefix = addTextureMaterials("shrub", shrubTextures);
  const materialIds = new Set(materials.map((material) => material.id));

  const mobyAssets = mobyClasses
    ? buildRac1MobyModels(mobyClasses, input.buildId, levelId, mobyMaterialPrefix, materialIds)
    : { models: [], modelIdsByClass: new Map<number, string>() };

  const terrainSource: OBPSourceRef = {
    game: "rac1",
    buildId: input.buildId,
    levelId,
    assetKind: "static-terrain",
    originalId: level.levelId,
  };
  const meshes: OBPMesh[] = toObpRcTfragMeshes(tfrags, terrainSource, `rac1-${levelId}-terrain`, tfragMaterialPrefix);
  validateMeshMaterials(meshes, materialIds, `R&C1 level ${levelId} terrain`);

  let bounds = unionBounds(tfragBounds(tfrags), obpCollisionBounds(collision));
  if (gameplay && staticClasses) {
    const tiePlaced = placeStaticClassInstances(
      "tie", gameplay.tieInstances, staticClasses.ties, input.buildId, levelId, tieMaterialPrefix,
    );
    const shrubPlaced = placeStaticClassInstances(
      "shrub", gameplay.shrubInstances, staticClasses.shrubs, input.buildId, levelId, shrubMaterialPrefix,
    );
    validateMeshMaterials(tiePlaced.meshes, materialIds, `R&C1 level ${levelId} Tie placements`);
    validateMeshMaterials(shrubPlaced.meshes, materialIds, `R&C1 level ${levelId} Shrub placements`);
    meshes.push(...tiePlaced.meshes, ...shrubPlaced.meshes);
    if (tiePlaced.bounds) bounds = unionBounds(bounds, tiePlaced.bounds);
    if (shrubPlaced.bounds) bounds = unionBounds(bounds, shrubPlaced.bounds);
  }

  const instances: OBPInstance[] = gameplay?.mobyInstances.map((instance) => {
    const [rx, ry, rz] = rac1MobyRotationToObpEuler(instance.rotation);
    const modelId = mobyAssets.modelIdsByClass.get(instance.oClass);
    return {
      id: `rac1-${levelId}-moby-${instance.index}`,
      name: `R&C1 Moby ${instance.oClass} instance ${instance.index}`,
      sourceClass: instance.oClass,
      ...(modelId ? { modelId } : {}),
      transform: {
        position: { x: instance.position[0], y: instance.position[2], z: instance.position[1] },
        rotationEuler: { x: rx, y: ry, z: rz },
        scale: { x: instance.scale, y: instance.scale, z: instance.scale },
      },
      properties: {
        nativeRotation: [...instance.rotation],
        nativeScale: instance.scale,
        spawnableMobyCount: instance.spawnableMobyCount,
        classGeometryBinding: modelId ? "neutral-model" : "unavailable",
      },
      source: {
        game: "rac1",
        buildId: input.buildId,
        levelId,
        assetKind: "moby-instance",
        originalId: instance.index,
        notes: [
          `native oClass ${instance.oClass}`,
          "native packed 0x78 placement transform promoted through retail runtime-basis archaeology plus all-level transform validation",
          modelId
            ? "linked to a reusable class-local OBP model asset; sourceClass remains the independent native gameplay/class identity"
            : "no geometry-bearing decoded class model is available; sourceClass is still preserved",
        ],
      },
    };
  }) ?? [];

  const collisionSource: OBPSourceRef = {
    game: "rac1",
    buildId: input.buildId,
    levelId,
    assetKind: "collision",
    originalId: level.levelId,
  };
  const collisionMesh = toObpCollisionMesh(
    collision,
    collisionSource,
    `rac1-${levelId}-collision`,
    `R&C1 native level ${levelId} collision`,
  );

  const environment = input.settings || input.sky ? {
    ...(input.sky ? { skyColor: input.sky.colour } : {}),
    ...(input.settings?.backgroundColour ? { backgroundColor: input.settings.backgroundColour } : {}),
    ...(input.settings?.fogColour ? { fogColor: input.settings.fogColour } : {}),
    ...(input.settings ? {
      fogNearDistance: input.settings.fogNearDistance,
      fogFarDistance: input.settings.fogFarDistance,
      fogNearIntensity: input.settings.fogNearIntensity,
      fogFarIntensity: input.settings.fogFarIntensity,
      deathHeight: input.settings.deathHeight,
    } : {}),
    source: {
      game: "rac1" as const,
      buildId: input.buildId,
      levelId,
      assetKind: "environment",
      originalId: level.levelId,
      notes: [
        ...(input.settings ? [
          `native gameplay settings block offset 0x${input.settings.blockOffset.toString(16)}`,
          "R&C1 0x50-byte settings generation: no GC spherical-world fields are synthesized",
          `native ship parking point retained by rac1-level-settings: (${input.settings.shipPosition.join(", ")})`,
        ] : []),
        ...(input.sky ? ["sky colour comes from the independently validated native core sky block"] : []),
        "sky shell geometry remains camera-centred in the native renderer and is not parked at an invented world transform",
      ],
    },
  } : undefined;

  return {
    schemaVersion: OBP_SCHEMA_VERSION,
    id: `rac1-level${levelId}`,
    displayName: `Ratchet & Clank native level ${levelId}`,
    source: worldSource,
    bounds,
    materials,
    meshes,
    ...(mobyAssets.models.length ? { models: mobyAssets.models } : {}),
    collisionMeshes: [collisionMesh],
    instances,
    splines: [],
    volumes: [],
    spawnPoints: [],
    ...(environment ? { environment } : {}),
  };
}

/**
 * End-to-end importer from a supported retail R&C1 random-access disc.
 * Core WAD-LZ is decompressed once and shared by terrain, collision, class
 * libraries, sky and texture tables. Gameplay WAD-LZ is also decompressed once
 * and shared by placement + settings parsers.
 */
export async function importRac1WorldSlice(
  discReader: RandomAccessReader,
  buildId: string,
  requestedLevelId: string | number,
): Promise<Rac1WorldImportObservation> {
  const levelNumber = parseLevelId(requestedLevelId);
  const catalogue = await readRac1LevelCatalogue(discReader);
  const level = catalogue.levels.find((candidate) => candidate.levelId === levelNumber);
  if (!level) {
    throw new Error(`R&C1 native level ${requestedLevelId} is unavailable; catalogue contains IDs [${catalogue.levels.map((candidate) => candidate.levelId).join(", ")}].`);
  }

  const levelData = openRac1LevelDataSource(discReader, level);
  const directory = await readRac1LevelDataDirectory(levelData);
  const coreIndexReader = openRac1LevelDataRange(levelData, directory, RAC1_LEVEL_DATA_CORE_INDEX_SLOT);
  const coreDataReader = openRac1LevelDataRange(levelData, directory, RAC1_LEVEL_DATA_CORE_DATA_SLOT);
  const gsRamReader = openRac1LevelDataRange(levelData, directory, RAC1_LEVEL_DATA_GS_RAM_SLOT);
  if (!coreIndexReader || !coreDataReader || !gsRamReader) {
    throw new Error(`R&C1 native level ${level.levelId} is missing one of the retail-validated core index/data/GS-RAM ranges.`);
  }

  const coreHeader = await readRac1CoreIndexHeader(coreIndexReader);
  const index = await coreIndexReader.read(0, coreIndexReader.size);
  const coreData = await readRac1CoreData(coreDataReader, coreHeader);
  const gsRam = await gsRamReader.read(0, gsRamReader.size);

  if (coreHeader.tfragsEndBoundary > coreData.data.length || coreHeader.collisionEndBoundary > coreData.data.length) {
    throw new Error(`R&C1 native level ${level.levelId} core section boundary exceeds decompressed core data.`);
  }
  const tfrags = readRcTfrags(coreData.data.subarray(coreHeader.tfragsOffset, coreHeader.tfragsEndBoundary));
  const collision = readRcCollision(coreData.data.subarray(coreHeader.collisionOffset, coreHeader.collisionEndBoundary));
  const staticClasses = readRac1StaticClasses(index, coreData.data);
  const mobyClasses = readRac1MobyBindPoseClasses(index, coreData.data);

  const gameplayReader = openRac1LevelCoreRange(discReader, level, RAC1_GAMEPLAY_RANGE_SLOT);
  if (!gameplayReader) throw new Error(`R&C1 native level ${level.levelId} is missing gameplay range ${RAC1_GAMEPLAY_RANGE_SLOT}.`);
  const gameplayDecoded = await readWadLz(gameplayReader, 0, { maxOutputBytes: 64 * 1024 * 1024 });
  const gameplay = parseRac1GameplayInstances(gameplayDecoded.data);
  const settings = parseRac1LevelSettings(gameplayDecoded.data);

  const iv = new DataView(index.buffer, index.byteOffset, index.byteLength);
  if (RAC1_TEXTURES_BASE_FIELD_OFFSET + 4 > index.length) throw new Error(`R&C1 native level ${level.levelId} core index is missing texture base.`);
  const texturesBaseOffset = iv.getInt32(RAC1_TEXTURES_BASE_FIELD_OFFSET, true);
  if (texturesBaseOffset !== coreHeader.collisionEndBoundary) {
    throw new Error(`R&C1 native level ${level.levelId} texture base ${texturesBaseOffset} disagrees with validated boundary ${coreHeader.collisionEndBoundary}.`);
  }
  const textures = readRac1TextureTable(index, coreData.data, gsRam, texturesBaseOffset, "tfrag").textures;
  const mobyTextures = readRac1TextureTable(index, coreData.data, gsRam, texturesBaseOffset, "moby").textures;
  const tieTextures = readRac1TextureTable(index, coreData.data, gsRam, texturesBaseOffset, "tie").textures;
  const shrubTextures = readRac1TextureTable(index, coreData.data, gsRam, texturesBaseOffset, "shrub").textures;

  const sky = coreHeader.skyOffset > 0
    ? readRcSky(coreData.data.subarray(coreHeader.skyOffset, coreHeader.skyEndBoundary))
    : null;

  const world = assembleRac1WorldSlice({
    buildId,
    level,
    tfrags,
    collision,
    textures,
    mobyTextures,
    tieTextures,
    shrubTextures,
    texturesBaseOffset,
    staticClasses,
    mobyClasses,
    gameplay,
    settings,
    sky,
  });
  const mobyLinkedInstanceCount = world.instances.filter((instance) => instance.modelId !== undefined).length;
  return {
    world,
    level,
    coreDataCompressedSize: coreData.compressedSize,
    coreDataDecompressedSize: coreData.decompressedSize,
    gameplayCompressedSize: gameplayDecoded.compressedSize,
    gameplayDecompressedSize: gameplayDecoded.data.length,
    tfragsOffset: coreHeader.tfragsOffset,
    collisionOffset: coreHeader.collisionOffset,
    texturesBaseOffset,
    tieInstanceCount: gameplay.tieInstances.length,
    shrubInstanceCount: gameplay.shrubInstances.length,
    mobyInstanceCount: gameplay.mobyInstances.length,
    mobyModelCount: world.models?.length ?? 0,
    mobyLinkedInstanceCount,
    mobyUnlinkedInstanceCount: gameplay.mobyInstances.length - mobyLinkedInstanceCount,
  };
}

function placeStaticClassInstances<T extends StaticClass>(
  kind: "tie" | "shrub",
  instances: readonly Rac1MatrixInstance[],
  classes: ReadonlyMap<number, T>,
  buildId: string,
  levelId: string,
  materialPrefix: string,
): { readonly meshes: OBPMesh[]; readonly bounds: OBPBounds | null } {
  interface Group {
    positions: number[];
    uvs: number[];
    indices: number[];
    instanceCount: number;
    classIds: Set<number>;
  }
  const groups = new Map<number, Group>();
  let min = { x: Infinity, y: Infinity, z: Infinity };
  let max = { x: -Infinity, y: -Infinity, z: -Infinity };

  for (const instance of instances) {
    const cls = classes.get(instance.oClass);
    if (!cls) throw new Error(`R&C1 ${kind} instance ${instance.index} references missing class ${instance.oClass}.`);
    const mesh = cls.mesh;
    if (cls.triangleTextureIds.length !== mesh.indices.length / 3) {
      throw new Error(`R&C1 ${kind} class ${instance.oClass} has inconsistent triangle texture mapping.`);
    }

    const worldPositions = new Float64Array(mesh.positions.length);
    for (let v = 0; v < mesh.positions.length; v += 3) {
      const [nx, ny, nz] = transformRac1InstancePoint(
        instance.matrix,
        mesh.positions[v]!, mesh.positions[v + 1]!, mesh.positions[v + 2]!,
      );
      // Native RC Z-up -> OBP Y-up.
      const ox = nx;
      const oy = nz;
      const oz = ny;
      worldPositions[v] = ox;
      worldPositions[v + 1] = oy;
      worldPositions[v + 2] = oz;
      if (ox < min.x) min.x = ox; if (oy < min.y) min.y = oy; if (oz < min.z) min.z = oz;
      if (ox > max.x) max.x = ox; if (oy > max.y) max.y = oy; if (oz > max.z) max.z = oz;
    }

    const remaps = new Map<number, Map<number, number>>();
    const touched = new Set<number>();
    for (let f = 0; f < cls.triangleTextureIds.length; f++) {
      const textureId = cls.triangleTextureIds[f]!;
      let group = groups.get(textureId);
      if (!group) {
        group = { positions: [], uvs: [], indices: [], instanceCount: 0, classIds: new Set() };
        groups.set(textureId, group);
      }
      group.classIds.add(instance.oClass);
      touched.add(textureId);
      let remap = remaps.get(textureId);
      if (!remap) { remap = new Map(); remaps.set(textureId, remap); }
      for (let k = 0; k < 3; k++) {
        const localVertex = mesh.indices[f * 3 + k]!;
        let placedVertex = remap.get(localVertex);
        if (placedVertex === undefined) {
          placedVertex = group.positions.length / 3;
          remap.set(localVertex, placedVertex);
          group.positions.push(
            worldPositions[localVertex * 3]!,
            worldPositions[localVertex * 3 + 1]!,
            worldPositions[localVertex * 3 + 2]!,
          );
          group.uvs.push(mesh.uvs[localVertex * 2]!, mesh.uvs[localVertex * 2 + 1]!);
        }
        group.indices.push(placedVertex);
      }
    }
    for (const textureId of touched) groups.get(textureId)!.instanceCount++;
  }

  const meshes: OBPMesh[] = [];
  for (const [textureId, group] of [...groups.entries()].sort((a, b) => a[0] - b[0])) {
    meshes.push({
      id: `rac1-${levelId}-${kind}-tex${textureId}`,
      name: `R&C1 placed ${kind} geometry texture ${textureId}`,
      geometry: { positions: group.positions, indices: group.indices, uvs: group.uvs },
      materialId: `${materialPrefix}-tex${textureId}`,
      source: {
        game: "rac1",
        buildId,
        levelId,
        assetKind: kind,
        originalId: textureId,
        notes: [
          `${group.instanceCount} native ${kind} placements contribute to this texture mesh`,
          `${group.classIds.size} distinct native class ids`,
          "native instance matrices were applied in Z-up space before deterministic RC->OBP Y-up conversion",
        ],
      },
    });
  }
  return { meshes, bounds: Number.isFinite(min.x) ? { min, max } : null };
}

function validateMeshMaterials(meshes: readonly OBPMesh[], materials: ReadonlySet<string>, label: string): void {
  for (const mesh of meshes) {
    if (mesh.materialId && !materials.has(mesh.materialId)) {
      throw new Error(`${label} references missing native texture material '${mesh.materialId}'.`);
    }
  }
}

function parseLevelId(value: string | number): number {
  const result = typeof value === "number" ? value : Number(value);
  if (!Number.isInteger(result) || result < 0) throw new Error(`R&C1 levelId '${value}' is not a non-negative native integer level ID.`);
  return result;
}

function tfragBounds(mesh: RcTfragMesh): OBPBounds {
  return {
    min: { x: mesh.bounds.min[0], y: mesh.bounds.min[1], z: mesh.bounds.min[2] },
    max: { x: mesh.bounds.max[0], y: mesh.bounds.max[1], z: mesh.bounds.max[2] },
  };
}

function unionBounds(a: OBPBounds, b: OBPBounds): OBPBounds {
  return {
    min: { x: Math.min(a.min.x, b.min.x), y: Math.min(a.min.y, b.min.y), z: Math.min(a.min.z, b.min.z) },
    max: { x: Math.max(a.max.x, b.max.x), y: Math.max(a.max.y, b.max.y), z: Math.max(a.max.z, b.max.z) },
  };
}

function averageTextureRgba(rgba: Uint8Array): readonly [number, number, number, number] {
  if (rgba.length === 0) return [0, 0, 0, 0];
  let r = 0;
  let g = 0;
  let b = 0;
  let a = 0;
  const pixels = Math.floor(rgba.length / 4);
  for (let i = 0; i < pixels; i++) {
    r += rgba[i * 4]!;
    g += rgba[i * 4 + 1]!;
    b += rgba[i * 4 + 2]!;
    a += rgba[i * 4 + 3]!;
  }
  const scale = 1 / (pixels * 255);
  return [r * scale, g * scale, b * scale, a * scale];
}
