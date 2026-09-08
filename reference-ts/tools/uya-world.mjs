#!/usr/bin/env node
import { mkdir, writeFile } from "node:fs/promises";
import { resolve, basename } from "node:path";
import { LocalFileRandomAccessReader } from "./local-random-access.mjs";
import { pngDataUri } from "./lib-png.mjs";
import { uyaEnvironmentFromGameplay, appendUyaSky } from "./lib-uya-environment.mjs";
import { SubRangeReader } from "../.build/packages/importer-common/src/index.js";
import { validateWorld, worldStats } from "../.build/packages/core/src/index.js";
import { assertUyaAuthorityTocWindow } from "./uya-retail-authority.mjs";
import { probeUyaWorldCandidatePublicLead } from "../.build/packages/uya-world-probe/src/index.js";
import { openUyaTocPayloadPublicLead } from "../.build/packages/uya-level-wad/src/index.js";
import { decodeUyaCoreDataPublicLead } from "../.build/packages/uya-core-decode/src/index.js";
import { probeUyaGcMobyCompatibilityPublicLead } from "../.build/packages/uya-moby-compat/src/index.js";
import { readGcTfrags, toObpTfragMeshes } from "../.build/packages/gc-tfrag/src/index.js";
import { readGcLevelTextures } from "../.build/packages/gc-level-textures/src/index.js";
import { readGcTieClasses } from "../.build/packages/gc-tie/src/index.js";
import { readGcShrubClasses } from "../.build/packages/gc-shrub/src/index.js";
import { readGcMobyClasses } from "../.build/packages/gc-moby/src/index.js";
import { readGcGameplayInstances, transformPoint, matrixFromPosRotScale } from "../.build/packages/gc-instances/src/index.js";
import { readRcCollision, toObpCollisionMesh, obpCollisionBounds } from "../.build/packages/rc-collision/src/index.js";
import { readWadLz } from "../.build/packages/wad-lz/src/index.js";

/**
 * Build a neutral OBPWorld candidate directly from the retail UYA authority ISO.
 *
 * This deliberately keeps the UYA disc/level provenance boundary explicit: the hidden ToC,
 * level-table interpretation and outer range names remain public-derived hypotheses. Every native
 * decoder admitted to world output has already been separately compatibility-censused against UYA.
 *
 * Current reconstruction scope:
 *   - coreData WAD-LZ
 *   - tfrag LOD0 terrain + baked colours/UVs
 *   - tfrag/Moby/TIE/shrub textures
 *   - TIE + shrub class geometry, flattened through retail gameplay placement matrices
 *   - renderable Moby class geometry, flattened through retail gameplay pos/rot/scale
 *   - collision octree
 *   - GC/UYA-compatible level atmosphere (background/fog/death-height/spherical-world fields)
 *   - UYA-versioned layered sky geometry/textures in the native initial pose
 *
 * Moby table entries with zero local core are valid under the pinned public format but are not
 * rendered here. Decoded Moby classes with empty geometry are likewise retained as evidence but
 * omitted from render output. Skinned Moby classes for which the current GC bind-pose
 * reconstruction was not applied are also omitted rather than emitted in a potentially misleading
 * pose. 0xff class-local texture slots are retained as materialless geometry (pinned public
 * no-texture sentinel).
 *
 * UYA sky shell rotation/angular velocity and bloom flags are retained in source notes/summary but
 * the debug viewer currently renders the native initial pose only; animation/bloom are not invented.
 *
 * Usage:
 *   npm run build && node tools/uya-world.mjs <uya.iso> --table-index 1 --out <world.json>
 */

const args = process.argv.slice(2);
const positional = [];
const opt = { tableIndex: 1, textures: true, collision: true, staticGeometry: true, sky: true };
for (let i = 0; i < args.length; i++) {
  const a = args[i];
  if (a === "--table-index") opt.tableIndex = parseIndex(args[++i], a);
  else if (a === "--out") opt.out = args[++i];
  else if (a === "--no-textures") opt.textures = false;
  else if (a === "--no-collision") opt.collision = false;
  else if (a === "--no-static") opt.staticGeometry = false;
  else if (a === "--no-sky") opt.sky = false;
  else if (a === "--help" || a === "-h") usage();
  else if (a.startsWith("--")) throw new Error(`Unknown flag: ${a}`);
  else positional.push(a);
}
if (positional.length !== 1 || !opt.out) usage();

