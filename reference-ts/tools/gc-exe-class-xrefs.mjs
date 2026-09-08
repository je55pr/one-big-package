import { hashRandomAccessReaderSha256 } from "../.build/packages/hashing/src/index.js";
import { openPs2Disc, readPs2BootProgramHeaders } from "../.build/packages/ps2-disc/src/index.js";
import { ELF_PROGRAM_TYPE_LOAD } from "../.build/packages/elf32/src/index.js";
import { LocalFileRandomAccessReader } from "./local-random-access.mjs";
import { basename, resolve } from "node:path";

const EXPECTED_SIZE = 3_828_350_976;
const EXPECTED_SHA256 = "9db2e33e276133cc283647fa3279b37911955e123d6199d10065547eaa9b1ce5";
const EXPECTED_SERIAL = "SCUS-97268";
const DEFAULT_CLASSES = [500, 501, 505, 511, 512];
const DEFAULT_CLUSTER_RADIUS = 0x400;
const DEFAULT_CONTEXT_RADIUS = 0x100;
const MAX_SEGMENT_BYTES = 32 * 1024 * 1024;
const MAX_TOTAL_BYTES = 64 * 1024 * 1024;
const MAX_HITS_PER_KIND = 512;
const MAX_CLUSTERS = 48;
const MAX_CONTEXT_EVENTS = 64;

const args = process.argv.slice(2);
const positional = [];
const options = {
  verifyHash: false,
  classes: DEFAULT_CLASSES,
  clusterRadius: DEFAULT_CLUSTER_RADIUS,
  contextRadius: DEFAULT_CONTEXT_RADIUS,
  json: false,
};
for (let i = 0; i < args.length; i++) {
  const arg = args[i];
  if (arg === "--verify-hash") options.verifyHash = true;
  else if (arg === "--json") options.json = true;
  else if (arg === "--classes") options.classes = parseClasses(args[++i]);
  else if (arg === "--cluster-radius") options.clusterRadius = parseBoundedInt(args[++i], 4, 0x10000, "cluster radius");
  else if (arg === "--context-radius") options.contextRadius = parseBoundedInt(args[++i], 4, 0x4000, "context radius");
  else if (arg.startsWith("--")) throw new Error(`Unknown flag: ${arg}`);
  else positional.push(arg);
}
if (positional.length !== 1) {
  console.error("Usage: node tools/gc-exe-class-xrefs.mjs <gc-iso> [--verify-hash] [--classes 500,501,...] [--cluster-radius N] [--context-radius N] [--json]");
  process.exit(2);
}

const isoPath = resolve(positional[0]);
const isoName = basename(isoPath);
const targetClasses = [...new Set(options.classes)].sort((a, b) => a - b);
const targetSet = new Set(targetClasses);
const reader = await LocalFileRandomAccessReader.open(isoPath, isoName);

