import { hashRandomAccessReaderSha256 } from "../.build/packages/hashing/src/index.js";
import { Iso9660Filesystem } from "../.build/packages/iso9660/src/index.js";
import { openPs2Disc } from "../.build/packages/ps2-disc/src/index.js";
import { readGcLevelWadHeader, openGcLevelWadLump } from "../.build/packages/gc-level-wad/src/index.js";
import { LocalFileRandomAccessReader } from "./local-random-access.mjs";
import { basename, resolve } from "node:path";

const EXPECTED_SIZE = 3_828_350_976;
const EXPECTED_SHA256 = "9db2e33e276133cc283647fa3279b37911955e123d6199d10065547eaa9b1ce5";
const EXPECTED_SERIAL = "SCUS-97268";
const MAX_OVERLAY_BYTES = 16 * 1024 * 1024;
const MAX_CONTEXT_BYTES = 1024;
const REG = ["zero","at","v0","v1","a0","a1","a2","a3","t0","t1","t2","t3","t4","t5","t6","t7","s0","s1","s2","s3","s4","s5","s6","s7","t8","t9","k0","k1","gp","sp","fp","ra"];

const args = process.argv.slice(2);
const positional = [];
let level = 1;
let immediatesRaw = "500,501,505,511,512,3291";
let targetsRaw = "0x003922b0,0x00392158,0x002c9738";
let contextBytes = 128;
let verifyHash = false;
for (let i = 0; i < args.length; i++) {
  const arg = args[i];
  if (arg === "--verify-hash") verifyHash = true;
  else if (arg === "--level") level = Number(args[++i]);
  else if (arg === "--immediates") immediatesRaw = args[++i];
  else if (arg === "--targets") targetsRaw = args[++i];
  else if (arg === "--context-bytes") contextBytes = Number(args[++i]);
  else if (arg.startsWith("--")) throw new Error(`Unknown flag: ${arg}`);
  else positional.push(arg);
}
if (positional.length !== 1) {
  console.error("Usage: node tools/gc-level-overlay-xrefs.mjs <gc-iso> [--verify-hash] [--level 1] [--immediates 500,3291] [--targets 0xADDR,...] [--context-bytes 128]");
  process.exit(2);
}
if (!Number.isInteger(level) || level < 0 || level > 26) throw new Error(`Invalid GC level: ${level}.`);
if (!Number.isInteger(contextBytes) || contextBytes < 0 || contextBytes > MAX_CONTEXT_BYTES || (contextBytes & 3) !== 0) throw new Error(`--context-bytes must be aligned and within 0..${MAX_CONTEXT_BYTES}.`);
const immediates = parseIntegerList(immediatesRaw, 0xffff, "immediate");
const targets = parseIntegerList(targetsRaw, 0xffffffff, "target");

