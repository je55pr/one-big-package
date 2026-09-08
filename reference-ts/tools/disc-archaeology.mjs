import { mkdir, writeFile } from "node:fs/promises";
import { resolve, basename } from "node:path";
import { LocalFileRandomAccessReader } from "./local-random-access.mjs";
import { Iso9660Filesystem } from "../.build/packages/iso9660/src/index.js";
import { openPs2Disc, readPs2BootProgramHeaders } from "../.build/packages/ps2-disc/src/index.js";
import { ELF_PROGRAM_TYPE_LOAD } from "../.build/packages/elf32/src/index.js";
import { hashRandomAccessReaderSha256 } from "../.build/packages/hashing/src/index.js";

/**
 * Deterministic Stage 0 disc archaeology.
 *
 * Runs the current safe source path (ISO-9660 -> SYSTEM.CNF -> PS2 boot ELF ->
 * ELF32 program headers / PT_LOAD mapping) plus a full bounded ISO inventory
 * against a local retail ISO, and writes a regenerable JSON + Markdown report.
 *
 * Usage:
 *   node tools/disc-archaeology.mjs <iso-path> [--build <buildId>] [--out-dir research/generated] [--hash] [--large-bytes 1048576]
 *
 * No absolute local path is written into the reports; only the ISO basename.
 */

const args = process.argv.slice(2);
const positional = [];
const options = { outDir: "research/generated", largeBytes: 1024 * 1024, hash: false, build: undefined };
for (let i = 0; i < args.length; i++) {
  const arg = args[i];
  if (arg === "--hash") options.hash = true;
  else if (arg === "--build") options.build = args[++i];
  else if (arg === "--out-dir") options.outDir = args[++i];
  else if (arg === "--large-bytes") options.largeBytes = Number(args[++i]);
  else if (arg.startsWith("--")) throw new Error(`Unknown flag: ${arg}`);
  else positional.push(arg);
}
if (positional.length !== 1) {
  console.error("Usage: node tools/disc-archaeology.mjs <iso-path> [--build <buildId>] [--out-dir <dir>] [--hash] [--large-bytes <n>]");
  process.exit(2);
}

const isoPath = resolve(positional[0]);
const isoName = basename(isoPath);
const reader = await LocalFileRandomAccessReader.open(isoPath, isoName);

