import { OBP_SCHEMA_VERSION, validateWorld } from "../../core/src/index.js";
import type { OBPEnvironment, OBPMaterial, OBPSourceRef, OBPWorld } from "../../core/src/index.js";
import { SubRangeReader } from "../../importer-common/src/index.js";
import type { RandomAccessReader } from "../../importer-common/src/index.js";
import { openGcLevelCore } from "../../gc-level-core/src/index.js";
import { parseGcGameplayInstances } from "../../gc-instances/src/index.js";
import { parseGcLevelSettings } from "../../gc-level-settings/src/index.js";
import { readGcLevelTextures } from "../../gc-level-textures/src/index.js";
import type { GcLevelTexture } from "../../gc-level-textures/src/index.js";
import { readGcTieClasses } from "../../gc-tie/src/index.js";
import { readGcShrubClasses } from "../../gc-shrub/src/index.js";
import { readGcMobyClasses } from "../../gc-moby/src/index.js";
import { rgbaPngDataUri } from "../../image-data-uri/src/index.js";
import { obpCollisionBounds, readRcCollision, toObpCollisionMesh } from "../../rc-collision/src/index.js";
import { readRcTfrags, toObpRcTfragMeshes } from "../../rc-tfrag/src/index.js";
import { openUyaTocPayloadPublicLead } from "../../uya-level-wad/src/index.js";
import { probeUyaGcPvarCompatibility } from "../../uya-pvars-compat/src/index.js";
import { readUyaSky } from "../../uya-sky/src/index.js";
import { probeUyaWorldCandidatePublicLead } from "../../uya-world-probe/src/index.js";
import { readWadLz } from "../../wad-lz/src/index.js";
import {
  buildRac3MobyInstances,
  buildRac3MobyModels,
  buildRac3SkyGeometry,
  placeRac3StaticInstances,
  tfragBoundsToObp,
  unionBounds,
} from "./geometry.js";

export * from "./geometry.js";

export interface Rac3WorldImportOptions {
  /** Embed decoded native texture pixels as PNG data URIs. Default true. */
  readonly embedTextureImages?: boolean;
}

export interface Rac3WorldImportObservation {
  readonly world: OBPWorld;
  readonly tableIndex: number;
  readonly publicNativeLevelIdHint: number;
  readonly gameplayCompressedSize: number;
  readonly gameplayDecompressedSize: number;
  readonly tfragCount: number;
  readonly tieInstanceCount: number;
  readonly shrubInstanceCount: number;
  readonly mobyInstanceCount: number;
  readonly mobiesWithPvar: number;
  readonly mobyModelCount: number;
  readonly linkedMobyInstanceCount: number;
  readonly unlinkedMobyInstanceCount: number;
  readonly skyShellCount: number;
  readonly pvarMobyLinkCount: number;
  readonly pvarRelativePointerCount: number;
}

/**
 * End-to-end retail RAC3 world importer using only the supported NTSC-U build.
 * The hidden ToC/outer level labels remain provenance-marked public leads; every
 * decoder admitted below has separate retail UYA compatibility evidence.
 */