function usage() {
  console.error("Usage: npm run build && node tools/uya-world.mjs <uya.iso> --table-index N --out <world.json> [--no-textures] [--no-collision] [--no-static] [--no-sky]");
  process.exit(2);
}

function parseIndex(value, label) {
  const n = Number(value);
  if (!Number.isSafeInteger(n) || n < 0) throw new Error(`${label} must be a non-negative integer.`);
  return n;
}

const isoPath = resolve(positional[0]);
const reader = await LocalFileRandomAccessReader.open(isoPath, basename(isoPath));
const tableIndex = opt.tableIndex;
const source = {
  game: "rac3",
  buildId: "rac3-ntscu-original",
  levelId: `table-${tableIndex}`,
  assetKind: "world",
  notes: [
    "Retail UYA authority bytes; table-row/outer-range semantics are public-derived compatibility leads, not native-loader-proven provenance.",
  ],
};
const matPrefix = `rac3-table${tableIndex}`;
const meshes = [];
const materials = [];
const collisionMeshes = [];
const min = { x: Infinity, y: Infinity, z: Infinity };
const max = { x: -Infinity, y: -Infinity, z: -Infinity };
let mobyCompatibilitySummary;
let mobyRenderSummary;
let levelSettingsSummary;
let skySummary;
let environment;
const grow = (bounds) => {
  min.x = Math.min(min.x, bounds.min.x); min.y = Math.min(min.y, bounds.min.y); min.z = Math.min(min.z, bounds.min.z);
  max.x = Math.max(max.x, bounds.max.x); max.y = Math.max(max.y, bounds.max.y); max.z = Math.max(max.z, bounds.max.z);
};
const r2 = (v) => Math.round(v * 100) / 100;
const r4 = (v) => Math.round(v * 10000) / 10000;

