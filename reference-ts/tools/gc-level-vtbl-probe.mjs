import { hashRandomAccessReaderSha256 } from "../.build/packages/hashing/src/index.js";
import { Iso9660Filesystem } from "../.build/packages/iso9660/src/index.js";
import { openPs2Disc } from "../.build/packages/ps2-disc/src/index.js";
import { readGcLevelWadHeader, openGcLevelWadLump } from "../.build/packages/gc-level-wad/src/index.js";
import { readWadLz } from "../.build/packages/wad-lz/src/index.js";
import { parseGcGameplayMobyPvars } from "../.build/packages/gc-pvars/src/index.js";
import { LocalFileRandomAccessReader } from "./local-random-access.mjs";
import { basename, resolve } from "node:path";

const EXPECTED_SIZE = 3_828_350_976;
const EXPECTED_SHA256 = "9db2e33e276133cc283647fa3279b37911955e123d6199d10065547eaa9b1ce5";
const EXPECTED_SERIAL = "SCUS-97268";
const MAX_OVERLAY_BYTES = 16 * 1024 * 1024;
const MAX_DUMP_BYTES = 4096;
const VTBL_RECORD_SIZE = 12;
const SECTION_NAMES = [".lit", ".bss", ".data", "lvl.vtbl", "lvl.camvtbl", "lvl.sndvtbl", ".text"];
const REG = ["zero","at","v0","v1","a0","a1","a2","a3","t0","t1","t2","t3","t4","t5","t6","t7","s0","s1","s2","s3","s4","s5","s6","s7","t8","t9","k0","k1","gp","sp","fp","ra"];

