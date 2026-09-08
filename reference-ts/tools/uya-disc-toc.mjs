#!/usr/bin/env node
import { createHash } from "node:crypto";
import { LocalFileRandomAccessReader } from "./local-random-access.mjs";
import {
  UYA_DISC_SECTOR_BYTES,
  UYA_PUBLIC_MAX_LEVEL_ROWS_HINT,
  UYA_PUBLIC_TOC_LBA_HINT,
  UYA_PUBLIC_TOC_WINDOW_BYTES_HINT,
  UYA_WRENCH_SOURCE,
  analyzeUyaTocWindow,
} from "../.build/packages/uya-disc-toc/src/index.js";
import { buildUyaLevelCataloguePublicLead } from "../.build/packages/uya-level-catalogue/src/index.js";

function usage() {
  console.error("Usage: npm run build && node tools/uya-disc-toc.mjs <UYA .iso or .iso.001> [--toc-lba N] [--window-bytes N] [--max-level-rows N]");
  process.exit(2);
}

function parseInteger(text, label) {
  const value = Number(text);
  if (!Number.isSafeInteger(value) || value <= 0) throw new Error(`${label} must be a positive integer, got '${text}'.`);
  return value;
}

const args = process.argv.slice(2);
if (args.length === 0 || args.includes("--help") || args.includes("-h")) usage();
const sourcePath = args.shift();
let tocLba = UYA_PUBLIC_TOC_LBA_HINT;
let windowBytes = UYA_PUBLIC_TOC_WINDOW_BYTES_HINT;
let maxLevelRows = UYA_PUBLIC_MAX_LEVEL_ROWS_HINT;
while (args.length) {
  const flag = args.shift();
  const value = args.shift();
  if (!value) usage();
  if (flag === "--toc-lba") tocLba = parseInteger(value, flag);
  else if (flag === "--window-bytes") windowBytes = parseInteger(value, flag);
  else if (flag === "--max-level-rows") maxLevelRows = parseInteger(value, flag);
  else usage();
}

const source = await LocalFileRandomAccessReader.open(sourcePath);
try {
  const readOffsetBytes = tocLba * UYA_DISC_SECTOR_BYTES;
  if (!Number.isSafeInteger(readOffsetBytes) || readOffsetBytes >= source.size) {
    throw new RangeError(`TOC candidate LBA ${tocLba} lies past ${source.name} (${source.size} bytes).`);
  }
  const readLength = Math.min(windowBytes, source.size - readOffsetBytes);
  const bytes = await source.read(readOffsetBytes, readLength);
  const analysis = analyzeUyaTocWindow(bytes, { tocLba, maxLevelRows });
  const publicCatalogueLead = buildUyaLevelCataloguePublicLead(bytes, analysis);
  const report = {
    schemaVersion: 2,
    evidenceStatus: "retail bytes observed by this run; semantic labels and catalogue fields explicitly marked public* remain public corroboration only",
    source: { name: source.name, sizeBytes: source.size },
    read: {
      offsetBytes: readOffsetBytes,
      lba: tocLba,
      lengthBytes: bytes.length,
      sha256: createHash("sha256").update(bytes).digest("hex"),
    },
    publicLead: {
      description: "Candidate GC/UYA/DL resident TOC address, WAD header-size labels, UYA part ordering and level-ID extraction",
      source: UYA_WRENCH_SOURCE,
    },
    analysis,
    publicCatalogueLead,
  };
  console.log(JSON.stringify(report, null, 2));
} finally {
  await source.close();
}
