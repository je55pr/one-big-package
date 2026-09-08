import { resolve, basename } from "node:path";
import { LocalFileRandomAccessReader } from "./local-random-access.mjs";
import { Iso9660Filesystem } from "../.build/packages/iso9660/src/index.js";

/**
 * Dump the leading bytes of a set of ISO-9660 files as parallel u32-LE columns,
 * for comparing native container headers across levels / builds.
 *
 * Usage:
 *   node tools/container-headers.mjs <iso> --glob "/G/LEVEL{0..26}.WAD" [--bytes 0x90] [--json]
 *   node tools/container-headers.mjs <iso> --file /G/LEVEL1.WAD --file /G/LEVEL2.WAD
 */

const args = process.argv.slice(2);
const positional = [];
const opt = { bytes: 0x90, files: [] };
for (let i = 0; i < args.length; i++) {
  const a = args[i];
  if (a === "--file") opt.files.push(args[++i]);
  else if (a === "--glob") opt.glob = args[++i];
  else if (a === "--bytes") opt.bytes = parseNum(args[++i]);
  else if (a === "--json") opt.json = true;
  else if (a.startsWith("--")) throw new Error(`Unknown flag: ${a}`);
  else positional.push(a);
}
if (positional.length !== 1 || (opt.files.length === 0 && !opt.glob)) {
  console.error('Usage: node tools/container-headers.mjs <iso> (--file <p> ... | --glob "/G/LEVEL{0..26}.WAD") [--bytes N] [--json]');
  process.exit(2);
}

const files = opt.glob ? expandGlob(opt.glob) : opt.files;
const isoPath = resolve(positional[0]);
const reader = await LocalFileRandomAccessReader.open(isoPath, basename(isoPath));
const results = [];
try {
  const fs = await Iso9660Filesystem.open(reader);
  for (const name of files) {
    const extent = await fs.openFile(name);
    if (!extent) { results.push({ name, missing: true }); continue; }
    const length = Math.min(opt.bytes, extent.size);
    const head = await extent.read(0, length);
    const dv = new DataView(head.buffer, head.byteOffset, head.byteLength);
    const words = [];
    for (let i = 0; i + 4 <= length; i += 4) words.push(dv.getUint32(i, true));
    results.push({ name, sizeBytes: extent.size, extentLba: null, headerSize: words[0], words });
  }
} finally {
  await reader.close();
}

if (opt.json) {
  console.log(JSON.stringify(results, null, 2));
} else {
  for (const r of results) {
    if (r.missing) { console.log(`\n${r.name}: MISSING`); continue; }
    console.log(`\n${r.name}  size=${r.sizeBytes.toLocaleString()}  header_size=0x${(r.headerSize >>> 0).toString(16)} (${r.headerSize})`);
    r.words.forEach((w, i) => {
      if (i % 8 === 0) process.stdout.write(`${i ? "\n" : ""}  +0x${(i * 4).toString(16).padStart(2, "0")}:`);
      process.stdout.write(` ${(w >>> 0).toString(16).padStart(8, "0")}`);
    });
    console.log();
  }
}

function parseNum(s) {
  return s?.startsWith("0x") ? parseInt(s, 16) : Number(s);
}

function expandGlob(pattern) {
  const m = /\{(\d+)\.\.(\d+)\}/.exec(pattern);
  if (!m) return [pattern];
  const out = [];
  for (let i = Number(m[1]); i <= Number(m[2]); i++) out.push(pattern.replace(m[0], String(i)));
  return out;
}