const args = process.argv.slice(2);
const positional = [];
let levelsRaw = "0-26";
let classesRaw = "500,501,505,511,512,3291";
let dumpLevel = 1;
let dumpBytes = 1024;
let verifyHash = false;
for (let i = 0; i < args.length; i++) {
  const arg = args[i];
  if (arg === "--verify-hash") verifyHash = true;
  else if (arg === "--levels") levelsRaw = args[++i];
  else if (arg === "--classes") classesRaw = args[++i];
  else if (arg === "--dump-level") dumpLevel = Number(args[++i]);
  else if (arg === "--dump-bytes") dumpBytes = Number(args[++i]);
  else if (arg.startsWith("--")) throw new Error(`Unknown flag: ${arg}`);
  else positional.push(arg);
}
if (positional.length !== 1) {
  console.error("Usage: node tools/gc-level-vtbl-probe.mjs <gc-iso> [--verify-hash] [--levels 0-26] [--classes 500,3291] [--dump-level 1] [--dump-bytes 1024]");
  process.exit(2);
}
const levels = parseLevels(levelsRaw);
const classes = parseClasses(classesRaw);
if (!Number.isInteger(dumpLevel) || !levels.includes(dumpLevel)) throw new Error(`--dump-level ${dumpLevel} must be one of the selected levels.`);
if (!Number.isInteger(dumpBytes) || dumpBytes < 0 || dumpBytes > MAX_DUMP_BYTES || (dumpBytes & 3) !== 0) throw new Error(`--dump-bytes must be aligned and within 0..${MAX_DUMP_BYTES}.`);

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
  console.log(`GC_VTBL_AUTHORITY serial=${disc.boot.serial} levels=${levels.join(",")} classes=${classes.join(",")} hashVerified=${verifyHash}`);

  let subsetLevels = 0;
  for (const level of levels) {
    const wad = await fs.openFile(`/G/LEVEL${level}.WAD`);
    if (!wad) throw new Error(`Missing /G/LEVEL${level}.WAD.`);
    const wadHeader = await readGcLevelWadHeader(wad);
    const dataLump = openGcLevelWadLump(wad, wadHeader, 0);
    const gameplayLump = openGcLevelWadLump(wad, wadHeader, 2);
    if (!dataLump || !gameplayLump) throw new Error(`LEVEL${level} is missing data or gameplay lump.`);

    const dh = await dataLump.read(0, 8);
    const dv = new DataView(dh.buffer, dh.byteOffset, dh.byteLength);
    const overlayOffset = dv.getInt32(0, true);
    const overlaySize = dv.getInt32(4, true);
    if (overlayOffset < 0 || overlaySize < 0 || overlaySize > MAX_OVERLAY_BYTES || overlayOffset > dataLump.size || overlaySize > dataLump.size - overlayOffset) throw new Error(`LEVEL${level} invalid overlay range ${overlayOffset}+${overlaySize}.`);
    const overlay = await dataLump.read(overlayOffset, overlaySize);
    const sections = parseOverlaySections(overlay, level);
    if (sections.length < 7) throw new Error(`LEVEL${level} has only ${sections.length} overlay sections.`);
    const vtbl = sections[3];
    const text = sections[6];
    const records = parseVtableRecords(overlay, vtbl, level);

    const { data: gameplay } = await readWadLz(gameplayLump, 0, { maxOutputBytes: 64 * 1024 * 1024 });
    const gameplayInfo = parseGcGameplayMobyPvars(gameplay);
    const gameplayClasses = new Set(gameplayInfo.mobyClasses);
    const missingFromGameplay = records.filter((r) => !gameplayClasses.has(r.oClass)).map((r) => r.oClass);
    if (!missingFromGameplay.length) subsetLevels += 1;

    console.log(`GC_VTBL_LEVEL level=${level} native=${wadHeader.levelId} gameplayClasses=${gameplayInfo.mobyClasses.length} records=${records.length} vtblSubsetOfGameplay=${missingFromGameplay.length === 0} missingFromGameplay=${missingFromGameplay.length} vtblDest=${hex32(vtbl.destAddress)} vtblBytes=${vtbl.copySize} textDest=${hex32(text.destAddress)} textBytes=${text.copySize}`);
    console.log(`GC_VTBL_FIELDS level=${level} updateKinds=${formatKinds(countKinds(records.map((r) => classifyAddress(sections, r.update))))} auxKinds=${formatKinds(countKinds(records.map((r) => classifyAddress(sections, r.aux))))}`);

    const byClass = new Map();
    for (const record of records) {
      const list = byClass.get(record.oClass) ?? [];
      list.push(record);
      byClass.set(record.oClass, list);
    }
    console.log(`GC_VTBL_UNIQUENESS level=${level} duplicateClasses=${[...byClass.values()].filter((list) => list.length > 1).length}`);

    const dumped = new Set();
    for (const oClass of classes) {
      const matches = byClass.get(oClass) ?? [];
      if (!matches.length) {
        console.log(`GC_VTBL_CLASS level=${level} oClass=${oClass} present=false gameplayListed=${gameplayClasses.has(oClass)}`);
        continue;
      }
      for (const record of matches) {
        const updateKind = classifyAddress(sections, record.update);
        const auxKind = classifyAddress(sections, record.aux);
        console.log(`GC_VTBL_CLASS level=${level} oClass=${oClass} present=true record=${record.index} recordAddress=${hex32(vtbl.destAddress + record.index * VTBL_RECORD_SIZE)} update=${hex32(record.update)} updateKind=${updateKind} aux=${hex32(record.aux)} auxKind=${auxKind}`);
        if (level === dumpLevel && dumpBytes > 0 && updateKind === ".text" && !dumped.has(record.update)) {
          dumped.add(record.update);
          dumpLoadedFunction(overlay, text, record.update, dumpBytes, oClass, record.index);
        }
      }
    }
  }
  console.log(`GC_VTBL_SUMMARY levels=${levels.length} vtblSubsetOfGameplayLevels=${subsetLevels}`);
} finally {
  await reader.close();
}

