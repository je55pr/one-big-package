import { writeFile } from "node:fs/promises";
import { resolve, basename } from "node:path";
import { LocalFileRandomAccessReader } from "./local-random-access.mjs";
import { Iso9660Filesystem } from "../.build/packages/iso9660/src/index.js";
import { openPs2Disc, readPs2BootProgramHeaders } from "../.build/packages/ps2-disc/src/index.js";
import { readElf32VirtualRange } from "../.build/packages/elf32/src/index.js";

/**
 * Bounded byte-range reader for retail-ISO archaeology.
 *
 * Reads one explicit range and prints an annotated hex dump (or writes raw bytes
 * to a file). Every access path stays range-bounded — no whole-file buffering.
 *
 * Usage:
 *   node tools/iso-read.mjs <iso> --file /G/LEVEL1.WAD --offset 0 --length 256
 *   node tools/iso-read.mjs <iso> --iso-offset 0x519000 --length 64
 *   node tools/iso-read.mjs <iso> --lba 2610 --length 2048
 *   node tools/iso-read.mjs <iso> --va 0x00131ae8 --length 128        # from boot ELF
 *   node tools/iso-read.mjs <iso> --file /G/LEVEL1.WAD --length 2048 --out level1.head.bin
 */

const args = process.argv.slice(2);
const positional = [];
const opt = { length: 256, offset: 0 };
for (let i = 0; i < args.length; i++) {
  const a = args[i];
  if (a === "--file") opt.file = args[++i];
  else if (a === "--offset") opt.offset = num(args[++i]);
  else if (a === "--length") opt.length = num(args[++i]);
  else if (a === "--iso-offset") opt.isoOffset = num(args[++i]);
  else if (a === "--lba") opt.lba = num(args[++i]);
  else if (a === "--va") opt.va = num(args[++i]);
  else if (a === "--out") opt.out = args[++i];
  else if (a === "--u32le") opt.u32le = true;
  else if (a.startsWith("--")) throw new Error(`Unknown flag: ${a}`);
  else positional.push(a);
}
if (positional.length !== 1) {
  console.error("Usage: node tools/iso-read.mjs <iso> [--file <isoPath> | --iso-offset <n> | --lba <n> | --va <n>] [--offset <n>] [--length <n>] [--out <file>] [--u32le]");
  process.exit(2);
}

const isoPath = resolve(positional[0]);
const reader = await LocalFileRandomAccessReader.open(isoPath, basename(isoPath));
try {
  let bytes;
  let describe;

  if (opt.va !== undefined) {
    const disc = await openPs2Disc(reader);
    const phdrs = await readPs2BootProgramHeaders(disc);
    bytes = await readElf32VirtualRange(disc.bootExecutable, phdrs, opt.va + opt.offset, opt.length);
    describe = `${disc.boot.executableName} VA 0x${(opt.va + opt.offset).toString(16)} +${opt.length}`;
  } else if (opt.file !== undefined) {
    const fs = await Iso9660Filesystem.open(reader);
    const extent = await fs.openFile(opt.file);
    if (!extent) throw new Error(`ISO file not found: ${opt.file}`);
    const length = Math.min(opt.length, extent.size - opt.offset);
    bytes = await extent.read(opt.offset, length);
    describe = `${opt.file} (${extent.size.toLocaleString()} B) @ ${opt.offset} +${length}`;
  } else {
    let base = 0;
    if (opt.isoOffset !== undefined) base = opt.isoOffset;
    else if (opt.lba !== undefined) base = opt.lba * 2048;
    else throw new Error("Provide one of --file, --iso-offset, --lba, --va");
    const start = base + opt.offset;
    const length = Math.min(opt.length, reader.size - start);
    bytes = await reader.read(start, length);
    describe = `ISO @ 0x${start.toString(16)} (${start}) +${length}`;
  }

  if (opt.out) {
    await writeFile(resolve(opt.out), bytes);
    console.log(`wrote ${bytes.length} bytes to ${resolve(opt.out)}`);
  } else {
    console.log(describe);
    console.log(hexDump(bytes, opt.offset));
    if (opt.u32le) {
      console.log("\nu32 LE words:");
      const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
      for (let i = 0; i + 4 <= bytes.length; i += 4) {
        const v = view.getUint32(i, true);
        console.log(`  +0x${i.toString(16).padStart(3, "0")}  ${v.toString().padStart(12)}  0x${v.toString(16).padStart(8, "0")}`);
      }
    }
  }
} finally {
  await reader.close();
}

function num(s) {
  if (s === undefined) throw new Error("Missing numeric argument");
  const v = s.startsWith("0x") || s.startsWith("0X") ? parseInt(s, 16) : Number(s);
  if (!Number.isFinite(v)) throw new Error(`Not a number: ${s}`);
  return v;
}

function hexDump(bytes, baseOffset = 0) {
  const lines = [];
  for (let i = 0; i < bytes.length; i += 16) {
    const row = bytes.subarray(i, i + 16);
    const hex = [...row].map((b, j) => (j === 8 ? " " : "") + b.toString(16).padStart(2, "0")).join(" ");
    const ascii = [...row].map((b) => (b >= 0x20 && b < 0x7f ? String.fromCharCode(b) : ".")).join("");
    lines.push(`${(baseOffset + i).toString(16).padStart(8, "0")}  ${hex.padEnd(49)}  |${ascii}|`);
  }
  return lines.join("\n");
}