export async function importRac3World(
  discReader: RandomAccessReader,
  buildId: string,
  requestedLevelId: string | number,
  options: Rac3WorldImportOptions = {},
): Promise<Rac3WorldImportObservation> {
  const tableIndex = parseLevelId(requestedLevelId);
  const probe = await probeUyaWorldCandidatePublicLead(discReader, { tableIndex });
  const levelId = String(probe.levelPayload.outer.publicLevelIdHint);
  if (probe.levelPayload.outer.publicLevelIdHint !== tableIndex) {
    throw new Error(`RAC3 table ${tableIndex} public level-id word is ${probe.levelPayload.outer.publicLevelIdHint}; refusing to collapse distinct identities.`);
  }
  const levelReader = openUyaTocPayloadPublicLead(
    discReader,
    probe.selectedMainPart,
    `rac3-table-${tableIndex}-level`,
  );
  const primary = probe.levelPayload.outer.ranges[0];
  const gameplayRange = probe.levelPayload.outer.ranges[2];
  if (!primary?.present || !gameplayRange?.present) {
    throw new Error(`RAC3 table ${tableIndex} requires both public primary and gameplay ranges.`);
  }

  const dataReader = new SubRangeReader(levelReader, primary.offsetBytes, primary.sizeBytes, `${levelReader.name}#primary`);
  const core = await openGcLevelCore(dataReader);
  if (core.coreHeader.assetsCompressedSize !== probe.levelPayload.core.publicFields.assetsCompressedSize ||
      core.coreHeader.assetsDecompressedSize !== probe.levelPayload.core.publicFields.assetsDecompressedSize) {
    throw new Error(`RAC3 table ${tableIndex} shared core parser disagrees with independently observed UYA core size fields.`);
  }

  const gameplayReader = new SubRangeReader(
    levelReader,
    gameplayRange.offsetBytes,
    gameplayRange.sizeBytes,
    `${levelReader.name}#gameplay`,
  );
  const gameplayDecoded = await readWadLz(gameplayReader, 0, { maxOutputBytes: 64 * 1024 * 1024 });
  const gameplay = parseGcGameplayInstances(gameplayDecoded.data);
  const settings = parseGcLevelSettings(gameplayDecoded.data);
  const pvars = probeUyaGcPvarCompatibility(gameplayDecoded.data);

  if (gameplay.mobyInstances.length !== pvars.parser.mobies.length) {
    throw new Error(`RAC3 table ${tableIndex} placement/PVar Moby counts disagree (${gameplay.mobyInstances.length} vs ${pvars.parser.mobies.length}).`);
  }
  for (let index = 0; index < gameplay.mobyInstances.length; index++) {
    const placement = gameplay.mobyInstances[index]!;
    const authored = pvars.parser.mobies[index]!;
    if (placement.index !== authored.index || placement.oClass !== authored.oClass) {
      throw new Error(`RAC3 table ${tableIndex} placement/PVar Moby ${index} identity disagrees.`);
    }
  }

  const tfragRange = sectionRange(core.coreHeader.tfrags, core.sectionBoundaries, core.assets.length, true);
  const collisionRange = sectionRange(core.coreHeader.collision, core.sectionBoundaries, core.assets.length, false);
  if (!tfragRange || !collisionRange) throw new Error(`RAC3 table ${tableIndex} is missing tfrag or collision section extent.`);
  const tfrags = readRcTfrags(core.assets.subarray(tfragRange.offset, tfragRange.offset + tfragRange.size));
  const collision = readRcCollision(core.assets.subarray(collisionRange.offset, collisionRange.offset + collisionRange.size));

  const tfragTextures = readGcLevelTextures(core, "tfrag", { strict: true });
  const mobyTextures = readGcLevelTextures(core, "moby", { strict: true });
  const tieTextures = readGcLevelTextures(core, "tie", { strict: true });
  const shrubTextures = readGcLevelTextures(core, "shrub", { strict: true });
  const tieClasses = readGcTieClasses(core);
  const shrubClasses = readGcShrubClasses(core);
  const mobyClasses = readGcMobyClasses(core);

  const source: OBPSourceRef = {
    game: "rac3",
    buildId,
    levelId,
    assetKind: "world",
    originalId: tableIndex,
    originalOffset: (probe.selectedMainPart.rawHeaderWordAt0x04 ?? 0) * 0x800,
    notes: [
      `retail UYA sparse table row ${tableIndex}`,
      `main header raw +0x08/public level-id hint ${probe.levelPayload.outer.publicLevelIdHint}`,
      "resident ToC address and outer range semantic labels remain provenance-separated public leads; native loader provenance is not asserted",
      "terrain/static geometry/textures/collision/Mobies/PVars/settings/sky use separately retail-censused UYA-compatible decoders",
    ],
  };
  const materials: OBPMaterial[] = [];
  const embedTextureImages = options.embedTextureImages !== false;
  const tfragMaterialPrefix = await addTextureMaterials(materials, tfragTextures, "tfrag", buildId, levelId, embedTextureImages);
  const mobyMaterialPrefix = await addTextureMaterials(materials, mobyTextures, "moby", buildId, levelId, embedTextureImages);
  const tieMaterialPrefix = await addTextureMaterials(materials, tieTextures, "tie", buildId, levelId, embedTextureImages);
  const shrubMaterialPrefix = await addTextureMaterials(materials, shrubTextures, "shrub", buildId, levelId, embedTextureImages);
  const materialIds = new Set(materials.map((material) => material.id));

  const terrainSource: OBPSourceRef = { ...source, assetKind: "static-terrain", originalId: tableIndex };
  const meshes = toObpRcTfragMeshes(tfrags, terrainSource, `rac3-${levelId}-terrain`, tfragMaterialPrefix);
  validateMeshMaterials(meshes, materialIds, `RAC3 table ${tableIndex} terrain`);
  let bounds = unionBounds(tfragBoundsToObp(tfrags.bounds), obpCollisionBounds(collision));

  const tiePlaced = placeRac3StaticInstances(
    "tie", gameplay.tieInstances, tieClasses, buildId, levelId, tieMaterialPrefix, materialIds,
  );
  const shrubPlaced = placeRac3StaticInstances(
    "shrub", gameplay.shrubInstances, shrubClasses, buildId, levelId, shrubMaterialPrefix, materialIds,
  );
  meshes.push(...tiePlaced.meshes, ...shrubPlaced.meshes);
  if (tiePlaced.bounds) bounds = unionBounds(bounds, tiePlaced.bounds);
  if (shrubPlaced.bounds) bounds = unionBounds(bounds, shrubPlaced.bounds);

  const placedMobyClasses = new Set(gameplay.mobyInstances.map((instance) => instance.oClass));
  const referencedMobyClasses = new Map([...mobyClasses].filter(([oClass]) => placedMobyClasses.has(oClass)));
  const mobyModels = buildRac3MobyModels(referencedMobyClasses, buildId, levelId, mobyMaterialPrefix, materialIds);
  const authoredByIndex = new Map(pvars.parser.mobies.map((moby) => [moby.index, {
    uid: moby.uid,
    pvarIndex: moby.pvarIndex,
    modeBits: moby.modeBits,
    raw0x14: moby.raw0x14,
    pvar: moby.pvar,
  }]));
  const instances = buildRac3MobyInstances(
    gameplay.mobyInstances,
    authoredByIndex,
    mobyModels.modelIdsByClass,
    buildId,
    levelId,
  );
  const linkedMobyInstanceCount = instances.filter((instance) => instance.modelId !== undefined).length;

  let environment: OBPEnvironment = {
    ...(settings.backgroundColour ? { backgroundColor: settings.backgroundColour } : {}),
    ...(settings.fogColour ? { fogColor: settings.fogColour } : {}),
    fogNearDistance: settings.fogNearDistance,
    fogFarDistance: settings.fogFarDistance,
    fogNearIntensity: settings.fogNearIntensity,
    fogFarIntensity: settings.fogFarIntensity,
    deathHeight: settings.deathHeight,
    isSphericalWorld: settings.isSphericalWorld,
    ...(settings.isSphericalWorld ? {
      sphereCenter: {
        x: settings.sphereCentre[0],
        y: settings.sphereCentre[2],
        z: settings.sphereCentre[1],
      },
    } : {}),
    source: {
      ...source,
      assetKind: "level-settings",
      notes: [
        `shared GC/UYA 0x5c settings layout accepted by retail UYA compatibility census`,
        `native settings block offset 0x${settings.blockOffset.toString(16)}`,
      ],
    },
  };

  let skyShellCount = 0;
  if (core.coreHeader.sky > 0) {
    const skyRange = sectionRange(core.coreHeader.sky, core.sectionBoundaries, core.assets.length, false);
    if (!skyRange) throw new Error(`RAC3 table ${tableIndex} sky offset has no following core boundary.`);
    const sky = readUyaSky(core.assets.subarray(skyRange.offset, skyRange.offset + skyRange.size), { framerate: 60 });
    const skyMaterialPrefix = await addSkyTextureMaterials(materials, sky.textures, buildId, levelId, embedTextureImages);
    const skyColourPresent = sky.colour[3] > 0 || sky.colour.slice(0, 3).some((component) => component > 0);
    const tint = skyColourPresent
      ? sky.colour.slice(0, 3)
      : environment.backgroundColor ?? environment.fogColor ?? [0.05, 0.06, 0.09];
    const gouraudMaterialId = `${skyMaterialPrefix}-gouraud`;
    materials.push({
      id: gouraudMaterialId,
      name: "RAC3 sky untextured",
      debugRgba: [tint[0]!, tint[1]!, tint[2]!, 1],
      source: { ...source, assetKind: "sky", originalId: "gouraud" },
    });
    const skyGeometry = buildRac3SkyGeometry(sky, bounds, source, skyMaterialPrefix, gouraudMaterialId);
    meshes.push(...skyGeometry.meshes);
    skyShellCount = sky.shells.length;
    if (skyColourPresent) environment = { ...environment, skyColor: sky.colour };
  }

  const collisionMesh = toObpCollisionMesh(
    collision,
    { ...source, assetKind: "collision", originalId: tableIndex },
    `rac3-${levelId}-collision`,
    `RAC3 native table ${tableIndex} collision`,
  );
  const world: OBPWorld = {
    schemaVersion: OBP_SCHEMA_VERSION,
    id: `rac3-level${levelId}`,
    displayName: `Ratchet & Clank: Up Your Arsenal native level ${levelId}`,
    source,
    bounds,
    materials,
    meshes,
    ...(mobyModels.models.length ? { models: mobyModels.models } : {}),
    collisionMeshes: [collisionMesh],
    instances,
    splines: [],
    volumes: [],
    spawnPoints: [],
    environment,
  };
  const issues = validateWorld(world);
  if (issues.length > 0) {
    throw new Error(`Generated RAC3 table ${tableIndex} OBPWorld failed validation: ${issues.slice(0, 8).map((issue) => `${issue.path}: ${issue.message}`).join(" | ")}`);
  }

  return {
    world,
    tableIndex,
    publicNativeLevelIdHint: probe.levelPayload.outer.publicLevelIdHint,
    gameplayCompressedSize: gameplayDecoded.compressedSize,
    gameplayDecompressedSize: gameplayDecoded.data.length,
    tfragCount: tfrags.tfragCount,
    tieInstanceCount: gameplay.tieInstances.length,
    shrubInstanceCount: gameplay.shrubInstances.length,
    mobyInstanceCount: gameplay.mobyInstances.length,
    mobiesWithPvar: pvars.prerequisiteCensus.mobiesWithPvar,
    mobyModelCount: mobyModels.models.length,
    linkedMobyInstanceCount,
    unlinkedMobyInstanceCount: gameplay.mobyInstances.length - linkedMobyInstanceCount,
    skyShellCount,
    pvarMobyLinkCount: pvars.prerequisiteCensus.pvarMobyLinks.length,
    pvarRelativePointerCount: pvars.prerequisiteCensus.pvarRelativePointers.length,
  };
}