function parseVtableRecords(overlay, section, level) {
  const bytes = overlay.subarray(section.dataOffset, section.dataOffset + section.copySize);
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  const records = [];
  for (let at = 0; at + VTBL_RECORD_SIZE <= bytes.byteLength; at += VTBL_RECORD_SIZE) {
    const oClass = view.getInt32(at, true);
    const update = view.getUint32(at + 4, true);
    const aux = view.getUint32(at + 8, true);
    if (oClass < 0) {
      if (oClass !== -1) throw new Error(`LEVEL${level} lvl.vtbl unexpected negative class ${oClass} at ${hexOff(at)}.`);
      console.log(`GC_VTBL_TERMINATOR level=${level} offset=${hexOff(at)} update=${hex32(update)} aux=${hex32(aux)}`);
      return records;
    }
    records.push({ index: records.length, oClass, update, aux });
  }
  throw new Error(`LEVEL${level} lvl.vtbl has no -1 class terminator.`);
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
    if (copySize > bytes.byteLength - dataOffset) throw new Error(`LEVEL${level} overlay section at ${hex32(cursor)} overruns overlay.`);
    sections.push({ dataOffset, destAddress, copySize, sectionType, entryPoint });
    cursor = dataOffset + copySize;
  }
  return sections;
}
function classifyAddress(sections, address) {
  if (address === 0) return "zero";
  const index = sections.findIndex((s) => address >= s.destAddress && address < s.destAddress + s.copySize);
  return index >= 0 ? (SECTION_NAMES[index] ?? `section${index}`) : "external";
}
function countKinds(kinds) { const m = new Map(); for (const k of kinds) m.set(k, (m.get(k) ?? 0) + 1); return m; }
function formatKinds(m) { return [...m.entries()].sort(([a],[b]) => a.localeCompare(b)).map(([k,v]) => `${k}:${v}`).join(","); }
function dumpLoadedFunction(overlay, section, address, requestedBytes, oClass, recordIndex) {
  const relative = address - section.destAddress;
  const bytes = Math.min(requestedBytes, section.copySize - relative) & ~3;
  if (bytes <= 0) return;
  const slice = overlay.subarray(section.dataOffset + relative, section.dataOffset + relative + bytes);
  const view = new DataView(slice.buffer, slice.byteOffset, slice.byteLength);
  console.log(`GC_VTBL_DUMP_BEGIN oClass=${oClass} record=${recordIndex} start=${hex32(address)} bytes=${bytes}`);
  for (let offset = 0; offset < bytes; offset += 4) {
    const pc = address + offset, word = view.getUint32(offset, true);
    console.log(`${hex32(pc)}  ${hex32(word)}  ${decode(word, pc)}`);
  }
  console.log(`GC_VTBL_DUMP_END oClass=${oClass} start=${hex32(address)}`);
}
function parseLevels(raw) {
  const out = new Set();
  for (const tokenRaw of raw.split(",")) {
    const token = tokenRaw.trim(); if (!token) continue;
    const range = /^(\d+)-(\d+)$/.exec(token);
    if (range) { const a = Number(range[1]), b = Number(range[2]); if (a > b) throw new Error(`Descending level range: ${token}.`); for (let n = a; n <= b; n++) out.add(n); }
    else out.add(Number(token));
  }
  const levels = [...out].sort((a,b) => a-b);
  if (!levels.length || levels.some((n) => !Number.isInteger(n) || n < 0 || n > 26)) throw new Error(`Invalid levels: ${raw}.`);
  return levels;
}
function parseClasses(raw) { const out = [...new Set(raw.split(",").map((s) => Number(s.trim())))]; if (!out.length || out.some((n) => !Number.isInteger(n) || n < 0 || n > 65535)) throw new Error(`Invalid classes: ${raw}.`); return out; }
function decode(word, pc) {
  const op = word >>> 26, rs = (word >>> 21) & 31, rt = (word >>> 16) & 31, rd = (word >>> 11) & 31, sa = (word >>> 6) & 31, fn = word & 63, imm = word & 0xffff, simm = (imm << 16) >> 16;
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
function hex32(v) { return `0x${(v >>> 0).toString(16).padStart(8,"0")}`; }
function hexOff(v) { return `0x${(v >>> 0).toString(16)}`; }
