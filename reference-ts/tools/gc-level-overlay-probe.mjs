import { createHash } from "node:crypto";
import { basename, resolve } from "node:path";
import { LocalFileRandomAccessReader } from "./local-random-access.mjs";
import { Iso9660Filesystem } from "../.build/packages/iso9660/src/index.js";
import { openPs2Disc } from "../.build/packages/ps2-disc/src/index.js";
import { hashRandomAccessReaderSha256 } from "../.build/packages/hashing/src/index.js";
import { readGcLevelWadHeader, openGcLevelWadLump } from "../.build/packages/gc-level-wad/src/index.js";

const EXPECTED_SIZE = 3_828_350_976;
const EXPECTED_SHA256 = "9db2e33e276133cc283647fa3279b37911955e123d6199d10065547eaa9b1ce5";
const EXPECTED_SERIAL = "SCUS-97268";
const MAX_TOTAL_WINDOW_BYTES = 16 * 1024;
const MAX_OVERLAY_BYTES = 16 * 1024 * 1024;
const REG = ["zero","at","v0","v1","a0","a1","a2","a3","t0","t1","t2","t3","t4","t5","t6","t7","s0","s1","s2","s3","s4","s5","s6","s7","t8","t9","k0","k1","gp","sp","fp","ra"];
// Pinned Wrench donor order for RAC/GC/UYA level ELF sections. These labels are
// corroboration only; retail dest/size/type/entry-point values remain authority.
const CORROBORATED_SECTION_NAMES = [".lit", ".bss", ".data", "lvl.vtbl", "lvl.camvtbl", "lvl.sndvtbl", ".text"];

const args = process.argv.slice(2);
const positional = [];
let levelsRaw = "1";
let rangesRaw = null;
let dumpLevel = null;
let verifyHash = false;
for (let i = 0; i < args.length; i++) {
  const arg = args[i];
  if (arg === "--verify-hash") verifyHash = true;
  else if (arg === "--levels") levelsRaw = args[++i];
  else if (arg === "--ranges") rangesRaw = args[++i];
  else if (arg === "--dump-level") dumpLevel = Number(args[++i]);
  else if (arg.startsWith("--")) throw new Error(`Unknown flag: ${arg}`);
  else positional.push(arg);
}
if (positional.length !== 1 || !rangesRaw) {
  console.error("Usage: node tools/gc-level-overlay-probe.mjs <gc-iso> --levels 0-26|1,2,11 --ranges 0xSTART:0xEND[...] [--dump-level N] [--verify-hash]");
  process.exit(2);
}
const levels = parseLevels(levelsRaw);
const ranges = parseRanges(rangesRaw);
const totalWindowBytes = ranges.reduce((sum, range) => sum + range.end - range.start, 0);
if (totalWindowBytes > MAX_TOTAL_WINDOW_BYTES) throw new Error(`Requested overlay windows total ${totalWindowBytes} bytes; cap is ${MAX_TOTAL_WINDOW_BYTES}.`);
if (dumpLevel !== null && !levels.includes(dumpLevel)) throw new Error(`--dump-level ${dumpLevel} must be included in --levels.`);

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
  const fs = await Iso9660Filesystem.open(reader);
  console.log(`GC_OVERLAY_AUTHORITY serial=${disc.boot.serial} levels=${levels.join(",")} ranges=${ranges.length} requestedBytes=${totalWindowBytes} hashVerified=${verifyHash}`);

  const rangeHashes = new Map(ranges.map((range) => [range.key, new Map()]));
  for (const level of levels) {
    const wad = await fs.openFile(`/G/LEVEL${level}.WAD`);
    if (!wad) throw new Error(`Missing /G/LEVEL${level}.WAD.`);
    const wadHeader = await readGcLevelWadHeader(wad);
    const dataLump = openGcLevelWadLump(wad, wadHeader, 0);
    if (!dataLump) throw new Error(`LEVEL${level}.WAD has no data lump in slot 0.`);
    if (dataLump.size < 8) throw new Error(`LEVEL${level} data lump is too small for overlay range.`);
    const header = await dataLump.read(0, 8);
    const hv = new DataView(header.buffer, header.byteOffset, header.byteLength);
    const overlayOffset = hv.getInt32(0, true);
    const overlaySize = hv.getInt32(4, true);
    if (!Number.isSafeInteger(overlayOffset) || !Number.isSafeInteger(overlaySize) || overlayOffset < 0 || overlaySize < 0 || overlaySize > MAX_OVERLAY_BYTES || overlayOffset > dataLump.size || overlaySize > dataLump.size - overlayOffset) {
      throw new Error(`LEVEL${level} invalid overlay range ${overlayOffset}+${overlaySize} in data lump ${dataLump.size}.`);
    }
    const overlay = await dataLump.read(overlayOffset, overlaySize);
    const sections = parseOverlaySections(overlay, level);
    console.log(`GC_OVERLAY_LEVEL level=${level} native=${wadHeader.levelId} offset=${hex32(overlayOffset)} bytes=${overlaySize} sections=${sections.length} entryPoint=${hex32(sections[0].entryPoint)}`);
    for (let i = 0; i < sections.length; i++) {
      const section = sections[i];
      const name = CORROBORATED_SECTION_NAMES[i] ?? "unknown";
      console.log(`GC_OVERLAY_SECTION level=${level} index=${i} nameLead=${name} dest=${hex32(section.destAddress)} bytes=${section.copySize} type=${hex32(section.sectionType)} entryPoint=${hex32(section.entryPoint)} headerOffset=${hex32(section.headerOffset)}`);
    }

    for (const range of ranges) {
      const mapped = mapOverlayRange(overlay, sections, range);
      if (!mapped) {
        console.log(`GC_OVERLAY_RANGE level=${level} range=${range.key} mapped=false`);
        rangeHashes.get(range.key).set(level, null);
        continue;
      }
      const sha256 = createHash("sha256").update(mapped.bytes).digest("hex");
      rangeHashes.get(range.key).set(level, sha256);
      console.log(`GC_OVERLAY_RANGE level=${level} range=${range.key} mapped=true sectionDest=${hex32(mapped.section.destAddress)} sectionBytes=${mapped.section.copySize} sectionType=${hex32(mapped.section.sectionType)} sha256=${sha256}`);
      if (level === dumpLevel) dumpWindow(range, mapped.bytes);
    }
  }

  for (const range of ranges) {
    const values = rangeHashes.get(range.key);
    const groups = new Map();
    for (const [level, hash] of values) {
      const key = hash ?? "UNMAPPED";
      const group = groups.get(key) ?? [];
      group.push(level);
      groups.set(key, group);
    }
    console.log(`GC_OVERLAY_COMPARE range=${range.key} variants=${groups.size}`);
    for (const [hash, groupLevels] of groups) console.log(`GC_OVERLAY_VARIANT range=${range.key} sha256=${hash} levels=${groupLevels.join(",")}`);
  }
} finally {
  await reader.close();
}

