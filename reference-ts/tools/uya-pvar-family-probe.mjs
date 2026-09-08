import { hashRandomAccessReaderSha256 } from "../.build/packages/hashing/src/index.js";
import { SubRangeReader } from "../.build/packages/importer-common/src/index.js";
import { openPs2Disc } from "../.build/packages/ps2-disc/src/index.js";
import { parseGcGameplayInstances } from "../.build/packages/gc-instances/src/index.js";
import { parseGcGameplayMobyPvars } from "../.build/packages/gc-pvars/src/index.js";
import { probeUyaDiscToc } from "../.build/packages/uya-disc-toc/src/index.js";
import { openUyaTocPayloadPublicLead, readUyaLevelWadHeaderPublicLead } from "../.build/packages/uya-level-wad/src/index.js";
import { readWadLz } from "../.build/packages/wad-lz/src/index.js";
import { LocalFileRandomAccessReader } from "./local-random-access.mjs";
import { mkdir, writeFile } from "node:fs/promises";
import { basename, dirname, resolve } from "node:path";

const EXPECTED_SIZE = 4_379_377_664;
const EXPECTED_SHA256 = "d2bb15c7c5b2205db868713fc0362c2b10e87751ca5bcc4e96c1e244a8c42444";
const EXPECTED_SERIAL = "SCUS-97353";
const DEFAULT_CLASSES = [500, 501, 502, 505, 511];
const FAMILY = [500, 501, 502, 505, 511];
const MAX_REPORTED_VALUES = 16;
const FAMILY_FOCUS_OFFSETS = [0xb0, 0xf4, 0xf8, 0x134, 0x138, 0x13c];

const args = process.argv.slice(2);
const positional = [];
let outPath;
let verifyHash = false;
let classes = DEFAULT_CLASSES;
for (let i = 0; i < args.length; i++) {
  const arg = args[i];
  if (arg === "--verify-hash") verifyHash = true;
  else if (arg === "--out") outPath = args[++i];
  else if (arg === "--classes") classes = parseClasses(args[++i]);
  else if (arg.startsWith("--")) throw new Error(`Unknown flag: ${arg}`);
  else positional.push(arg);
}
if (positional.length !== 1) {
  console.error("Usage: node tools/uya-pvar-family-probe.mjs <uya-iso> [--verify-hash] [--classes 500,501,502,505,511] [--out report.json]");
  process.exit(2);
}