async function addTextureMaterials(
  materials: OBPMaterial[],
  textures: readonly GcLevelTexture[],
  kind: "tfrag" | "moby" | "tie" | "shrub",
  buildId: string,
  levelId: string,
  embedTextureImages: boolean,
): Promise<string> {
  const prefix = `rac3-${levelId}-${kind}`;
  for (const texture of textures) {
    materials.push({
      id: `${prefix}-tex${texture.index}`,
      name: `RAC3 native ${kind} texture ${texture.index}`,
      debugRgba: averageTextureRgba(texture.rgba),
      ...(embedTextureImages ? { image: await rgbaPngDataUri(texture.width, texture.height, texture.rgba) } : {}),
      source: {
        game: "rac3",
        buildId,
        levelId,
        assetKind: "texture",
        originalId: `${kind}-${texture.index}`,
        notes: [
          `native TextureEntry ${texture.width}x${texture.height}`,
          `type=${texture.type} paletteSlot=${texture.paletteSlot}`,
          `dataOffset=${texture.dataOffset} raw0x0c=${texture.raw0x0c} raw0x0e=${texture.raw0x0e}`,
        ],
      },
    });
  }
  return prefix;
}

async function addSkyTextureMaterials(
  materials: OBPMaterial[],
  textures: readonly { readonly index: number; readonly width: number; readonly height: number; readonly rgba: Uint8Array }[],
  buildId: string,
  levelId: string,
  embedTextureImages: boolean,
): Promise<string> {
  const prefix = `rac3-${levelId}-sky`;
  for (const texture of textures) {
    materials.push({
      id: `${prefix}-tex${texture.index}`,
      name: `RAC3 sky texture ${texture.index}`,
      debugRgba: averageTextureRgba(texture.rgba),
      ...(embedTextureImages ? { image: await rgbaPngDataUri(texture.width, texture.height, texture.rgba) } : {}),
      source: {
        game: "rac3",
        buildId,
        levelId,
        assetKind: "texture",
        originalId: `sky-${texture.index}`,
        notes: ["strict UYA 0x10-shell-header sky decoder; native initial pose"],
      },
    });
  }
  return prefix;
}

