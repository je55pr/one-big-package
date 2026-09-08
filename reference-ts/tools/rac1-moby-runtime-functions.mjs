import { mkdir, writeFile } from "node:fs/promises";
import { basename, resolve } from "node:path";
import { LocalFileRandomAccessReader } from "./local-random-access.mjs";
import { openPs2Disc, readPs2BootProgramHeaders } from "../.build/packages/ps2-disc/src/index.js";
import { ELF_PROGRAM_TYPE_LOAD } from "../.build/packages/elf32/src/index.js";

const TARGETS = [
  { name: "moby-render-setup-a", address: 0x0020d1f0 },
  { name: "moby-render-setup-b", address: 0x0020d218 },
  { name: "moby-render-setup-c", address: 0x0020d248 },
  { name: "moby-render-setup-d", address: 0x0020d278 },
  { name: "moby-render-setup-e", address: 0x0020d330 },
  { name: "moby-render-list-prep", address: 0x00211808 },
];
const GPR=["zero","at","v0","v1","a0","a1","a2","a3","t0","t1","t2","t3","t4","t5","t6","t7","s0","s1","s2","s3","s4","s5","s6","s7","t8","t9","k0","k1","gp","sp","fp","ra"];
const MEM_OPS=new Set([0x1a,0x1b,0x1e,0x20,0x21,0x22,0x23,0x24,0x25,0x26,0x27,0x28,0x29,0x2a,0x2b,0x2e,0x31,0x32,0x35,0x36,0x37,0x39,0x3a,0x3e]);
const isoPath=resolve(process.argv[2]??"");
const outDir=resolve(process.argv[3]??"research/generated");
if(!process.argv[2]) throw new Error("Usage: node tools/rac1-moby-runtime-functions.mjs <rac1-iso> [out-dir]");

const reader=await LocalFileRandomAccessReader.open(isoPath,basename(isoPath));
try {
  const disc=await openPs2Disc(reader);
  if(disc.boot.serial!=="SCUS-97199") throw new Error(`Expected SCUS-97199, got ${disc.boot.serial??"unknown"}`);
  const ph=await readPs2BootProgramHeaders(disc);
  const segments=[];
  for(const seg of ph.filter(p=>p.type===ELF_PROGRAM_TYPE_LOAD&&p.fileSize>0&&(p.flags&1)!==0)){
    const bytes=await disc.bootExecutable.read(seg.offset,seg.fileSize);
    segments.push({seg,bytes,view:new DataView(bytes.buffer,bytes.byteOffset,bytes.byteLength)});
  }

  const reports=[];
  for(const target of TARGETS){
    const loc=findSegment(segments,target.address); if(!loc) throw new Error(`No executable segment contains ${hex(target.address)}`);
    const bounds=findFunction(loc,target.address);
    const instructions=[], memoryAccesses=[], calls=[];
    for(let pc=bounds.start;pc<bounds.end;pc+=4){
      const word=readWord(loc,pc), op=word>>>26, text=dis(word,pc);
      instructions.push({pc:hex(pc),word:hex(word),text});
      if(op===3) calls.push(jumpTarget(word,pc));
      if(MEM_OPS.has(op)) memoryAccesses.push({pc:hex(pc),base:reg((word>>>21)&31),offset:sign16(word&0xffff),op,text});
    }
    const grouped={};
    for(const m of memoryAccesses){(grouped[m.base]??=[]).push(m);}
    reports.push({name:target.name,target:hex(target.address),start:hex(bounds.start),end:hex(bounds.end),callers:findCallers(segments,bounds.start).map(hex),calls:[...new Set(calls)].sort((a,b)=>a-b).map(hex),memoryByBase:grouped,instructions});
  }

  const report={generator:"tools/rac1-moby-runtime-functions.mjs",serial:disc.boot.serial,functions:reports};
  await mkdir(outDir,{recursive:true}); const path=resolve(outDir,"rac1-local.moby-runtime-functions.json"); await writeFile(path,JSON.stringify(report,null,2)+"\n");
  console.log(`MOBY_RUNTIME_FUNCTIONS count=${reports.length}`);
  for(const f of reports){
    console.log(`RUNTIME_FUNC name=${f.name} target=${f.target} start=${f.start} end=${f.end} callers=${f.callers.join(",")||"none"} calls=${f.calls.join(",")||"none"}`);
    for(const [base,list] of Object.entries(f.memoryByBase)){
      const offsets=[...new Set(list.map(x=>x.offset))].sort((a,b)=>a-b);
      console.log(`MEM_BASE func=${f.name} base=${base} offsets=${offsets.map(o=>signedHex(o)).join(",")}`);
    }
    for(const i of f.instructions) console.log(`${i.pc} ${i.word} ${i.text}`);
  }
  console.log(`wrote ${path}`);
} finally { await reader.close(); }