const isoPath = resolve(positional[0]);
const reader = await LocalFileRandomAccessReader.open(isoPath, basename(isoPath));
try {
  if (reader.size !== EXPECTED_SIZE) throw new Error(`GC authority size mismatch: ${reader.size} != ${EXPECTED_SIZE}.`);
  const disc = await openPs2Disc(reader);
  if (disc.boot.serial !== EXPECTED_SERIAL) throw new Error(`GC authority serial mismatch: ${disc.boot.serial ?? "none"} != ${EXPECTED_SERIAL}.`);
  if (verifyHash) {
    const hashed = await hashRandomAccessReaderSha256(reader);
    if (hashed.sha256 !== EXPECTED_SHA256) throw new Error(`GC authority SHA-256 mismatch: ${hashed.sha256}.`);
  }
  const fs = await Iso9660Filesystem.open(reader);
  const wad = await fs.openFile(`/G/LEVEL${level}.WAD`);
  if (!wad) throw new Error(`Missing /G/LEVEL${level}.WAD.`);
  const wadHeader = await readGcLevelWadHeader(wad);
  const dataLump = openGcLevelWadLump(wad, wadHeader, 0);
  if (!dataLump) throw new Error(`LEVEL${level} has no data lump.`);
  const dh = await dataLump.read(0, 8);
  const dv = new DataView(dh.buffer, dh.byteOffset, dh.byteLength);
  const overlayOffset = dv.getInt32(0, true);
  const overlaySize = dv.getInt32(4, true);
  if (overlayOffset < 0 || overlaySize < 0 || overlaySize > MAX_OVERLAY_BYTES || overlayOffset > dataLump.size || overlaySize > dataLump.size - overlayOffset) throw new Error(`LEVEL${level} invalid overlay range.`);
  const overlay = await dataLump.read(overlayOffset, overlaySize);
  const sections = parseOverlaySections(overlay, level);
  if (sections.length < 7) throw new Error(`LEVEL${level} has only ${sections.length} overlay sections.`);
  const text = sections[6];
  const textBytes = overlay.subarray(text.dataOffset, text.dataOffset + text.copySize);
  const view = new DataView(textBytes.buffer, textBytes.byteOffset, textBytes.byteLength);

  console.log(`GC_OVERLAY_XREF_AUTHORITY level=${level} native=${wadHeader.levelId} text=${hex32(text.destAddress)} bytes=${text.copySize} hashVerified=${verifyHash} immediates=${immediates.join(",")} targets=${targets.map(hex32).join(",")}`);

  const immediateHits = new Map(immediates.map((value) => [value, []]));
  const targetHits = new Map(targets.map((value) => [value, []]));
  for (let offset = 0; offset + 4 <= view.byteLength; offset += 4) {
    const pc = text.destAddress + offset;
    const word = view.getUint32(offset, true);
    const decoded = decodeMetadata(word, pc);
    if (decoded.immediate !== null && immediateHits.has(decoded.immediate)) immediateHits.get(decoded.immediate).push({ pc, offset, word });
    if (decoded.target !== null && targetHits.has(decoded.target)) targetHits.get(decoded.target).push({ pc, offset, word });
  }

  const dumped = new Set();
  for (const value of immediates) {
    const hits = immediateHits.get(value);
    console.log(`GC_OVERLAY_IMMEDIATE value=${value} hits=${hits.length}`);
    for (const hit of hits) {
      console.log(`GC_OVERLAY_IMMEDIATE_HIT value=${value} pc=${hex32(hit.pc)} word=${hex32(hit.word)}`);
      dumpContextOnce(textBytes, text.destAddress, hit.offset, contextBytes, `IMM_${value}_${hex32(hit.pc)}`, dumped);
    }
  }
  for (const target of targets) {
    const hits = targetHits.get(target);
    console.log(`GC_OVERLAY_CALL_TARGET target=${hex32(target)} xrefs=${hits.length}`);
    for (const hit of hits) {
      console.log(`GC_OVERLAY_CALL_XREF target=${hex32(target)} caller=${hex32(hit.pc)}`);
      dumpContextOnce(textBytes, text.destAddress, hit.offset, contextBytes, `CALL_${hex32(target)}_${hex32(hit.pc)}`, dumped);
    }
  }
} finally {
  await reader.close();
}

function dumpContextOnce(textBytes, base, hitOffset, radius, label, dumped) {
  if (radius === 0) return;
  const start = Math.max(0, (hitOffset - radius) & ~3);
  const end = Math.min(textBytes.byteLength, (hitOffset + radius + 4 + 3) & ~3);
  const key = `${start}:${end}`;
  if (dumped.has(key)) return;
  dumped.add(key);
  const view = new DataView(textBytes.buffer, textBytes.byteOffset + start, end - start);
  console.log(`GC_OVERLAY_CONTEXT_BEGIN label=${label} start=${hex32(base + start)} end=${hex32(base + end)}`);
  for (let offset = 0; offset < view.byteLength; offset += 4) {
    const pc = base + start + offset;
    const word = view.getUint32(offset, true);
    console.log(`${hex32(pc)}  ${hex32(word)}  ${decodeText(word, pc)}`);
  }
  console.log(`GC_OVERLAY_CONTEXT_END label=${label}`);
}

function parseOverlaySections(bytes, level) {
  const sections = [];
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  let cursor = 0, expectedEntryPoint = null;
  while (cursor + 16 <= bytes.byteLength) {
    const destAddress = view.getUint32(cursor, true), copySize = view.getUint32(cursor + 4, true), sectionType = view.getUint32(cursor + 8, true), entryPoint = view.getUint32(cursor + 12, true);
    if (expectedEntryPoint === null) expectedEntryPoint = entryPoint;
    else if (entryPoint !== expectedEntryPoint) break;
    const dataOffset = cursor + 16;
    if (copySize > bytes.byteLength - dataOffset) throw new Error(`LEVEL${level} overlay section overruns overlay.`);
    sections.push({ dataOffset, destAddress, copySize, sectionType, entryPoint });
    cursor = dataOffset + copySize;
  }
  return sections;
}

