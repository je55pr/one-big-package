#!/usr/bin/env node
import { createHash } from "node:crypto";
import { basename, resolve } from "node:path";
import { LocalFileRandomAccessReader } from "./local-random-access.mjs";
import { assertUyaAuthorityTocWindow } from "./uya-retail-authority.mjs";
import { probeUyaWorldCandidatePublicLead } from "../.build/packages/uya-world-probe/src/index.js";
import { openUyaTocPayloadPublicLead } from "../.build/packages/uya-level-wad/src/index.js";
import { decodeUyaCoreDataPublicLead } from "../.build/packages/uya-core-decode/src/index.js";
import { readUyaSky } from "../.build/packages/uya-sky/src/index.js";

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
  console.error("Usage: npm run build && node tools/uya-sky-compat-probe.mjs <uya.iso> --table-index N");
  process.exit(2);
}

const isoPath = resolve(positional[0]);
const reader = await LocalFileRandomAccessReader.open(isoPath, basename(isoPath));
try {
  const authority = await assertUyaAuthorityTocWindow(reader);
  if (!authority.matched) throw new Error("UYA authority identity did not match pinned retail bytes.");

  const probe = await probeUyaWorldCandidatePublicLead(reader, { tableIndex });
  const levelReader = openUyaTocPayloadPublicLead(reader, probe.selectedMainPart, `uya-table-${tableIndex}-candidate-level`);
  const decoded = await decodeUyaCoreDataPublicLead(
    levelReader,
    probe.levelPayload.outer,
    probe.levelPayload.data,
    probe.levelPayload.core,
    probe.levelPayload.coreDataWadLz,
  );
  if (decoded.warnings.length) throw new Error(`UYA core decode produced warnings: ${decoded.warnings.join(" | ")}`);

  const fields = probe.levelPayload.core.publicFields;
  const range = sectionRangeAllowZero(fields.sky, decoded.sectionBoundaries, decoded.assets.length);
  if (!range) throw new Error(`No UYA sky section range at asset offset ${fields.sky}.`);
  const skyBytes = decoded.assets.subarray(range.offset, range.offset + range.size);
  const skySha256 = createHash("sha256").update(skyBytes).digest("hex");
  const sky = readUyaSky(skyBytes, { framerate: 60 });

  let vertices = 0;
  let triangles = 0;
  let clusters = 0;
  let nonFinitePositions = 0;
  const textureIds = new Set();
  const shells = sky.shells.map((shell, index) => {
    vertices += shell.positions.length / 3;
    triangles += shell.indices.length / 3;
    clusters += shell.clusterCount;
    for (const value of shell.positions) if (!Number.isFinite(value)) nonFinitePositions++;
    for (const id of shell.triangleTextureIds) if (id >= 0) textureIds.add(id);
    return {
      index,
      offset: sky.shellOffsets[index],
      clusterCount: shell.clusterCount,
      textured: shell.textured,
      bloom: shell.bloom,
      rotationRaw: shell.rotationRaw,
      angularVelocityRaw: shell.angularVelocityRaw,
      vertices: shell.positions.length / 3,
      triangles: shell.indices.length / 3,
      textureIds: [...new Set([...shell.triangleTextureIds].filter((id) => id >= 0))].sort((a, b) => a - b),
      noTextureTriangles: [...shell.triangleTextureIds].filter((id) => id < 0).length,
    };
  });

  console.log(JSON.stringify({
    result: "UYA_SKY_COMPAT_PASS",
    tableIndex,
    authoritySha256: authority.sha256,
    coreIndexSha256: decoded.coreIndexSha256,
    assetsSha256: decoded.assetsSha256,
    publicSkyOffset: fields.sky,
    skySectionOffset: range.offset,
    skySectionSize: range.size,
    skySectionSha256: skySha256,
    colour: sky.colour,
    clearScreen: sky.clearScreen,
    spriteCount: sky.spriteCount,
    maximumSpriteCount: sky.maximumSpriteCount,
    fxCount: sky.fxCount,
    textureCount: sky.textures.length,
    shellCount: sky.shells.length,
    clusterCount: clusters,
    vertices,
    triangles,
    referencedTextureIds: [...textureIds].sort((a, b) => a - b),
    nonFinitePositions,
    shells,
  }, null, 2));
} finally {
  await reader.close();
}

function sectionRangeAllowZero(offset, boundaries, assetsLength) {
  if (!Number.isSafeInteger(offset) || offset < 0 || offset >= assetsLength) return undefined;
  let next;
  for (const bound of boundaries) {
    if (bound > offset && bound <= assetsLength && (next === undefined || bound < next)) next = bound;
  }
  return next === undefined ? undefined : { offset, size: next - offset };
}