function findSegment(segs,pc){return segs.find(s=>pc>=s.seg.virtualAddress&&pc<s.seg.virtualAddress+s.bytes.length);}
function readWord(loc,pc){return loc.view.getUint32(pc-loc.seg.virtualAddress,true);}
function findFunction(loc,pc){
  const base=loc.seg.virtualAddress,endSeg=base+loc.bytes.length; let start=Math.max(base,pc-0x1000)&~3;
  for(let p=pc&~3;p>=Math.max(base,pc-0x1000);p-=4){const w=readWord(loc,p);if((w>>>26)===0x09&&((w>>>21)&31)===29&&((w>>>16)&31)===29&&sign16(w&0xffff)<0){start=p;break;}}
  let end=Math.min(endSeg,pc+0x2000)&~3;
  for(let p=Math.max(pc,boundsFloor(start));p+8<=Math.min(endSeg,pc+0x2000);p+=4){const w=readWord(loc,p);if(w===0x03e00008){end=p+8;break;}}
  return {start,end};
}
function boundsFloor(v){return v&~3;}
function findCallers(segs,target){const out=[];for(const loc of segs){for(let at=0;at+4<=loc.bytes.length;at+=4){const w=loc.view.getUint32(at,true);if((w>>>26)===3){const pc=loc.seg.virtualAddress+at;if(jumpTarget(w,pc)===target)out.push(pc>>>0);}}}return out.sort((a,b)=>a-b);}
function jumpTarget(w,pc){return((((pc+4)&0xf0000000)|((w&0x03ffffff)<<2))>>>0);}
function sign16(v){return(v<<16)>>16;} function hex(v){return`0x${(v>>>0).toString(16).padStart(8,"0")}`;} function signedHex(v){return v<0?`-0x${(-v).toString(16)}`:`0x${v.toString(16)}`;} function reg(n){return`$${GPR[n]}`;} function freg(n){return`$f${n}`;}
function dis(w,pc){if(w===0)return"nop";const op=w>>>26,rs=(w>>>21)&31,rt=(w>>>16)&31,rd=(w>>>11)&31,sa=(w>>>6)&31,fn=w&63,imm=sign16(w&0xffff),ui=w&0xffff;
 if(op===0){if(fn===8)return`jr ${reg(rs)}`;if(fn===9)return`jalr ${reg(rd)}, ${reg(rs)}`;const n={0:"sll",2:"srl",3:"sra",0x10:"mfhi",0x12:"mflo",0x18:"mult",0x19:"multu",0x1a:"div",0x1b:"divu",0x20:"add",0x21:"addu",0x22:"sub",0x23:"subu",0x24:"and",0x25:"or",0x26:"xor",0x27:"nor",0x2a:"slt",0x2b:"sltu"}[fn];if([0,2,3].includes(fn))return`${n} ${reg(rd)}, ${reg(rt)}, ${sa}`;if(n)return`${n} ${reg(rd)}, ${reg(rs)}, ${reg(rt)}`;}
 if(op===2||op===3)return`${op===3?"jal":"j"} ${hex(jumpTarget(w,pc))}`;if(op===4||op===5)return`${op===4?"beq":"bne"} ${reg(rs)}, ${reg(rt)}, ${hex((pc+4+(imm<<2))>>>0)}`;if(op===6||op===7)return`${op===6?"blez":"bgtz"} ${reg(rs)}, ${hex((pc+4+(imm<<2))>>>0)}`;
 const im={8:"addi",9:"addiu",0xa:"slti",0xb:"sltiu",0xc:"andi",0xd:"ori",0xe:"xori"};if(im[op])return`${im[op]} ${reg(rt)}, ${reg(rs)}, ${op>=0xc?`0x${ui.toString(16)}`:imm}`;if(op===0xf)return`lui ${reg(rt)}, 0x${ui.toString(16)}`;
 const mem={0x1e:"lq",0x20:"lb",0x21:"lh",0x22:"lwl",0x23:"lw",0x24:"lbu",0x25:"lhu",0x26:"lwr",0x27:"lwu",0x28:"sb",0x29:"sh",0x2b:"sw",0x31:"lwc1",0x32:"lwc2",0x35:"ldc1",0x36:"lqc2",0x37:"ld",0x39:"swc1",0x3e:"sq"};if(mem[op])return`${mem[op]} ${op===0x31||op===0x35||op===0x39?freg(rt):reg(rt)}, ${imm}(${reg(rs)})`;
 if(op===0x11){const fmt=rs;if(fmt===0)return`mfc1 ${reg(rt)}, ${freg(rd)}`;if(fmt===4)return`mtc1 ${reg(rt)}, ${freg(rd)}`;const ft=rt,fs=rd,fd=sa,suffix=fmt===0x10?".s":fmt===0x11?".d":` fmt=${fmt}`,name={0:"add",1:"sub",2:"mul",3:"div",4:"sqrt",5:"abs",6:"mov",7:"neg",0x24:"cvt.w"}[fn];if(name)return`${name}${suffix} ${freg(fd)}, ${freg(fs)}${[0,1,2,3].includes(fn)?`, ${freg(ft)}`:""}`;}
 return`.word ${hex(w)}`;}
