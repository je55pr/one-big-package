import { createHash } from "node:crypto";
import { mkdir, writeFile } from "node:fs/promises";
import { basename, dirname, resolve } from "node:path";
import { LocalFileRandomAccessReader } from "./local-random-access.mjs";
import { Iso9660Filesystem } from "../.build/packages/iso9660/src/index.js";
import { openPs2Disc } from "../.build/packages/ps2-disc/src/index.js";
import { hashRandomAccessReaderSha256 } from "../.build/packages/hashing/src/index.js";
import { readGcLevelWadHeader, openGcLevelWadLump } from "../.build/packages/gc-level-wad/src/index.js";
import { readWadLz } from "../.build/packages/wad-lz/src/index.js";
import { parseGcGameplayMobyPvars } from "../.build/packages/gc-pvars/src/index.js";

const EXPECTED_SIZE = 3_828_350_976;
const EXPECTED_SHA256 = "9db2e33e276133cc283647fa3279b37911955e123d6199d10065547eaa9b1ce5";
const EXPECTED_SERIAL = "SCUS-97268";
const LEVEL_COUNT = 27;

const args = process.argv.slice(2);
const positional = [];
const options = { out: undefined, verifyHash: false, stdoutJson: false, summaryClasses: [] };
for (let i = 0; i < args.length; i++) {
  const arg = args[i];
  if (arg === "--verify-hash") options.verifyHash = true;
  else if (arg === "--stdout-json") options.stdoutJson = true;
  else if (arg === "--out") options.out = args[++i];
  else if (arg === "--summary-classes") options.summaryClasses = parseClassList(args[++i]);
  else if (arg.startsWith("--")) throw new Error(`Unknown flag: ${arg}`);
  else positional.push(arg);
}
if (positional.length !== 1) {
  console.error("Usage: node tools/gc-moby-census.mjs <gc-iso> [--verify-hash] [--out <json>] [--stdout-json] [--summary-classes <ids>]");
  process.exit(2);
}

const isoPath = resolve(positional[0]);
const isoName = basename(isoPath);
const reader = await LocalFileRandomAccessReader.open(isoPath, isoName);

