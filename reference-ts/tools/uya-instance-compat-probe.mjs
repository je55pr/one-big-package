#!/usr/bin/env node
import { SubRangeReader } from "../.build/packages/importer-common/src/index.js";
import { Sha256 } from "../.build/packages/hashing/src/index.js";
import { decodeUyaCoreDataPublicLead } from "../.build/packages/uya-core-decode/src/index.js";
import { probeUyaGcInstancePlacementCompatibilityPublicLead } from "../.build/packages/uya-instance-compat/src/index.js";
import { openUyaTocPayloadPublicLead } from "../.build/packages/uya-level-wad/src/index.js";
import { probeUyaWorldCandidatePublicLead } from "../.build/packages/uya-world-probe/src/index.js";
import { readWadLz } from "../.build/packages/wad-lz/src/index.js";
import { hasUyaHttpRangeSourceEnv, openUyaHttpRangeSourceFromEnv } from "./uya-http-range-source.mjs";
import { LocalFileRandomAccessReader } from "./local-random-access.mjs";
import { assertUyaAuthorityTocWindow } from "./uya-retail-authority.mjs";

function usage() {
  console.error([
    "Usage:",
    "  npm run build && node tools/uya-instance-compat-probe.mjs --iso '<retail UYA ISO>' --table-index N",
    "  OBP_UYA_HTTP_RANGE_URL='<whole-disc shared/range URL>' npm run build && node tools/uya-instance-compat-probe.mjs --table-index N",
    "",
    "The source must pass the pinned retail NTSC-U ToC-window identity hash before semantic probing.",
    "The public-derived gameplay slot is WAD-LZ decoded, then TIE/shrub blocks are independently censused before the unchanged GC instance parser is compared.",
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

function sha256(bytes) {
  return new Sha256().update(bytes).digestHex();
}

function compact(result) {
  const ids = result.oClassesObserved;
  return {
    prerequisiteCensus: result.prerequisiteCensus,
    candidateClassCount: result.candidateClassCount,
    parserDecodedInstanceCount: result.parserDecodedInstanceCount,
    parserDecodedAllDeclaredEntries: result.parserDecodedAllDeclaredEntries,
    observedOClassCount: ids.length,
    ...(ids.length ? { oClassMin: ids[0], oClassMax: ids[ids.length - 1] } : {}),
    oClassesMissingFromCandidateClassTable: result.oClassesMissingFromCandidateClassTable,
    nonFiniteParsedMatrixComponentCount: result.nonFiniteParsedMatrixComponentCount,
    ...(result.translationBounds ? { translationBounds: result.translationBounds } : {}),
    matricesSha256: result.matricesSha256,
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
if (!isoPath && !hasUyaHttpRangeSourceEnv()) throw new Error("UYA instance compatibility probe requires --iso or an HTTP range source environment input.");

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
  const decodedCore = await decodeUyaCoreDataPublicLead(
    levelReader,
    world.levelPayload.outer,
    world.levelPayload.data,
    world.levelPayload.core,
    world.levelPayload.coreDataWadLz,
  );

  const gameplayRange = world.levelPayload.outer.ranges[2];
  if (!gameplayRange?.present) throw new Error("UYA candidate outer header has no public gameplay range in slot 2.");
  const gameplayReader = new SubRangeReader(
    levelReader,
    gameplayRange.offsetBytes,
    gameplayRange.sizeBytes,
    `${levelReader.name}#public-gameplay`,
  );
  const wad = await readWadLz(gameplayReader, 0, { maxOutputBytes: 64 * 1024 * 1024 });
  const compressedBytes = await gameplayReader.read(0, wad.compressedSize);
  const compatibility = probeUyaGcInstancePlacementCompatibilityPublicLead(
    wad.data,
    decodedCore,
    world.levelPayload.core.publicFields,
  );

  console.log(JSON.stringify({
    schemaVersion: 1,
    evidenceStatus: "retail bytes observed and decoded by this run; gameplay-slot and block-pointer semantics remain compatibility hypotheses until independently promoted",
    sourceMode,
    sourceParts,
    logicalDiscSizeBytes: disc.size,
    authorityIdentity,
    selectedTableIndex: tableIndex,
    selectedMainPart: world.selectedMainPart,
    gameplay: {
      publicRange: gameplayRange,
      compressedBytes: wad.compressedSize,
      compressedSha256: sha256(compressedBytes),
      decompressedBytes: wad.data.length,
      decompressedSha256: sha256(wad.data),
      wadName: wad.name,
    },
    tie: compact(compatibility.tie),
    shrub: compact(compatibility.shrub),
  }, null, 2));
} finally {
  if (localDisc) await localDisc.close();
}
