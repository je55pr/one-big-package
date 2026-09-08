import { mkdir, writeFile } from "node:fs/promises";
import { basename, resolve } from "node:path";
import { LocalFileRandomAccessReader } from "./local-random-access.mjs";
import { openPs2Disc, readPs2BootProgramHeaders } from "../.build/packages/ps2-disc/src/index.js";
import { ELF_PROGRAM_TYPE_LOAD } from "../.build/packages/elf32/src/index.js";

const GPR=["zero","at","v0","v1","a0","a1","a2","a3","t0","t1","t2","t3","t4","t5","t6","t7","s0","s1","s2","s3","s4","s5","s6","s7","t8","t9","k0","k1","gp","sp","fp","ra"];
const STORE_OPS=new Set([0x28,0x29,0x2a,0x2b,0x2e,0x39,0x3a,0x3e]);
const MEM_OPS=new Set([0x1a,0x1b,0x1e,0x20,0x21,0x22,0x23,0x24,0x25,0x26,0x27,0x28,0x29,0x2a,0x2b,0x2e,0x31,0x32,0x35,0x36,0x37,0x39,0x3a,0x3e]);
const RUNTIME_FIELDS=new Set([0x00,0x10,0x20,0x22,0x23,0x24,0x2c,0x31,0x34,0x36,0x37,0x38,0x50,0x54,0x60,0x64,0x68,0x6c,0x72,0x73,0xc0,0xd0,0xe0]);
const MATRIX_FIELDS=new Set([0x00,0x10,0xc0,0xd0,0xe0]);
const isoPath=resolve(process.argv[2]??"");
const outDir=resolve(process.argv[3]??"research/generated");
if(!process.argv[2]) throw new Error("Usage: node tools/rac1-moby-runtime-writers.mjs <rac1-iso> [out-dir]");