try {
  const authority = await assertUyaAuthorityTocWindow(reader);
  if (!authority.matched) throw new Error("UYA authority identity did not match pinned retail bytes.");

  const probe = await probeUyaWorldCandidatePublicLead(reader, { tableIndex });
  const levelReader = openUyaTocPayloadPublicLead(reader, probe.selectedMainPart, `uya-table-${tableIndex}-candidate-level`);
  const decoded = await decodeUyaCoreDataPublicLead(
    levelReader,
    probe.levelPayload.outer,
    probe.levelPayload.data,
    probe.levelPayload.core,
    probe.levelPayload.coreDataWadLz,
  );
  if (decoded.warnings.length) throw new Error(`UYA core decode produced warnings: ${decoded.warnings.join(" | ")}`);

  const fields = probe.levelPayload.core.publicFields;
  const primary = probe.levelPayload.outer.ranges[0];
  const gs = probe.levelPayload.data.gsRam;
  if (!primary?.present || !gs.present) throw new Error("UYA candidate primary/GS-RAM ranges are required for world reconstruction.");
  const gsOffset = primary.offsetBytes + gs.offset;
  if (gsOffset < 0 || gsOffset > levelReader.size || gs.size > levelReader.size - gsOffset) {
    throw new RangeError(`UYA GS-RAM range ${gsOffset}+${gs.size} lies outside level payload ${levelReader.size}.`);
  }
  const gsRam = await levelReader.read(gsOffset, gs.size);

  // Compatibility facade only: the bytes/fields come from the independently retained UYA evidence path.
  const core = {
    dataHeader: {},
    coreHeader: {
      ...fields,
      ratchetSeqsOffset: fields.ratchetSeqsRac123,
      raw: [...probe.levelPayload.core.rawWordsU32],
    },
    index: decoded.coreIndex,
    assets: decoded.assets,
    gsRam,
    sectionBoundaries: decoded.sectionBoundaries,
  };

  const addTextures = (kind) => {
    if (!opt.textures) return;
    const decodedTextures = readGcLevelTextures(core, kind);
    for (const tex of decodedTextures) {
      materials.push({
        id: `${matPrefix}-${kind}-tex${tex.index}`,
        name: `${kind} texture ${tex.index}`,
        image: pngDataUri(tex.width, tex.height, tex.rgba),
        source: { ...source, assetKind: "texture", originalId: `${kind}-${tex.index}` },
      });
    }
    console.log(`textures ${kind}: ${decodedTextures.length}`);
  };

  // --- tfrag terrain ---
  const tfragRange = sectionRangeAllowZero(fields.tfrags, decoded.sectionBoundaries, decoded.assets.length);
  if (!tfragRange) throw new Error(`No UYA tfrag section range at asset offset ${fields.tfrags}.`);
  const tfragBytes = decoded.assets.subarray(tfragRange.offset, tfragRange.offset + tfragRange.size);
  const tfrags = readGcTfrags(tfragBytes);
  addTextures("tfrag");
  for (const mesh of toObpTfragMeshes(tfrags, source, `${matPrefix}-tfrag`, `${matPrefix}-tfrag`)) meshes.push(mesh);
  grow({
    min: { x: tfrags.bounds.min[0], y: tfrags.bounds.min[1], z: tfrags.bounds.min[2] },
    max: { x: tfrags.bounds.max[0], y: tfrags.bounds.max[1], z: tfrags.bounds.max[2] },
  });
  console.log(`tfrags: ${tfrags.tfragCount} -> ${tfrags.positions.length / 3} verts, ${tfrags.indices.length / 3} tris`);

  // --- TIE + shrub + evidence-backed Moby placement + level settings ---
  if (opt.staticGeometry) {
    const gameplay = probe.levelPayload.outer.ranges[2];
    if (!gameplay?.present) throw new Error("UYA candidate has no public gameplay range in outer slot 2.");
    const gameplayReader = new SubRangeReader(
      levelReader,
      gameplay.offsetBytes,
      gameplay.sizeBytes,
      `${levelReader.name}#gameplay-slot-2`,
    );

    // Decode once explicitly for the Moby compatibility census and the retail-censused shared
    // level-settings first part. The established instance helper below performs its own strict
    // WAD-LZ read; keeping those paths independent remains useful evidence.
    const gameplayWad = await readWadLz(gameplayReader, 0, { maxOutputBytes: 64 * 1024 * 1024 });
    const levelSettingsResult = uyaEnvironmentFromGameplay(gameplayWad.data, source);
    environment = levelSettingsResult.environment;
    levelSettingsSummary = levelSettingsResult.summary;
    console.log(`environment: background=${JSON.stringify(environment.backgroundColor ?? null)} fog=${JSON.stringify(environment.fogColor ?? null)} near=${environment.fogNearDistance} far=${environment.fogFarDistance} death=${environment.deathHeight}`);

    const mobyCompatibility = probeUyaGcMobyCompatibilityPublicLead(decoded, fields, gameplayWad.data);
    const cp = mobyCompatibility.classes.prerequisiteCensus;
    mobyCompatibilitySummary = {
      declaredClassEntries: cp.declaredClassCount,
      declaredUniqueOClasses: cp.declaredUniqueOClassCount,
      zeroLocalCoreClassEntries: cp.zeroAssetOffsetCount,
      invalidNonzeroAssetOffsets: cp.invalidAssetOffsetCount,
      classHeadersOutsideAssets: cp.classHeaderOutsideAssetCount,
      candidateClasses: cp.candidateUniqueOClassCount,
      decodedClasses: mobyCompatibility.classes.parserDecodedClassCount,
      allCandidateClassesDecoded: mobyCompatibility.classes.parserDecodedAllCandidateOClasses,
      missingClassOClasses: mobyCompatibility.classes.candidateOClassesMissingFromParser,
      classVertices: mobyCompatibility.classes.totalVertices,
      classTriangles: mobyCompatibility.classes.totalTriangles,
      skinnedClasses: mobyCompatibility.classes.skinnedClassCount,
      skinningAppliedClasses: mobyCompatibility.classes.skinningAppliedClassCount,
      nonFiniteClassPositions: mobyCompatibility.classes.nonFinitePositionCount,
      noTextureSentinelTriangleCount: mobyCompatibility.classes.noTextureSentinelTriangleCount,
      textureIdsOutsideMobyTable: mobyCompatibility.classes.textureIdsOutsidePublicTable,
      textureIdsOutsideMobyTableExcludingNoTextureSentinel: mobyCompatibility.classes.textureIdsOutsidePublicTableExcludingNoTextureSentinel,
      declaredInstances: mobyCompatibility.instances.prerequisiteCensus.declaredCount,
      decodedInstances: mobyCompatibility.instances.parserDecodedInstanceCount,
      allDeclaredInstancesDecoded: mobyCompatibility.instances.parserDecodedAllDeclaredEntries,
      instanceOClassesMissingFromDeclaredClassTable: mobyCompatibility.instances.oClassesMissingFromDeclaredClassTable,
      instanceOClassesWithZeroLocalAsset: mobyCompatibility.instances.oClassesWithZeroLocalAsset,
      instanceOClassesWithoutDecodedGeometry: mobyCompatibility.instances.oClassesMissingFromDecodedClassGeometry,
      instancesWithZeroLocalAsset: mobyCompatibility.instances.instanceCountWithZeroLocalAsset,
      instancesWithoutDecodedClassGeometry: mobyCompatibility.instances.instanceCountWithoutDecodedClassGeometry,
      nonFiniteInstanceComponents: mobyCompatibility.instances.nonFiniteParsedComponentCount,
      geometrySha256: mobyCompatibility.classes.geometrySha256,
      placementSha256: mobyCompatibility.instances.placementSha256,
    };
    console.log(`moby compatibility: ${mobyCompatibilitySummary.decodedClasses}/${mobyCompatibilitySummary.candidateClasses} local-core classes, ${mobyCompatibilitySummary.decodedInstances}/${mobyCompatibilitySummary.declaredInstances ?? 0} instances`);

    // Hard gates before any Moby bytes are promoted to render output.
    if (cp.invalidAssetOffsetCount !== 0 || cp.classHeaderOutsideAssetCount !== 0) throw new Error("UYA Moby class table contains invalid nonzero local-core offsets.");
    if (!mobyCompatibility.classes.parserDecodedAllCandidateOClasses) throw new Error("UYA Moby parser did not decode every local-core candidate class.");
    if (mobyCompatibility.classes.nonFinitePositionCount !== 0) throw new Error("UYA Moby class geometry contains non-finite positions.");
    if (mobyCompatibility.classes.textureIdsOutsidePublicTableExcludingNoTextureSentinel.length !== 0) throw new Error("UYA Moby geometry references unexpected texture ids beyond the confirmed table/sentinel model.");
    if (!mobyCompatibility.instances.parserDecodedAllDeclaredEntries) throw new Error("UYA Moby instance parser did not decode every declared gameplay entry.");
    if (mobyCompatibility.instances.oClassesMissingFromDeclaredClassTable.length !== 0) throw new Error("UYA gameplay references Moby oClasses absent from the declared class table.");
    if (mobyCompatibility.instances.nonFiniteParsedComponentCount !== 0) throw new Error("UYA Moby placement data contains non-finite components.");

    const placements = await readGcGameplayInstances(gameplayReader);
    addTextures("tie");
    addTextures("shrub");
    addTextures("moby");
    const tieClasses = readGcTieClasses(core);
    const shrubClasses = readGcShrubClasses(core);
    const mobyClasses = readGcMobyClasses(core);

    placeInstances("tie", placements.tieInstances, tieClasses);
    placeInstances("shrub", placements.shrubInstances, shrubClasses);

    const zeroCoreOClasses = new Set(mobyCompatibility.instances.oClassesWithZeroLocalAsset);
    const emptyGeometryOClasses = new Set(
      [...mobyClasses.entries()]
        .filter(([, cls]) => cls.mesh.indices.length === 0)
        .map(([oClass]) => oClass),
    );
    const unresolvedSkinningOClasses = new Set(
      [...mobyClasses.entries()]
        .filter(([, cls]) => cls.mesh.skinned && !cls.mesh.skinningApplied)
        .map(([oClass]) => oClass),
    );
    const nonRenderableNoGeometryOClasses = new Set([...zeroCoreOClasses, ...emptyGeometryOClasses]);
    const zeroLocalCoreInstancesSkipped = placements.mobyInstances.filter((inst) => zeroCoreOClasses.has(inst.oClass)).length;
    const emptyGeometryInstancesSkipped = placements.mobyInstances.filter((inst) => emptyGeometryOClasses.has(inst.oClass)).length;
    const unresolvedSkinningInstancesSkipped = placements.mobyInstances.filter((inst) => unresolvedSkinningOClasses.has(inst.oClass)).length;
    const mobyPlaced = placeInstances("moby", placements.mobyInstances, mobyClasses, {
      allowedMissingOClasses: nonRenderableNoGeometryOClasses,
      skipOClasses: unresolvedSkinningOClasses,
      boundsFromInstances: false,
      noTextureSentinel: 0xff,
    });
    mobyRenderSummary = {
      renderedInstances: mobyPlaced.placed,
      zeroLocalCoreOClasses: [...zeroCoreOClasses].sort((a, b) => a - b),
      zeroLocalCoreInstancesSkipped,
      emptyGeometryOClasses: [...emptyGeometryOClasses].sort((a, b) => a - b),
      emptyGeometryInstancesSkipped,
      unresolvedSkinningOClasses: [...unresolvedSkinningOClasses].sort((a, b) => a - b),
      unresolvedSkinningInstancesSkipped,
      renderTriangles: mobyPlaced.triangles,
      materiallessNoTextureTriangles: mobyPlaced.noTextureTriangles,
    };
    console.log(`moby render subset: ${mobyRenderSummary.renderedInstances}/${placements.mobyInstances.length} instances -> ${mobyRenderSummary.renderTriangles} tris; zero-core skipped ${mobyRenderSummary.zeroLocalCoreInstancesSkipped}, empty-geometry skipped ${mobyRenderSummary.emptyGeometryInstancesSkipped}, unresolved-skinning skipped ${mobyRenderSummary.unresolvedSkinningInstancesSkipped}`);
  }

  // --- collision ---
  if (opt.collision) {
    const range = decoded.publicCollisionRange;
    if (!range) throw new Error("UYA decoded core exposes no public collision range.");
    const nativeCollision = readRcCollision(decoded.assets.subarray(range.offset, range.offset + range.size));
    collisionMeshes.push(toObpCollisionMesh(nativeCollision, source, `${matPrefix}-collision`, `UYA table ${tableIndex} collision`));
    grow(obpCollisionBounds(nativeCollision));
    console.log(`collision: ${nativeCollision.octants.length} octants, ${nativeCollision.positions.length / 3} verts, ${nativeCollision.triangles.length} tris`);
  }

  if (!Number.isFinite(min.x)) { min.x = min.y = min.z = 0; max.x = max.y = max.z = 0; }

  // --- layered UYA sky ---
  // Deliberately do not grow the world bounds with the dome: the viewer identifies sky meshes by
  // source.assetKind, recentres them on the camera and draws them without writing depth.
  if (opt.sky) {
    const skyResult = appendUyaSky({
      decoded,
      fields,
      source,
      matPrefix,
      materials,
      meshes,
      bounds: { min, max },
      environment,
      textures: opt.textures,
    });
    environment = skyResult.environment;
    skySummary = skyResult.summary;
    console.log(`sky: ${skySummary.shellCount} shells, ${skySummary.textureCount} textures, ${skySummary.clusterCount} clusters -> ${skySummary.triangles} tris; moving=${skySummary.movingShells} bloom=${skySummary.bloomShells}`);
  }

  const world = {
    schemaVersion: 1,
    id: `rac3-table${tableIndex}-retail-candidate`,
    displayName: `Up Your Arsenal retail table ${tableIndex} candidate`,
    source,
    bounds: { min, max },
    materials,
    meshes,
    collisionMeshes,
    // TIE/shrub placements are baked from exact matrices. Moby pos/rot/scale is likewise baked for
    // renderable local-core classes. Unsupported/zero-core objects remain evidenced by probe output.
    instances: [],
    splines: [],
    volumes: [],
    spawnPoints: [],
    ...(environment ? { environment } : {}),
  };
  const issues = validateWorld(world);
  if (issues.length) throw new Error(`Generated UYA OBPWorld failed validation: ${issues.slice(0, 8).map((i) => `${i.path}: ${i.message}`).join(" | ")}`);
  const stats = worldStats(world);
  const json = JSON.stringify(world);
  await mkdir(resolve(opt.out, ".."), { recursive: true });
  await writeFile(resolve(opt.out), json);
  console.log(JSON.stringify({
    result: "UYA_OBP_WORLD_PASS",
    tableIndex,
    authoritySha256: authority.sha256,
    coreIndexSha256: decoded.coreIndexSha256,
    assetsSha256: decoded.assetsSha256,
    tfragCount: tfrags.tfragCount,
    tfragTextureCount: fields.tfragTextures.count,
    tieClassCount: fields.tieClasses.count,
    tieTextureCount: fields.tieTextures.count,
    shrubClassCount: fields.shrubClasses.count,
    shrubTextureCount: fields.shrubTextures.count,
    mobyTextureCount: fields.mobyTextures.count,
    ...(levelSettingsSummary ? { levelSettings: levelSettingsSummary } : {}),
    ...(skySummary ? { sky: skySummary } : {}),
    ...(mobyCompatibilitySummary ? { mobyCompatibility: mobyCompatibilitySummary } : {}),
    ...(mobyRenderSummary ? { mobyRender: mobyRenderSummary } : {}),
    worldStats: stats,
    bounds: world.bounds,
    outputBytes: Buffer.byteLength(json),
    output: resolve(opt.out),
  }, null, 2));

  function placeInstances(kind, instances, classes, options = {}) {
    const byTex = new Map();
    const allowedMissingOClasses = options.allowedMissingOClasses ?? new Set();
    const skipOClasses = options.skipOClasses ?? new Set();
    const boundsFromInstances = options.boundsFromInstances ?? true;
    const noTextureSentinel = options.noTextureSentinel;
    const unexpectedMissing = new Set();
    let placed = 0;
    let allowedMissing = 0;
    let skipped = 0;
    let noTextureTriangles = 0;

    for (const inst of instances) {
      if (skipOClasses.has(inst.oClass)) { skipped++; continue; }
      const cls = classes.get(inst.oClass);
      if (!cls || cls.mesh.indices.length === 0) {
        if (allowedMissingOClasses.has(inst.oClass)) { allowedMissing++; continue; }
        unexpectedMissing.add(inst.oClass);
        continue;
      }
      placed++;
      const matrix = inst.matrix ?? matrixFromPosRotScale(inst.position, inst.rotation, inst.scale || 1);
      const m = cls.mesh;
      const worldPos = new Float64Array(m.positions.length);
      for (let i = 0; i < m.positions.length; i += 3) {
        const [x, y, z] = transformPoint(matrix, m.positions[i], m.positions[i + 1], m.positions[i + 2]);
        const ox = r2(x), oy = r2(z), oz = r2(y); // native Z-up -> OBP Y-up
        worldPos[i] = ox; worldPos[i + 1] = oy; worldPos[i + 2] = oz;
        if (boundsFromInstances) {
          min.x = Math.min(min.x, ox); min.y = Math.min(min.y, oy); min.z = Math.min(min.z, oz);
          max.x = Math.max(max.x, ox); max.y = Math.max(max.y, oy); max.z = Math.max(max.z, oz);
        }
      }
      for (let f = 0; f < cls.triangleTextureIds.length; f++) {
        const tex = cls.triangleTextureIds[f];
        if (noTextureSentinel !== undefined && tex === noTextureSentinel) noTextureTriangles++;
        let g = byTex.get(tex);
        if (!g) byTex.set(tex, (g = { positions: [], uvs: [], indices: [], weld: new Map() }));
        for (let k = 0; k < 3; k++) {
          const vi = m.indices[f * 3 + k];
          const px = worldPos[vi * 3], py = worldPos[vi * 3 + 1], pz = worldPos[vi * 3 + 2];
          const s = r4(m.uvs[vi * 2]), t = r4(m.uvs[vi * 2 + 1]);
          const key = `${px},${py},${pz},${s},${t}`;
          let idx = g.weld.get(key);
          if (idx === undefined) {
            idx = g.positions.length / 3;
            g.weld.set(key, idx);
            g.positions.push(px, py, pz);
            g.uvs.push(s, t);
          }
          g.indices.push(idx);
        }
      }
    }
    if (unexpectedMissing.size !== 0) throw new Error(`${kind}: unexpected placements without renderable decoded classes: ${[...unexpectedMissing].sort((a, b) => a - b).join(",")}`);
    let triangles = 0;
    for (const [tex, g] of [...byTex.entries()].sort((a, b) => a[0] - b[0])) {
      triangles += g.indices.length / 3;
      const materialless = noTextureSentinel !== undefined && tex === noTextureSentinel;
      meshes.push({
        id: `${matPrefix}-${kind}-tex${tex}`,
        name: materialless ? `${kind} no-texture geometry` : `${kind} placed texture ${tex}`,
        geometry: { positions: g.positions, indices: g.indices, uvs: g.uvs },
        ...(!materialless ? { materialId: `${matPrefix}-${kind}-tex${tex}` } : {}),
        source: { ...source, assetKind: kind, originalId: tex },
      });
    }
    console.log(`${kind}: ${placed}/${instances.length} instances, ${classes.size} classes -> ${triangles} tris`);
    return { placed, allowedMissing, skipped, triangles, noTextureTriangles };
  }
} finally {
  await reader.close();
}

function sectionRangeAllowZero(offset, boundaries, assetsLength) {
  if (!Number.isSafeInteger(offset) || offset < 0 || offset >= assetsLength) return undefined;
  let next;
  for (const bound of boundaries) {
    if (bound > offset && bound <= assetsLength && (next === undefined || bound < next)) next = bound;
  }
  return next === undefined ? undefined : { offset, size: next - offset };
}
