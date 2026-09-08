#!/usr/bin/env node
import { mkdir, writeFile } from "node:fs/promises";
import { resolve, dirname, basename } from "node:path";
import { LocalFileRandomAccessReader } from "./local-random-access.mjs";
import { assertUyaAuthorityTocWindow } from "./uya-retail-authority.mjs";
import { probeUyaDiscToc } from "../.build/packages/uya-disc-toc/src/index.js";
import { probeUyaWorldCandidatePublicLead } from "../.build/packages/uya-world-probe/src/index.js";
import { openUyaTocPayloadPublicLead } from "../.build/packages/uya-level-wad/src/index.js";
import { SubRangeReader } from "../.build/packages/importer-common/src/index.js";
import { readWadLz } from "../.build/packages/wad-lz/src/index.js";
import { censusUyaGameplayHeaderBlocks } from "../.build/packages/uya-gameplay-census/src/index.js";

const args = process.argv.slice(2);
if (args.length < 1 || args.includes("--help") || args.includes("-h")) usage();
const isoPath = resolve(args[0]);
const outAt = args.indexOf("--out");
if (outAt < 0 || !args[outAt + 1]) usage();
const outPath = resolve(args[outAt + 1]);

function usage() {
  console.error("Usage: npm run build && node tools/uya-gameplay-block-census.mjs <retail-uya.iso> --out report.json");
  process.exit(2);
}

const reader = await LocalFileRandomAccessReader.open(isoPath, basename(isoPath));
try {
  const authority = await assertUyaAuthorityTocWindow(reader);
  if (!authority.matched) throw new Error("UYA authority identity did not match.");
  const toc = await probeUyaDiscToc(reader);
  const rows = toc.levelRows.filter((row) => row.parts.filter((part) => part.publicFormatHint?.label === "level" && part.issues.length === 0).length === 1);
  if (rows.length !== 51) throw new Error(`Expected 51 UYA main-level rows, found ${rows.length}.`);

  const rowReports = [];
  for (const row of rows) {
    const tableIndex = row.index;
    const probe = await probeUyaWorldCandidatePublicLead(reader, { tableIndex });
    const gameplayRange = probe.levelPayload.outer.ranges[2];
    if (!gameplayRange?.present) throw new Error(`UYA table ${tableIndex} has no gameplay range in outer slot 2.`);
    const levelReader = openUyaTocPayloadPublicLead(reader, probe.selectedMainPart, `uya-table-${tableIndex}-candidate-level`);
    const gameplayReader = new SubRangeReader(levelReader, gameplayRange.offsetBytes, gameplayRange.sizeBytes, `${levelReader.name}#gameplay-slot-2`);
    const gameplay = await readWadLz(gameplayReader, 0, { maxOutputBytes: 64 * 1024 * 1024 });
    const census = censusUyaGameplayHeaderBlocks(gameplay.data);
    rowReports.push({ tableIndex, gameplayBytes: gameplay.data.length, ...census });
    console.error(`UYA table ${tableIndex}: nonzero header slots=${census.nonZeroSlotCount}, unique pointers=${census.uniquePointerCount}`);
  }

  const slotFamilies = buildSlotFamilies(rowReports);
  const report = {
    schemaVersion: 1,
    evidenceStatus: "semantic-free census of the first 32 little-endian gameplay header words from retail UYA main-level gameplay lumps",
    extentPolicy: "apparent extents run from one unique nonzero top-level pointer to the next; they are structural partitions, not asserted native block sizes",
    stridePolicy: "candidate strides are arithmetic divisibility hypotheses only and carry no semantic claim",
    authority: {
      buildId: "rac3-ntscu-original",
      serial: "SCUS-97353",
      tocWindowSha256: authority.sha256,
    },
    totals: {
      rows: rowReports.length,
      minNonZeroSlots: Math.min(...rowReports.map((row) => row.nonZeroSlotCount)),
      maxNonZeroSlots: Math.max(...rowReports.map((row) => row.nonZeroSlotCount)),
    },
    slotFamilies,
    rows: rowReports,
  };
  const json = `${JSON.stringify(report, null, 2)}\n`;
  await mkdir(dirname(outPath), { recursive: true });
  await writeFile(outPath, json);
  console.error(`wrote ${outPath} (${Buffer.byteLength(json)} bytes)`);
} finally {
  await reader.close();
}
function buildSlotFamilies(rows) {
  const families = [];
  for (let slotIndex = 0; slotIndex < 32; slotIndex++) {
    const observations = rows
      .map((row) => ({ tableIndex: row.tableIndex, slot: row.slots[slotIndex] }))
      .filter((item) => item.slot.present);
    const byHash = new Map();
    for (const item of observations) {
      const hash = item.slot.apparentExtentSha256;
      const list = byHash.get(hash) ?? [];
      list.push(item.tableIndex);
      byHash.set(hash, list);
    }
    const repeatedContentGroups = [...byHash.entries()]
      .filter(([, tableIndices]) => tableIndices.length > 1)
      .map(([sha256, tableIndices]) => ({ sha256, tableIndices }))
      .sort((a, b) => b.tableIndices.length - a.tableIndices.length || a.sha256.localeCompare(b.sha256));
    families.push({
      slotIndex,
      headerOffset: slotIndex * 4,
      presentRows: observations.length,
      absentRows: rows.length - observations.length,
      tableIndices: observations.map((item) => item.tableIndex),
      apparentExtentBytes: observations.length ? {
        min: Math.min(...observations.map((item) => item.slot.apparentExtentBytes)),
        max: Math.max(...observations.map((item) => item.slot.apparentExtentBytes)),
        distinct: [...new Set(observations.map((item) => item.slot.apparentExtentBytes))].sort((a, b) => a - b),
      } : null,
      distinctFirstS32: [...new Set(observations.map((item) => item.slot.firstS32))].sort((a, b) => a - b),
      strideHypothesisFrequency: frequency(
        observations.flatMap((item) => item.slot.strideHypotheses.map((hyp) => `${hyp.assumedHeaderBytes}:${hyp.strideBytes}`)),
      ),
      repeatedContentGroups,
    });
  }
  return families;
}

function frequency(values) {
  const map = new Map();
  for (const value of values) map.set(value, (map.get(value) ?? 0) + 1);
  return [...map.entries()]
    .map(([value, count]) => ({ value, count }))
    .sort((a, b) => b.count - a.count || a.value.localeCompare(b.value));
}
