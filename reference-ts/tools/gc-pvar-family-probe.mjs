import { hashRandomAccessReaderSha256 } from "../.build/packages/hashing/src/index.js";
import { Iso9660Filesystem } from "../.build/packages/iso9660/src/index.js";
import { openPs2Disc } from "../.build/packages/ps2-disc/src/index.js";
import { parseGcGameplayInstances } from "../.build/packages/gc-instances/src/index.js";
import { readGcLevelWadHeader, openGcLevelWadLump } from "../.build/packages/gc-level-wad/src/index.js";
import { parseGcGameplayMobyPvars } from "../.build/packages/gc-pvars/src/index.js";
import { readWadLz } from "../.build/packages/wad-lz/src/index.js";
import { LocalFileRandomAccessReader } from "./local-random-access.mjs";
import { mkdir, writeFile } from "node:fs/promises";
import { basename, dirname, resolve } from "node:path";

const EXPECTED_SIZE = 3_828_350_976;
const EXPECTED_SHA256 = "9db2e33e276133cc283647fa3279b37911955e123d6199d10065547eaa9b1ce5";
const EXPECTED_SERIAL = "SCUS-97268";
const LEVEL_COUNT = 27;
const DEFAULT_CLASSES = [500, 501, 505, 511, 512, 4018, 4021];
const CRATE_FAMILY = [500, 501, 505, 511, 512];
const CRATE_FOCUS_OFFSETS = [0xc8, 0xcc];
const DETAILED_SMALL_CLASSES = [4018, 4021];
const FLOAT_CANDIDATES = [0x00, 0x04, 0x08, 0x0c, 0x34];
const HEADER_SIZE = 0x20;
const MAX_REPORTED_VALUES = 16;

