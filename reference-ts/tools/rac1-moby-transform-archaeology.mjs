import { mkdir, writeFile } from "node:fs/promises";
import { basename, resolve } from "node:path";
import { LocalFileRandomAccessReader } from "./local-random-access.mjs";
import { openPs2Disc, readPs2BootProgramHeaders } from "../.build/packages/ps2-disc/src/index.js";
import { ELF_PROGRAM_TYPE_LOAD } from "../.build/packages/elf32/src/index.js";

const GPR = ["zero","at","v0","v1","a0","a1","a2","a3","t0","t1","t2","t3","t4","t5","t6","t7","s0","s1","s2","s3","s4","s5","s6","s7","t8","t9","k0","k1","gp","sp","fp","ra"];
const FIELD_OFFSETS = new Set([0x00, 0x18, 0x1c, 0x30, 0x34, 0x38, 0x3c, 0x40, 0x44]);
const TRANSFORM_OFFSETS = new Set([0x1c, 0x30, 0x34, 0x38, 0x3c, 0x40, 0x44]);
// R5900/MIPS memory-reading opcodes relevant to packed records. 0x1e is EE LQ.
const LOAD_OPS = new Set([0x1a,0x1b,0x1e,0x20,0x21,0x22,0x23,0x24,0x25,0x26,0x27,0x31,0x32,0x35,0x36,0x37]);

const args = process.argv.slice(2);
const positional = [];
const options = { outDir: "research/generated", build: "rac1-local", maxCandidates: 24 };
for (let i = 0; i < args.length; i++) {
  const arg = args[i];
  if (arg === "--out-dir") options.outDir = args[++i];
  else if (arg === "--build") options.build = args[++i];
  else if (arg === "--max-candidates") options.maxCandidates = Number(args[++i]);
  else if (arg.startsWith("--")) throw new Error(`Unknown flag: ${arg}`);
  else positional.push(arg);
}
if (positional.length !== 1) throw new Error("Expected one R&C1 ISO path.");