function parseOverlaySections(bytes, level) {
  const sections = [];
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  let cursor = 0;
  let expectedEntryPoint = null;
  while (cursor + 16 <= bytes.byteLength) {
    const destAddress = view.getUint32(cursor, true);
    const copySize = view.getUint32(cursor + 4, true);
    const sectionType = view.getUint32(cursor + 8, true);
    const entryPoint = view.getUint32(cursor + 12, true);
    if (expectedEntryPoint === null) expectedEntryPoint = entryPoint;
    else if (entryPoint !== expectedEntryPoint) break; // mirrors the retail-loader/Wrench sentinel rule
    const dataOffset = cursor + 16;
    if (copySize > bytes.byteLength - dataOffset) throw new Error(`LEVEL${level} overlay section at ${hex32(cursor)} overruns overlay: copy=${copySize}, remaining=${bytes.byteLength - dataOffset}.`);
    const endAddress = destAddress + copySize;
    if (!Number.isSafeInteger(endAddress) || endAddress > 0x1_0000_0000) throw new Error(`LEVEL${level} overlay section destination overflows 32-bit address space.`);
    sections.push({ headerOffset: cursor, dataOffset, destAddress, copySize, sectionType, entryPoint });
    cursor = dataOffset + copySize;
  }
  if (sections.length === 0) throw new Error(`LEVEL${level} overlay contained no executable sections.`);
  return sections;
}

function mapOverlayRange(overlay, sections, range) {
  const section = sections.find((candidate) => range.start >= candidate.destAddress && range.end <= candidate.destAddress + candidate.copySize);
  if (!section) return null;
  const relative = range.start - section.destAddress;
  const start = section.dataOffset + relative;
  return { section, bytes: overlay.subarray(start, start + range.end - range.start) };
}

function dumpWindow(range, bytes) {
  console.log(`GC_OVERLAY_DUMP_BEGIN range=${range.key} bytes=${bytes.byteLength}`);
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  for (let offset = 0; offset + 4 <= bytes.byteLength; offset += 4) {
    const pc = range.start + offset;
    const word = view.getUint32(offset, true);
    console.log(`${hex32(pc)}  ${hex32(word)}  ${decode(word, pc)}`);
  }
  console.log(`GC_OVERLAY_DUMP_END range=${range.key}`);
}

function parseLevels(raw) {
  const result = new Set();
  for (const partRaw of raw.split(",")) {
    const part = partRaw.trim();
    if (!part) continue;
    const match = /^(\d+)-(\d+)$/.exec(part);
    if (match) {
      const first = Number(match[1]);
      const last = Number(match[2]);
      if (first > last) throw new Error(`Invalid descending level range: ${part}`);
      for (let level = first; level <= last; level++) result.add(level);
    } else {
      const level = Number(part);
      if (!Number.isInteger(level)) throw new Error(`Invalid level: ${part}`);
      result.add(level);
    }
  }
  const levels = [...result].sort((a, b) => a - b);
  if (levels.length === 0 || levels.some((level) => level < 0 || level > 26)) throw new Error(`GC levels must be within 0..26: ${raw}`);
  return levels;
}