const isoPath = resolve(positional[0]);
const isoName = basename(isoPath);
const targetClasses = [...new Set(classes)].sort((a, b) => a - b);
const targetSet = new Set(targetClasses);
const stats = new Map(targetClasses.map((oClass) => [oClass, newClassStats(oClass)]));
const reader = await LocalFileRandomAccessReader.open(isoPath, isoName);
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
  const rows = toc.levelRows.filter((row) => row.parts.filter((part) => part.publicFormatHint?.label === "level").length === 1);
  for (const row of rows) {
    const mainPart = row.parts.find((part) => part.publicFormatHint?.label === "level");
    const level = openUyaTocPayloadPublicLead(reader, mainPart, `uya-table-${row.index}-pvar-family`);
    const outer = await readUyaLevelWadHeaderPublicLead(level);
    const gameplayRange = outer.ranges[2];
    if (!gameplayRange?.present) throw new Error(`UYA table row ${row.index} lacks the public gameplay range.`);
    const gameplayReader = new SubRangeReader(level, gameplayRange.offsetBytes, gameplayRange.sizeBytes, `${level.name}#gameplay`);
    const { data } = await readWadLz(gameplayReader, 0, { maxOutputBytes: 64 * 1024 * 1024 });
    const parsed = parseGcGameplayMobyPvars(data);
    const instances = parseGcGameplayInstances(data).mobyInstances;
    const instanceByIndex = new Map(instances.map((instance) => [instance.index, instance]));
    const fixupsByPvar = indexFixups(parsed.pvarRelativePointers);

    for (const moby of parsed.mobies) {
      if (!targetSet.has(moby.oClass)) continue;
      const classStats = stats.get(moby.oClass);
      classStats.instanceCount += 1;
      classStats.rows.add(row.index);
      classStats.modeBits.add(moby.modeBits >>> 0);
      if ((moby.modeBits & 0x20) !== 0) classStats.modeBit0x20Count += 1;
      if (!moby.pvar) { classStats.noPvarCount += 1; continue; }
      classStats.pvarCount += 1;
      classStats.pvarSizes.add(moby.pvar.size);
      const pvar = data.subarray(moby.pvar.dataOffset, moby.pvar.dataOffset + moby.pvar.size);
      const view = new DataView(pvar.buffer, pvar.byteOffset, pvar.byteLength);
      ensureWordStats(classStats, Math.floor(pvar.byteLength / 4));
      for (let word = 0; word < classStats.wordStats.length && word * 4 + 4 <= pvar.byteLength; word++) {
        addWordValue(classStats.wordStats[word], view.getUint32(word * 4, true));
      }
      for (const fixupOffset of fixupsByPvar.get(moby.pvarIndex) ?? []) classStats.relativePointerOffsets.add(fixupOffset);
      let focus = classStats.focusByRow.get(row.index);
      if (!focus) {
        focus = new Map(FAMILY_FOCUS_OFFSETS.map((offset) => [offset, newWordStats()]));
        classStats.focusByRow.set(row.index, focus);
      }
      for (const offset of FAMILY_FOCUS_OFFSETS) {
        if (offset + 4 <= pvar.byteLength) addWordValue(focus.get(offset), view.getUint32(offset, true));
      }
      const world = instanceByIndex.get(moby.index);
      if (world && classStats.samples.length < 8) classStats.samples.push({ row: row.index, mobyIndex: moby.index, position: world.position, scale: world.scale });
    }
  }

  const classReports = targetClasses.map((oClass) => finalizeClass(stats.get(oClass)));
  const familyReport = compareFamily(classReports.filter((row) => FAMILY.includes(row.oClass)));
  const report = {
    schemaVersion: 1,
    evidenceStatus: "exact retail UYA authored PVar census using GC-compatible structural labels; field semantics remain unnamed until native UYA dataflow proves them",
    generator: "tools/uya-pvar-family-probe.mjs",
    authority: { buildId: "rac3-ntscu-original", serial: EXPECTED_SERIAL, isoFileName: isoName, sizeBytes: reader.size, expectedSha256: EXPECTED_SHA256, sha256VerifiedThisRun: verifyHash, sha256 },
    targetClasses,
    rowsObserved: rows.map((row) => row.index),
    classes: classReports,
    family: familyReport,
  };
  if (outPath) {
    const output = resolve(outPath);
    await mkdir(dirname(output), { recursive: true });
    await writeFile(output, JSON.stringify(report, null, 2) + "\n");
    console.log(`UYA_PVAR_FAMILY_REPORT=${output}`);
  }
  for (const row of classReports) {
    console.log(`UYA_PVAR_FAMILY class=${row.oClass} instances=${row.instanceCount} pvars=${row.pvarCount} sizes=${row.pvarSizes.join(",") || "none"} rows=${row.rowsObserved.join(",") || "none"} mode20=${row.modeBit0x20Count} relPtrs=${row.relativePointerOffsets.join(",") || "none"} variableWords=${row.variableDwordOffsets.join(",") || "none"}`);
  }
  console.log(`UYA_PVAR_FAMILY_SHARED classes=${familyReport.classes.join(",")} sharedSize=${familyReport.sharedPvarSizeBytes ?? "none"} allMode20=${familyReport.allMode0x20} commonRelPtrs=${familyReport.commonRelativePointerOffsets.join(",") || "none"} commonConstantWords=${familyReport.commonConstantDwords.length} classConstantWords=${familyReport.classDiscriminatingConstantDwords.length} variableWords=${familyReport.anyVariableDwordOffsets.join(",") || "none"}`);
} finally {
  await reader.close();
}

