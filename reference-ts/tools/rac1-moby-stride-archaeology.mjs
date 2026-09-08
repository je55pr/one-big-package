import { mkdir, writeFile } from "node:fs/promises";
import { basename, resolve } from "node:path";
import { LocalFileRandomAccessReader } from "./local-random-access.mjs";
import { openPs2Disc, readPs2BootProgramHeaders } from "../.build/packages/ps2-disc/src/index.js";
import { ELF_PROGRAM_TYPE_LOAD } from "../.build/packages/elf32/src/index.js";

const GPR=["zero","at","v0","v1","a0","a1","a2","a3","t0","t1","t2","t3","t4","t5","t6","t7","s0","s1","s2","s3","s4","s5","s6","s7","t8","t9","k0","k1","gp","sp","fp","ra"];
const KNOWN=new Set([0x00,0x18,0x1c,0x30,0x34,0x38,0x3c,0x40,0x44]);
const TRANSFORM=new Set([0x1c,0x30,0x34,0x38,0x3c,0x40,0x44]);
const MEM_OPS=new Set([0x1a,0x1b,0x1e,0x20,0x21,0x22,0x23,0x24,0x25,0x26,0x27,0x28,0x29,0x2a,0x2b,0x2e,0x31,0x32,0x35,0x36,0x37,0x39,0x3a,0x3e]);
const isoPath=resolve(process.argv[2]??"");
const outDir=resolve(process.argv[3]??"research/generated");
if(!process.argv[2]) throw new Error("Usage: node tools/rac1-moby-stride-archaeology.mjs <rac1-iso> [out-dir]");
const reader=await LocalFileRandomAccessReader.open(isoPath,basename(isoPath));
try{
  const disc=await openPs2Disc(reader);
  if(disc.boot.serial!=="SCUS-97199") throw new Error(`Expected SCUS-97199, got ${disc.boot.serial??"unknown"}`);
  const ph=await readPs2BootProgramHeaders(disc);
  const reports=[];
  for(const seg of ph.filter(p=>p.type===ELF_PROGRAM_TYPE_LOAD&&p.fileSize>0&&(p.flags&1)!==0)){
    const bytes=await disc.bootExecutable.read(seg.offset,seg.fileSize);
    const v=new DataView(bytes.buffer,bytes.byteOffset,bytes.byteLength);
    for(let at=0;at+4<=bytes.length;at+=4){
      const word=v.getUint32(at,true),op=word>>>26,imm=sign16(word&0xffff),rs=(word>>>21)&31,rt=(word>>>16)&31;
      if(op!==0x09||Math.abs(imm)!==0x78) continue;
      const tracked=new Set([rs,rt]);
      const accesses=[];
      const start=Math.max(0,at-0x200)&~3,end=Math.min(bytes.length,at+0x104)&~3;
      for(let p=start;p<end;p+=4){
        const w=v.getUint32(p,true),o=w>>>26,base=(w>>>21)&31,off=sign16(w&0xffff);
        if(MEM_OPS.has(o)&&tracked.has(base)) accesses.push({address:hex(seg.virtualAddress+p),base:reg(base),op:o,offset:off,known:KNOWN.has(off),transform:TRANSFORM.has(off)});
      }
      const known=[...new Set(accesses.filter(x=>x.known).map(x=>x.offset))].sort((a,b)=>a-b);
      const transform=[...new Set(accesses.filter(x=>x.transform).map(x=>x.offset))].sort((a,b)=>a-b);
      const instructions=[];
      for(let p=start;p<end;p+=4){const w=v.getUint32(p,true),addr=(seg.virtualAddress+p)>>>0;instructions.push({address:hex(addr),word:hex(w),text:dis(w,addr)});}
      const score=transform.length*5+known.length*2+(known.includes(0x18)?4:0)+(known.includes(0x1c)?4:0)+(rs===rt?3:0);
      reports.push({segmentIndex:seg.index,strideAddress:hex(seg.virtualAddress+at),sourceRegister:reg(rs),destinationRegister:reg(rt),selfAdvance:rs===rt,stride:imm,knownOffsets:known.map(h16),transformOffsets:transform.map(h16),score,accesses,instructions});
    }
  }
  reports.sort((a,b)=>b.score-a.score||b.transformOffsets.length-a.transformOffsets.length||parseInt(a.strideAddress.slice(2),16)-parseInt(b.strideAddress.slice(2),16));
  await mkdir(outDir,{recursive:true});
  const path=resolve(outDir,"rac1-local.moby-stride-loops.json");
  await writeFile(path,JSON.stringify({generator:"tools/rac1-moby-stride-archaeology.mjs",serial:disc.boot.serial,recordSize:"0x78",sites:reports},null,2)+"\n");
  console.log(`MOBY_STRIDE_PROBE sites=${reports.length} selfAdvance=${reports.filter(x=>x.selfAdvance).length}`);
  for(const [i,r] of reports.entries()) console.log(`STRIDE_SUMMARY #${i} pc=${r.strideAddress} src=${r.sourceRegister} dst=${r.destinationRegister} stride=${r.stride} score=${r.score} transform=${r.transformOffsets.join(",")||"none"} known=${r.knownOffsets.join(",")||"none"}`);
  for(const [i,r] of reports.slice(0,10).entries()){
    console.log(`STRIDE_WINDOW #${i} pc=${r.strideAddress} src=${r.sourceRegister} dst=${r.destinationRegister} score=${r.score} transform=${r.transformOffsets.join(",")||"none"}`);
    for(const ins of r.instructions) console.log(`${ins.address} ${ins.word} ${ins.text}`);
  }
  console.log(`wrote ${path}`);
}finally{await reader.close();}

