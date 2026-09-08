import { resolve, basename } from "node:path";
import { LocalFileRandomAccessReader } from "./local-random-access.mjs";
import { Iso9660Filesystem } from "../.build/packages/iso9660/src/index.js";

/**
 * Exploratory dump of Going Commando's `/RC2.HDR` asset directory.
 *
 * `RC2.HDR` is an array of fixed 0x800-byte asset slots. Each slot for a level
 * container begins with the container's absolute disc LBA (u32 LE), then mirrors
 * that WAD's on-disc lump table (see research/GC_LEVEL_LOADING.md). Slots are
 * grouped per level in 0x3800-byte blocks (7 slots: LEVEL, AUDIO, _, _, SCENE, _, _).
 *
 * This tool cross-references every u32 in RC2.HDR against the ISO-9660 file LBAs
 * and prints the slot layout. It does not yet claim a full field decode.
 *
 * Usage: node tools/rc2-hdr.mjs <gc-iso> [--slot 0x5804] [--bytes 0x40]
 */

const args = process.argv.slice(2);
const positional = [];
const opt = { bytes: 0x40 };
for (let i = 0; i < args.length; i++) {
  const a = args[i];
  if (a === "--slot") opt.slot = parseNum(args[++i]);
  else if (a === "--bytes") opt.bytes = parseNum(args[++i]);
  else if (a.startsWith("--")) throw new Error(`Unknown flag: ${a}`);
  else positional.push(a);
}
if (positional.length !== 1) {
  console.error("Usage: node tools/rc2-hdr.mjs <gc-iso> [--slot <off>] [--bytes <n>]");
  process.exit(2);
}

const isoPath = resolve(positional[0]);
const reader = await LocalFileRandomAccessReader.open(isoPath, basename(isoPath));
try {
  const fs = await Iso9660Filesystem.open(reader);
  const byLba = new Map();
  for await (const e of fs.walk("/", { includeDirectories: false })) byLba.set(e.extentLba, { name: e.path, size: e.dataLength });

  const hdr = await fs.openFile("/RC2.HDR");
  if (!hdr) throw new Error("/RC2.HDR not found (not a Going Commando disc?)");
  const buf = await hdr.read(0, hdr.size);
  const dv = new DataView(buf.buffer, buf.byteOffset, buf.byteLength);

  if (opt.slot !== undefined) {
    process.stdout.write(`slot @0x${opt.slot.toString(16)}:`);
    for (let i = 0; i < opt.bytes; i += 4) process.stdout.write(` ${dv.getUint32(opt.slot + i, true).toString(16).padStart(8, "0")}`);
    console.log();
    process.exit(0);
  }

  console.log(`RC2.HDR: ${hdr.size} bytes`);
  console.log("\nu32 words equal to a known ISO-9660 file LBA:");
  console.log("offset      lba         ISO-9660 file        Δ(prev)");
  let prev = null;
  for (let off = 0; off + 4 <= buf.length; off += 4) {
    const w = dv.getUint32(off, true);
    const file = byLba.get(w);
    if (!file) continue;
    const delta = prev === null ? "" : `0x${(off - prev).toString(16)}`;
    console.log(`+0x${off.toString(16).padStart(5, "0")}   ${w.toString(16).padStart(8, "0")}    ${file.name.padEnd(18)}  ${delta}`);
    prev = off;
  }
} finally {
  await reader.close();
}

function parseNum(s) {
  return s?.startsWith("0x") ? parseInt(s, 16) : Number(s);
}