try {
  if (reader.size !== EXPECTED_SIZE) {
    throw new Error(`GC authority size mismatch: ${reader.size} != ${EXPECTED_SIZE}.`);
  }
  const disc = await openPs2Disc(reader);
  if (disc.boot.serial !== EXPECTED_SERIAL) {
    throw new Error(`GC authority serial mismatch: ${disc.boot.serial ?? "none"} != ${EXPECTED_SERIAL}.`);
  }

  let sha256 = null;
  if (options.verifyHash) {
    const hashed = await hashRandomAccessReaderSha256(reader);
    sha256 = hashed.sha256;
    if (sha256 !== EXPECTED_SHA256) throw new Error(`GC authority SHA-256 mismatch: ${sha256}.`);
  }

  const fs = await Iso9660Filesystem.open(reader);
  const levels = [];
  const aggregate = new Map();
  let totalMobyLinks = 0;
  let totalRelativePointers = 0;
  let mobyOwnedMobyLinks = 0;
  let mobyOwnedRelativePointers = 0;

  for (let levelNumber = 0; levelNumber < LEVEL_COUNT; levelNumber++) {
    const isoPathForLevel = `/G/LEVEL${levelNumber}.WAD`;
    const wad = await fs.openFile(isoPathForLevel);
    if (!wad) throw new Error(`Missing authority level ${isoPathForLevel}.`);
    const header = await readGcLevelWadHeader(wad);
    const gameplay = openGcLevelWadLump(wad, header, 2);
    if (!gameplay) throw new Error(`${isoPathForLevel} has no gameplay lump in slot 2.`);
    const { data } = await readWadLz(gameplay, 0, { maxOutputBytes: 64 * 1024 * 1024 });
    const parsed = parseGcGameplayMobyPvars(data);

    const classTable = new Set(parsed.mobyClasses);
    const pvarOwners = new Map();
    for (const moby of parsed.mobies) {
      if (moby.pvarIndex < 0) continue;
      const owners = pvarOwners.get(moby.pvarIndex) ?? [];
      owners.push(moby);
      pvarOwners.set(moby.pvarIndex, owners);
    }

    for (const fixup of parsed.pvarMobyLinks) {
      totalMobyLinks += 1;
      const owners = pvarOwners.get(fixup.pvarIndex);
      if (!owners) continue;
      mobyOwnedMobyLinks += 1;
      for (const owner of owners) validateFixup(owner, fixup, "Moby-link", levelNumber);
    }
    for (const fixup of parsed.pvarRelativePointers) {
      totalRelativePointers += 1;
      const owners = pvarOwners.get(fixup.pvarIndex);
      if (!owners) continue;
      mobyOwnedRelativePointers += 1;
      for (const owner of owners) validateFixup(owner, fixup, "relative-pointer", levelNumber);
    }

    const observedClasses = new Set();
    for (const moby of parsed.mobies) {
      observedClasses.add(moby.oClass);
      const global = aggregate.get(moby.oClass) ?? newStats(moby.oClass);
      addMoby(global, moby, data, levelNumber);
      aggregate.set(moby.oClass, global);
    }
    for (const oClass of classTable) {
      const global = aggregate.get(oClass) ?? newStats(oClass);
      global.classTableLevels.add(levelNumber);
      aggregate.set(oClass, global);
    }

    for (const fixup of parsed.pvarMobyLinks) {
      for (const owner of pvarOwners.get(fixup.pvarIndex) ?? []) {
        aggregate.get(owner.oClass)?.mobyLinkOffsets.add(fixup.offset);
      }
    }
    for (const fixup of parsed.pvarRelativePointers) {
      for (const owner of pvarOwners.get(fixup.pvarIndex) ?? []) {
        aggregate.get(owner.oClass)?.relativePointerOffsets.add(fixup.offset);
      }
    }

    levels.push({
      levelNumber,
      nativeLevelId: header.levelId,
      wadFile: `LEVEL${levelNumber}.WAD`,
      gameplayCompressedBytes: gameplay.size,
      gameplayDecompressedBytes: data.byteLength,
      mobyClassTableCount: parsed.mobyClasses.length,
      observedMobyClassCount: observedClasses.size,
      staticMobyCount: parsed.mobies.length,
      staticMobyPvarCount: parsed.mobies.filter((m) => m.pvarIndex >= 0).length,
      uniqueMobyPvarCount: parsed.mobyPvars.length,
      pvarMobyLinkFixupCount: parsed.pvarMobyLinks.length,
      pvarRelativePointerFixupCount: parsed.pvarRelativePointers.length,
      duplicateStaticMobyPvarIndices: [...pvarOwners.entries()].filter(([, owners]) => owners.length > 1).map(([index, owners]) => ({ index, mobyIndices: owners.map((m) => m.index) })),
    });
    console.log(`GC_MOBY_LEVEL level=${levelNumber} native=${header.levelId} mobies=${parsed.mobies.length} classes=${observedClasses.size} pvarMobies=${parsed.mobies.filter((m) => m.pvarIndex >= 0).length} mobyLinks=${parsed.pvarMobyLinks.length} relPtrs=${parsed.pvarRelativePointers.length}`);
  }

  const report = {
    generator: "tools/gc-moby-census.mjs",
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
    layout: {
      mobyClassBlockHeaderPointer: "0x48",
      mobyInstanceBlockHeaderPointer: "0x4c",
      mobyInstanceSize: "0x88",
      mobyOClassOffset: "0x28",
      mobyPvarIndexOffset: "0x68",
      mobyModeBitsOffset: "0x70",
      pvarMobyLinkFixupBlockHeaderPointer: "0x58",
      pvarTableBlockHeaderPointer: "0x5c",
      pvarDataBlockHeaderPointer: "0x60",
      pvarRelativePointerFixupBlockHeaderPointer: "0x64",
      pvarTableEntrySize: 8,
      pvarFixupEntrySize: 8,
    },
    summary: {
      levelCount: levels.length,
      distinctObservedMobyClasses: [...aggregate.values()].filter((s) => s.instanceCount > 0).length,
      distinctListedMobyClasses: [...aggregate.values()].filter((s) => s.classTableLevels.size > 0).length,
      staticMobyCount: levels.reduce((sum, l) => sum + l.staticMobyCount, 0),
      staticMobyPvarCount: levels.reduce((sum, l) => sum + l.staticMobyPvarCount, 0),
      pvarMobyLinkFixupCount: totalMobyLinks,
      pvarRelativePointerFixupCount: totalRelativePointers,
      mobyOwnedMobyLinkFixupCount: mobyOwnedMobyLinks,
      mobyOwnedRelativePointerFixupCount: mobyOwnedRelativePointers,
      duplicateStaticMobyPvarIndexCount: levels.reduce((sum, l) => sum + l.duplicateStaticMobyPvarIndices.length, 0),
    },
    classes: [...aggregate.values()].sort((a, b) => a.oClass - b.oClass).map((stats) => finalizeGlobal(stats)),
    levels,
  };

  if (options.out) {
    const output = resolve(options.out);
    await mkdir(dirname(output), { recursive: true });
    await writeFile(output, JSON.stringify(report, null, 2) + "\n");
    console.log(`GC_MOBY_REPORT=${output}`);
  }
  console.log(`GC_MOBY_SUMMARY levels=${report.summary.levelCount} mobies=${report.summary.staticMobyCount} observedClasses=${report.summary.distinctObservedMobyClasses} pvarMobies=${report.summary.staticMobyPvarCount} duplicatePvarIndices=${report.summary.duplicateStaticMobyPvarIndexCount}`);
  for (const oClass of options.summaryClasses) {
    const row = report.classes.find((entry) => entry.oClass === oClass);
    if (!row) {
      console.log(`GC_MOBY_CLASS oClass=${oClass} listed=false observed=false instances=0 pvarInstances=0 levels=none classTableLevels=none`);
      continue;
    }
    console.log(`GC_MOBY_CLASS oClass=${oClass} listed=${row.classListed} observed=${row.instanceCount > 0} instances=${row.instanceCount} pvarInstances=${row.pvarInstanceCount} pvarSizes=${row.pvarSizes.join(",") || "none"} modeBits=${row.modeBits.join(",") || "none"} levels=${row.levelsObserved.join(",") || "none"} classTableLevels=${row.classTableLevels.join(",") || "none"} relPtrOffsets=${row.relativePointerFixupOffsets.join(",") || "none"} mobyLinkOffsets=${row.mobyLinkFixupOffsets.join(",") || "none"}`);
  }
  if (options.stdoutJson) {
    console.log("GC_MOBY_JSON_BEGIN");
    console.log(JSON.stringify(report));
    console.log("GC_MOBY_JSON_END");
  }
} finally {
  await reader.close();
}