// The same opt-in runner job also anchors likely runtime subsystems through
// retained retail debug/assert strings. The imported script uses the same
// <iso> [out-dir] argv contract and performs its own bounded ELF reads.
await import("./rac1-moby-string-xrefs.mjs");

function sign16(v){return(v<<16)>>16;} function hex(v){return`0x${(v>>>0).toString(16).padStart(8,"0")}`;} function h16(v){return`0x${(v&0xffff).toString(16).padStart(4,"0")}`;} function reg(n){return`$${GPR[n]}`;}
function dis(w,pc){if(w===0)return"nop";const op=w>>>26,rs=(w>>>21)&31,rt=(w>>>16)&31,rd=(w>>>11)&31,sa=(w>>>6)&31,fn=w&63,imm=sign16(w&0xffff),ui=w&0xffff;
 if(op===0){if(fn===8)return`jr ${reg(rs)}`;if(fn===9)return`jalr ${reg(rd)}, ${reg(rs)}`;const n={0:"sll",2:"srl",3:"sra",0x21:"addu",0x23:"subu",0x24:"and",0x25:"or",0x2a:"slt"}[fn];if([0,2,3].includes(fn))return`${n} ${reg(rd)}, ${reg(rt)}, ${sa}`;if(n)return`${n} ${reg(rd)}, ${reg(rs)}, ${reg(rt)}`;}
 if(op===2||op===3){const t=(((pc+4)&0xf0000000)|((w&0x03ffffff)<<2))>>>0;return`${op===3?"jal":"j"} ${hex(t)}`;} if(op===4||op===5)return`${op===4?"beq":"bne"} ${reg(rs)}, ${reg(rt)}, ${hex((pc+4+(imm<<2))>>>0)}`; if(op===9)return`addiu ${reg(rt)}, ${reg(rs)}, ${imm}`; if(op===0xf)return`lui ${reg(rt)}, 0x${ui.toString(16)}`;
 const mem={0x1e:"lq",0x20:"lb",0x21:"lh",0x23:"lw",0x24:"lbu",0x25:"lhu",0x28:"sb",0x29:"sh",0x2b:"sw",0x31:"lwc1",0x32:"lwc2",0x36:"lqc2",0x37:"ld",0x39:"swc1",0x3e:"sq"};if(mem[op])return`${mem[op]} ${op===0x31||op===0x39?`$f${rt}`:reg(rt)}, ${imm}(${reg(rs)})`;return`.word ${hex(w)}`;}
