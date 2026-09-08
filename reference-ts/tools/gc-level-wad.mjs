import { mkdir, writeFile } from "node:fs/promises";
import { resolve, basename } from "node:path";
import { LocalFileRandomAccessReader } from "./local-random-access.mjs";
import { Iso9660Filesystem } from "../.build/packages/iso9660/src/index.js";
import {
  readGcLevelWadHeader,
  analyzeGcLevelWadTiling,
  GC_LEVEL_WAD_SECTOR_BYTES,
} from "../.build/packages/gc-level-wad/src/index.js";

/**
 * Run the conservative GC level-WAD outer parser over every `/G/LEVEL<n>.WAD` on a
 * Going Commando ISO and emit a deterministic level catalogue.
 *
 * Usage:
 *   node tools/gc-level-wad.mjs <iso> [--out research/generated/rac2-level-catalogue.json] [--md]
 */

const args = process.argv.slice(2);
const positional = [];
const opt = {};
for (let i = 0; i < args.length; i++) {
  const a = args[i];
  if (a === "--out") opt.out = args[++i];
  else if (a === "--md") opt.md = true;
  else if (a.startsWith("--")) throw new Error(`Unknown flag: ${a}`);
  else positional.push(a);
}
if (positional.length !== 1) {
  console.error("Usage: node tools/gc-level-wad.mjs <iso> [--out <file.json>] [--md]");
  process.exit(2);
}

const isoPath = resolve(positional[0]);
const reader = await LocalFileRandomAccessReader.open(isoPath, basename(isoPath));
const entries = [];
try {
  const fs = await Iso9660Filesystem.open(reader);
  for (let n = 0; n < 64; n++) {
    const path = `/G/LEVEL${n}.WAD`;
    const extent = await fs.openFile(path);
    if (!extent) continue;
    const header = await readGcLevelWadHeader(extent);
    const tiling = analyzeGcLevelWadTiling(header);
    entries.push({
      fileIndex: n,
      file: path,
      fileSizeBytes: extent.size,
      levelId: header.levelId,
      unknown0x04: header.unknown0x04,
      unknown0x0c: header.unknown0x0c,
      lumpCount: header.lumps.filter((l) => l.present).length,
      slotsPresent: header.lumps.filter((l) => l.present).map((l) => l.slot),
      lumps: header.lumps
        .filter((l) => l.present)
        .map((l) => ({ slot: l.slot, offsetSectors: l.offsetSectors, sizeSectors: l.sizeSectors, offsetBytes: l.offsetBytes, sizeBytes: l.sizeBytes })),
      tiling,
    });
  }
} finally {
  await reader.close();
}

entries.sort((a, b) => a.fileIndex - b.fileIndex);
const catalogue = {
  generator: "tools/gc-level-wad.mjs",
  generatedFrom: { isoFileName: basename(isoPath) },
  sectorBytes: GC_LEVEL_WAD_SECTOR_BYTES,
  levelCount: entries.length,
  entries,
};

if (opt.out) {
  await mkdir(resolve(opt.out, ".."), { recursive: true });
  await writeFile(resolve(opt.out), JSON.stringify(catalogue, null, 2) + "\n");
  console.log(`wrote ${resolve(opt.out)}`);
}

if (opt.md || !opt.out) {
  const rows = [
    "| File | levelId | unknown0x0c | lumps | slots | contiguous | tail overrun (B) |",
    "|---|---|---|---|---|---|---|",
    ...entries.map((e) =>
      `| \`LEVEL${e.fileIndex}.WAD\` | ${e.levelId} | ${e.unknown0x0c} | ${e.lumpCount} | ${e.slotsPresent.join(",")} | ${e.tiling.contiguousFromSectorOne} | ${e.tiling.tailOverrunBytes} |`),
  ];
  console.log(rows.join("\n"));
}
