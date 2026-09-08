#!/usr/bin/env node
import { decodeUyaCoreDataPublicLead } from "../.build/packages/uya-core-decode/src/index.js";
import { openUyaTocPayloadPublicLead } from "../.build/packages/uya-level-wad/src/index.js";
import { probeUyaGcTextureCompatibilityPublicLead } from "../.build/packages/uya-texture-compat/src/index.js";
import { probeUyaWorldCandidatePublicLead } from "../.build/packages/uya-world-probe/src/index.js";
import {
  hasUyaHttpRangeSourceEnv,
  openUyaHttpRangeSourceFromEnv,
} from "./uya-http-range-source.mjs";
import { LocalFileRandomAccessReader } from "./local-random-access.mjs";
import { assertUyaAuthorityTocWindow } from "./uya-retail-authority.mjs";

const TABLES = ["tfrag", "moby", "tie", "shrub", "part", "fx"];

function usage() {
  console.error([
    "Usage:",
    "  npm run build && node tools/uya-texture-compat-probe.mjs --iso '<retail UYA ISO>' --table-index N [--max-dimension N]",
    "  OBP_UYA_HTTP_RANGE_URL='<whole-disc shared/range URL>' npm run build && node tools/uya-texture-compat-probe.mjs --table-index N [--max-dimension N]",
    "",
    "The source must pass the pinned retail NTSC-U ToC-window identity hash before semantic probing.",
    "All six public-derived level texture tables are probed through the unchanged retail-GC texture reader.",
    "Output is intentionally compact; the compatibility package retains exact eligible/decoded index lists for tests and callers.",
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

function compactTexture(result) {
  const census = result.prerequisiteCensus;
  return {
    publicRange: result.publicRange,
    prerequisiteCensus: {
      declaredTextureCount: census.declaredTextureCount,
      tableOffset: census.tableOffset,
      tableByteLength: census.tableByteLength,
      tableWithinCoreIndex: census.tableWithinCoreIndex,
      invalidDimensionCount: census.invalidDimensionCount,
      invalidPixelRangeCount: census.invalidPixelRangeCount,
      invalidPaletteRangeCount: census.invalidPaletteRangeCount,
      parserEligibleEntryCount: census.parserEligibleEntryCount,
      typeIds: census.typeIds,
      ...(census.minWidth !== undefined ? { minWidth: census.minWidth } : {}),
      ...(census.maxWidth !== undefined ? { maxWidth: census.maxWidth } : {}),
      ...(census.minHeight !== undefined ? { minHeight: census.minHeight } : {}),
      ...(census.maxHeight !== undefined ? { maxHeight: census.maxHeight } : {}),
      ...(census.minPaletteSlot !== undefined ? { minPaletteSlot: census.minPaletteSlot } : {}),
      ...(census.maxPaletteSlot !== undefined ? { maxPaletteSlot: census.maxPaletteSlot } : {}),
    },
    parserDecodedTextureCount: result.parserDecodedTextureCount,
    parserDecodedAllEligibleEntries: result.parserDecodedAllEligibleEntries,
    decodedRgbaSha256: result.decodedRgbaSha256,
    decodedRgbaByteLength: result.decodedRgbaByteLength,
  };
}

const args = process.argv.slice(2);
if (args.length === 0 || args.includes("--help") || args.includes("-h")) usage();
let isoPath;
let tableIndex;
let maxDimension;
for (let i = 0; i < args.length; i++) {
  const arg = args[i];
  if (!arg.startsWith("--")) usage();
  const value = args[++i];
  if (value === undefined) usage();
  if (arg === "--iso") isoPath = value;
  else if (arg === "--table-index") tableIndex = parseInteger(value, arg, { allowZero: true });
  else if (arg === "--max-dimension") maxDimension = parseInteger(value, arg);
  else usage();
}
if (tableIndex === undefined) usage();
if (isoPath && hasUyaHttpRangeSourceEnv()) throw new Error("Choose either --iso or an HTTP range source, not both.");
if (!isoPath && !hasUyaHttpRangeSourceEnv()) throw new Error("UYA texture compatibility probe requires --iso or an HTTP range source environment input.");

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
  );

  const primary = world.levelPayload.outer.ranges[0];
  const gsRange = world.levelPayload.data.gsRam;
  if (!primary?.present) throw new Error("UYA candidate outer header has no public primary range.");
  if (!gsRange.present) throw new Error("UYA candidate data header has no public GS-RAM range.");
  const gsRamOffset = primary.offsetBytes + gsRange.offset;
  if (!Number.isSafeInteger(gsRamOffset) || gsRamOffset < 0 || gsRamOffset > levelReader.size || gsRange.size > levelReader.size - gsRamOffset) {
    throw new RangeError(`UYA candidate GS-RAM range ${gsRamOffset}+${gsRange.size} lies outside level payload ${levelReader.size}.`);
  }
  const gsRam = await levelReader.read(gsRamOffset, gsRange.size);

  const publicFields = world.levelPayload.core.publicFields;
  const fullTextures = Object.fromEntries(TABLES.map((table) => [
    table,
    probeUyaGcTextureCompatibilityPublicLead(decoded, publicFields, gsRam, table, {
      ...(maxDimension !== undefined ? { maxDimension } : {}),
    }),
  ]));
  const textures = Object.fromEntries(TABLES.map((table) => [table, compactTexture(fullTextures[table])]));

  console.log(JSON.stringify({
    schemaVersion: 2,
    evidenceStatus: "retail bytes observed and decoded by this run; texture-table and GS-RAM semantics remain pinned-public leads until independently promoted",
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
      publicTexturesBaseOffset: publicFields.texturesBaseOffset,
      gsRamOffsetInLevelBytes: gsRamOffset,
      gsRamBytes: gsRam.length,
      gsRamSha256: fullTextures.tfrag.gsRamSha256,
      warnings: decoded.warnings,
    },
    textures,
  }, null, 2));
} finally {
  if (localDisc) await localDisc.close();
}
