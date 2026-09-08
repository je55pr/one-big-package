import { hashRandomAccessReaderSha256 } from "../.build/packages/hashing/src/index.js";
import { SubRangeReader } from "../.build/packages/importer-common/src/index.js";
import { openPs2Disc } from "../.build/packages/ps2-disc/src/index.js";
import { openGcLevelCore } from "../.build/packages/gc-level-core/src/index.js";
import { parseGcGameplayMobyPvars } from "../.build/packages/gc-pvars/src/index.js";
import { classifyUyaLevelOverlayAddressPublicLead, parseUyaLevelOverlayPublicLead } from "../.build/packages/uya-level-overlay/src/index.js";
import { probeUyaDiscToc } from "../.build/packages/uya-disc-toc/src/index.js";
import { openUyaTocPayloadPublicLead, readUyaLevelWadHeaderPublicLead } from "../.build/packages/uya-level-wad/src/index.js";
import { readWadLz } from "../.build/packages/wad-lz/src/index.js";
import { LocalFileRandomAccessReader } from "./local-random-access.mjs";
import { mkdir, writeFile } from "node:fs/promises";
import { basename, dirname, resolve } from "node:path";

const EXPECTED_SIZE = 4_379_377_664;
const EXPECTED_SHA256 = "d2bb15c7c5b2205db868713fc0362c2b10e87751ca5bcc4e96c1e244a8c42444";
const EXPECTED_SERIAL = "SCUS-97353";
const MAX_OVERLAY_BYTES = 16 * 1024 * 1024;
const MAX_DUMP_BYTES = 4096;
const RUNTIME_FAMILY_CLASSES = new Set([500, 501, 502, 505, 511]);
const SECTION_NAMES = [".lit", ".bss", ".data", "lvl.vtbl", "lvl.camvtbl", "lvl.sndvtbl", ".text"];
const REG = ["zero","at","v0","v1","a0","a1","a2","a3","t0","t1","t2","t3","t4","t5","t6","t7","s0","s1","s2","s3","s4","s5","s6","s7","t8","t9","k0","k1","gp","sp","fp","ra"];

const args = process.argv.slice(2);
const positional = [];
let rowsRaw = "all";
let classesRaw = "500";
let dumpRow = 1;
let dumpBytes = 1024;
let verifyHash = false;
let outPath;
for (let i = 0; i < args.length; i++) {
  const arg = args[i];
  if (arg === "--verify-hash") verifyHash = true;
  else if (arg === "--out") outPath = args[++i];
  else if (arg === "--rows") rowsRaw = args[++i];
  else if (arg === "--classes") classesRaw = args[++i];
  else if (arg === "--dump-row") dumpRow = Number(args[++i]);
  else if (arg === "--dump-bytes") dumpBytes = Number(args[++i]);
  else if (arg.startsWith("--")) throw new Error(`Unknown flag: ${arg}`);
  else positional.push(arg);
}
if (positional.length !== 1) {
  console.error("Usage: node tools/uya-level-vtbl-probe.mjs <uya-iso> [--verify-hash] [--rows all|1,8,20,50] [--classes 500] [--dump-row 1] [--dump-bytes 1024] [--out report.json]");
  process.exit(2);
}
const classes = parseClasses(classesRaw);
if (!Number.isInteger(dumpRow) || dumpRow < 0 || dumpRow > 255) throw new Error(`Invalid --dump-row ${dumpRow}.`);
if (!Number.isInteger(dumpBytes) || dumpBytes < 0 || dumpBytes > MAX_DUMP_BYTES || (dumpBytes & 3) !== 0) throw new Error(`--dump-bytes must be aligned and within 0..${MAX_DUMP_BYTES}.`);