const reader=await LocalFileRandomAccessReader.open(isoPath,basename(isoPath));
try{
  const disc=await openPs2Disc(reader);
  if(disc.boot.serial!=="SCUS-97199") throw new Error(`Expected SCUS-97199, got ${disc.boot.serial??"unknown"}`);
  const ph=await readPs2BootProgramHeaders(disc);
  const segments=[];
  for(const seg of ph.filter(p=>p.type===ELF_PROGRAM_TYPE_LOAD&&p.fileSize>0&&(p.flags&1)!==0)){
    const bytes=await disc.bootExecutable.read(seg.offset,seg.fileSize);
    segments.push({seg,bytes,view:new DataView(bytes.buffer,bytes.byteOffset,bytes.byteLength)});
  }

  const strideSites=[];
  for(const loc of segments){
    const {seg,bytes,view}=loc;
    for(let at=0;at+4<=bytes.length;at+=4){
      const word=view.getUint32(at,true),op=word>>>26,rs=(word>>>21)&31,rt=(word>>>16)&31,imm=sign16(word&0xffff);
      if(op!==0x09||Math.abs(imm)!==0x100) continue;
      const tracked=new Set([rs,rt]);
      const start=Math.max(0,at-0x180)&~3,end=Math.min(bytes.length,at+0x184)&~3;
      const stores=[];
      for(let p=start;p<end;p+=4){
        const w=view.getUint32(p,true),o=w>>>26,base=(w>>>21)&31,off=sign16(w&0xffff);
        if(STORE_OPS.has(o)&&tracked.has(base)&&off>=-0x100&&off<=0x1ff){
          stores.push({pc:(seg.virtualAddress+p)>>>0,base,offset:off,op:o,runtime:RUNTIME_FIELDS.has(off),matrix:MATRIX_FIELDS.has(off)});
        }
      }
      const runtime=[...new Set(stores.filter(x=>x.runtime).map(x=>x.offset))].sort((a,b)=>a-b);
      const matrix=[...new Set(stores.filter(x=>x.matrix).map(x=>x.offset))].sort((a,b)=>a-b);
      const score=runtime.length*4+matrix.length*12+(rs===rt?5:0)+stores.filter(x=>x.op===0x3e).length*2;
      strideSites.push({pc:(seg.virtualAddress+at)>>>0,source:rs,destination:rt,selfAdvance:rs===rt,stride:imm,score,runtime,matrix,stores});
    }
  }
  strideSites.sort((a,b)=>b.score-a.score||b.matrix.length-a.matrix.length||a.pc-b.pc);

  // Independent store clustering catches record addressing compiled as index<<8 + base.
  const clusters=[];
  for(const loc of segments){
    const {seg,bytes,view}=loc;
    for(let at=0;at+4<=bytes.length;at+=4){
      const word=view.getUint32(at,true),op=word>>>26,base=(word>>>21)&31,off=sign16(word&0xffff);
      if(!STORE_OPS.has(op)||!RUNTIME_FIELDS.has(off)||base===0) continue;
      const start=Math.max(0,at-0x100)&~3,end=Math.min(bytes.length,at+0x104)&~3;
      const stores=[]; let stride100=false,indexShift8=false;
      for(let p=start;p<end;p+=4){
        const w=view.getUint32(p,true),o=w>>>26,rs=(w>>>21)&31,rt=(w>>>16)&31,rd=(w>>>11)&31,sa=(w>>>6)&31,fn=w&63,imm=sign16(w&0xffff);
        if(o===0x09&&Math.abs(imm)===0x100&&(rs===base||rt===base)) stride100=true;
        if(o===0&&fn===0&&sa===8&&(rd===base||rt===base)) indexShift8=true;
        if(STORE_OPS.has(o)&&rs===base){const f=sign16(w&0xffff);if(f>=-0x100&&f<=0x1ff)stores.push({pc:(seg.virtualAddress+p)>>>0,offset:f,op:o,runtime:RUNTIME_FIELDS.has(f),matrix:MATRIX_FIELDS.has(f)});}
      }
      const runtime=[...new Set(stores.filter(x=>x.runtime).map(x=>x.offset))].sort((a,b)=>a-b);
      const matrix=[...new Set(stores.filter(x=>x.matrix).map(x=>x.offset))].sort((a,b)=>a-b);
      const score=runtime.length*4+matrix.length*12+(stride100?10:0)+(indexShift8?8:0)+stores.filter(x=>x.op===0x3e).length*2;
      if(runtime.length>=2||matrix.length>=2||stride100||indexShift8) clusters.push({pc:(seg.virtualAddress+at)>>>0,base,score,stride100,indexShift8,runtime,matrix,stores,startPc:(seg.virtualAddress+start)>>>0,endPc:(seg.virtualAddress+end)>>>0});
    }
  }
  // Collapse many stores from the same local cluster/base to one representative.
  clusters.sort((a,b)=>b.score-a.score||a.pc-b.pc);
  const unique=[];
  for(const c of clusters){if(unique.some(u=>u.base===c.base&&Math.abs(u.pc-c.pc)<0x100))continue;unique.push(c);}

  await mkdir(outDir,{recursive:true});
  const path=resolve(outDir,"rac1-local.moby-runtime-writers.json");
  const report={generator:"tools/rac1-moby-runtime-writers.mjs",serial:disc.boot.serial,runtimeRecordSize:"0x100",strideSites:strideSites.map(serialise),writerClusters:unique.map(serialise)};
  await writeFile(path,JSON.stringify(report,null,2)+"\n");
  console.log(`MOBY_RUNTIME_WRITER_PROBE strideSites=${strideSites.length} selfAdvance=${strideSites.filter(x=>x.selfAdvance).length} clusters=${unique.length}`);
  for(const [i,s] of strideSites.slice(0,20).entries())console.log(`RUNTIME_STRIDE #${i} pc=${hex(s.pc)} src=${reg(s.source)} dst=${reg(s.destination)} self=${s.selfAdvance} score=${s.score} matrix=${fields(s.matrix)} runtime=${fields(s.runtime)}`);
  for(const [i,c] of unique.slice(0,20).entries())console.log(`RUNTIME_WRITER #${i} pc=${hex(c.pc)} base=${reg(c.base)} score=${c.score} stride100=${c.stride100} shift8=${c.indexShift8} matrix=${fields(c.matrix)} runtime=${fields(c.runtime)}`);
  for(const [i,c] of unique.slice(0,10).entries()){
    console.log(`RUNTIME_WRITER_WINDOW #${i} pc=${hex(c.pc)} base=${reg(c.base)} score=${c.score}`);
    const loc=findSegment(segments,c.pc); if(!loc)continue;
    const lo=Math.max(loc.seg.virtualAddress,c.startPc),hi=Math.min(loc.seg.virtualAddress+loc.bytes.length,c.endPc);
    for(let pc=lo;pc<hi;pc+=4){const w=readWord(loc,pc);console.log(`${hex(pc)} ${hex(w)} ${dis(w,pc)}`);}
  }
  console.log(`wrote ${path}`);
}finally{await reader.close();}

