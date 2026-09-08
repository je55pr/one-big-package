#!/usr/bin/env node
import { createHash } from "node:crypto";
import { basename, resolve } from "node:path";
import { LocalFileRandomAccessReader } from "./local-random-access.mjs";
import { SubRangeReader } from "../.build/packages/importer-common/src/index.js";
import { assertUyaAuthorityTocWindow } from "./uya-retail-authority.mjs";
import { probeUyaWorldCandidatePublicLead } from "../.build/packages/uya-world-probe/src/index.js";
import { openUyaTocPayloadPublicLead } from "../.build/packages/uya-level-wad/src/index.js";
import { readWadLz } from "../.build/packages/wad-lz/src/index.js";
import { parseGcLevelSettings, GC_LEVEL_SETTINGS_FIRST_PART_SIZE } from "../.build/packages/gc-level-settings/src/index.js";

const args = process.argv.slice(2);
const positional = [];
let tableIndex = 1;
for (let i = 0; i < args.length; i++) {
  const a = args[i];
  if (a === "--table-index") tableIndex = Number(args[++i]);
  else if (a === "--help" || a === "-h") usage();
  else if (a.startsWith("--")) throw new Error(`Unknown flag: ${a}`);
  else positional.push(a);
}
if (positional.length !== 1 || !Number.isSafeInteger(tableIndex) || tableIndex < 0) usage();

function usage() {
  console.error("Usage: npm run build && node tools/uya-level-settings-compat-probe.mjs <uya.iso> --table-index N");
  process.exit(2);
}

function assertFinite(label, value) {
  if (!Number.isFinite(value)) throw new Error(`UYA level settings: ${label} is non-finite (${value}).`);
}

function assertFiniteVec(label, values) {
  values.forEach((value, i) => assertFinite(`${label}[${i}]`, value));
}

function readRawRgb(view, at) {
  return [view.getInt32(at, true), view.getInt32(at + 4, true), view.getInt32(at + 8, true)];
}

function assertRawRgb(label, rgb) {
  if (rgb[0] === -1) return;
  for (const value of rgb) {
    if (!Number.isSafeInteger(value) || value < 0 || value > 255) {
      throw new Error(`UYA level settings: ${label} contains implausible channel ${value}.`);
    }
  }
}

const isoPath = resolve(positional[0]);
const reader = await LocalFileRandomAccessReader.open(isoPath, basename(isoPath));
try {
  const authority = await assertUyaAuthorityTocWindow(reader);
  if (!authority.matched) throw new Error("UYA authority identity did not match pinned retail bytes.");

  const probe = await probeUyaWorldCandidatePublicLead(reader, { tableIndex });
  const levelReader = openUyaTocPayloadPublicLead(reader, probe.selectedMainPart, `uya-table-${tableIndex}-candidate-level`);
  const gameplay = probe.levelPayload.outer.ranges[2];
  if (!gameplay?.present) throw new Error(`UYA table ${tableIndex} has no public gameplay range in outer slot 2.`);
  const gameplayReader = new SubRangeReader(
    levelReader,
    gameplay.offsetBytes,
    gameplay.sizeBytes,
    `${levelReader.name}#gameplay-slot-2`,
  );
  const gameplayWad = await readWadLz(gameplayReader, 0, { maxOutputBytes: 64 * 1024 * 1024 });
  const data = gameplayWad.data;
  const settings = parseGcLevelSettings(data);
  if (settings.blockOffset + GC_LEVEL_SETTINGS_FIRST_PART_SIZE > data.length) {
    throw new Error("UYA level settings first-part range lies outside decoded gameplay data.");
  }
  const block = data.subarray(settings.blockOffset, settings.blockOffset + GC_LEVEL_SETTINGS_FIRST_PART_SIZE);
  const blockSha256 = createHash("sha256").update(block).digest("hex");
  const view = new DataView(data.buffer, data.byteOffset, data.byteLength);
  const backgroundRaw = readRawRgb(view, settings.blockOffset + 0x00);
  const fogRaw = readRawRgb(view, settings.blockOffset + 0x0c);
  assertRawRgb("background colour", backgroundRaw);
  assertRawRgb("fog colour", fogRaw);

  assertFinite("fogNearDistance", settings.fogNearDistance);
  assertFinite("fogFarDistance", settings.fogFarDistance);
  assertFinite("fogNearIntensity", settings.fogNearIntensity);
  assertFinite("fogFarIntensity", settings.fogFarIntensity);
  assertFinite("deathHeight", settings.deathHeight);
  assertFiniteVec("sphereCentre", settings.sphereCentre);
  assertFiniteVec("shipPosition", settings.shipPosition);
  assertFinite("shipRotationZ", settings.shipRotationZ);

  console.log(JSON.stringify({
    result: "UYA_LEVEL_SETTINGS_COMPAT_PASS",
    tableIndex,
    authoritySha256: authority.sha256,
    gameplayCompressedOffset: gameplay.offsetBytes,
    gameplayCompressedSize: gameplay.sizeBytes,
    gameplayDecodedSize: data.length,
    settingsBlockOffset: settings.blockOffset,
    settingsBlockSize: GC_LEVEL_SETTINGS_FIRST_PART_SIZE,
    settingsBlockSha256: blockSha256,
    backgroundRaw,
    fogRaw,
    backgroundColour: settings.backgroundColour,
    fogColour: settings.fogColour,
    fogNearDistance: settings.fogNearDistance,
    fogFarDistance: settings.fogFarDistance,
    fogNearIntensity: settings.fogNearIntensity,
    fogFarIntensity: settings.fogFarIntensity,
    deathHeight: settings.deathHeight,
    isSphericalWorld: settings.isSphericalWorld,
    sphereCentre: settings.sphereCentre,
    shipPosition: settings.shipPosition,
    shipRotationZ: settings.shipRotationZ,
  }, null, 2));
} finally {
  await reader.close();
}