function decodeMetadata(word, pc) {
  const op = word >>> 26;
  let immediate = null, target = null;
  const imm = word & 0xffff;
  const simm = (imm << 16) >> 16;
  if ([8,9,10,11].includes(op) && simm >= 0) immediate = simm;
  else if ([12,13,14].includes(op)) immediate = imm;
  if (op === 3) target = ((((pc + 4) & 0xf0000000) | ((word & 0x03ffffff) << 2)) >>> 0);
  return { immediate, target };
}

function decodeText(word, pc) {
  const op = word >>> 26, rs = (word >>> 21) & 31, rt = (word >>> 16) & 31, rd = (word >>> 11) & 31, sa = (word >>> 6) & 31, fn = word & 63;
  const imm = word & 0xffff, simm = (imm << 16) >> 16;
  const r = (n) => `$${REG[n]}`, branch = hex32((pc + 4 + (simm << 2)) >>> 0), jump = hex32((((pc + 4) & 0xf0000000) | ((word & 0x03ffffff) << 2)) >>> 0);
  if (op === 0) {
    const names = {0:"sll",2:"srl",3:"sra",8:"jr",9:"jalr",16:"mfhi",18:"mflo",24:"mult",25:"multu",26:"div",27:"divu",32:"add",33:"addu",34:"sub",35:"subu",36:"and",37:"or",38:"xor",39:"nor",42:"slt",43:"sltu",45:"daddu"}, name = names[fn];
    if (fn === 0) return `sll ${r(rd)},${r(rt)},${sa}`; if (fn === 2 || fn === 3) return `${name} ${r(rd)},${r(rt)},${sa}`; if (fn === 8) return `jr ${r(rs)}`; if (fn === 9) return `jalr ${r(rd)},${r(rs)}`; if (fn === 16 || fn === 18) return `${name} ${r(rd)}`; if ([24,25,26,27].includes(fn)) return `${name} ${r(rs)},${r(rt)}`; if (name) return `${name} ${r(rd)},${r(rs)},${r(rt)}`;
  }
  if (op === 1) { const names = {0:"bltz",1:"bgez",16:"bltzal",17:"bgezal"}; return `${names[rt] ?? "regimm"} ${r(rs)},${branch}`; }
  if (op === 2 || op === 3) return `${op === 2 ? "j" : "jal"} ${jump}`;
  if (op >= 4 && op <= 7) { const name = ["beq","bne","blez","bgtz"][op-4]; return op <= 5 ? `${name} ${r(rs)},${r(rt)},${branch}` : `${name} ${r(rs)},${branch}`; }
  if ([8,9,10,11,12,13,14].includes(op)) { const names = {8:"addi",9:"addiu",10:"slti",11:"sltiu",12:"andi",13:"ori",14:"xori"}, unsigned = op >= 12, val = unsigned ? imm : simm; return `${names[op]} ${r(rt)},${r(rs)},${unsigned ? hexOff(imm) : val}`; }
  if (op === 15) return `lui ${r(rt)},${hexOff(imm)}`;
  const mem = {30:"lq",31:"sq",32:"lb",33:"lh",34:"lwl",35:"lw",36:"lbu",37:"lhu",38:"lwr",40:"sb",41:"sh",42:"swl",43:"sw",46:"swr",49:"lwc1",55:"ld",57:"swc1",63:"sd"};
  if (mem[op]) return `${mem[op]} ${r(rt)},${simm}(${r(rs)})`; if (op === 28) return `mmi ${hex32(word)}`; if (op === 16 || op === 17 || op === 18) return `cop${op-16} ${hex32(word)}`; return `.word ${hex32(word)}`;
}
function parseIntegerList(raw, max, label) { const out = [...new Set(raw.split(",").filter(Boolean).map((s) => Number(s.trim())))]; if (out.some((n) => !Number.isInteger(n) || n < 0 || n > max)) throw new Error(`Invalid ${label} list: ${raw}.`); return out; }
function hex32(v) { return `0x${(v >>> 0).toString(16).padStart(8,"0")}`; }
function hexOff(v) { return `0x${(v >>> 0).toString(16)}`; }