try {
  const filesystem = await Iso9660Filesystem.open(reader);
  const volume = filesystem.volume;

  const disc = await openPs2Disc(reader);
  const boot = disc.boot;
  const elf = disc.bootExecutableHeader;
  const programHeaders = await readPs2BootProgramHeaders(disc);

  const ptLoad = programHeaders
    .filter((ph) => ph.type === ELF_PROGRAM_TYPE_LOAD)
    .map((ph) => ({
      index: ph.index,
      virtualStart: hex(ph.virtualAddress),
      virtualEnd: hex(ph.virtualAddress + ph.memorySize),
      fileStart: hex(ph.offset),
      fileEnd: hex(ph.offset + ph.fileSize),
      fileSize: ph.fileSize,
      memorySize: ph.memorySize,
      bssBytes: ph.memorySize - ph.fileSize,
      flags: flagString(ph.flags),
      alignment: hex(ph.alignment),
    }));

  // Full bounded inventory walk. Directory extents only; no file payloads read.
  const inventory = [];
  for await (const entry of filesystem.walk("/", { includeDirectories: true, maxDepth: 64, maxEntries: 500_000 })) {
    inventory.push({
      path: entry.path,
      name: entry.name,
      isDirectory: entry.isDirectory,
      extentLba: entry.extentLba,
      offset: entry.extentLba * volume.logicalBlockSize,
      sizeBytes: entry.dataLength,
      version: entry.version ?? null,
    });
  }
  inventory.sort((a, b) => (a.path < b.path ? -1 : a.path > b.path ? 1 : 0));

  const files = inventory.filter((e) => !e.isDirectory);
  const directories = inventory.filter((e) => e.isDirectory);
  const totalFileBytes = files.reduce((sum, e) => sum + e.sizeBytes, 0);
  const largeContainers = files
    .filter((e) => e.sizeBytes >= options.largeBytes)
    .sort((a, b) => b.sizeBytes - a.sizeBytes);
  const extensionHistogram = {};
  for (const file of files) {
    const match = /\.([A-Za-z0-9]+)$/.exec(file.name);
    const ext = match ? match[1].toUpperCase() : "(none)";
    extensionHistogram[ext] = (extensionHistogram[ext] ?? 0) + 1;
  }

  let payloadHash;
  if (options.hash) {
    const result = await hashRandomAccessReaderSha256(reader, {
      onProgress: ({ bytesRead, totalBytes }) => {
        if (process.stderr.isTTY) process.stderr.write(`\rhashing ${(100 * bytesRead / totalBytes).toFixed(1)}%`);
      },
    });
    if (process.stderr.isTTY) process.stderr.write("\n");
    payloadHash = result.sha256;
  }

  const buildId = options.build ?? boot.serial ?? isoName;
  const report = {
    generator: "tools/disc-archaeology.mjs",
    generatedFrom: { isoFileName: isoName, sizeBytes: reader.size, ...(payloadHash ? { sha256: payloadHash } : {}) },
    buildId,
    iso9660: {
      descriptorLba: volume.descriptorLba,
      systemIdentifier: volume.systemIdentifier,
      volumeIdentifier: volume.volumeIdentifier,
      volumeSpaceSize: volume.volumeSpaceSize,
      logicalBlockSize: volume.logicalBlockSize,
      rootDirectory: {
        extentLba: volume.rootDirectory.extentLba,
        dataLength: volume.rootDirectory.dataLength,
      },
    },
    ps2Boot: {
      volumeIdentifier: boot.volumeIdentifier,
      bootKey: boot.bootKey,
      bootPath: boot.bootPath,
      executableIsoPath: boot.executableIsoPath,
      executableName: boot.executableName,
      normalizedSerial: boot.serial ?? null,
      executableSizeBytes: boot.executableSize,
      systemCnf: boot.config,
    },
    bootElf: {
      endian: elf.endian,
      type: elf.type,
      machine: elf.machine,
      entryPoint: hex(elf.entry),
      programHeaderOffset: hex(elf.programHeaderOffset),
      programHeaderEntrySize: elf.programHeaderEntrySize,
      programHeaderCount: elf.programHeaderCount,
      sectionHeaderOffset: hex(elf.sectionHeaderOffset),
      sectionHeaderCount: elf.sectionHeaderCount,
      flags: hex(elf.flags),
    },
    programHeaders: programHeaders.map((ph) => ({
      index: ph.index,
      type: hex(ph.type),
      typeName: ph.type === ELF_PROGRAM_TYPE_LOAD ? "PT_LOAD" : programTypeName(ph.type),
      offset: hex(ph.offset),
      virtualAddress: hex(ph.virtualAddress),
      fileSize: ph.fileSize,
      memorySize: ph.memorySize,
      flags: flagString(ph.flags),
      alignment: hex(ph.alignment),
    })),
    ptLoad,
    inventorySummary: {
      entryCount: inventory.length,
      fileCount: files.length,
      directoryCount: directories.length,
      totalFileBytes,
      largeContainerThresholdBytes: options.largeBytes,
      largeContainerCount: largeContainers.length,
      extensionHistogram: Object.fromEntries(Object.entries(extensionHistogram).sort((a, b) => b[1] - a[1] || (a[0] < b[0] ? -1 : 1))),
    },
    largeContainers: largeContainers.map((e) => ({ path: e.path, sizeBytes: e.sizeBytes, offset: e.offset, extentLba: e.extentLba })),
    inventory,
  };

  await mkdir(resolve(options.outDir), { recursive: true });
  const jsonPath = resolve(options.outDir, `${buildId}.stage0.json`);
  const mdPath = resolve(options.outDir, `${buildId}.stage0.md`);
  await writeFile(jsonPath, JSON.stringify(report, null, 2) + "\n");
  await writeFile(mdPath, renderMarkdown(report));
  console.log(`wrote ${jsonPath}`);
  console.log(`wrote ${mdPath}`);
} finally {
  await reader.close();
}

function hex(value) {
  return `0x${(value >>> 0).toString(16).padStart(8, "0")}`;
}

function flagString(flags) {
  return `${flags & 4 ? "R" : "-"}${flags & 2 ? "W" : "-"}${flags & 1 ? "X" : "-"}`;
}

function programTypeName(type) {
  return { 0: "PT_NULL", 2: "PT_DYNAMIC", 3: "PT_INTERP", 4: "PT_NOTE", 6: "PT_PHDR", 0x70000000: "PT_MIPS_REGINFO" }[type] ?? `0x${type.toString(16)}`;
}