const args = process.argv.slice(2);
const positional = [];
const options = {
  out: undefined,
  verifyHash: false,
  summaryJson: false,
  classes: DEFAULT_CLASSES,
};
for (let i = 0; i < args.length; i++) {
  const arg = args[i];
  if (arg === "--verify-hash") options.verifyHash = true;
  else if (arg === "--summary-json") options.summaryJson = true;
  else if (arg === "--out") options.out = args[++i];
  else if (arg === "--classes") options.classes = parseClasses(args[++i]);
  else if (arg.startsWith("--")) throw new Error(`Unknown flag: ${arg}`);
  else positional.push(arg);
}
if (positional.length !== 1) {
  console.error("Usage: node tools/gc-pvar-family-probe.mjs <gc-iso> [--verify-hash] [--out <json>] [--summary-json] [--classes 500,501,...]");
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

  const fs = await Iso9660Filesystem.open(reader);
  const stats = new Map(targetClasses.map((oClass) => [oClass, newClassStats(oClass)]));

  for (let levelNumber = 0; levelNumber < LEVEL_COUNT; levelNumber++) {
    const wadPath = `/G/LEVEL${levelNumber}.WAD`;
    const wad = await fs.openFile(wadPath);
    if (!wad) throw new Error(`Missing authority level ${wadPath}.`);
    const header = await readGcLevelWadHeader(wad);
    const gameplay = openGcLevelWadLump(wad, header, 2);
    if (!gameplay) throw new Error(`${wadPath} has no gameplay lump in slot 2.`);
    const { data } = await readWadLz(gameplay, 0, { maxOutputBytes: 64 * 1024 * 1024 });
    const parsed = parseGcGameplayMobyPvars(data);
    const instances = parseGcGameplayInstances(data).mobyInstances;
    const instanceByIndex = new Map(instances.map((instance) => [instance.index, instance]));
    const fixupsByPvar = indexFixups(parsed.pvarRelativePointers);

    for (const moby of parsed.mobies) {
      if (!targetSet.has(moby.oClass)) continue;
      const classStats = stats.get(moby.oClass);
      classStats.instanceCount += 1;
      classStats.levels.add(levelNumber);
      classStats.modeBits.add(moby.modeBits >>> 0);
      if ((moby.modeBits & 0x20) !== 0) classStats.modeBit0x20Count += 1;
      if (!moby.pvar) {
        classStats.noPvarCount += 1;
        continue;
      }

      classStats.pvarCount += 1;
      classStats.pvarSizes.add(moby.pvar.size);
      const pvar = data.subarray(moby.pvar.dataOffset, moby.pvar.dataOffset + moby.pvar.size);
      const view = new DataView(pvar.buffer, pvar.byteOffset, pvar.byteLength);
      const wordCount = Math.floor(pvar.byteLength / 4);
      ensureWordStats(classStats, wordCount);
      for (let word = 0; word < wordCount; word++) {
        const value = view.getUint32(word * 4, true);
        addWordValue(classStats.wordStats[word], value);
      }

      for (const fixupOffset of fixupsByPvar.get(moby.pvarIndex) ?? []) {
        classStats.relativePointerOffsets.add(fixupOffset);
      }

      if (CRATE_FAMILY.includes(moby.oClass)) {
        let perLevel = classStats.focusByLevel.get(levelNumber);
        if (!perLevel) {
          perLevel = new Map(CRATE_FOCUS_OFFSETS.map((offset) => [offset, newWordStats()]));
          classStats.focusByLevel.set(levelNumber, perLevel);
        }
        for (const offset of CRATE_FOCUS_OFFSETS) {
          if (offset + 4 <= pvar.byteLength) addWordValue(perLevel.get(offset), view.getUint32(offset, true));
        }
      }

      const world = instanceByIndex.get(moby.index);
      if (classStats.positions.length < 32 && classStats.instanceCount <= 32 && world) {
        classStats.positions.push({
          level: levelNumber,
          mobyIndex: moby.index,
          position: world.position.map(round6),
          scale: round6(world.scale),
        });
      }

      if (DETAILED_SMALL_CLASSES.includes(moby.oClass) && world && classStats.focusInstances.length < 32) {
        classStats.focusInstances.push({
          level: levelNumber,
          mobyIndex: moby.index,
          position: world.position.map(round6),
          scale: round6(world.scale),
          dwords: Array.from({ length: wordCount }, (_, word) => ({ offset: hexOffset(word * 4), value: hex32(view.getUint32(word * 4, true)) })),
          floatCandidates: FLOAT_CANDIDATES
            .filter((offset) => offset + 4 <= pvar.byteLength)
            .map((offset) => ({ offset: hexOffset(offset), value: round6(view.getFloat32(offset, true)) })),
        });
      }
    }
  }

  const classReports = targetClasses.map((oClass) => finalizeClass(stats.get(oClass)));
  const report = {
    generator: "tools/gc-pvar-family-probe.mjs",
    generatorCommit: process.env.CI_COMMIT_SHA ?? null,
    authority: {
      buildId: "rac2-ntscu-v1.01",
      serial: EXPECTED_SERIAL,
      isoFileName: isoName,
      sizeBytes: reader.size,
      expectedSha256: EXPECTED_SHA256,
      sha256VerifiedThisRun: options.verifyHash,
      sha256,
    },
    targetClasses,
    classes: classReports,
    crateFamily: compareFamily(classReports.filter((row) => CRATE_FAMILY.includes(row.oClass))),
  };

  if (options.out) {
    const output = resolve(options.out);
    await mkdir(dirname(output), { recursive: true });
    await writeFile(output, JSON.stringify(report, null, 2) + "\n");
    console.log(`GC_PVAR_FAMILY_REPORT=${output}`);
  }

  for (const row of classReports) {
    console.log(
      `GC_PVAR_FAMILY class=${row.oClass} instances=${row.instanceCount} pvars=${row.pvarCount} sizes=${row.pvarSizes.join(",") || "none"} levels=${row.levelsObserved.join(",") || "none"} mode20=${row.modeBit0x20Count} relPtrs=${row.relativePointerOffsets.join(",") || "none"}`,
    );
    if (CRATE_FAMILY.includes(row.oClass)) {
      const c8 = row.focusDwords.find((word) => word.offsetBytes === 0xc8);
      const cc = row.focusDwords.find((word) => word.offsetBytes === 0xcc);
      console.log(`GC_PVAR_CRATE_FIELDS class=${row.oClass} c8=${compactWord(c8)} cc=${compactWord(cc)}`);
    }
    if (DETAILED_SMALL_CLASSES.includes(row.oClass)) {
      console.log(`GC_PVAR_SMALL_PROFILE class=${row.oClass} header=${JSON.stringify(row.headerDwords)}`);
      for (const instance of row.focusInstances) console.log(`GC_PVAR_SMALL_INSTANCE class=${row.oClass} data=${JSON.stringify(instance)}`);
    }
  }
  console.log(
    `GC_PVAR_CRATE_FAMILY sharedSize=${report.crateFamily.sharedPvarSizeBytes ?? "none"} sharedMode20=${report.crateFamily.allMode0x20} relPtrs=${report.crateFamily.commonRelativePointerOffsets.join(",") || "none"} commonConstantWords=${report.crateFamily.commonConstantDwords.length} classConstantWords=${report.crateFamily.classDiscriminatingConstantDwords.length}`,
  );

  if (options.summaryJson) {
    const summary = {
      authority: report.authority,
      classes: classReports.map((row) => ({
        oClass: row.oClass,
        instanceCount: row.instanceCount,
        pvarCount: row.pvarCount,
        pvarSizes: row.pvarSizes,
        modeBits: row.modeBits,
        modeBit0x20Count: row.modeBit0x20Count,
        levelsObserved: row.levelsObserved,
        relativePointerOffsets: row.relativePointerOffsets,
        headerDwords: row.headerDwords,
        variableDwordOffsets: row.variableDwordOffsets,
        focusDwords: row.focusDwords,
        focusDwordsByLevel: row.focusDwordsByLevel,
        positions: DETAILED_SMALL_CLASSES.includes(row.oClass) ? row.positions : [],
        focusInstances: DETAILED_SMALL_CLASSES.includes(row.oClass) ? row.focusInstances : [],
        dwordProfile: DETAILED_SMALL_CLASSES.includes(row.oClass) ? row.dwordProfile : [],
      })),
      crateFamily: report.crateFamily,
    };
    console.log("GC_PVAR_SUMMARY_JSON_BEGIN");
    console.log(JSON.stringify(summary));
    console.log("GC_PVAR_SUMMARY_JSON_END");
  }
} finally {
  await reader.close();
}

