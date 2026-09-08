#!/usr/bin/env node
import { createHash } from "node:crypto";
import { mkdir, writeFile } from "node:fs/promises";
import { resolve, dirname, basename } from "node:path";
import { LocalFileRandomAccessReader } from "./local-random-access.mjs";
import { assertUyaAuthorityTocWindow } from "./uya-retail-authority.mjs";
import { probeUyaDiscToc } from "../.build/packages/uya-disc-toc/src/index.js";
import { probeUyaWorldCandidatePublicLead } from "../.build/packages/uya-world-probe/src/index.js";
import { openUyaTocPayloadPublicLead } from "../.build/packages/uya-level-wad/src/index.js";
import { SubRangeReader } from "../.build/packages/importer-common/src/index.js";
import { readWadLz } from "../.build/packages/wad-lz/src/index.js";
import { probeUyaGcPvarCompatibility } from "../.build/packages/uya-pvars-compat/src/index.js";

const args = process.argv.slice(2);
if (args.length < 1 || args.includes("--help") || args.includes("-h")) usage();
const isoPath = resolve(args[0]);
const outAt = args.indexOf("--out");
if (outAt < 0 || !args[outAt + 1]) usage();
const outPath = resolve(args[outAt + 1]);

function usage() {
  console.error("Usage: npm run build && node tools/uya-moby-pvar-catalogue.mjs <retail-uya.iso> --out catalogue.json");
  process.exit(2);
}

const sha256 = (bytes) => createHash("sha256").update(bytes).digest("hex");
const reader = await LocalFileRandomAccessReader.open(isoPath, basename(isoPath));
try {
  const authority = await assertUyaAuthorityTocWindow(reader);
  if (!authority.matched) throw new Error("UYA authority identity did not match.");
  const toc = await probeUyaDiscToc(reader);
  const rows = toc.levelRows.filter((row) => row.parts.filter((part) => part.publicFormatHint?.label === "level" && part.issues.length === 0).length === 1);
  if (rows.length !== 51) throw new Error(`Expected 51 UYA main-level rows, found ${rows.length}.`);

  const rowReports = [];
  const families = new Map();
  for (const row of rows) {
    const tableIndex = row.index;
    const probe = await probeUyaWorldCandidatePublicLead(reader, { tableIndex });
    const gameplayRange = probe.levelPayload.outer.ranges[2];
    if (!gameplayRange?.present) throw new Error(`UYA table ${tableIndex} has no gameplay range in outer slot 2.`);
    const levelReader = openUyaTocPayloadPublicLead(reader, probe.selectedMainPart, `uya-table-${tableIndex}-candidate-level`);
    const gameplayReader = new SubRangeReader(levelReader, gameplayRange.offsetBytes, gameplayRange.sizeBytes, `${levelReader.name}#gameplay-slot-2`);
    const gameplay = await readWadLz(gameplayReader, 0, { maxOutputBytes: 64 * 1024 * 1024 });
    const compatibility = probeUyaGcPvarCompatibility(gameplay.data);
    const census = compatibility.prerequisiteCensus;
    const parsed = compatibility.parser;
    const linksByPvar = groupFixupOffsets(census.pvarMobyLinks);
    const relativesByPvar = groupFixupOffsets(census.pvarRelativePointers);
    const pvars = census.referencedPvars.map((entry) => ({
      index: entry.index,
      offset: entry.offset,
      size: entry.size,
      sha256: sha256(gameplay.data.subarray(entry.dataOffset, entry.dataOffset + entry.size)),
      referencedByMoby: entry.referencedByMoby,
      referencedByMobyLink: entry.referencedByMobyLink,
      referencedByRelativePointer: entry.referencedByRelativePointer,
      mobyLinkFixupOffsets: linksByPvar.get(entry.index) ?? [],
      relativePointerFixupOffsets: relativesByPvar.get(entry.index) ?? [],
    }));
    const pvarByIndex = new Map(pvars.map((entry) => [entry.index, entry]));

    const mobies = parsed.mobies.map((moby) => {
      const pvar = moby.pvarIndex >= 0 ? pvarByIndex.get(moby.pvarIndex) : undefined;
      const entry = {
        index: moby.index,
        gcCompatibilityFields: {
          uid: moby.uid,
          oClass: moby.oClass,
          pvarIndex: moby.pvarIndex,
          modeBits: moby.modeBits,
          raw0x14: moby.raw0x14,
        },
        ...(pvar ? { pvar: { size: pvar.size, sha256: pvar.sha256 } } : {}),
      };
      let family = families.get(moby.oClass);
      if (!family) {
        family = { oClass: moby.oClass, instances: 0, withPvar: 0, rows: new Set(), pvarSizes: new Set(), modeBits: new Set() };
        families.set(moby.oClass, family);
      }
      family.instances++;
      family.rows.add(tableIndex);
      family.modeBits.add(moby.modeBits);
      if (pvar) {
        family.withPvar++;
        family.pvarSizes.add(pvar.size);
      }
      return entry;
    });

    rowReports.push({
      tableIndex,
      gameplayBytes: gameplay.data.length,
      gameplaySha256: census.gameplaySha256,
      classListGcCompatibility: parsed.mobyClasses,
      mobies,
      pvars,
    });
    console.error(`catalogued UYA table ${tableIndex}: ${mobies.length} Mobies, ${pvars.length} referenced PVars`);
  }
  const familyReports = [...families.values()]
    .sort((a, b) => a.oClass - b.oClass)
    .map((family) => ({
      oClassGcCompatibility: family.oClass,
      instances: family.instances,
      withPvar: family.withPvar,
      tableIndices: [...family.rows].sort((a, b) => a - b),
      pvarSizes: [...family.pvarSizes].sort((a, b) => a - b),
      modeBits: [...family.modeBits].sort((a, b) => a - b),
    }));

  const report = {
    schemaVersion: 1,
    evidenceStatus: "derived retail UYA metadata; gcCompatibilityFields are shared/GC field labels pending independent UYA executable confirmation",
    payloadPolicy: "no retail PVar payload bytes are embedded; PVar contents are represented by size and SHA-256 only",
    authority: {
      buildId: "rac3-ntscu-original",
      serial: "SCUS-97353",
      tocWindowSha256: authority.sha256,
    },
    totals: {
      rows: rowReports.length,
      mobies: rowReports.reduce((sum, row) => sum + row.mobies.length, 0),
      referencedPvars: rowReports.reduce((sum, row) => sum + row.pvars.length, 0),
      observedOClasses: familyReports.length,
    },
    classFamilies: familyReports,
    rows: rowReports,
  };
  const json = `${JSON.stringify(report, null, 2)}\n`;
  await mkdir(dirname(outPath), { recursive: true });
  await writeFile(outPath, json);
  console.error(`wrote ${outPath} (${Buffer.byteLength(json)} bytes)`);
} finally {
  await reader.close();
}

function groupFixupOffsets(fixups) {
  const groups = new Map();
  for (const fixup of fixups) {
    const list = groups.get(fixup.pvarIndex) ?? [];
    list.push(fixup.offset);
    groups.set(fixup.pvarIndex, list);
  }
  for (const list of groups.values()) list.sort((a, b) => a - b);
  return groups;
}
