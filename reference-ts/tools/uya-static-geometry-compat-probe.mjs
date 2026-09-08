#!/usr/bin/env node
import { decodeUyaCoreDataPublicLead } from "../.build/packages/uya-core-decode/src/index.js";
import { openUyaTocPayloadPublicLead } from "../.build/packages/uya-level-wad/src/index.js";
import { probeUyaGcStaticGeometryCompatibilityPublicLead } from "../.build/packages/uya-static-geometry-compat/src/index.js";
import { probeUyaWorldCandidatePublicLead } from "../.build/packages/uya-world-probe/src/index.js";
import {
  hasUyaHttpRangeSourceEnv,
  openUyaHttpRangeSourceFromEnv,
} from "./uya-http-range-source.mjs";
import { LocalFileRandomAccessReader } from "./local-random-access.mjs";
import { assertUyaAuthorityTocWindow } from "./uya-retail-authority.mjs";

function usage() {
  console.error([
    "Usage:",
    "  npm run build && node tools/uya-static-geometry-compat-probe.mjs --iso '<retail UYA ISO>' --table-index N",
    "  OBP_UYA_HTTP_RANGE_URL='<whole-disc shared/range URL>' npm run build && node tools/uya-static-geometry-compat-probe.mjs --table-index N",
    "",
    "The source must pass the pinned retail NTSC-U ToC-window identity hash before semantic probing.",
    "The unchanged GC TIE and shrub readers are compared against an independent class-table prerequisite census.",
    "Texture ID lists are summarized by min/max/count; callers can use the compatibility package for exact lists.",
  ].join("\n"));
  process.exit(2);
}

function parseInteger(text, label, { allowZero = false } = {}) {
  const value = Number(text);
  if (!Number.isSafeInteger(value) || value < (allowZero ? 0 : 1)) {
    throw new Error(`${label} must be ${allowZero ? "a non-negative" : "a positive"} integer, got '${text}'.`);
  }
  return value;
}

function compactGeometry(result) {
  const ids = result.textureIdsObserved;
  return {
    publicClassRange: result.publicClassRange,
    publicTextureCount: result.publicTextureCount,
    prerequisiteCensus: result.prerequisiteCensus,
    parserDecodedClassCount: result.parserDecodedClassCount,
    parserDecodedAllCandidateOClasses: result.parserDecodedAllCandidateOClasses,
    totalVertices: result.totalVertices,
    totalTriangles: result.totalTriangles,
    textureIdsObservedCount: ids.length,
    ...(ids.length ? { textureIdMin: ids[0], textureIdMax: ids[ids.length - 1] } : {}),
    textureIdsOutsidePublicTable: result.textureIdsOutsidePublicTable,
    nonFinitePositionCount: result.nonFinitePositionCount,
    ...(result.bounds ? { bounds: result.bounds } : {}),
    geometrySha256: result.geometrySha256,
  };
}

const args = process.argv.slice(2);
if (args.length === 0 || args.includes("--help") || args.includes("-h")) usage();
let isoPath;
let tableIndex;
for (let i = 0; i < args.length; i++) {
  const arg = args[i];
  if (!arg.startsWith("--")) usage();
  const value = args[++i];
  if (value === undefined) usage();
  if (arg === "--iso") isoPath = value;
  else if (arg === "--table-index") tableIndex = parseInteger(value, arg, { allowZero: true });
  else usage();
}
if (tableIndex === undefined) usage();
if (isoPath && hasUyaHttpRangeSourceEnv()) throw new Error("Choose either --iso or an HTTP range source, not both.");
if (!isoPath && !hasUyaHttpRangeSourceEnv()) throw new Error("UYA static geometry compatibility probe requires --iso or an HTTP range source environment input.");

let localDisc;
let disc;
let sourceMode;
let sourceParts;
if (isoPath) {
  localDisc = await LocalFileRandomAccessReader.open(isoPath, "uya-local-retail-iso");
  disc = localDisc;
  sourceMode = "local-file-random-access";
  sourceParts = [{ partNumber: 1, name: localDisc.name, sizeBytes: localDisc.size, logicalStartBytes: 0, transport: "local-file" }];
} else {
  const httpSource = await openUyaHttpRangeSourceFromEnv();
  if (!httpSource) throw new Error("UYA HTTP range mode was selected without a usable source.");
  disc = httpSource.disc;
  sourceMode = httpSource.sourceMode;
  sourceParts = httpSource.sourceParts;
}

try {
  const authorityIdentity = await assertUyaAuthorityTocWindow(disc);
  const world = await probeUyaWorldCandidatePublicLead(disc, { tableIndex });
  const levelReader = openUyaTocPayloadPublicLead(disc, world.selectedMainPart, `uya-table-${tableIndex}-candidate-level`);
  const decoded = await decodeUyaCoreDataPublicLead(
    levelReader,
    world.levelPayload.outer,
    world.levelPayload.data,
    world.levelPayload.core,
    world.levelPayload.coreDataWadLz,
  );
  const compatibility = probeUyaGcStaticGeometryCompatibilityPublicLead(decoded, world.levelPayload.core.publicFields);

  console.log(JSON.stringify({
    schemaVersion: 2,
    evidenceStatus: "retail bytes observed and decoded by this run; class-table semantic names remain pinned-public leads until independently promoted",
    sourceMode,
    sourceParts,
    logicalDiscSizeBytes: disc.size,
    authorityIdentity,
    selectedTableIndex: tableIndex,
    selectedMainPart: world.selectedMainPart,
    core: {
      coreIndexBytes: decoded.coreIndex.length,
      coreIndexSha256: decoded.coreIndexSha256,
      compressedCoreDataBytes: decoded.compressedCoreData.length,
      compressedCoreDataSha256: decoded.compressedCoreDataSha256,
      decompressedAssetsBytes: decoded.assets.length,
      decompressedAssetsSha256: decoded.assetsSha256,
      warnings: decoded.warnings,
    },
    sectionBoundaryCount: compatibility.sectionBoundaries.length,
    tie: compactGeometry(compatibility.tie),
    shrub: compactGeometry(compatibility.shrub),
  }, null, 2));
} finally {
  if (localDisc) await localDisc.close();
}