function newStats(oClass) {
  return {
    oClass,
    instanceCount: 0,
    pvarInstanceCount: 0,
    pvarSizes: new Set(),
    modeBits: new Set(),
    modeBit0x20Count: 0,
    raw0x14Values: new Set(),
    pvarDigest: createHash("sha256"),
    mobyLinkOffsets: new Set(),
    relativePointerOffsets: new Set(),
    observedLevels: new Set(),
    classTableLevels: new Set(),
    levelInstanceCounts: new Map(),
    levelPvarCounts: new Map(),
  };
}

function addMoby(stats, moby, gameplayData, levelNumber = undefined) {
  stats.instanceCount += 1;
  stats.modeBits.add(moby.modeBits >>> 0);
  if ((moby.modeBits & 0x20) !== 0) stats.modeBit0x20Count += 1;
  stats.raw0x14Values.add(moby.raw0x14);
  if (levelNumber !== undefined) {
    stats.observedLevels.add(levelNumber);
    stats.levelInstanceCounts.set(levelNumber, (stats.levelInstanceCounts.get(levelNumber) ?? 0) + 1);
  }
  if (moby.pvar) {
    stats.pvarInstanceCount += 1;
    stats.pvarSizes.add(moby.pvar.size);
    if (levelNumber !== undefined) stats.levelPvarCounts.set(levelNumber, (stats.levelPvarCounts.get(levelNumber) ?? 0) + 1);
    const prefix = Buffer.allocUnsafe(12);
    prefix.writeUInt32LE(moby.pvar.size >>> 0, 0);
    prefix.writeInt32LE(moby.oClass, 4);
    prefix.writeInt32LE(moby.pvarIndex, 8);
    stats.pvarDigest.update(prefix);
    stats.pvarDigest.update(gameplayData.subarray(moby.pvar.dataOffset, moby.pvar.dataOffset + moby.pvar.size));
  }
}