function parseClasses(raw) {
  if (!raw) throw new Error("--classes requires a comma-separated class list.");
  const values = raw.split(",").map((part) => Number(part.trim()));
  if (values.some((value) => !Number.isInteger(value) || value < 0 || value > 0x7fffffff)) throw new Error(`Invalid --classes value: ${raw}`);
  return values;
}
function newClassStats(oClass) { return { oClass, instanceCount: 0, pvarCount: 0, noPvarCount: 0, modeBit0x20Count: 0, rows: new Set(), modeBits: new Set(), pvarSizes: new Set(), relativePointerOffsets: new Set(), wordStats: [], samples: [], focusByRow: new Map() }; }
function newWordStats() { return { count: 0, zeroCount: 0, min: 0xffffffff, max: 0, values: new Set(), truncated: false }; }
function ensureWordStats(classStats, count) { while (classStats.wordStats.length < count) classStats.wordStats.push(newWordStats()); }
function addWordValue(stats, value) {
  stats.count += 1;
  if (value === 0) stats.zeroCount += 1;
  if (value < stats.min) stats.min = value;
  if (value > stats.max) stats.max = value;
  if (!stats.truncated) {
    stats.values.add(value >>> 0);
    if (stats.values.size > MAX_REPORTED_VALUES) { stats.values.clear(); stats.truncated = true; }
  }
}
function finalizeClass(stats) {
  const dwordProfile = stats.wordStats.map((word, index) => finalizeWord(word, index * 4));
  return {
    oClass: stats.oClass,
    instanceCount: stats.instanceCount,
    pvarCount: stats.pvarCount,
    noPvarCount: stats.noPvarCount,
    pvarSizes: [...stats.pvarSizes].sort((a, b) => a - b),
    modeBits: [...stats.modeBits].sort((a, b) => a - b).map(hex32),
    modeBit0x20Count: stats.modeBit0x20Count,
    rowsObserved: [...stats.rows].sort((a, b) => a - b),
    relativePointerOffsets: [...stats.relativePointerOffsets].sort((a, b) => a - b).map(hexOffset),
    variableDwordOffsets: dwordProfile.filter((word) => word.distinctCount !== 1).map((word) => word.offset),
    dwordProfile,
    focusDwordsByRow: [...stats.focusByRow.entries()].sort(([a],[b]) => a-b).map(([row, words]) => ({ row, words: FAMILY_FOCUS_OFFSETS.map((offset) => finalizeWord(words.get(offset), offset)) })),
    samples: stats.samples,
  };
}
function finalizeWord(word, offsetBytes) {
  const values = word.truncated ? [] : [...word.values].sort((a, b) => a - b);
  return { offset: hexOffset(offsetBytes), offsetBytes, sampleCount: word.count, zeroCount: word.zeroCount, distinctCount: word.truncated ? `>${MAX_REPORTED_VALUES}` : values.length, min: word.count ? hex32(word.min) : null, max: word.count ? hex32(word.max) : null, values: word.truncated ? [] : values.map(hex32), valuesTruncated: word.truncated };
}
function compareFamily(rows) {
  const populated = rows.filter((row) => row.pvarCount > 0);
  const sizes = new Set(populated.flatMap((row) => row.pvarSizes));
  const sharedPvarSizeBytes = sizes.size === 1 ? [...sizes][0] : null;
  const allMode0x20 = populated.length > 0 && populated.every((row) => row.modeBit0x20Count === row.instanceCount && row.instanceCount > 0);
  const relativeSets = populated.map((row) => new Set(row.relativePointerOffsets));
  const commonRelativePointerOffsets = relativeSets.length ? [...relativeSets[0]].filter((offset) => relativeSets.every((set) => set.has(offset))).sort() : [];
  const wordCount = sharedPvarSizeBytes === null ? 0 : Math.floor(sharedPvarSizeBytes / 4);
  const commonConstantDwords = [], classDiscriminatingConstantDwords = [], anyVariableDwordOffsets = [];
  for (let word = 0; word < wordCount; word++) {
    const profiles = populated.map((row) => row.dwordProfile[word]);
    if (profiles.some((profile) => !profile)) continue;
    const allConstant = profiles.every((profile) => profile.distinctCount === 1 && profile.values.length === 1);
    if (!allConstant) { if (profiles.some((profile) => profile.distinctCount !== 1)) anyVariableDwordOffsets.push(hexOffset(word * 4)); continue; }
    const values = profiles.map((profile) => profile.values[0]);
    const unique = new Set(values);
    if (unique.size === 1) commonConstantDwords.push({ offset: hexOffset(word * 4), value: values[0] });
    else classDiscriminatingConstantDwords.push({ offset: hexOffset(word * 4), valuesByClass: Object.fromEntries(populated.map((row, index) => [String(row.oClass), values[index]])) });
  }
  return { classes: populated.map((row) => row.oClass), sharedPvarSizeBytes, allMode0x20, commonRelativePointerOffsets, commonConstantDwords, classDiscriminatingConstantDwords, anyVariableDwordOffsets };
}
function indexFixups(fixups) { const out = new Map(); for (const fixup of fixups) { const offsets = out.get(fixup.pvarIndex) ?? []; offsets.push(fixup.offset); out.set(fixup.pvarIndex, offsets); } return out; }
function hex32(value) { return `0x${(value >>> 0).toString(16).padStart(8,"0")}`; }
function hexOffset(value) { return `0x${value.toString(16).padStart(2,"0")}`; }