function parseRanges(raw) {
  const ranges = raw.split(",").map((item) => {
    const [a, b] = item.split(":");
    const start = Number(a);
    const end = Number(b);
    if (!Number.isSafeInteger(start) || !Number.isSafeInteger(end) || start < 0 || end <= start || (start & 3) !== 0 || (end & 3) !== 0) throw new Error(`Invalid aligned range: ${item}`);
    if (end - start > 8192) throw new Error(`Individual overlay window exceeds 8192-byte cap: ${item}`);
    return { start, end, key: `${hex32(start)}:${hex32(end)}` };
  });
  ranges.sort((x, y) => x.start - y.start);
  for (let i = 1; i < ranges.length; i++) if (ranges[i].start < ranges[i - 1].end) throw new Error("Overlay windows must not overlap.");
  return ranges;
}

function decode(word, pc) {
  const op = word >>> 26;
  const rs = (word >>> 21) & 31;
  const rt = (word >>> 16) & 31;
  const rd = (word >>> 11) & 31;
  const sa = (word >>> 6) & 31;
  const fn = word & 63;
  const imm = word & 0xffff;
  const simm = (imm << 16) >> 16;
  const r = (n) => `$${REG[n]}`;
  const branch = hex32((pc + 4 + (simm << 2)) >>> 0);
  const jump = hex32((((pc + 4) & 0xf0000000) | ((word & 0x03ffffff) << 2)) >>> 0);
  if (op === 0) {
    const names = {0:"sll",2:"srl",3:"sra",8:"jr",9:"jalr",16:"mfhi",18:"mflo",24:"mult",25:"multu",26:"div",27:"divu",32:"add",33:"addu",34:"sub",35:"subu",36:"and",37:"or",38:"xor",39:"nor",42:"slt",43:"sltu",45:"daddu"};
    const name = names[fn];
    if (fn === 0) return `sll ${r(rd)},${r(rt)},${sa}`;
    if (fn === 2 || fn === 3) return `${name} ${r(rd)},${r(rt)},${sa}`;
    if (fn === 8) return `jr ${r(rs)}`;
    if (fn === 9) return `jalr ${r(rd)},${r(rs)}`;
    if (fn === 16 || fn === 18) return `${name} ${r(rd)}`;
    if ([24,25,26,27].includes(fn)) return `${name} ${r(rs)},${r(rt)}`;
    if (name) return `${name} ${r(rd)},${r(rs)},${r(rt)}`;
  }
  if (op === 1) {
    const names = {0:"bltz",1:"bgez",16:"bltzal",17:"bgezal"};
    return `${names[rt] ?? "regimm"} ${r(rs)},${branch}`;
  }
  if (op === 2 || op === 3) return `${op === 2 ? "j" : "jal"} ${jump}`;
  if (op >= 4 && op <= 7) {
    const name = ["beq","bne","blez","bgtz"][op - 4];
    return op <= 5 ? `${name} ${r(rs)},${r(rt)},${branch}` : `${name} ${r(rs)},${branch}`;
  }
  if ([8,9,10,11,12,13,14].includes(op)) {
    const names = {8:"addi",9:"addiu",10:"slti",11:"sltiu",12:"andi",13:"ori",14:"xori"};
    const unsigned = op >= 12;
    const val = unsigned ? imm : simm;
    return `${names[op]} ${r(rt)},${r(rs)},${unsigned ? hexOff(imm) : val}` + (((op === 8 || op === 9 || op === 13) && rs === 0) ? ` ; li ${r(rt)},${val}` : "");
  }
  if (op === 15) return `lui ${r(rt)},${hexOff(imm)}`;
  const memoryNames = {30:"lq",31:"sq",32:"lb",33:"lh",34:"lwl",35:"lw",36:"lbu",37:"lhu",38:"lwr",40:"sb",41:"sh",42:"swl",43:"sw",46:"swr",49:"lwc1",55:"ld",57:"swc1",63:"sd"};
  if (memoryNames[op]) return `${memoryNames[op]} ${r(rt)},${simm}(${r(rs)})`;
  if (op === 28) return `mmi ${hex32(word)}`;
  if (op === 16 || op === 17 || op === 18) return `cop${op - 16} ${hex32(word)}`;
  return `.word ${hex32(word)}`;
}

function hex32(value) { return `0x${(value >>> 0).toString(16).padStart(8, "0")}`; }
function hexOff(value) { return `0x${(value >>> 0).toString(16)}`; }