function finalizeStats(stats, classListed) {
  const raw = [...stats.raw0x14Values].sort((a, b) => a - b);
  return {
    oClass: stats.oClass,
    classListed,
    instanceCount: stats.instanceCount,
    pvarInstanceCount: stats.pvarInstanceCount,
    pvarSizes: [...stats.pvarSizes].sort((a, b) => a - b),
    modeBits: [...stats.modeBits].sort((a, b) => a - b).map(hex32),
    modeBit0x20Count: stats.modeBit0x20Count,
    raw0x14: summarizeValues(raw),
    mobyLinkFixupOffsets: [...stats.mobyLinkOffsets].sort((a, b) => a - b).map(hex32),
    relativePointerFixupOffsets: [...stats.relativePointerOffsets].sort((a, b) => a - b).map(hex32),
    pvarAggregateSha256: stats.pvarDigest.digest("hex"),
  };
}

function finalizeGlobal(stats) {
  const row = finalizeStats(stats, stats.classTableLevels.size > 0);
  return {
    ...row,
    levelsObserved: [...stats.observedLevels].sort((a, b) => a - b),
    classTableLevels: [...stats.classTableLevels].sort((a, b) => a - b),
    perLevel: [...stats.observedLevels].sort((a, b) => a - b).map((level) => ({
      level,
      instanceCount: stats.levelInstanceCounts.get(level) ?? 0,
      pvarInstanceCount: stats.levelPvarCounts.get(level) ?? 0,
    })),
  };
}

function summarizeValues(values) {
  if (values.length === 0) return { distinctCount: 0, min: null, max: null, values: [] };
  return {
    distinctCount: values.length,
    min: values[0],
    max: values[values.length - 1],
    values: values.slice(0, 32),
    truncated: values.length > 32,
  };
}

function validateFixup(owner, fixup, label, levelNumber) {
  if (!owner.pvar) throw new Error(`${label} fixup targets unresolved Moby PVar ${owner.pvarIndex} on level ${levelNumber}.`);
  if (fixup.offset + 4 > owner.pvar.size) {
    throw new Error(`${label} fixup +0x${fixup.offset.toString(16)} exceeds PVar ${owner.pvarIndex} size 0x${owner.pvar.size.toString(16)} (level ${levelNumber}, Moby ${owner.index}, oClass ${owner.oClass}).`);
  }
}

function parseClassList(value) {
  if (!value) throw new Error("--summary-classes requires a comma-separated class list.");
  return [...new Set(value.split(",").map((part) => {
    const parsed = Number(part.trim());
    if (!Number.isInteger(parsed) || parsed < -32768 || parsed > 65535) throw new Error(`Invalid oClass: ${part}.`);
    return parsed;
  }))];
}

function hex32(value) {
  return `0x${(value >>> 0).toString(16).padStart(8, "0")}`;
}