try {
  if (reader.size !== EXPECTED_SIZE) throw new Error(`GC authority size mismatch: ${reader.size} != ${EXPECTED_SIZE}.`);
  const disc = await openPs2Disc(reader);
  if (disc.boot.serial !== EXPECTED_SERIAL) throw new Error(`GC authority serial mismatch: ${disc.boot.serial ?? "none"} != ${EXPECTED_SERIAL}.`);

  let sha256 = null;
  if (options.verifyHash) {
    const hashed = await hashRandomAccessReaderSha256(reader);
    sha256 = hashed.sha256;
    if (sha256 !== EXPECTED_SHA256) throw new Error(`GC authority SHA-256 mismatch: ${sha256}.`);
  }

  const phdrs = await readPs2BootProgramHeaders(disc, { maxProgramHeaders: 64 });
  const loadSegments = phdrs.filter((segment) => segment.type === ELF_PROGRAM_TYPE_LOAD && segment.fileSize > 0);
  const totalBytes = loadSegments.reduce((sum, segment) => sum + segment.fileSize, 0);
  if (totalBytes > MAX_TOTAL_BYTES) throw new Error(`GC executable PT_LOAD bytes ${totalBytes} exceed bounded scan cap ${MAX_TOTAL_BYTES}.`);
  for (const segment of loadSegments) {
    if (segment.fileSize > MAX_SEGMENT_BYTES) throw new Error(`GC executable segment ${segment.index} size ${segment.fileSize} exceeds cap ${MAX_SEGMENT_BYTES}.`);
  }

  const segmentReports = [];
  const allHits = [];
  for (const segment of loadSegments) {
    const bytes = await disc.bootExecutable.read(segment.offset, segment.fileSize);
    if (bytes.byteLength !== segment.fileSize) throw new Error(`Short executable segment read for PT_LOAD ${segment.index}.`);
    const executable = (segment.flags & 1) !== 0;
    const report = scanSegment(bytes, segment, executable, targetSet);
    segmentReports.push(report.summary);
    allHits.push(...report.hits);
  }

  const clusters = buildClusters(allHits, options.clusterRadius, targetClasses)
    .slice(0, MAX_CLUSTERS)
    .map((cluster) => enrichCluster(cluster, options.contextRadius));

  const report = {
    generator: "tools/gc-exe-class-xrefs.mjs",
    generatorCommit: process.env.CI_COMMIT_SHA ?? null,
    authority: {
      buildId: "rac2-ntscu-v1.01",
      serial: EXPECTED_SERIAL,
      executableName: disc.boot.executableName,
      executableSizeBytes: disc.boot.executableSize,
      executableEntryPoint: hex32(disc.boot.executableEntryPoint),
      isoFileName: isoName,
      isoSizeBytes: reader.size,
      expectedSha256: EXPECTED_SHA256,
      sha256VerifiedThisRun: options.verifyHash,
      sha256,
    },
    targetClasses,
    scan: {
      clusterRadius: options.clusterRadius,
      contextRadius: options.contextRadius,
      loadSegmentCount: loadSegments.length,
      totalLoadBytes: totalBytes,
      segments: segmentReports,
      hitCount: allHits.length,
      clusterCount: clusters.length,
    },
    clusters,
  };

  console.log(`GC_EXE_XREF_SUMMARY entry=${report.authority.executableEntryPoint} loadSegments=${loadSegments.length} loadBytes=${totalBytes} hits=${allHits.length} clusters=${clusters.length}`);
  for (const segment of segmentReports) {
    console.log(`GC_EXE_SEGMENT index=${segment.index} vaddr=${segment.virtualAddress} fileOffset=${segment.fileOffset} bytes=${segment.fileSize} flags=${segment.flags} executable=${segment.executable} classHits=${segment.classHitCount} dwordHits=${segment.dwordHitCount}`);
  }
  for (const cluster of clusters) {
    console.log(`GC_EXE_CLUSTER start=${cluster.startAddress} end=${cluster.endAddress} executable=${cluster.executable} classes=${cluster.classes.join(",")} classEvents=${cluster.classEvents.length} pvarC8=${cluster.pvarOffsetEvents.c8.length} pvarCC=${cluster.pvarOffsetEvents.cc.length} calls=${cluster.calls.length}`);
    for (const event of cluster.classEvents.slice(0, 24)) {
      console.log(`GC_EXE_CLASS_EVENT pc=${event.address} class=${event.oClass} kind=${event.kind} op=${event.operation}`);
    }
    for (const event of [...cluster.pvarOffsetEvents.c8, ...cluster.pvarOffsetEvents.cc].slice(0, 24)) {
      console.log(`GC_EXE_PVAR_EVENT pc=${event.address} offset=${event.offset} op=${event.operation} base=r${event.baseRegister} reg=r${event.valueRegister}`);
    }
    for (const call of cluster.calls.slice(0, 16)) console.log(`GC_EXE_CALL pc=${call.address} target=${call.target}`);
  }
  if (options.json) {
    console.log("GC_EXE_XREF_JSON_BEGIN");
    console.log(JSON.stringify(report));
    console.log("GC_EXE_XREF_JSON_END");
  }
} finally {
  await reader.close();
}

