#!/usr/bin/env node
import {
  ConcatenatedRandomAccessReader,
  MappedRandomAccessReader,
} from "../.build/packages/importer-common/src/index.js";
import {
  decodeUyaCoreDataPublicLead,
  probeUyaRcCollisionCompatibilityPublicLead,
} from "../.build/packages/uya-core-decode/src/index.js";
import { openUyaTocPayloadPublicLead } from "../.build/packages/uya-level-wad/src/index.js";
import { probeUyaWorldCandidatePublicLead } from "../.build/packages/uya-world-probe/src/index.js";
import { LocalFileRandomAccessReader } from "./local-random-access.mjs";
import {
  hasUyaHttpRangeSourceEnv,
  openUyaHttpRangeSourceFromEnv,
} from "./uya-http-range-source.mjs";
import { assertUyaAuthorityTocWindow } from "./uya-retail-authority.mjs";

function usage() {
  console.error([
    "Usage:",
    "  npm run build && node tools/uya-core-collision-probe.mjs <UYA .iso or ordered split parts...> --table-index N [options]",
    "  OBP_UYA_HTTP_RANGE_URL='<whole-disc shared/range URL>' npm run build && node tools/uya-core-collision-probe.mjs --table-index N [options]",
    "  OBP_UYA_HTTP_RANGE_PARTS_JSON='[{\"partNumber\":1,\"url\":\"...\"},...]' npm run build && node tools/uya-core-collision-probe.mjs --table-index N [options]",
    "  npm run build && node tools/uya-core-collision-probe.mjs --sparse-part N PATH [--sparse-part N PATH ...] --split-part-bytes N --logical-disc-bytes N --table-index N [options]",
    "",
    "Options: --toc-lba N --window-bytes N --max-level-rows N --max-core-index-bytes N --max-decompressed-bytes N --max-class-entries N",
    "HTTP modes first require the pinned retail NTSC-U ToC-window identity hash to match.",
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

const sourcePaths = [];
const sparsePartSpecs = [];
let splitPartBytes;
let logicalDiscBytes;
let tableIndex;
let tocLba;
let tocWindowBytes;
let maxLevelRows;
let maxCoreIndexBytes;
let maxDecompressedBytes;
let maxClassEntries;
for (let i = 0; i < args.length; i++) {
  const arg = args[i];
  if (!arg.startsWith("--")) {
    sourcePaths.push(arg);
    continue;
  }
  if (arg === "--sparse-part") {
    const partNumberText = args[++i];
    const path = args[++i];
    if (partNumberText === undefined || path === undefined || path.startsWith("--")) usage();
    sparsePartSpecs.push({ partNumber: parseInteger(partNumberText, arg), path });
    continue;
  }

  const value = args[++i];
  if (value === undefined) usage();
  if (arg === "--split-part-bytes") splitPartBytes = parseInteger(value, arg);
  else if (arg === "--logical-disc-bytes") logicalDiscBytes = parseInteger(value, arg);
  else if (arg === "--table-index") tableIndex = parseInteger(value, arg, { allowZero: true });
  else if (arg === "--toc-lba") tocLba = parseInteger(value, arg, { allowZero: true });
  else if (arg === "--window-bytes") tocWindowBytes = parseInteger(value, arg);
  else if (arg === "--max-level-rows") maxLevelRows = parseInteger(value, arg);
  else if (arg === "--max-core-index-bytes") maxCoreIndexBytes = parseInteger(value, arg);
  else if (arg === "--max-decompressed-bytes") maxDecompressedBytes = parseInteger(value, arg);
  else if (arg === "--max-class-entries") maxClassEntries = parseInteger(value, arg);
  else usage();
}
if (tableIndex === undefined) usage();

const httpMode = hasUyaHttpRangeSourceEnv();
const sparseMode = sparsePartSpecs.length > 0;
if (httpMode && sparseMode) throw new Error("UYA HTTP range mode cannot be mixed with --sparse-part mode.");
if (httpMode && sourcePaths.length > 0) throw new Error("UYA HTTP range mode cannot be mixed with positional complete/split source paths.");
if (sparseMode && sourcePaths.length > 0) throw new Error("Sparse --sparse-part mode cannot be mixed with positional complete/split source paths.");
if (!sparseMode && (splitPartBytes !== undefined || logicalDiscBytes !== undefined)) {
  throw new Error("--split-part-bytes/--logical-disc-bytes require --sparse-part mode.");
}
if (sparseMode && (splitPartBytes === undefined || logicalDiscBytes === undefined)) {
  throw new Error("Sparse mode requires both --split-part-bytes and --logical-disc-bytes.");
}
if (!httpMode && !sparseMode && sourcePaths.length === 0) usage();

const duplicateParts = new Set();
for (const spec of sparsePartSpecs) {
  if (duplicateParts.has(spec.partNumber)) throw new Error(`Sparse part ${spec.partNumber} was supplied more than once.`);
  duplicateParts.add(spec.partNumber);
}

const openedReaders = [];
try {
  let disc;
  let sourceParts;
  let sourceMode;
  let authorityIdentity;
  if (httpMode) {
    const httpSource = await openUyaHttpRangeSourceFromEnv();
    if (!httpSource) throw new Error("UYA HTTP range mode was selected without a usable source.");
    disc = httpSource.disc;
    sourceParts = httpSource.sourceParts;
    sourceMode = httpSource.sourceMode;
    // Establish the supported retail source from raw bounded bytes before public semantic parsing.
    authorityIdentity = await assertUyaAuthorityTocWindow(disc);
  } else if (sparseMode) {
    const mapped = [];
    sourceParts = [];
    for (const spec of sparsePartSpecs.slice().sort((a, b) => a.partNumber - b.partNumber)) {
      const reader = await LocalFileRandomAccessReader.open(spec.path);
      openedReaders.push(reader);
      if (reader.size > splitPartBytes) {
        throw new Error(`Sparse part ${spec.partNumber} '${reader.name}' is ${reader.size} bytes, larger than --split-part-bytes ${splitPartBytes}.`);
      }
      const logicalStartBytes = (spec.partNumber - 1) * splitPartBytes;
      if (!Number.isSafeInteger(logicalStartBytes)) throw new RangeError(`Sparse part ${spec.partNumber} logical start exceeds JavaScript safe integer range.`);
      mapped.push({ start: logicalStartBytes, reader });
      sourceParts.push({
        partNumber: spec.partNumber,
        name: reader.name,
        sizeBytes: reader.size,
        logicalStartBytes,
      });
    }
    disc = new MappedRandomAccessReader(mapped, logicalDiscBytes, "uya-sparse-split-disc");
    sourceMode = "sparse-mapped-split-parts";
  } else {
    for (const path of sourcePaths) openedReaders.push(await LocalFileRandomAccessReader.open(path));
    disc = openedReaders.length === 1
      ? openedReaders[0]
      : new ConcatenatedRandomAccessReader(openedReaders, "uya-split-disc");
    let logicalStartBytes = 0;
    sourceParts = openedReaders.map((part, index) => {
      const result = { index, name: part.name, sizeBytes: part.size, logicalStartBytes };
      logicalStartBytes += part.size;
      return result;
    });
    sourceMode = "complete-or-contiguous-split-source";
  }

  const world = await probeUyaWorldCandidatePublicLead(disc, {
    tableIndex,
    ...(tocLba !== undefined ? { tocLba } : {}),
    ...(tocWindowBytes !== undefined ? { tocWindowBytes } : {}),
    ...(maxLevelRows !== undefined ? { maxLevelRows } : {}),
  });

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
  const collision = probeUyaRcCollisionCompatibilityPublicLead(decoded);

  const report = {
    schemaVersion: 1,
    evidenceStatus: "retail bytes observed and decoded by this run; layout/section semantics selected through pinned public leads remain provisional until independently promoted",
    sourceMode,
    sourceParts,
    logicalDiscSizeBytes: disc.size,
    ...(authorityIdentity ? { authorityIdentity } : {}),
    selectedTableIndex: tableIndex,
    selectedMainPart: world.selectedMainPart,
    compatibilityChecks: {
      coreCompressedSizeMatchesWadHeader: world.levelPayload.compatibilityChecks.coreCompressedSizeMatchesWadHeader,
      publicCompressedSizeMatchesWad: decoded.compatibilityChecks.publicCompressedSizeMatchesWad,
      publicDecompressedSizeMatchesOutput: decoded.compatibilityChecks.publicDecompressedSizeMatchesOutput,
    },
    core: {
      coreIndexBytes: decoded.coreIndex.length,
      coreIndexSha256: decoded.coreIndexSha256,
      compressedCoreDataBytes: decoded.compressedCoreData.length,
      compressedCoreDataSha256: decoded.compressedCoreDataSha256,
      decompressedAssetsBytes: decoded.assets.length,
      decompressedAssetsSha256: decoded.assetsSha256,
      sectionBoundaries: decoded.sectionBoundaries,
      publicCollisionRange: decoded.publicCollisionRange,
      publicCollisionSha256: decoded.publicCollisionSha256,
      warnings: decoded.warnings,
    },
    collision,
  };
  console.log(JSON.stringify(report, null, 2));
} finally {
  await Promise.allSettled(openedReaders.map((part) => part.close()));
}
