#!/usr/bin/env node
import { createHash } from "node:crypto";
import { ConcatenatedRandomAccessReader } from "../.build/packages/importer-common/src/index.js";
import { UYA_DISC_SECTOR_BYTES } from "../.build/packages/uya-disc-toc/src/index.js";
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
    "  npm run build && node tools/uya-world-probe.mjs <UYA .iso or ordered split parts...> --table-index N [options]",
    "  OBP_UYA_HTTP_RANGE_URL='<whole-disc shared/range URL>' npm run build && node tools/uya-world-probe.mjs --table-index N [options]",
    "  OBP_UYA_HTTP_RANGE_PARTS_JSON='[{\"partNumber\":1,\"url\":\"...\"},...]' npm run build && node tools/uya-world-probe.mjs --table-index N [options]",
    "",
    "Options: --toc-lba N --window-bytes N --max-level-rows N",
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

async function hashRange(reader, offset, length) {
  const hash = createHash("sha256");
  const chunkBytes = 1024 * 1024;
  let cursor = 0;
  while (cursor < length) {
    const take = Math.min(chunkBytes, length - cursor);
    hash.update(await reader.read(offset + cursor, take));
    cursor += take;
  }
  return hash.digest("hex");
}

const args = process.argv.slice(2);
if (args.length === 0 || args.includes("--help") || args.includes("-h")) usage();

const sourcePaths = [];
let tableIndex;
let tocLba;
let tocWindowBytes;
let maxLevelRows;
for (let i = 0; i < args.length; i++) {
  const arg = args[i];
  if (!arg.startsWith("--")) {
    sourcePaths.push(arg);
    continue;
  }
  const value = args[++i];
  if (value === undefined) usage();
  if (arg === "--table-index") tableIndex = parseInteger(value, arg, { allowZero: true });
  else if (arg === "--toc-lba") tocLba = parseInteger(value, arg, { allowZero: true });
  else if (arg === "--window-bytes") tocWindowBytes = parseInteger(value, arg);
  else if (arg === "--max-level-rows") maxLevelRows = parseInteger(value, arg);
  else usage();
}
if (tableIndex === undefined) usage();

const httpMode = hasUyaHttpRangeSourceEnv();
if (httpMode && sourcePaths.length > 0) throw new Error("UYA HTTP range mode cannot be mixed with positional complete/split source paths.");
if (!httpMode && sourcePaths.length === 0) usage();

const partReaders = [];
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
    // Identity must be established from raw bounded bytes before any public semantic lead is applied.
    authorityIdentity = await assertUyaAuthorityTocWindow(disc);
  } else {
    for (const path of sourcePaths) partReaders.push(await LocalFileRandomAccessReader.open(path));
    disc = partReaders.length === 1
      ? partReaders[0]
      : new ConcatenatedRandomAccessReader(partReaders, "uya-split-disc");
    sourceParts = partReaders.map((part, index) => ({ index, name: part.name, sizeBytes: part.size }));
    sourceMode = "complete-or-contiguous-split-source";
  }

  const probe = await probeUyaWorldCandidatePublicLead(disc, {
    tableIndex,
    ...(tocLba !== undefined ? { tocLba } : {}),
    ...(tocWindowBytes !== undefined ? { tocWindowBytes } : {}),
    ...(maxLevelRows !== undefined ? { maxLevelRows } : {}),
  });

  const payloadLba = probe.selectedMainPart.rawHeaderWordAt0x04;
  if (payloadLba === undefined) throw new Error("Selected public main-level part does not expose the candidate payload LBA word.");
  const payloadOffset = payloadLba * UYA_DISC_SECTOR_BYTES;
  if (!Number.isSafeInteger(payloadOffset)) throw new RangeError("Candidate payload offset exceeds JavaScript safe integer range.");
  const dataOffset = payloadOffset + probe.levelPayload.outer.ranges[0].offsetBytes;
  const coreOffset = payloadOffset + probe.levelPayload.core.offsetInLevelBytes;
  const wadOffset = payloadOffset + probe.levelPayload.coreDataWadLz.offsetInLevelBytes;

  const report = {
    schemaVersion: 1,
    evidenceStatus: "retail bytes observed by this run; every field named public*, selected by a public format label, or under publicFields remains corroborating public interpretation until promoted separately",
    sourceMode,
    sourceParts,
    logicalDiscSizeBytes: disc.size,
    ...(authorityIdentity ? { authorityIdentity } : {}),
    hashes: {
      tocWindowSha256: await hashRange(disc, probe.toc.readOffsetBytes, probe.toc.windowBytes),
      outerHeaderSha256: await hashRange(disc, payloadOffset, 0x60),
      dataHeaderSha256: await hashRange(disc, dataOffset, 0x58),
      coreHeaderSha256: await hashRange(disc, coreOffset, 0xbc),
      coreDataWadHeaderSha256: await hashRange(disc, wadOffset, 0x10),
    },
    probe,
  };
  console.log(JSON.stringify(report, null, 2));
} finally {
  await Promise.allSettled(partReaders.map((part) => part.close()));
}