function scanSegment(bytes, segment, executable, targetSet) {
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  const hits = [];
  let classHitCount = 0;
  let dwordHitCount = 0;

  // Executable instructions: find immediate forms that materially encode a target class.
  if (executable) {
    for (let offset = 0; offset + 4 <= bytes.byteLength; offset += 4) {
      const word = view.getUint32(offset, true);
      const decoded = decodeInstruction(word, segment.virtualAddress + offset);
      if (decoded.classImmediate !== null && targetSet.has(decoded.classImmediate)) {
        if (classHitCount < MAX_HITS_PER_KIND) {
          hits.push({
            segmentIndex: segment.index,
            executable,
            address: segment.virtualAddress + offset,
            fileOffset: segment.offset + offset,
            kind: "instruction-immediate",
            oClass: decoded.classImmediate,
            operation: decoded.operation,
            word,
            decoded,
          });
        }
        classHitCount += 1;
      }
    }
  }

  // Any PT_LOAD segment may contain class-id tables. Only scan 4-byte aligned dwords
  // to keep false positives and output bounded.
  for (let offset = 0; offset + 4 <= bytes.byteLength; offset += 4) {
    const value = view.getUint32(offset, true);
    if (!targetSet.has(value)) continue;
    if (dwordHitCount < MAX_HITS_PER_KIND) {
      hits.push({
        segmentIndex: segment.index,
        executable,
        address: segment.virtualAddress + offset,
        fileOffset: segment.offset + offset,
        kind: "aligned-dword",
        oClass: value,
        operation: "data32",
        word: value,
        decoded: null,
      });
    }
    dwordHitCount += 1;
  }

  return {
    summary: {
      index: segment.index,
      virtualAddress: hex32(segment.virtualAddress),
      fileOffset: hex32(segment.offset),
      fileSize: segment.fileSize,
      flags: hex32(segment.flags),
      executable,
      classHitCount,
      dwordHitCount,
    },
    hits,
  };
}

function buildClusters(hits, radius, targetClasses) {
  const bySegment = new Map();
  for (const hit of hits) {
    const list = bySegment.get(hit.segmentIndex) ?? [];
    list.push(hit);
    bySegment.set(hit.segmentIndex, list);
  }
  const clusters = [];
  for (const [segmentIndex, segmentHits] of bySegment) {
    segmentHits.sort((a, b) => a.address - b.address || a.kind.localeCompare(b.kind));
    for (let left = 0; left < segmentHits.length; left++) {
      const anchor = segmentHits[left];
      const window = [];
      const classes = new Set();
      let right = left;
      while (right < segmentHits.length && segmentHits[right].address - anchor.address <= radius) {
        window.push(segmentHits[right]);
        classes.add(segmentHits[right].oClass);
        right += 1;
      }
      if (classes.size < 2) continue;
      const start = window[0].address;
      const end = window.at(-1).address;
      // Deduplicate overlapping windows by keeping the broadest/highest-class-count one.
      const existing = clusters.find((cluster) => cluster.segmentIndex === segmentIndex && rangesOverlap(cluster.start, cluster.end, start, end));
      const candidate = { segmentIndex, executable: window.some((hit) => hit.executable), start, end, classes: [...classes].sort((a, b) => a - b), hits: window };
      if (!existing) clusters.push(candidate);
      else if (scoreCluster(candidate, targetClasses) > scoreCluster(existing, targetClasses)) Object.assign(existing, candidate);
    }
  }
  return clusters.sort((a, b) => scoreCluster(b, targetClasses) - scoreCluster(a, targetClasses) || a.start - b.start);
}

function scoreCluster(cluster, targetClasses) {
  return cluster.classes.length * 10000 + cluster.hits.length * 10 - Math.min(9999, cluster.end - cluster.start);
}

