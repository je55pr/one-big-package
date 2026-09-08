import { mkdir, writeFile } from "node:fs/promises";
import { basename, resolve } from "node:path";
import { LocalFileRandomAccessReader } from "./local-random-access.mjs";
import { openPs2Disc, readPs2BootProgramHeaders } from "../.build/packages/ps2-disc/src/index.js";
import { ELF_PROGRAM_TYPE_LOAD } from "../.build/packages/elf32/src/index.js";

const isoPath=resolve(process.argv[2]??"");
const outDir=resolve(process.argv[3]??"research/generated");
if(!process.argv[2]) throw new Error("Usage: node tools/rac1-moby-string-xrefs.mjs <rac1-iso> [out-dir]");
const MATCH=/(moby|oclass|spawn|matrix|transform|position|rotation|joint|anim|instance)/i;
const reader=await LocalFileRandomAccessReader.open(isoPath,basename(isoPath));
try{
 const disc=await openPs2Disc(reader); if(disc.boot.serial!=="SCUS-97199")throw new Error(`Expected SCUS-97199, got ${disc.boot.serial??"unknown"}`);
 const ph=await readPs2BootProgramHeaders(disc); const segs=[];
 for(const seg of ph.filter(p=>p.type===ELF_PROGRAM_TYPE_LOAD&&p.fileSize>0)){
   const bytes=await disc.bootExecutable.read(seg.offset,seg.fileSize); segs.push({seg,bytes,view:new DataView(bytes.buffer,bytes.byteOffset,bytes.byteLength)});
 }
 const strings=[];
 for(const {seg,bytes} of segs){
   for(let i=0;i<bytes.length;){
     if(bytes[i]<0x20||bytes[i]>0x7e){i++;continue;} const start=i; while(i<bytes.length&&bytes[i]>=0x20&&bytes[i]<=0x7e)i++; const len=i-start;
     if(len>=4&&i<bytes.length&&bytes[i]===0){const text=Buffer.from(bytes.subarray(start,i)).toString("ascii");if(MATCH.test(text))strings.push({address:(seg.virtualAddress+start)>>>0,text});}
   }
 }
 for(const s of strings){
   const refs=[]; const hi=(s.address>>>16)&0xffff, adjustedHi=((s.address+0x8000)>>>16)&0xffff;
   for(const {seg,view,bytes} of segs){
     for(let at=0;at+8<=bytes.length;at+=4){
       const w0=view.getUint32(at,true),op0=w0>>>26,rt0=(w0>>>16)&31,imm0=w0&0xffff;
       if(op0!==0x0f||(imm0!==hi&&imm0!==adjustedHi))continue;
       for(let delta=4;delta<=0x30&&at+delta+4<=bytes.length;delta+=4){
         const w=view.getUint32(at+delta,true),op=w>>>26,rs=(w>>>21)&31,rt=(w>>>16)&31,imm=(w&0xffff),sim=(imm<<16)>>16;
         if(rs!==rt0)continue;
         let address=null;
         if(op===0x09&&rt!==0) address=(((imm0<<16)+sim)>>>0);
         else if(op===0x0d&&rt!==0) address=(((imm0<<16)|imm)>>>0);
         if(address===s.address){refs.push({pc:(seg.virtualAddress+at)>>>0,consumerPc:(seg.virtualAddress+at+delta)>>>0,baseReg:rt0,delta});break;}
       }
     }
   }
   s.xrefs=refs;
 }
 const report={generator:"tools/rac1-moby-string-xrefs.mjs",serial:disc.boot.serial,strings:strings.map(s=>({address:hex(s.address),text:s.text,xrefs:s.xrefs.map(r=>({pc:hex(r.pc),consumerPc:hex(r.consumerPc),baseReg:r.baseReg,delta:r.delta}))}))};
 await mkdir(outDir,{recursive:true}); const path=resolve(outDir,"rac1-local.moby-string-xrefs.json"); await writeFile(path,JSON.stringify(report,null,2)+"\n");
 console.log(`MOBY_STRING_PROBE matches=${strings.length} xrefs=${strings.reduce((n,s)=>n+s.xrefs.length,0)}`);
 for(const s of strings)console.log(`STRING addr=${hex(s.address)} xrefs=${s.xrefs.map(r=>hex(r.pc)).join(",")||"none"} text=${JSON.stringify(s.text)}`);
 console.log(`wrote ${path}`);
}finally{await reader.close();}

// Trace the functions containing the retained Moby profiler/debug labels,
// then dump the exact draw/skin routines reached beneath those labels.
await import("./rac1-moby-xref-disassembly.mjs");
await import("./rac1-moby-runtime-functions.mjs");
// Rank retail writers of the independently recovered 0x100 runtime Moby layout.
await import("./rac1-moby-runtime-writers.mjs");
// Validate the candidate packed-instance transform against every authority level.
await import("./rac1-moby-transform-validation.mjs");

function hex(v){return`0x${(v>>>0).toString(16).padStart(8,"0")}`;}
