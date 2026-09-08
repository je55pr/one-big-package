import { hashRandomAccessReaderSha256 } from "../.build/packages/hashing/src/index.js";
import { openPs2Disc, readPs2BootProgramHeaders } from "../.build/packages/ps2-disc/src/index.js";
import { ELF_PROGRAM_TYPE_LOAD } from "../.build/packages/elf32/src/index.js";
import { LocalFileRandomAccessReader } from "./local-random-access.mjs";
import { basename, resolve } from "node:path";

const EXPECTED_SIZE = 3_828_350_976;
const EXPECTED_SHA256 = "9db2e33e276133cc283647fa3279b37911955e123d6199d10065547eaa9b1ce5";
const EXPECTED_SERIAL = "SCUS-97268";
const MAX_TOTAL_WINDOW_BYTES = 16 * 1024;
const TARGET_CLASSES = new Set([500, 501, 505, 511, 512, 4018]);
const PV_OFFSETS = new Set([0xc8, 0xcc]);
const REG = ["zero","at","v0","v1","a0","a1","a2","a3","t0","t1","t2","t3","t4","t5","t6","t7","s0","s1","s2","s3","s4","s5","s6","s7","t8","t9","k0","k1","gp","sp","fp","ra"];

const args = process.argv.slice(2);
const positional = [];
let rangesRaw = null;
let verifyHash = false;
for (let i = 0; i < args.length; i++) {
  if (args[i] === "--verify-hash") verifyHash = true;
  else if (args[i] === "--ranges") rangesRaw = args[++i];
  else if (args[i].startsWith("--")) throw new Error(`Unknown flag: ${args[i]}`);
  else positional.push(args[i]);
}
if (positional.length !== 1 || !rangesRaw) {
  console.error("Usage: node tools/gc-exe-window-probe.mjs <gc-iso> --ranges 0xSTART:0xEND[,0xSTART:0xEND...] [--verify-hash]");
  process.exit(2);
}
const ranges = parseRanges(rangesRaw);
const totalWindowBytes = ranges.reduce((sum, range) => sum + range.end - range.start, 0);
if (totalWindowBytes > MAX_TOTAL_WINDOW_BYTES) throw new Error(`Requested executable windows total ${totalWindowBytes} bytes; cap is ${MAX_TOTAL_WINDOW_BYTES}.`);

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
  const loads = phdrs.filter((segment) => segment.type === ELF_PROGRAM_TYPE_LOAD && segment.fileSize > 0);
  console.log(`GC_EXE_WINDOW_AUTHORITY exe=${disc.boot.executableName} entry=${hex32(disc.boot.executableEntryPoint)} ranges=${ranges.length} bytes=${totalWindowBytes} hashVerified=${verifyHash}`);

  for (const range of ranges) {
    const segment = loads.find((candidate) => range.start >= candidate.virtualAddress && range.end <= candidate.virtualAddress + candidate.fileSize);
    if (!segment) throw new Error(`Window ${hex32(range.start)}:${hex32(range.end)} is not fully file-backed by one PT_LOAD segment.`);
    const fileOffset = segment.offset + (range.start - segment.virtualAddress);
    const bytes = await disc.bootExecutable.read(fileOffset, range.end - range.start);
    if (bytes.byteLength !== range.end - range.start) throw new Error(`Short executable window read at ${hex32(range.start)}.`);
    console.log(`GC_EXE_WINDOW_BEGIN start=${hex32(range.start)} end=${hex32(range.end)} fileOffset=${hex32(fileOffset)} bytes=${bytes.byteLength}`);
    const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
    for (let offset = 0; offset + 4 <= bytes.byteLength; offset += 4) {
      const pc = range.start + offset;
      const word = view.getUint32(offset, true);
      const decoded = decode(word, pc);
      const marks = [];
      if (decoded.classImmediate !== null && TARGET_CLASSES.has(decoded.classImmediate)) marks.push(`CLASS=${decoded.classImmediate}`);
      if (decoded.memoryOffset !== null && PV_OFFSETS.has(decoded.memoryOffset)) marks.push(`PVAR=${hexOff(decoded.memoryOffset)}`);
      if (decoded.operation === "jal") marks.push(`CALL=${decoded.target}`);
      console.log(`${hex32(pc)}  ${hex32(word)}  ${decoded.text}${marks.length ? `  ; ${marks.join(" ")}` : ""}`);
    }
    console.log(`GC_EXE_WINDOW_END start=${hex32(range.start)}`);
  }
} finally {
  await reader.close();
}

