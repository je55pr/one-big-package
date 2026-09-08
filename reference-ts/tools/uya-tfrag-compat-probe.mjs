#!/usr/bin/env node
import { decodeUyaCoreDataPublicLead } from "../.build/packages/uya-core-decode/src/index.js";
import { openUyaTocPayloadPublicLead } from "../.build/packages/uya-level-wad/src/index.js";
import { probeUyaGcTfragCompatibilityPublicLead } from "../.build/packages/uya-tfrag-compat/src/index.js";
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
    "  npm run build && node tools/uya-tfrag-compat-probe.mjs --iso '<retail UYA ISO>' --table-index N [options]",
    "  OBP_UYA_HTTP_RANGE_URL='<whole-disc shared/range URL>' npm run build && node tools/uya-tfrag-compat-probe.mjs --table-index N [options]",
    "  OBP_UYA_HTTP_RANGE_PARTS_JSON='[{\"partNumber\":1,\"url\":\"...\"},...]' npm run build && node tools/uya-tfrag-compat-probe.mjs --table-index N [options]",
    "",
    "Options: --max-core-index-bytes N --max-decompressed-bytes N --max-class-entries N --max-vertices N --max-triangles N",
    "The source must pass the pinned retail NTSC-U ToC-window identity hash before semantic probing.",
    "This tool leaves the existing GC tfrag/VIF reader unchanged and reports its tolerant per-fragment skip prerequisites separately.",
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

const args = process.argv.slice(2);
if (args.length === 0 || args.includes("--help") || args.includes("-h")) usage();
let isoPath;
let tableIndex;
let maxCoreIndexBytes;
let maxDecompressedBytes;
let maxClassEntries;
let maxVertices;
let maxTriangles;
for (let i = 0; i < args.length; i++) {
  const arg = args[i];
  if (!arg.startsWith("--")) usage();
  const value = args[++i];
  if (value === undefined) usage();
  if (arg === "--iso") isoPath = value;
  else if (arg === "--table-index") tableIndex = parseInteger(value, arg, { allowZero: true });
  else if (arg === "--max-core-index-bytes") maxCoreIndexBytes = parseInteger(value, arg);
  else if (arg === "--max-decompressed-bytes") maxDecompressedBytes = parseInteger(value, arg);
  else if (arg === "--max-class-entries") maxClassEntries = parseInteger(value, arg);
  else if (arg === "--max-vertices") maxVertices = parseInteger(value, arg);
  else if (arg === "--max-triangles") maxTriangles = parseInteger(value, arg);
  else usage();
}
if (tableIndex === undefined) usage();
if (isoPath && hasUyaHttpRangeSourceEnv()) throw new Error("Choose either --iso or an HTTP range source, not both.");
if (!isoPath && !hasUyaHttpRangeSourceEnv()) throw new Error("UYA tfrag compatibility probe requires --iso or an HTTP range source environment input.");

let localDisc;
let disc;
let sourceMode;
let sourceParts;
if (isoPath) {
  localDisc = await LocalFileRandomAccessReader.open(isoPath, "uya-local-retail-iso");
  disc = localDisc;
  sourceMode = "local-file-random-access";
  sourceParts = [{
    partNumber: 1,
    name: localDisc.name,
    sizeBytes: localDisc.size,
    logicalStartBytes: 0,
    transport: "local-file",
  }];
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
  const levelReader = openUyaTocPayloadPublicLead(
    disc,
    world.selectedMainPart,
    `uya-table-${tableIndex}-candidate-level`,
  );
  const decoded = await decodeUyaCoreDataPublicLead(
    levelReader,
    world.levelPayload.outer,
    world.levelPayload.data,
    world.levelPayload.core,
    world.levelPayload.coreDataWadLz,
    {
      ...(maxCoreIndexBytes !== undefined ? { maxCoreIndexBytes } : {}),
      ...(maxDecompressedBytes !== undefined ? { maxDecompressedBytes } : {}),
      ...(maxClassEntries !== undefined ? { maxClassEntries } : {}),
    },
  );
  const publicFields = world.levelPayload.core.publicFields;
  const tfrags = probeUyaGcTfragCompatibilityPublicLead(decoded, publicFields.tfrags, {
    publicTfragTextureCount: publicFields.tfragTextures.count,
    ...(maxVertices !== undefined ? { maxVertices } : {}),
    ...(maxTriangles !== undefined ? { maxTriangles } : {}),
  });

  console.log(JSON.stringify({
    schemaVersion: 1,
    evidenceStatus: "retail bytes observed and decoded by this run; candidate tfrag extent and texture-table semantics remain pinned-public leads until independently promoted",
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
      publicTfragsOffset: publicFields.tfrags,
      publicTfragTextureCount: publicFields.tfragTextures.count,
      warnings: decoded.warnings,
    },
    tfrags,
  }, null, 2));
} finally {
  if (localDisc) await localDisc.close();
}
