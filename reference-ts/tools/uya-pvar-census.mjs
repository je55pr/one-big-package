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
import { probeUyaGcPvarCompatibility } from "../.build/packages/uya-pvars-compat/src/index.js";

const args = process.argv.slice(2);
const positional = [];
let outPath;
for (let i = 0; i < args.length; i++) {
  const arg = args[i];
  if (arg === "--out") outPath = args[++i];
  else if (arg === "--help" || arg === "-h") usage();
  else if (arg.startsWith("--")) usage();
  else positional.push(arg);
}
if (positional.length !== 1) usage();

function usage() {
  console.error("Usage: npm run build && node tools/uya-pvar-census.mjs <retail-uya.iso> [--out report.json]");
  process.exit(2);
}
const isoPath = resolve(positional[0]);
const reader = await LocalFileRandomAccessReader.open(isoPath, basename(isoPath));
try {
  const authority = await assertUyaAuthorityTocWindow(reader);
  if (!authority.matched) throw new Error("UYA authority ToC-window identity did not match the pinned retail build.");

  const toc = await probeUyaDiscToc(reader);
  const rows = toc.levelRows.filter((row) => {
    const mainParts = row.parts.filter((part) => part.publicFormatHint?.label === "level" && part.issues.length === 0);
    return mainParts.length === 1;
  });
  if (rows.length !== 51) {
    throw new Error(`Expected 51 observed UYA main-level rows under the current bounded catalogue evidence; found ${rows.length}.`);
  }

  const reports = [];
  for (const row of rows) {
    const tableIndex = row.index;
    const probe = await probeUyaWorldCandidatePublicLead(reader, { tableIndex });
    const gameplayRange = probe.levelPayload.outer.ranges[2];
    if (!gameplayRange?.present) throw new Error(`UYA table ${tableIndex} has no gameplay range in outer slot 2.`);
    const levelReader = openUyaTocPayloadPublicLead(reader, probe.selectedMainPart, `uya-table-${tableIndex}-candidate-level`);
    const gameplayReader = new SubRangeReader(
      levelReader,
      gameplayRange.offsetBytes,
      gameplayRange.sizeBytes,
      `${levelReader.name}#gameplay-slot-2`,
    );
    const gameplay = await readWadLz(gameplayReader, 0, { maxOutputBytes: 64 * 1024 * 1024 });
    const compatibility = probeUyaGcPvarCompatibility(gameplay.data);
    const c = compatibility.prerequisiteCensus;
    reports.push({
      tableIndex,
      gameplayBytes: c.gameplayBytes,
      gameplaySha256: c.gameplaySha256,
      classCount: c.classCount,
      mobyCount: c.mobyCount,
      mobiesWithPvar: c.mobiesWithPvar,
      mobyReferencedPvarCount: c.mobyReferencedPvarCount,
      allReferencedPvarCount: c.allReferencedPvarCount,
      maxReferencedPvarIndex: c.maxReferencedPvarIndex,
      pvarMobyLinkCount: c.pvarMobyLinks.length,
      pvarRelativePointerCount: c.pvarRelativePointers.length,
      pointerOffsets: Object.fromEntries(Object.entries(c.pointers).map(([key, value]) => [key, value.rawValue])),
    });
    console.error(`UYA table ${tableIndex}: mobies=${c.mobyCount} withPVar=${c.mobiesWithPvar} pvars=${c.mobyReferencedPvarCount} links=${c.pvarMobyLinks.length} rel=${c.pvarRelativePointers.length}`);
  }

  const sum = (key) => reports.reduce((total, row) => total + row[key], 0);
  const report = {
    schemaVersion: 1,
    evidenceStatus: "retail UYA gameplay bytes accepted by unchanged GC PVar parser after independent range/table/fixup prerequisite census; field semantics remain compatibility evidence unless separately native-proven",
    authority: {
      game: "rac3",
      buildId: "rac3-ntscu-original",
      tocWindowSha256: authority.sha256,
      tocWindowMatched: authority.matched,
    },
    catalogue: {
      tocLbaEvidenceStatus: "candidate/public-derived address with retail window identity; executable loader provenance remains provisional",
      mainLevelRows: reports.length,
      tableIndices: reports.map((row) => row.tableIndex),
    },
    totals: {
      gameplayBytes: sum("gameplayBytes"),
      classes: sum("classCount"),
      mobies: sum("mobyCount"),
      mobiesWithPvar: sum("mobiesWithPvar"),
      mobyReferencedPvars: sum("mobyReferencedPvarCount"),
      allReferencedPvars: sum("allReferencedPvarCount"),
      pvarMobyLinks: sum("pvarMobyLinkCount"),
      pvarRelativePointers: sum("pvarRelativePointerCount"),
    },
    rows: reports,
  };

  const json = `${JSON.stringify(report, null, 2)}\n`;
  if (outPath) {
    const output = resolve(outPath);
    await mkdir(dirname(output), { recursive: true });
    await writeFile(output, json);
    console.error(`wrote ${output}`);
  }
  console.log(json.trimEnd());
} finally {
  await reader.close();
}