const isoPath = resolve(positional[0]);
const reader = await LocalFileRandomAccessReader.open(isoPath, basename(isoPath));
try {
  if (reader.size !== EXPECTED_SIZE) throw new Error(`UYA authority size mismatch: ${reader.size} != ${EXPECTED_SIZE}.`);
  const disc = await openPs2Disc(reader);
  if (disc.boot.serial !== EXPECTED_SERIAL) throw new Error(`UYA authority serial mismatch: ${disc.boot.serial ?? "none"} != ${EXPECTED_SERIAL}.`);
  let sha256 = null;
  if (verifyHash) {
    const hashed = await hashRandomAccessReaderSha256(reader);
    sha256 = hashed.sha256;
    if (sha256 !== EXPECTED_SHA256) throw new Error(`UYA authority SHA-256 mismatch: ${sha256}.`);
  }

  const toc = await probeUyaDiscToc(reader);
  const rows = selectRows(toc, rowsRaw);
  if (!rows.some((row) => row.index === dumpRow) && dumpBytes > 0) throw new Error(`--dump-row ${dumpRow} is not selected.`);
  console.log(`UYA_VTBL_AUTHORITY serial=${disc.boot.serial} rows=${rows.map((row) => row.index).join(",")} classes=${classes.join(",")} hashVerified=${verifyHash}`);
  console.log("UYA_VTBL_PROVENANCE section names and 12-byte lvl.vtbl record semantics are pinned-public/GC-lineage leads being tested against exact retail UYA bytes; successful structure alone does not prove native symbol names.");

  let subsetRows = 0;
  let sevenSectionRows = 0;
  let classRecordRows = 0;
  const classUpdates = new Map(classes.map((oClass) => [oClass, new Map()]));
  const rowReports = [];
  let familyRowsWithRecords = 0;
  let familyRowsWithMultipleRecords = 0;
  let familyRowsWithSharedUpdate = 0;
  const familyUpdateViolations = [];
  for (const row of rows) {
    const mainParts = row.parts.filter((part) => part.publicFormatHint?.label === "level");
    if (mainParts.length !== 1) throw new Error(`UYA table row ${row.index} has ${mainParts.length} public main-level parts.`);
    const level = openUyaTocPayloadPublicLead(reader, mainParts[0], `uya-table-${row.index}-vtbl`);
    const outer = await readUyaLevelWadHeaderPublicLead(level);
    const primary = outer.ranges[0];
    const gameplayRange = outer.ranges[2];
    if (!primary?.present || !gameplayRange?.present) throw new Error(`UYA table row ${row.index} lacks public primary/gameplay ranges.`);
    const data = new SubRangeReader(level, primary.offsetBytes, primary.sizeBytes, `${level.name}#primary`);
    const core = await openGcLevelCore(data);
    const overlayRange = core.dataHeader.overlay;
    if (overlayRange.offset < 0 || overlayRange.size <= 0 || overlayRange.size > MAX_OVERLAY_BYTES || overlayRange.offset > data.size || overlayRange.size > data.size - overlayRange.offset) {
      throw new Error(`UYA table row ${row.index} invalid overlay range ${overlayRange.offset}+${overlayRange.size}.`);
    }
    const overlay = await data.read(overlayRange.offset, overlayRange.size);
    const overlayLead = parseUyaLevelOverlayPublicLead(overlay);
    const sections = overlayLead.sections;
    if (sections.length === 7) sevenSectionRows += 1;
    if (sections.length < 7 || !overlayLead.mobyDispatch) throw new Error(`UYA table row ${row.index} has only ${sections.length} overlay sections or no public Moby dispatch table.`);
    const vtbl = sections[3];
    const text = sections[6];
    const dispatch = overlayLead.mobyDispatch;
    const records = dispatch.records.map((record) => ({
      index: record.index,
      oClass: record.oClassGcCompatibility,
      update: record.updateAddressU32,
      aux: record.auxAddressU32,
      recordAddress: record.recordAddressU32,
    }));
    console.log(`UYA_VTBL_TERMINATOR row=${row.index} offset=${hexOff(dispatch.terminatorOffset)} update=${hex32(dispatch.terminatorUpdateAddressU32)} aux=${hex32(dispatch.terminatorAuxAddressU32)}`);

    const gameplayReader = new SubRangeReader(level, gameplayRange.offsetBytes, gameplayRange.sizeBytes, `${level.name}#gameplay`);
    const { data: gameplay } = await readWadLz(gameplayReader, 0, { maxOutputBytes: 64 * 1024 * 1024 });
    const gameplayInfo = parseGcGameplayMobyPvars(gameplay);
    const gameplayClasses = new Set(gameplayInfo.mobyClasses);
    const missingFromGameplay = records.filter((record) => !gameplayClasses.has(record.oClass)).map((record) => record.oClass);
    if (!missingFromGameplay.length) subsetRows += 1;

    console.log(`UYA_VTBL_ROW row=${row.index} nativeHint=${outer.publicLevelIdHint} gameplayClasses=${gameplayInfo.mobyClasses.length} records=${records.length} vtblSubsetOfGameplay=${missingFromGameplay.length === 0} missingFromGameplay=${missingFromGameplay.length} sections=${sections.length} vtblDest=${hex32(vtbl.destAddressU32)} vtblBytes=${vtbl.copySize} textDest=${hex32(text.destAddressU32)} textBytes=${text.copySize}`);
    console.log(`UYA_VTBL_FIELDS row=${row.index} updateKinds=${formatKinds(countKinds(records.map((record) => classifyUyaLevelOverlayAddressPublicLead(overlayLead, record.update))))} auxKinds=${formatKinds(countKinds(records.map((record) => classifyUyaLevelOverlayAddressPublicLead(overlayLead, record.aux))))}`);

    const byClass = new Map();
    for (const record of records) {
      const list = byClass.get(record.oClass) ?? [];
      list.push(record);
      byClass.set(record.oClass, list);
    }
    console.log(`UYA_VTBL_UNIQUENESS row=${row.index} duplicateClasses=${[...byClass.values()].filter((list) => list.length > 1).length}`);

    const familyRecords = records.filter((record) => RUNTIME_FAMILY_CLASSES.has(record.oClass));
    const familyUpdateAddresses = [...new Set(familyRecords.map((record) => record.update))];
    const authoredFamilyCounts = Object.fromEntries([...RUNTIME_FAMILY_CLASSES].map((oClass) => [String(oClass), 0]));
    for (const moby of gameplayInfo.mobies) {
      if (RUNTIME_FAMILY_CLASSES.has(moby.oClass)) authoredFamilyCounts[String(moby.oClass)] += 1;
    }
    if (familyRecords.length > 0) familyRowsWithRecords += 1;
    if (familyRecords.length > 1) {
      familyRowsWithMultipleRecords += 1;
      if (familyUpdateAddresses.length === 1) familyRowsWithSharedUpdate += 1;
      else familyUpdateViolations.push({ row: row.index, classes: familyRecords.map((record) => record.oClass), updates: familyUpdateAddresses.map(hex32) });
    }
    console.log(`UYA_VTBL_FAMILY row=${row.index} records=${familyRecords.map((record) => record.oClass).join(",") || "none"} updates=${familyUpdateAddresses.map(hex32).join(",") || "none"} shared=${familyRecords.length < 2 || familyUpdateAddresses.length === 1} authoredInstances=${Object.entries(authoredFamilyCounts).filter(([,count]) => count > 0).map(([oClass,count]) => `${oClass}:${count}`).join(",") || "none"}`);

    rowReports.push({
      tableIndex: row.index,
      publicNativeLevelIdHint: outer.publicLevelIdHint,
      gameplayClassCount: gameplayInfo.mobyClasses.length,
      dispatchRecordCount: records.length,
      dispatchSubsetOfGameplayClasses: missingFromGameplay.length === 0,
      sectionCount: sections.length,
      sections: sections.map((section) => ({ index: section.index, publicLabel: section.publicLabel ?? null, destAddress: hex32(section.destAddressU32), copySize: section.copySize, sectionType: hex32(section.sectionTypeU32) })),
      dispatchTerminatorOffset: hexOff(dispatch.terminatorOffset),
      updateKinds: Object.fromEntries(countKinds(records.map((record) => classifyUyaLevelOverlayAddressPublicLead(overlayLead, record.update)))),
      auxKinds: Object.fromEntries(countKinds(records.map((record) => classifyUyaLevelOverlayAddressPublicLead(overlayLead, record.aux)))),
      family: {
        records: familyRecords.map((record) => ({ oClass: record.oClass, update: hex32(record.update), aux: hex32(record.aux), recordAddress: hex32(record.recordAddress) })),
        uniqueUpdateAddresses: familyUpdateAddresses.map(hex32),
        sharedUpdateWhenMultiple: familyRecords.length < 2 || familyUpdateAddresses.length === 1,
        authoredInstanceCounts: authoredFamilyCounts,
      },
    });

    const dumpedFunctions = new Set();
    for (const oClass of classes) {
      const matches = byClass.get(oClass) ?? [];
      if (!matches.length) {
        console.log(`UYA_VTBL_CLASS row=${row.index} oClass=${oClass} present=false gameplayListed=${gameplayClasses.has(oClass)}`);
        continue;
      }
      classRecordRows += 1;
      for (const record of matches) {
        const updateKind = classifyUyaLevelOverlayAddressPublicLead(overlayLead, record.update);
        const auxKind = classifyUyaLevelOverlayAddressPublicLead(overlayLead, record.aux);
        console.log(`UYA_VTBL_CLASS row=${row.index} oClass=${oClass} present=true record=${record.index} recordAddress=${hex32(record.recordAddress)} update=${hex32(record.update)} updateKind=${updateKind} aux=${hex32(record.aux)} auxKind=${auxKind}`);
        const updateMap = classUpdates.get(oClass);
        const updateKey = `${hex32(record.update)}:${updateKind}`;
        const updateRows = updateMap.get(updateKey) ?? [];
        updateRows.push(row.index);
        updateMap.set(updateKey, updateRows);
        const dumpKey = `${row.index}:${record.update}`;
        if (row.index === dumpRow && dumpBytes > 0 && updateKind === ".text" && !dumpedFunctions.has(dumpKey)) {
          dumpedFunctions.add(dumpKey);
          dumpLoadedFunction(overlay, text, record.update, dumpBytes, oClass, record.index);
        }
      }
    }
  }

  for (const [oClass, updates] of classUpdates) {
    console.log(`UYA_VTBL_CLASS_SUMMARY oClass=${oClass} variants=${updates.size}`);
    for (const [key, variantRows] of updates) console.log(`UYA_VTBL_CLASS_VARIANT oClass=${oClass} update=${key} rows=${variantRows.join(",")}`);
  }
  console.log(`UYA_VTBL_FAMILY_SUMMARY rowsWithRecords=${familyRowsWithRecords} multiRecordRows=${familyRowsWithMultipleRecords} sharedUpdateRows=${familyRowsWithSharedUpdate} violations=${familyUpdateViolations.length}`);
  console.log(`UYA_VTBL_SUMMARY rows=${rows.length} sevenSectionRows=${sevenSectionRows} vtblSubsetOfGameplayRows=${subsetRows} selectedClassRecordRows=${classRecordRows}`);
  if (outPath) {
    const report = {
      schemaVersion: 1,
      evidenceStatus: "exact retail UYA level-overlay structure with public/GC-lineage section and dispatch-table labels; labels are not recovered native symbols",
      generator: "tools/uya-level-vtbl-probe.mjs",
      authority: { buildId: "rac3-ntscu-original", serial: EXPECTED_SERIAL, sizeBytes: reader.size, expectedSha256: EXPECTED_SHA256, sha256VerifiedThisRun: verifyHash, sha256 },
      selectedRows: rows.map((row) => row.index),
      selectedClasses: classes,
      totals: { rows: rows.length, sevenSectionRows, dispatchSubsetOfGameplayRows: subsetRows, selectedClassRecordRows: classRecordRows },
      runtimeFamily: { classes: [...RUNTIME_FAMILY_CLASSES], rowsWithRecords: familyRowsWithRecords, multiRecordRows: familyRowsWithMultipleRecords, sharedUpdateRows: familyRowsWithSharedUpdate, violations: familyUpdateViolations },
      rows: rowReports,
    };
    const output = resolve(outPath);
    await mkdir(dirname(output), { recursive: true });
    await writeFile(output, JSON.stringify(report, null, 2) + "\n");
    console.log(`UYA_VTBL_REPORT=${output}`);
  }
} finally {
  await reader.close();
}