function serialise(x){return{...x,pc:hex(x.pc),source:x.source===undefined?undefined:reg(x.source),destination:x.destination===undefined?undefined:reg(x.destination),base:x.base===undefined?undefined:reg(x.base),startPc:x.startPc===undefined?undefined:hex(x.startPc),endPc:x.endPc===undefined?undefined:hex(x.endPc),runtime:x.runtime?.map(h16),matrix:x.matrix?.map(h16),stores:x.stores?.map(s=>({...s,pc:hex(s.pc),base:s.base===undefined?undefined:reg(s.base),offset:h16(s.offset)}))};}
function findSegment(segs,pc){return segs.find(s=>pc>=s.seg.virtualAddress&&pc<s.seg.virtualAddress+s.bytes.length);}
function readWord(loc,pc){return loc.view.getUint32(pc-loc.seg.virtualAddress,true);}
function sign16(v){return(v<<16)>>16;}function hex(v){return`0x${(v>>>0).toString(16).padStart(8,"0")}`;}function h16(v){return v<0?`-0x${(-v).toString(16)}`:`0x${v.toString(16).padStart(2,"0")}`;}function fields(v){return v.length?v.map(h16).join(","):"none";}function reg(n){return`$${GPR[n]}`;}
function dis(w,pc){if(w===0)return"nop";const op=w>>>26,rs=(w>>>21)&31,rt=(w>>>16)&31,rd=(w>>>11)&31,sa=(w>>>6)&31,fn=w&63,imm=sign16(w&0xffff),ui=w&0xffff;
 if(op===0){if(fn===8)return`jr ${reg(rs)}`;if(fn===9)return`jalr ${reg(rd)}, ${reg(rs)}`;const n={0:"sll",2:"srl",3:"sra",0x20:"add",0x21:"addu",0x22:"sub",0x23:"subu",0x24:"and",0x25:"or",0x2a:"slt"}[fn];if([0,2,3].includes(fn))return`${n} ${reg(rd)}, ${reg(rt)}, ${sa}`;if(n)return`${n} ${reg(rd)}, ${reg(rs)}, ${reg(rt)}`;}
 if(op===2||op===3){const t=(((pc+4)&0xf0000000)|((w&0x03ffffff)<<2))>>>0;return`${op===3?"jal":"j"} ${hex(t)}`;}if(op===4||op===5)return`${op===4?"beq":"bne"} ${reg(rs)}, ${reg(rt)}, ${hex((pc+4+(imm<<2))>>>0)}`;if(op===6||op===7)return`${op===6?"blez":"bgtz"} ${reg(rs)}, ${hex((pc+4+(imm<<2))>>>0)}`;
 const im={8:"addi",9:"addiu",0xa:"slti",0xb:"sltiu",0xc:"andi",0xd:"ori",0xe:"xori"};if(im[op])return`${im[op]} ${reg(rt)}, ${reg(rs)}, ${op>=0xc?`0x${ui.toString(16)}`:imm}`;if(op===0xf)return`lui ${reg(rt)}, 0x${ui.toString(16)}`;
 const mem={0x1e:"lq",0x20:"lb",0x21:"lh",0x23:"lw",0x24:"lbu",0x25:"lhu",0x28:"sb",0x29:"sh",0x2b:"sw",0x31:"lwc1",0x32:"lwc2",0x36:"lqc2",0x37:"ld",0x39:"swc1",0x3a:"swc2",0x3e:"sq"};if(mem[op])return`${mem[op]} ${op===0x31||op===0x39?`$f${rt}`:reg(rt)}, ${imm}(${reg(rs)})`;return`.word ${hex(w)}`;}
