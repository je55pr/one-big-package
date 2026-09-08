#!/usr/bin/env node
import { ConcatenatedRandomAccessReader } from "../.build/packages/importer-common/src/index.js";
import { openPs2Disc } from "../.build/packages/ps2-disc/src/index.js";
import { probeUyaPackedBootExecutablePublicLead } from "../.build/packages/uya-packed-elf/src/index.js";
import { LocalFileRandomAccessReader } from "./local-random-access.mjs";
import {
  hasUyaHttpRangeSourceEnv,
  openUyaHttpRangeSourceFromEnv,
} from "./uya-http-range-source.mjs";
import { UYA_NTSCU_ORIGINAL_AUTHORITY } from "./uya-retail-authority.mjs";

function usage() {
  console.error([
    "Usage:",
    "  npm run build && node tools/uya-packed-elf.mjs <UYA .iso or ordered split parts...> [--decode] [--max-decompressed-bytes N]",
    "  OBP_UYA_HTTP_RANGE_URL='<whole-disc shared/range URL>' npm run build && node tools/uya-packed-elf.mjs [--decode] [--max-decompressed-bytes N]",
    "  OBP_UYA_HTTP_RANGE_PARTS_JSON='[{\"partNumber\":1,\"url\":\"...\"}]' npm run build && node tools/uya-packed-elf.mjs [--decode] [--max-decompressed-bytes N]",
    "",
    "HTTP mode requires the exact pinned retail SCUS_973.53 size and SHA-256 before packed semantics are applied.",
  ].join("\n"));
  process.exit(2);
}

function parsePositiveInteger(text, label) {
  const value = Number(text);
  if (!Number.isSafeInteger(value) || value <= 0) throw new Error(`${label} must be a positive integer, got '${text}'.`);
  return value;
}

const args = process.argv.slice(2);
if (args.includes("--help") || args.includes("-h")) usage();
const sourcePaths = [];
let decode = false;
let maxDecompressedBytes;
for (let i = 0; i < args.length; i++) {
  const arg = args[i];
  if (arg === "--decode") {
    decode = true;
    continue;
  }
  if (arg === "--max-decompressed-bytes") {
    const value = args[++i];
    if (value === undefined) usage();
    maxDecompressedBytes = parsePositiveInteger(value, arg);
    continue;
  }
  if (arg.startsWith("--")) usage();
  sourcePaths.push(arg);
}

const httpMode = hasUyaHttpRangeSourceEnv();
if (httpMode && sourcePaths.length > 0) throw new Error("UYA HTTP range mode cannot be mixed with positional complete/split source paths.");
if (!httpMode && sourcePaths.length === 0) usage();

const partReaders = [];
try {
  let discReader;
  let sourceParts;
  let sourceMode;
  if (httpMode) {
    const httpSource = await openUyaHttpRangeSourceFromEnv();
    if (!httpSource) throw new Error("UYA HTTP range mode was selected without a usable source.");
    discReader = httpSource.disc;
    sourceParts = httpSource.sourceParts;
    sourceMode = httpSource.sourceMode;
  } else {
    for (const path of sourcePaths) partReaders.push(await LocalFileRandomAccessReader.open(path));
    discReader = partReaders.length === 1
      ? partReaders[0]
      : new ConcatenatedRandomAccessReader(partReaders, "uya-split-disc");
    sourceParts = partReaders.map((part, index) => ({ index, name: part.name, sizeBytes: part.size }));
    sourceMode = "complete-or-contiguous-split-source";
  }

  const disc = await openPs2Disc(discReader);
  if (httpMode && disc.bootExecutable.size !== UYA_NTSCU_ORIGINAL_AUTHORITY.bootExecutableBytes) {
    throw new Error(
      `UYA retail boot executable size mismatch: expected ${UYA_NTSCU_ORIGINAL_AUTHORITY.bootExecutableBytes}, got ${disc.bootExecutable.size}.`,
    );
  }
  const packed = await probeUyaPackedBootExecutablePublicLead(disc.bootExecutable, {
    ...(httpMode ? { expectedSha256: UYA_NTSCU_ORIGINAL_AUTHORITY.bootExecutableSha256 } : {}),
    decodeUniqueCandidate: decode,
    ...(maxDecompressedBytes !== undefined ? { maxDecompressedBytes } : {}),
  });
  const report = {
    schemaVersion: 1,
    evidenceStatus: "boot identity/hash are observations from the supplied bytes; packed-WAD/Ratchet-section semantic labels remain pinned-public leads until corroborated with executable behaviour",
    sourceMode,
    sourceParts,
    logicalDiscSizeBytes: discReader.size,
    ...(httpMode ? {
      authorityBootIdentity: {
        expectedSizeBytes: UYA_NTSCU_ORIGINAL_AUTHORITY.bootExecutableBytes,
        expectedSha256: UYA_NTSCU_ORIGINAL_AUTHORITY.bootExecutableSha256,
        matched: true,
      },
    } : {}),
    boot: disc.boot,
    packedExecutable: packed,
  };
  console.log(JSON.stringify(report, null, 2));
} finally {
  await Promise.allSettled(partReaders.map((part) => part.close()));
}
