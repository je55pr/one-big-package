import test from "node:test";
import assert from "node:assert/strict";
import { mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { spawnSync } from "node:child_process";

const SECTOR = 0x800;
const TOC_LBA = 1001;
const PAYLOAD_LBA = 2500;

function packVertex(x16, y16, z64) {
  return ((x16 & 0x3ff) | ((y16 & 0x3ff) << 10) | ((z64 & 0xfff) << 20)) >>> 0;
}

function minimalCollision() {
  const bytes = new Uint8Array(0x80);
  const view = new DataView(bytes.buffer);
  view.setInt32(0x00, 0x40, true);
  view.setInt32(0x04, 0, true);
  const mesh = 0x40;
  view.setInt16(mesh + 0x00, 0, true);
  view.setUint16(mesh + 0x02, 1, true);
  view.setUint16(mesh + 0x04, 2, true);
  view.setInt16(mesh + 0x08, 0, true);
  view.setUint16(mesh + 0x0a, 1, true);
  view.setUint32(mesh + 0x0c, 0x10, true);
  view.setInt16(mesh + 0x10, 0, true);
  view.setUint16(mesh + 0x12, 1, true);
  view.setUint32(mesh + 0x14, 0x20 << 8, true);
  const oct = mesh + 0x20;
  view.setUint16(oct + 0x00, 1, true);
  view.setUint8(oct + 0x02, 3);
  view.setUint8(oct + 0x03, 0);
  view.setUint32(oct + 0x04, packVertex(0, 0, 0), true);
  view.setUint32(oct + 0x08, packVertex(16, 0, 0), true);
  view.setUint32(oct + 0x0c, packVertex(0, 16, 0), true);
  bytes.set([0, 1, 2, 7], oct + 0x10);
  return bytes;
}

function literalWad(payload, name = "coredata") {
  if (payload.length < 18 || payload.length > 273) throw new Error("fixture payload must fit one literal packet");
  const body = new Uint8Array(2 + payload.length);
  body[0] = 0;
  body[1] = payload.length - 18;
  body.set(payload, 2);
  const wad = new Uint8Array(0x10 + body.length);
  wad.set([0x57, 0x41, 0x44], 0);
  new DataView(wad.buffer).setInt32(3, wad.length, true);
  for (let i = 0; i < name.length && i < 9; i++) wad[7 + i] = name.charCodeAt(i);
  wad.set(body, 0x10);
  return wad;
}

function putResidentHeader(view, tocBase, relativeSector, headerSize, payloadLba) {
  const at = tocBase + relativeSector * SECTOR;
  view.setInt32(at, headerSize, true);
  view.setUint32(at + 4, payloadLba, true);
}

function syntheticDisc() {
  const payloadSectors = 4;
  const bytes = new Uint8Array((PAYLOAD_LBA + payloadSectors + 1) * SECTOR);
  const view = new DataView(bytes.buffer);
  const tocBase = TOC_LBA * SECTOR;

  view.setInt32(tocBase + 0x000, 0x048, true);
  view.setUint32(tocBase + 0x004, 0x1234, true);
  view.setInt32(tocBase + 0x048, 0x398, true);
  view.setUint32(tocBase + 0x04c, 0x2345, true);
  const table = tocBase + 0x3e0;

  view.setUint32(table + 0x00, TOC_LBA + 8, true);
  view.setUint32(table + 0x04, 3, true);
  view.setUint32(table + 0x08, TOC_LBA + 2, true);
  view.setUint32(table + 0x0c, payloadSectors, true);
  view.setUint32(table + 0x10, TOC_LBA + 16, true);
  view.setUint32(table + 0x14, 4, true);
  putResidentHeader(view, tocBase, 8, 0x1818, 2600);
  putResidentHeader(view, tocBase, 2, 0x60, PAYLOAD_LBA);
  putResidentHeader(view, tocBase, 16, 0x26f0, 2700);

  const assets = new Uint8Array(0x100);
  assets.set(minimalCollision(), 0x80);
  const wad = literalWad(assets);

  const level = PAYLOAD_LBA * SECTOR;
  view.setInt32(level + 0x00, 0x60, true);
  view.setInt32(level + 0x08, 7, true);
  view.setInt32(level + 0x10, 1, true);
  view.setInt32(level + 0x14, 2, true);

  const data = level + 0x800;
  view.setInt32(data + 0x08, 0x100, true);
  view.setInt32(data + 0x0c, 0x100, true);
  view.setInt32(data + 0x48, 0x300, true);
  view.setInt32(data + 0x4c, 0x200, true);

  const core = data + 0x100;
  view.setInt32(core + 0x14, 0x80, true);
  view.setInt32(core + 0x60, 0x100, true);
  view.setInt32(core + 0x88, wad.length, true);
  view.setInt32(core + 0x8c, assets.length, true);
  bytes.set(wad, data + 0x300);
  return bytes;
}

test("UYA core/collision CLI reads across ordered split files and reports strict compatibility census", () => {
  const dir = mkdtempSync(join(tmpdir(), "obp-uya-core-cli-"));
  try {
    const disc = syntheticDisc();
    const split = PAYLOAD_LBA * SECTOR + 0x830; // deliberately bisect the 0x58 data-header read
    const part1 = join(dir, "synthetic.iso.001");
    const part2 = join(dir, "synthetic.iso.002");
    writeFileSync(part1, disc.subarray(0, split));
    writeFileSync(part2, disc.subarray(split));

    const run = spawnSync(process.execPath, [
      "tools/uya-core-collision-probe.mjs",
      part1,
      part2,
      "--table-index", "0",
      "--max-level-rows", "4",
    ], {
      cwd: process.cwd(),
      encoding: "utf8",
      maxBuffer: 1024 * 1024,
    });

    assert.equal(run.status, 0, run.stderr);
    assert.equal(run.stderr, "");
    const report = JSON.parse(run.stdout);
    assert.equal(report.schemaVersion, 1);
    assert.equal(report.sourceParts.length, 2);
    assert.equal(report.selectedTableIndex, 0);
    assert.equal(report.selectedMainPart.slot, 1);
    assert.equal(report.compatibilityChecks.coreCompressedSizeMatchesWadHeader, true);
    assert.equal(report.compatibilityChecks.publicCompressedSizeMatchesWad, true);
    assert.equal(report.compatibilityChecks.publicDecompressedSizeMatchesOutput, true);
    assert.equal(report.core.coreIndexBytes, 0x100);
    assert.equal(report.core.decompressedAssetsBytes, 0x100);
    assert.deepEqual(report.core.publicCollisionRange, { offset: 0x80, size: 0x80 });
    assert.match(report.core.coreIndexSha256, /^[0-9a-f]{64}$/);
    assert.match(report.core.compressedCoreDataSha256, /^[0-9a-f]{64}$/);
    assert.match(report.core.decompressedAssetsSha256, /^[0-9a-f]{64}$/);
    assert.match(report.core.publicCollisionSha256, /^[0-9a-f]{64}$/);
    assert.equal(report.collision.byteLength, 0x80);
    assert.equal(report.collision.octantCount, 1);
    assert.equal(report.collision.vertexCount, 3);
    assert.equal(report.collision.triangleCount, 1);
    assert.deepEqual(report.collision.materialIds, [7]);
    assert.deepEqual(report.collision.nativeBounds, {
      min: { x: 2, y: 2, z: 2 },
      max: { x: 3, y: 3, z: 2 },
    });
  } finally {
    rmSync(dir, { recursive: true, force: true });
  }
});