const isoPath = resolve(positional[0]);
const reader = await LocalFileRandomAccessReader.open(isoPath, basename(isoPath));
try {
  const disc = await openPs2Disc(reader);
  if (disc.boot.serial !== "SCUS-97199") throw new Error(`Expected R&C1 SCUS-97199, got ${disc.boot.serial ?? "unknown"}.`);
  const ph = await readPs2BootProgramHeaders(disc);
  const executableSegments = ph.filter((p) => p.type === ELF_PROGRAM_TYPE_LOAD && p.fileSize > 0 && (p.flags & 1) !== 0);
  const candidates = [];
  const segmentReports = [];

  for (const segment of executableSegments) {
    const bytes = await disc.bootExecutable.read(segment.offset, segment.fileSize);
    const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
    const hits = [];
    const strides = [];
    for (let at = 0; at + 4 <= bytes.length; at += 4) {
      const word = view.getUint32(at, true);
      const op = word >>> 26;
      const imm = sign16(word & 0xffff);
      if (LOAD_OPS.has(op) && FIELD_OFFSETS.has(imm)) {
        hits.push({ at, address: (segment.virtualAddress + at) >>> 0, base: (word >>> 21) & 31, rt: (word >>> 16) & 31, offset: imm, op });
      }
      if (op === 0x09 && Math.abs(imm) === 0x78) { // ADDIU stride by one Moby record
        strides.push({ at, rs: (word >>> 21) & 31, rt: (word >>> 16) & 31, imm });
      }
    }

    const raw = [];
    for (const hit of hits) {
      for (const radiusBytes of [0x60, 0xa0, 0x100, 0x180, 0x240]) {
        const nearby = hits.filter((other) => other.base === hit.base && Math.abs(other.at - hit.at) <= radiusBytes);
        const offsets = [...new Set(nearby.map((h) => h.offset))].sort((a, b) => a - b);
        const transformOffsets = offsets.filter((x) => TRANSFORM_OFFSETS.has(x));
        const hasPackedVecPair = offsets.includes(0x30) && offsets.includes(0x40);
        const hasIdentityFields = offsets.includes(0x18) || offsets.includes(0x00);
        const hasScale = offsets.includes(0x1c);
        const nearbyStride = strides.some((s) => Math.abs(s.at - hit.at) <= radiusBytes + 0x80 && (s.rs === hit.base || s.rt === hit.base));
        const positionCount = [0x30,0x34,0x38].filter((x) => offsets.includes(x)).length;
        const rotationCount = [0x3c,0x40,0x44].filter((x) => offsets.includes(x)).length;
        const score = transformOffsets.length * 3 + positionCount * 2 + rotationCount * 2 + (hasPackedVecPair ? 6 : 0) + (hasScale ? 4 : 0) + (hasIdentityFields ? 3 : 0) + (nearbyStride ? 5 : 0) - Math.floor(radiusBytes / 0x100);
        const plausible = transformOffsets.length >= 4 || (hasPackedVecPair && (hasScale || hasIdentityFields || nearbyStride));
        if (plausible) raw.push({ anchorAt: hit.at, anchorAddress: hit.address, base: hit.base, radiusBytes, offsets, score, hasPackedVecPair, hasScale, hasIdentityFields, nearbyStride });
      }
    }
    raw.sort((a, b) => b.score - a.score || a.radiusBytes - b.radiusBytes || a.anchorAt - b.anchorAt);
    const selected = [];
    for (const candidate of raw) {
      if (selected.some((prior) => prior.base === candidate.base && Math.abs(prior.anchorAt - candidate.anchorAt) < 0x140)) continue;
      selected.push(candidate);
      if (selected.length >= options.maxCandidates) break;
    }

    for (const candidate of selected) {
      const start = Math.max(0, candidate.anchorAt - 0xc0) & ~3;
      const end = Math.min(bytes.length, candidate.anchorAt + 0x184) & ~3;
      const instructions = [];
      for (let at = start; at < end; at += 4) {
        const word = view.getUint32(at, true);
        const address = (segment.virtualAddress + at) >>> 0;
        instructions.push({ address: hex(address), word: hex(word), text: disassemble(word, address) });
      }
      candidates.push({ segmentIndex: segment.index, anchorAddress: hex(candidate.anchorAddress), baseRegister: reg(candidate.base), radiusBytes: candidate.radiusBytes, offsets: candidate.offsets.map(hex16), score: candidate.score, packedVecPair: candidate.hasPackedVecPair, scale: candidate.hasScale, identityFields: candidate.hasIdentityFields, nearbyRecordStride: candidate.nearbyStride, instructions });
    }
    segmentReports.push({ index: segment.index, fileOffset: hex(segment.offset), virtualAddress: hex(segment.virtualAddress), fileSize: segment.fileSize, interestingMemoryHits: hits.length, recordStrideAddiuHits: strides.length, candidateCount: selected.length });
  }

  candidates.sort((a, b) => b.score - a.score || parseInt(a.anchorAddress.slice(2),16) - parseInt(b.anchorAddress.slice(2),16));
  const report = { generator: "tools/rac1-moby-transform-archaeology.mjs", generatedFrom: { isoFileName: basename(isoPath), executableName: disc.boot.executableName, executableSize: disc.boot.executableSize, serial: disc.boot.serial }, buildId: options.build, knownRetailInstanceLayout: { recordSize: "0x78", oClass: "0x18", scale: "0x1c", position: ["0x30","0x34","0x38"], rotation: ["0x3c","0x40","0x44"] }, executableSegments: segmentReports, candidates: candidates.slice(0, options.maxCandidates) };
  await mkdir(resolve(options.outDir), { recursive: true });
  const path = resolve(options.outDir, `${options.build}.moby-transform-candidates.json`);
  await writeFile(path, JSON.stringify(report, null, 2) + "\n");

  console.log(`MOBY_TRANSFORM_PROBE serial=${disc.boot.serial} executable=${disc.boot.executableName} size=${disc.boot.executableSize}`);
  for (const seg of segmentReports) console.log(`SEGMENT index=${seg.index} vaddr=${seg.virtualAddress} size=${seg.fileSize} memHits=${seg.interestingMemoryHits} stride78=${seg.recordStrideAddiuHits} candidates=${seg.candidateCount}`);
  console.log(`CANDIDATES=${report.candidates.length}`);
  for (const [i,c] of report.candidates.entries()) {
    console.log(`CANDIDATE #${i} anchor=${c.anchorAddress} base=${c.baseRegister} score=${c.score} radius=0x${c.radiusBytes.toString(16)} offsets=${c.offsets.join(",")} vec30_40=${c.packedVecPair} scale=${c.scale} identity=${c.identityFields} stride78=${c.nearbyRecordStride}`);
    for (const ins of c.instructions) console.log(`${ins.address}  ${ins.word}  ${ins.text}`);
  }
  console.log(`wrote ${path}`);
} finally { await reader.close(); }