function enrichCluster(cluster, contextRadius) {
  // We have intentionally retained only class-hit words. For nearby PVar offsets and
  // calls, rescan a compact address window from retained executable words is not enough;
  // the local probe therefore stores decoded context events as part of each instruction
  // hit's surrounding scan in a second pass below. Since segment bytes are not retained
  // in the report, collect context from the instruction words attached to hits first;
  // a subsequent targeted disassembly probe can expand promising addresses.
  //
  // To still identify coincident memory offsets/calls in this pass, use any class-hit
  // instruction whose own immediate is 0xc8/0xcc (none expected) and any JAL class hit.
  // This placeholder is deliberately explicit so it cannot be mistaken for a full xref.
  const classEvents = dedupeClassEvents(cluster.hits.map((hit) => ({
    address: hex32(hit.address),
    fileOffset: hex32(hit.fileOffset),
    oClass: hit.oClass,
    kind: hit.kind,
    operation: hit.operation,
  })));
  return {
    segmentIndex: cluster.segmentIndex,
    executable: cluster.executable,
    startAddress: hex32(cluster.start),
    endAddress: hex32(cluster.end),
    classes: cluster.classes,
    contextRadius,
    classEvents,
    pvarOffsetEvents: { c8: [], cc: [] },
    calls: [],
    note: "Class-ID cluster only. Use a targeted executable-window probe for complete nearby memory-offset and JAL context.",
  };
}

function decodeInstruction(word, pc) {
  const opcode = word >>> 26;
  const rs = (word >>> 21) & 31;
  const rt = (word >>> 16) & 31;
  const imm = word & 0xffff;
  const simm = (imm << 16) >> 16;
  let operation = `op${opcode}`;
  let classImmediate = null;

  switch (opcode) {
    case 8: operation = "addi"; classImmediate = simm >= 0 ? simm : null; break;
    case 9: operation = "addiu"; classImmediate = simm >= 0 ? simm : null; break;
    case 10: operation = "slti"; classImmediate = simm >= 0 ? simm : null; break;
    case 11: operation = "sltiu"; classImmediate = simm >= 0 ? simm : null; break;
    case 12: operation = "andi"; classImmediate = imm; break;
    case 13: operation = "ori"; classImmediate = imm; break;
    case 14: operation = "xori"; classImmediate = imm; break;
    case 15: operation = "lui"; break;
    case 35: operation = "lw"; break;
    case 32: operation = "lb"; break;
    case 36: operation = "lbu"; break;
    case 33: operation = "lh"; break;
    case 37: operation = "lhu"; break;
    case 43: operation = "sw"; break;
    case 40: operation = "sb"; break;
    case 41: operation = "sh"; break;
    case 2: operation = "j"; break;
    case 3: operation = "jal"; break;
    case 4: operation = "beq"; break;
    case 5: operation = "bne"; break;
    case 0: operation = `special${word & 63}`; break;
  }
  return { opcode, rs, rt, imm, simm, operation, classImmediate, pc };
}

function dedupeClassEvents(events) {
  const seen = new Set();
  return events.filter((event) => {
    const key = `${event.address}:${event.oClass}:${event.kind}`;
    if (seen.has(key)) return false;
    seen.add(key);
    return true;
  });
}

function parseClasses(raw) {
  if (!raw) throw new Error("--classes requires a comma-separated class list.");
  const values = raw.split(",").map((part) => Number(part.trim()));
  if (values.some((value) => !Number.isInteger(value) || value < 0 || value > 0xffff)) throw new Error(`Invalid class list: ${raw}`);
  return values;
}

function parseBoundedInt(raw, min, max, label) {
  const value = Number(raw);
  if (!Number.isInteger(value) || value < min || value > max) throw new Error(`Invalid ${label}: ${raw}`);
  return value;
}

function rangesOverlap(a0, a1, b0, b1) {
  return a0 <= b1 && b0 <= a1;
}

function hex32(value) {
  return `0x${(value >>> 0).toString(16).padStart(8, "0")}`;
}