function renderMarkdown(report) {
  const lines = [];
  const b = report.ps2Boot;
  const e = report.bootElf;
  lines.push(`# Stage 0 disc report — ${report.buildId}`);
  lines.push("");
  lines.push(`Generated by \`${report.generator}\` from \`${report.generatedFrom.isoFileName}\` (${report.generatedFrom.sizeBytes.toLocaleString()} bytes).`);
  if (report.generatedFrom.sha256) lines.push(`Payload SHA-256: \`${report.generatedFrom.sha256}\``);
  lines.push("");
  lines.push("Regenerate:");
  lines.push("");
  lines.push("```");
  lines.push(`node tools/disc-archaeology.mjs "<path to ${report.generatedFrom.isoFileName}>" --build ${report.buildId}${report.generatedFrom.sha256 ? " --hash" : ""}`);
  lines.push("```");
  lines.push("");
  lines.push("## ISO-9660 volume");
  lines.push("");
  lines.push("| Field | Value |");
  lines.push("|---|---|");
  lines.push(`| Volume identifier | \`${report.iso9660.volumeIdentifier}\` |`);
  lines.push(`| System identifier | \`${report.iso9660.systemIdentifier}\` |`);
  lines.push(`| Volume space size (blocks) | ${report.iso9660.volumeSpaceSize.toLocaleString()} |`);
  lines.push(`| Logical block size | ${report.iso9660.logicalBlockSize} |`);
  lines.push(`| Root directory extent LBA | ${report.iso9660.rootDirectory.extentLba} |`);
  lines.push("");
  lines.push("## PS2 boot");
  lines.push("");
  lines.push("| Field | Value |");
  lines.push("|---|---|");
  lines.push(`| Boot key | ${b.bootKey} |`);
  lines.push(`| Boot path | \`${b.bootPath}\` |`);
  lines.push(`| Executable ISO path | \`${b.executableIsoPath}\` |`);
  lines.push(`| Executable name | \`${b.executableName}\` |`);
  lines.push(`| Normalized serial | \`${b.normalizedSerial}\` |`);
  lines.push(`| Executable size | ${b.executableSizeBytes.toLocaleString()} bytes |`);
  lines.push("");
  lines.push("### SYSTEM.CNF");
  lines.push("");
  lines.push("```");
  for (const [key, value] of Object.entries(b.systemCnf)) lines.push(`${key} = ${value}`);
  lines.push("```");
  lines.push("");
  lines.push("## Boot ELF");
  lines.push("");
  lines.push("| Field | Value |");
  lines.push("|---|---|");
  lines.push(`| Endian / type / machine | ${e.endian} / ${e.type} / ${e.machine} (MIPS) |`);
  lines.push(`| Entry point | \`${e.entryPoint}\` |`);
  lines.push(`| Program headers | ${e.programHeaderCount} @ \`${e.programHeaderOffset}\` (${e.programHeaderEntrySize} bytes each) |`);
  lines.push(`| Section headers | ${e.sectionHeaderCount} @ \`${e.sectionHeaderOffset}\` |`);
  lines.push(`| ELF flags | \`${e.flags}\` |`);
  lines.push("");
  lines.push("### Program headers");
  lines.push("");
  lines.push("| # | Type | File offset | Virtual addr | File size | Mem size | Flags | Align |");
  lines.push("|---|---|---|---|---|---|---|---|");
  for (const ph of report.programHeaders) {
    lines.push(`| ${ph.index} | ${ph.typeName} | \`${ph.offset}\` | \`${ph.virtualAddress}\` | ${ph.fileSize.toLocaleString()} | ${ph.memorySize.toLocaleString()} | ${ph.flags} | \`${ph.alignment}\` |`);
  }
  lines.push("");
  lines.push("### PT_LOAD virtual-address ranges");
  lines.push("");
  lines.push("| # | Virtual range | File-offset range | BSS bytes | Flags |");
  lines.push("|---|---|---|---|---|");
  for (const seg of report.ptLoad) {
    lines.push(`| ${seg.index} | \`${seg.virtualStart}\`–\`${seg.virtualEnd}\` | \`${seg.fileStart}\`–\`${seg.fileEnd}\` | ${seg.bssBytes.toLocaleString()} | ${seg.flags} |`);
  }
  lines.push("");
  lines.push("## ISO inventory summary");
  lines.push("");
  const s = report.inventorySummary;
  lines.push(`- ${s.fileCount.toLocaleString()} files, ${s.directoryCount} directories, ${s.totalFileBytes.toLocaleString()} bytes total`);
  lines.push(`- ${s.largeContainerCount} files ≥ ${s.largeContainerThresholdBytes.toLocaleString()} bytes`);
  lines.push("");
  lines.push("Extension histogram:");
  lines.push("");
  lines.push("| Extension | Files |");
  lines.push("|---|---|");
  for (const [ext, count] of Object.entries(s.extensionHistogram)) lines.push(`| ${ext} | ${count} |`);
  lines.push("");
  lines.push(`## Large containers (≥ ${s.largeContainerThresholdBytes.toLocaleString()} bytes)`);
  lines.push("");
  lines.push("| Path | Size (bytes) | Offset | Extent LBA |");
  lines.push("|---|---|---|---|");
  for (const c of report.largeContainers) {
    lines.push(`| \`${c.path}\` | ${c.sizeBytes.toLocaleString()} | \`0x${c.offset.toString(16)}\` | ${c.extentLba} |`);
  }
  lines.push("");
  lines.push("## Full inventory");
  lines.push("");
  lines.push("| Path | Type | Size (bytes) | Extent LBA |");
  lines.push("|---|---|---|---|");
  for (const entry of report.inventory) {
    lines.push(`| \`${entry.path}\` | ${entry.isDirectory ? "dir" : "file"} | ${entry.isDirectory ? "" : entry.sizeBytes.toLocaleString()} | ${entry.extentLba} |`);
  }
  lines.push("");
  return lines.join("\n");
}