function sign16(v) { return (v << 16) >> 16; }
function hex(v) { return `0x${(v >>> 0).toString(16).padStart(8,"0")}`; }
function hex16(v) { return `0x${(v & 0xffff).toString(16).padStart(4,"0")}`; }
function reg(n) { return `$${GPR[n] ?? `r${n}`}`; }
function freg(n) { return `$f${n}`; }
function disassemble(word, pc) {
  if (word === 0) return "nop";
  const op=word>>>26, rs=(word>>>21)&31, rt=(word>>>16)&31, rd=(word>>>11)&31, sa=(word>>>6)&31, fn=word&63, imm=sign16(word&0xffff), uimm=word&0xffff;
  if (op===0) {
    const names={0x00:"sll",0x02:"srl",0x03:"sra",0x20:"add",0x21:"addu",0x22:"sub",0x23:"subu",0x24:"and",0x25:"or",0x26:"xor",0x27:"nor",0x2a:"slt",0x2b:"sltu"};
    if(fn===0x08)return `jr ${reg(rs)}`; if(fn===0x09)return `jalr ${reg(rd)}, ${reg(rs)}`; if([0,2,3].includes(fn))return `${names[fn]} ${reg(rd)}, ${reg(rt)}, ${sa}`; if(names[fn])return `${names[fn]} ${reg(rd)}, ${reg(rs)}, ${reg(rt)}`;
  }
  if(op===2||op===3){const target=(((pc+4)&0xf0000000)|((word&0x03ffffff)<<2))>>>0;return `${op===3?"jal":"j"} ${hex(target)}`;}
  if(op===4||op===5)return `${op===4?"beq":"bne"} ${reg(rs)}, ${reg(rt)}, ${hex((pc+4+(imm<<2))>>>0)}`;
  const immed={0x08:"addi",0x09:"addiu",0x0c:"andi",0x0d:"ori",0x0e:"xori"}; if(immed[op])return `${immed[op]} ${reg(rt)}, ${reg(rs)}, ${op>=0x0c?`0x${uimm.toString(16)}`:imm}`; if(op===0x0f)return `lui ${reg(rt)}, 0x${uimm.toString(16)}`;
  const mem={0x1a:"ldl",0x1b:"ldr",0x1e:"lq",0x20:"lb",0x21:"lh",0x22:"lwl",0x23:"lw",0x24:"lbu",0x25:"lhu",0x26:"lwr",0x27:"lwu",0x28:"sb",0x29:"sh",0x2b:"sw",0x31:"lwc1",0x32:"lwc2",0x35:"ldc1",0x36:"lqc2",0x37:"ld",0x39:"swc1",0x3e:"sq"};
  if(mem[op])return `${mem[op]} ${op===0x31||op===0x35||op===0x39?freg(rt):reg(rt)}, ${imm}(${reg(rs)})`;
  if(op===0x11){const fmt=rs;if(fmt===0)return `mfc1 ${reg(rt)}, ${freg(rd)}`;if(fmt===4)return `mtc1 ${reg(rt)}, ${freg(rd)}`;const ft=rt,fs=rd,fd=sa,suffix=fmt===0x10?".s":fmt===0x11?".d":` fmt=${fmt}`,name={0:"add",1:"sub",2:"mul",3:"div",4:"sqrt",5:"abs",6:"mov",7:"neg",0x24:"cvt.w"}[fn];if(name)return `${name}${suffix} ${freg(fd)}, ${freg(fs)}${[0,1,2,3].includes(fn)?`, ${freg(ft)}`:""}`;}
  return `.word ${hex(word)}`;
}