function parseClasses(raw) {
  if (!raw) throw new Error("--classes requires a comma-separated class list.");
  const values = raw.split(",").map((part) => Number(part.trim()));
  if (values.some((value) => !Number.isInteger(value) || value < 0 || value > 0x7fffffff)) {
    throw new Error(`Invalid --classes value: ${raw}`);
  }
  return values;
}

function newClassStats(oClass) {
  return {
    oClass,
    instanceCount: 0,
    pvarCount: 0,
    noPvarCount: 0,
    modeBit0x20Count: 0,
    levels: new Set(),
    modeBits: new Set(),
    pvarSizes: new Set(),
    relativePointerOffsets: new Set(),
    wordStats: [],
    positions: [],
    focusByLevel: new Map(),
    focusInstances: [],
  };
}

function newWordStats() {
  return { count: 0, zeroCount: 0, min: 0xffffffff, max: 0, values: new Set(), truncated: false };
}

function ensureWordStats(classStats, count) {
  while (classStats.wordStats.length < count) classStats.wordStats.push(newWordStats());
}

function addWordValue(stats, value) {
  stats.count += 1;
  if (value === 0) stats.zeroCount += 1;
  if (value < stats.min) stats.min = value;
  if (value > stats.max) stats.max = value;
  if (!stats.truncated) {
    stats.values.add(value >>> 0);
    if (stats.values.size > MAX_REPORTED_VALUES) {
      stats.values.clear();
      stats.truncated = true;
    }
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
    levelsObserved: [...stats.levels].sort((a, b) => a - b),
    relativePointerOffsets: [...stats.relativePointerOffsets].sort((a, b) => a - b).map(hexOffset),
    headerDwords: dwordProfile.filter((word) => word.offsetBytes < HEADER_SIZE),
    variableDwordOffsets: dwordProfile.filter((word) => word.distinctCount > 1 || typeof word.distinctCount === "string").map((word) => word.offset),
    focusDwords: dwordProfile.filter((word) => CRATE_FOCUS_OFFSETS.includes(word.offsetBytes)),
    focusDwordsByLevel: [...stats.focusByLevel.entries()]
      .sort(([a], [b]) => a - b)
      .map(([level, words]) => ({
        level,
        words: CRATE_FOCUS_OFFSETS.map((offset) => finalizeWord(words.get(offset), offset)),
      })),
    dwordProfile,
    positions: stats.positions,
    focusInstances: stats.focusInstances,
  };
}

