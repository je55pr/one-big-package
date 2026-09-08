#!/usr/bin/env node
import { SubRangeReader } from "../.build/packages/importer-common/src/index.js";
import { Sha256 } from "../.build/packages/hashing/src/index.js";
import { decodeUyaCoreDataPublicLead } from "../.build/packages/uya-core-decode/src/index.js";
import { probeUyaGcMobyCompatibilityPublicLead } from "../.build/packages/uya-moby-compat/src/index.js";
import { openUyaTocPayloadPublicLead } from "../.build/packages/uya-level-wad/src/index.js";
import { probeUyaWorldCandidatePublicLead } from "../.build/packages/uya-world-probe/src/index.js";
import { readWadLz } from "../.build/packages/wad-lz/src/index.js";
import { hasUyaHttpRangeSourceEnv, openUyaHttpRangeSourceFromEnv } from "./uya-http-range-source.mjs";
import { LocalFileRandomAccessReader } from "./local-random-access.mjs";
import { assertUyaAuthorityTocWindow } from "./uya-retail-authority.mjs";

function usage() {
  console.error([
    "Usage:",
    "  npm run build && node tools/uya-moby-compat-probe.mjs --iso '<retail UYA ISO>' --table-index N",
    "  OBP_UYA_HTTP_RANGE_URL='<whole-disc shared/range URL>' npm run build && node tools/uya-moby-compat-probe.mjs --table-index N",
    "",
    "The source must pass the pinned retail NTSC-U ToC-window identity hash before semantic probing.",
    "Moby class geometry and the public-derived gameplay Moby block are independently censused before unchanged GC readers are compared.",
  ].join("\n"));
  process.exit(2);
}

function parseIndex(text) {
  const value = Number(text);
  if (!Number.isSafeInteger(value) || value < 0) throw new Error(`--table-index must be a non-negative integer, got '${text}'.`);
  return value;
}
function sha256(bytes) { return new Sha256().update(bytes).digestHex(); }
function compactClasses(c) {
  return {
    publicClassRange: c.publicClassRange,
    publicTextureCount: c.publicTextureCount,
    prerequisiteCensus: c.prerequisiteCensus,
    parserDecodedClassCount: c.parserDecodedClassCount,
    parserDecodedAllCandidateOClasses: c.parserDecodedAllCandidateOClasses,
    candidateOClassesMissingFromParser: c.candidateOClassesMissingFromParser,
    totalVertices: c.totalVertices,
    totalTriangles: c.totalTriangles,
    skinnedClassCount: c.skinnedClassCount,
    skinningAppliedClassCount: c.skinningAppliedClassCount,
    nonFinitePositionCount: c.nonFinitePositionCount,
    textureIdsObservedCount: c.textureIdsObserved.length,
    ...(c.textureIdsObserved.length ? { textureIdMin: c.textureIdsObserved[0], textureIdMax: c.textureIdsObserved.at(-1) } : {}),
    textureIdsOutsidePublicTable: c.textureIdsOutsidePublicTable,
    geometrySha256: c.geometrySha256,
  };
}
function compactInstances(i) {
  return {
    prerequisiteCensus: i.prerequisiteCensus,
    parserDecodedInstanceCount: i.parserDecodedInstanceCount,
    parserDecodedAllDeclaredEntries: i.parserDecodedAllDeclaredEntries,
    observedOClassCount: i.observedOClasses.length,
    ...(i.observedOClasses.length ? { oClassMin: i.observedOClasses[0], oClassMax: i.observedOClasses.at(-1) } : {}),
    oClassesMissingFromCandidateClassTable: i.oClassesMissingFromCandidateClassTable,
    oClassesMissingFromDecodedClassGeometry: i.oClassesMissingFromDecodedClassGeometry,
    nonFiniteParsedComponentCount: i.nonFiniteParsedComponentCount,
    placementSha256: i.placementSha256,
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
  else if (arg === "--table-index") tableIndex = parseIndex(value);
  else usage();
}
if (tableIndex === undefined) usage();
if (isoPath && hasUyaHttpRangeSourceEnv()) throw new Error("Choose either --iso or an HTTP range source, not both.");
if (!isoPath && !hasUyaHttpRangeSourceEnv()) throw new Error("UYA Moby compatibility probe requires --iso or an HTTP range source.");

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
  const http = await openUyaHttpRangeSourceFromEnv();
  if (!http) throw new Error("UYA HTTP range mode selected without a usable source.");
  disc = http.disc;
  sourceMode = http.sourceMode;
  sourceParts = http.sourceParts;
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
  const gameplayReader = new SubRangeReader(levelReader, gameplayRange.offsetBytes, gameplayRange.sizeBytes, `${levelReader.name}#public-gameplay`);
  const wad = await readWadLz(gameplayReader, 0, { maxOutputBytes: 64 * 1024 * 1024 });
  const compressed = await gameplayReader.read(0, wad.compressedSize);
  const compatibility = probeUyaGcMobyCompatibilityPublicLead(decodedCore, world.levelPayload.core.publicFields, wad.data);
  console.log(JSON.stringify({
    schemaVersion: 1,
    evidenceStatus: "retail bytes observed and decoded by this run; Moby class/gameplay semantic names remain compatibility hypotheses until independently promoted",
    sourceMode,
    sourceParts,
    logicalDiscSizeBytes: disc.size,
    authorityIdentity,
    selectedTableIndex: tableIndex,
    selectedMainPart: world.selectedMainPart,
    gameplay: {
      publicRange: gameplayRange,
      compressedBytes: wad.compressedSize,
      compressedSha256: sha256(compressed),
      decompressedBytes: wad.data.length,
      decompressedSha256: sha256(wad.data),
    },
    classes: compactClasses(compatibility.classes),
    instances: compactInstances(compatibility.instances),
  }, null, 2));
} finally {
  if (localDisc) await localDisc.close();
}