function selectRows(toc, raw) {
  const mainRows = toc.levelRows.filter((row) => row.parts.filter((part) => part.publicFormatHint?.label === "level").length === 1);
  if (raw === "all") return mainRows;
  const wanted = new Set(parseIntegerRanges(raw));
  const selected = mainRows.filter((row) => wanted.has(row.index));
  const missing = [...wanted].filter((index) => !selected.some((row) => row.index === index));
  if (missing.length) throw new Error(`Selected UYA rows are not observed main-level rows: ${missing.join(",")}.`);
  return selected;
}
function parseIntegerRanges(raw) {
  const out = new Set();
  for (const tokenRaw of raw.split(",")) {
    const token = tokenRaw.trim();
    if (!token) continue;
    const range = /^(\d+)-(\d+)$/.exec(token);
    if (range) {
      const a = Number(range[1]), b = Number(range[2]);
      if (a > b) throw new Error(`Descending row range: ${token}.`);
      for (let n = a; n <= b; n++) out.add(n);
    } else out.add(Number(token));
  }
  const values = [...out].sort((a, b) => a - b);
  if (!values.length || values.some((n) => !Number.isInteger(n) || n < 0 || n > 255)) throw new Error(`Invalid row list: ${raw}.`);
  return values;
}
function parseClasses(raw) {
  const out = [...new Set(raw.split(",").map((value) => Number(value.trim())))];
  if (!out.length || out.some((value) => !Number.isInteger(value) || value < 0 || value > 65535)) throw new Error(`Invalid classes: ${raw}.`);
  return out;
}
function countKinds(kinds) { const result = new Map(); for (const kind of kinds) result.set(kind, (result.get(kind) ?? 0) + 1); return result; }
function formatKinds(kinds) { return [...kinds.entries()].sort(([a],[b]) => a.localeCompare(b)).map(([kind,count]) => `${kind}:${count}`).join(","); }
function dumpLoadedFunction(overlay, section, address, requestedBytes, oClass, recordIndex) {
  const relative = address - section.destAddressU32;
  const bytes = Math.min(requestedBytes, section.copySize - relative) & ~3;
  if (bytes <= 0) return;
  const slice = overlay.subarray(section.dataOffset + relative, section.dataOffset + relative + bytes);
  const view = new DataView(slice.buffer, slice.byteOffset, slice.byteLength);
  console.log(`UYA_VTBL_DUMP_BEGIN oClass=${oClass} record=${recordIndex} start=${hex32(address)} bytes=${bytes}`);
  for (let offset = 0; offset < bytes; offset += 4) {
    const pc = address + offset, word = view.getUint32(offset, true);
    console.log(`${hex32(pc)}  ${hex32(word)}  ${decode(word, pc)}`);
  }
  console.log(`UYA_VTBL_DUMP_END oClass=${oClass} start=${hex32(address)}`);
}
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
function hex32(value) { return `0x${(value >>> 0).toString(16).padStart(8,"0")}`; }
function hexOff(value) { return `0x${(value >>> 0).toString(16)}`; }