function finalizeWord(word, offsetBytes) {
  const values = word.truncated ? [] : [...word.values].sort((a, b) => a - b);
  return {
    offset: hexOffset(offsetBytes),
    offsetBytes,
    sampleCount: word.count,
    zeroCount: word.zeroCount,
    distinctCount: word.truncated ? `>${MAX_REPORTED_VALUES}` : values.length,
    min: word.count === 0 ? null : hex32(word.min),
    max: word.count === 0 ? null : hex32(word.max),
    values: word.truncated ? [] : values.map(hex32),
    valuesTruncated: word.truncated,
  };
}

function compareFamily(rows) {
  const populated = rows.filter((row) => row.pvarCount > 0);
  const sizes = new Set(populated.flatMap((row) => row.pvarSizes));
  const sharedPvarSizeBytes = sizes.size === 1 ? [...sizes][0] : null;
  const allMode0x20 = populated.length > 0 && populated.every((row) => row.modeBit0x20Count === row.instanceCount && row.instanceCount > 0);
  const relativeSets = populated.map((row) => new Set(row.relativePointerOffsets));
  const commonRelativePointerOffsets = relativeSets.length === 0
    ? []
    : [...relativeSets[0]].filter((offset) => relativeSets.every((set) => set.has(offset))).sort();

  const wordCount = sharedPvarSizeBytes === null ? 0 : Math.floor(sharedPvarSizeBytes / 4);
  const commonConstantDwords = [];
  const classDiscriminatingConstantDwords = [];
  const anyVariableDwordOffsets = [];
  for (let word = 0; word < wordCount; word++) {
    const profiles = populated.map((row) => row.dwordProfile[word]);
    if (profiles.some((profile) => !profile)) continue;
    const allConstant = profiles.every((profile) => profile.distinctCount === 1 && profile.values.length === 1);
    if (!allConstant) {
      if (profiles.some((profile) => profile.distinctCount !== 1)) anyVariableDwordOffsets.push(hexOffset(word * 4));
      continue;
    }
    const values = profiles.map((profile) => profile.values[0]);
    const unique = new Set(values);
    if (unique.size === 1) {
      commonConstantDwords.push({ offset: hexOffset(word * 4), value: values[0] });
    } else {
      classDiscriminatingConstantDwords.push({
        offset: hexOffset(word * 4),
        valuesByClass: Object.fromEntries(populated.map((row, i) => [String(row.oClass), values[i]])),
      });
    }
  }

  return {
    classes: populated.map((row) => row.oClass),
    sharedPvarSizeBytes,
    allMode0x20,
    commonRelativePointerOffsets,
    commonConstantDwords,
    classDiscriminatingConstantDwords,
    anyVariableDwordOffsets,
  };
}

function indexFixups(fixups) {
  const out = new Map();
  for (const fixup of fixups) {
    const offsets = out.get(fixup.pvarIndex) ?? [];
    offsets.push(fixup.offset);
    out.set(fixup.pvarIndex, offsets);
  }
  return out;
}

function compactWord(word) {
  if (!word) return "missing";
  if (word.valuesTruncated) return `${word.distinctCount}[${word.min}..${word.max}]`;
  return word.values.join("|") || "none";
}

function hex32(value) {
  return `0x${(value >>> 0).toString(16).padStart(8, "0")}`;
}

function hexOffset(value) {
  return `0x${value.toString(16).padStart(2, "0")}`;
}

function round6(value) {
  return Math.round(value * 1_000_000) / 1_000_000;
}