function parseRanges(raw) {
  const ranges = raw.split(",").map((item) => {
    const [a, b] = item.split(":");
    const start = Number(a);
    const end = Number(b);
    if (!Number.isSafeInteger(start) || !Number.isSafeInteger(end) || start < 0 || end <= start || (start & 3) !== 0 || (end & 3) !== 0) {
      throw new Error(`Invalid aligned range: ${item}`);
    }
    if (end - start > 8192) throw new Error(`Individual executable window exceeds 8192-byte cap: ${item}`);
    return { start, end };
  });
  ranges.sort((x, y) => x.start - y.start);
  for (let i = 1; i < ranges.length; i++) if (ranges[i].start < ranges[i - 1].end) throw new Error("Executable windows must not overlap.");
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
  const branch = hex32((pc + 4 + (simm << 2)) >>> 0);
  const jump = hex32((((pc + 4) & 0xf0000000) | ((word & 0x03ffffff) << 2)) >>> 0);
  let operation = `op${op}`;
  let text = `.word ${hex32(word)}`;
  let classImmediate = null;
  let memoryOffset = null;
  let target = null;

  if (op === 0) {
    const r = (n) => `$${REG[n]}`;
    switch (fn) {
      case 0: operation="sll"; text=`sll ${r(rd)},${r(rt)},${sa}`; break;
      case 2: operation="srl"; text=`srl ${r(rd)},${r(rt)},${sa}`; break;
      case 3: operation="sra"; text=`sra ${r(rd)},${r(rt)},${sa}`; break;
      case 8: operation="jr"; text=`jr ${r(rs)}`; break;
      case 9: operation="jalr"; text=`jalr ${r(rd)},${r(rs)}`; break;
      case 16: operation="mfhi"; text=`mfhi ${r(rd)}`; break;
      case 18: operation="mflo"; text=`mflo ${r(rd)}`; break;
      case 24: operation="mult"; text=`mult ${r(rs)},${r(rt)}`; break;
      case 25: operation="multu"; text=`multu ${r(rs)},${r(rt)}`; break;
      case 26: operation="div"; text=`div ${r(rs)},${r(rt)}`; break;
      case 27: operation="divu"; text=`divu ${r(rs)},${r(rt)}`; break;
      case 32: operation="add"; text=`add ${r(rd)},${r(rs)},${r(rt)}`; break;
      case 33: operation="addu"; text=`addu ${r(rd)},${r(rs)},${r(rt)}`; break;
      case 34: operation="sub"; text=`sub ${r(rd)},${r(rs)},${r(rt)}`; break;
      case 35: operation="subu"; text=`subu ${r(rd)},${r(rs)},${r(rt)}`; break;
      case 36: operation="and"; text=`and ${r(rd)},${r(rs)},${r(rt)}`; break;
      case 37: operation="or"; text=`or ${r(rd)},${r(rs)},${r(rt)}`; break;
      case 38: operation="xor"; text=`xor ${r(rd)},${r(rs)},${r(rt)}`; break;
      case 39: operation="nor"; text=`nor ${r(rd)},${r(rs)},${r(rt)}`; break;
      case 42: operation="slt"; text=`slt ${r(rd)},${r(rs)},${r(rt)}`; break;
      case 43: operation="sltu"; text=`sltu ${r(rd)},${r(rs)},${r(rt)}`; break;
      default: operation=`special${fn}`;
    }
  } else if (op === 1) {
    const names = {0:"bltz",1:"bgez",16:"bltzal",17:"bgezal"};
    operation = names[rt] ?? "regimm";
    text = `${operation} $${REG[rs]},${branch}`;
  } else if (op === 2 || op === 3) {
    operation = op === 2 ? "j" : "jal";
    target = jump;
    text = `${operation} ${jump}`;
  } else if (op >= 4 && op <= 7) {
    operation = ["beq","bne","blez","bgtz"][op - 4];
    text = op <= 5 ? `${operation} $${REG[rs]},$${REG[rt]},${branch}` : `${operation} $${REG[rs]},${branch}`;
  } else if ([8,9,10,11,12,13,14].includes(op)) {
    const names = {8:"addi",9:"addiu",10:"slti",11:"sltiu",12:"andi",13:"ori",14:"xori"};
    operation = names[op];
    const unsigned = op >= 12;
    const val = unsigned ? imm : simm;
    if (val >= 0) classImmediate = val;
    const rendered = unsigned ? hexOff(imm) : String(simm);
    text = `${operation} $${REG[rt]},$${REG[rs]},${rendered}`;
    if ((op === 8 || op === 9 || op === 13) && rs === 0) text += ` ; li $${REG[rt]},${val}`;
  } else if (op === 15) {
    operation = "lui";
    text = `lui $${REG[rt]},${hexOff(imm)}`;
  } else {
    const memoryNames = {30:"lq",31:"sq",32:"lb",33:"lh",34:"lwl",35:"lw",36:"lbu",37:"lhu",38:"lwr",40:"sb",41:"sh",42:"swl",43:"sw",46:"swr",49:"lwc1",55:"ld",57:"swc1",63:"sd"};
    if (memoryNames[op]) {
      operation = memoryNames[op];
      memoryOffset = simm >= 0 ? simm : null;
      text = `${operation} $${REG[rt]},${simm}($${REG[rs]})`;
    } else if (op === 28) {
      operation = "mmi";
      text = `mmi ${hex32(word)}`;
    } else if (op === 16 || op === 17 || op === 18) {
      operation = `cop${op - 16}`;
      text = `${operation} ${hex32(word)}`;
    }
  }

  return { operation, text, classImmediate, memoryOffset, target };
}

function hex32(value) { return `0x${(value >>> 0).toString(16).padStart(8,"0")}`; }
function hexOff(value) { return `0x${(value >>> 0).toString(16)}`; }
