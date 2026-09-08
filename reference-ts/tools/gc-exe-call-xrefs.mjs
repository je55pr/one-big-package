import { hashRandomAccessReaderSha256 } from "../.build/packages/hashing/src/index.js";
import { openPs2Disc, readPs2BootProgramHeaders } from "../.build/packages/ps2-disc/src/index.js";
import { ELF_PROGRAM_TYPE_LOAD } from "../.build/packages/elf32/src/index.js";
import { LocalFileRandomAccessReader } from "./local-random-access.mjs";
import { basename, resolve } from "node:path";

const EXPECTED_SIZE = 3_828_350_976;
const EXPECTED_SHA256 = "9db2e33e276133cc283647fa3279b37911955e123d6199d10065547eaa9b1ce5";
const EXPECTED_SERIAL = "SCUS-97268";
const DEFAULT_TARGETS = [0x003116e8, 0x00311f70, 0x003120c8, 0x002e5da0, 0x002e5cf8, 0x003202e8];
const CRATE_CLASSES = new Set([500, 501, 505, 511, 512]);
const PVAR_OFFSETS = new Set([0xc6, 0xc8, 0xcb, 0xcc, 0xac, 0xb4, 0xfc]);
const CONTEXT_RADIUS = 0x80;
const MAX_TOTAL_LOAD_BYTES = 64 * 1024 * 1024;
const MAX_XREFS_PER_TARGET = 128;

const args = process.argv.slice(2);
const positional = [];
let targets = DEFAULT_TARGETS;
let verifyHash = false;
for (let i = 0; i < args.length; i++) {
  if (args[i] === "--verify-hash") verifyHash = true;
  else if (args[i] === "--targets") targets = parseAddresses(args[++i]);
  else if (args[i].startsWith("--")) throw new Error(`Unknown flag: ${args[i]}`);
  else positional.push(args[i]);
}
if (positional.length !== 1) {
  console.error("Usage: node tools/gc-exe-call-xrefs.mjs <gc-iso> [--targets 0xADDR,...] [--verify-hash]");
  process.exit(2);
}
const targetSet = new Set(targets);
const isoPath = resolve(positional[0]);
const isoName = basename(isoPath);
const reader = await LocalFileRandomAccessReader.open(isoPath, isoName);

try {
  if (reader.size !== EXPECTED_SIZE) throw new Error(`GC authority size mismatch: ${reader.size} != ${EXPECTED_SIZE}.`);
  const disc = await openPs2Disc(reader);
  if (disc.boot.serial !== EXPECTED_SERIAL) throw new Error(`GC authority serial mismatch: ${disc.boot.serial ?? "none"} != ${EXPECTED_SERIAL}.`);
  if (verifyHash) {
    const hashed = await hashRandomAccessReaderSha256(reader);
    if (hashed.sha256 !== EXPECTED_SHA256) throw new Error(`GC authority SHA-256 mismatch: ${hashed.sha256}.`);
  }

  const phdrs = await readPs2BootProgramHeaders(disc, { maxProgramHeaders: 64 });
  const executableSegments = phdrs.filter((segment) => segment.type === ELF_PROGRAM_TYPE_LOAD && segment.fileSize > 0 && (segment.flags & 1) !== 0);
  const totalBytes = executableSegments.reduce((sum, segment) => sum + segment.fileSize, 0);
  if (totalBytes > MAX_TOTAL_LOAD_BYTES) throw new Error(`Executable scan exceeds ${MAX_TOTAL_LOAD_BYTES}-byte cap.`);

  const loaded = [];
  for (const segment of executableSegments) {
    const bytes = await disc.bootExecutable.read(segment.offset, segment.fileSize);
    if (bytes.byteLength !== segment.fileSize) throw new Error(`Short PT_LOAD read for segment ${segment.index}.`);
    loaded.push({ segment, bytes, view: new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength) });
  }

  const xrefs = new Map(targets.map((target) => [target, []]));
  for (const { segment, view } of loaded) {
    for (let offset = 0; offset + 4 <= view.byteLength; offset += 4) {
      const word = view.getUint32(offset, true);
      if ((word >>> 26) !== 3) continue;
      const pc = segment.virtualAddress + offset;
      const target = (((pc + 4) & 0xf0000000) | ((word & 0x03ffffff) << 2)) >>> 0;
      if (!targetSet.has(target)) continue;
      const list = xrefs.get(target);
      if (list.length >= MAX_XREFS_PER_TARGET) continue;
      list.push({
        caller: pc,
        fileOffset: segment.offset + offset,
        context: scanContext(view, segment.virtualAddress, offset, CONTEXT_RADIUS),
      });
    }
  }

  console.log(`GC_CALL_XREF_AUTHORITY exe=${disc.boot.executableName} entry=${hex32(disc.boot.executableEntryPoint)} loadBytes=${totalBytes} hashVerified=${verifyHash}`);
  for (const target of targets) {
    const rows = xrefs.get(target);
    console.log(`GC_CALL_TARGET target=${hex32(target)} xrefs=${rows.length}`);
    for (const row of rows) {
      console.log(`GC_CALL_XREF target=${hex32(target)} caller=${hex32(row.caller)} fileOffset=${hex32(row.fileOffset)} nearbyClasses=${row.context.classes.join(",") || "none"} nearbyPvar=${row.context.pvarOffsets.join(",") || "none"} nearbyCalls=${row.context.calls.join(",") || "none"}`);
    }
  }
} finally {
  await reader.close();
}

function scanContext(view, segmentVaddr, centerOffset, radius) {
  const start = Math.max(0, (centerOffset - radius) & ~3);
  const end = Math.min(view.byteLength, (centerOffset + radius + 4) & ~3);
  const classes = new Set();
  const pvarOffsets = new Set();
  const calls = new Set();
  for (let offset = start; offset + 4 <= end; offset += 4) {
    const word = view.getUint32(offset, true);
    const op = word >>> 26;
    const imm = word & 0xffff;
    const simm = (imm << 16) >> 16;
    if ([8, 9, 10, 11].includes(op) && simm >= 0 && CRATE_CLASSES.has(simm)) classes.add(simm);
    if ([12, 13, 14].includes(op) && CRATE_CLASSES.has(imm)) classes.add(imm);
    if ([30,31,32,33,34,35,36,37,38,40,41,42,43,46,49,55,57,63].includes(op) && simm >= 0 && PVAR_OFFSETS.has(simm)) pvarOffsets.add(hexOff(simm));
    if (op === 3) {
      const pc = segmentVaddr + offset;
      const target = (((pc + 4) & 0xf0000000) | ((word & 0x03ffffff) << 2)) >>> 0;
      calls.add(hex32(target));
    }
  }
  return { classes: [...classes].sort((a,b) => a-b), pvarOffsets: [...pvarOffsets].sort(), calls: [...calls].sort() };
}

function parseAddresses(raw) {
  if (!raw) throw new Error("--targets requires a comma-separated address list.");
  const values = raw.split(",").map((item) => Number(item.trim()));
  if (values.some((value) => !Number.isSafeInteger(value) || value < 0 || value > 0xffffffff || (value & 3) !== 0)) throw new Error(`Invalid target list: ${raw}`);
  return [...new Set(values)];
}

function hex32(value) { return `0x${(value >>> 0).toString(16).padStart(8,"0")}`; }
function hexOff(value) { return `0x${value.toString(16)}`; }