function averageTextureRgba(rgba: Uint8Array): readonly [number, number, number, number] {
  if (rgba.length === 0) return [0, 0, 0, 0];
  let r = 0, g = 0, b = 0, a = 0;
  const pixels = Math.floor(rgba.length / 4);
  for (let index = 0; index < pixels; index++) {
    r += rgba[index * 4]!;
    g += rgba[index * 4 + 1]!;
    b += rgba[index * 4 + 2]!;
    a += rgba[index * 4 + 3]!;
  }
  const scale = 1 / (pixels * 255);
  return [r * scale, g * scale, b * scale, a * scale];
}

function validateMeshMaterials(meshes: readonly { readonly materialId?: string }[], materials: ReadonlySet<string>, label: string): void {
  for (const mesh of meshes) {
    if (mesh.materialId && !materials.has(mesh.materialId)) {
      throw new Error(`${label} references missing native texture material '${mesh.materialId}'.`);
    }
  }
}

function sectionRange(
  offset: number,
  boundaries: readonly number[],
  assetBytes: number,
  allowZero: boolean,
): { readonly offset: number; readonly size: number } | undefined {
  if (!Number.isSafeInteger(offset) || offset < (allowZero ? 0 : 1) || offset >= assetBytes) return undefined;
  const next = boundaries.find((boundary) => boundary > offset && boundary <= assetBytes);
  return next === undefined ? undefined : { offset, size: next - offset };
}

function parseLevelId(value: string | number): number {
  const result = typeof value === "number" ? value : Number(value);
  if (!Number.isInteger(result) || result < 0) {
    throw new Error(`RAC3 levelId '${value}' is not a non-negative retail table/native integer ID.`);
  }
  return result;
}
