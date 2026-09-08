import test from "node:test";
import assert from "node:assert/strict";
import {
  censusUyaInstanceBlockPrerequisites,
  probeUyaGcInstancePlacementCompatibilityPublicLead,
} from "../.build/packages/uya-instance-compat/src/index.js";

function writeMatrix(view, at, tx, ty, tz) {
  const values = [1,0,0,0, 0,1,0,0, 0,0,1,0, tx,ty,tz,1];
  values.forEach((value, i) => view.setFloat32(at + i * 4, value, true));
}

function fixture() {
  const gameplay = new Uint8Array(0x400);
  const gv = new DataView(gameplay.buffer);
  gv.setInt32(0x34, 0x80, true);
  gv.setInt32(0x40, 0x180, true);
  gv.setInt32(0x80, 2, true);
  gv.setInt32(0x90, 10, true);
  writeMatrix(gv, 0xa0, 1, 2, 3);
  gv.setInt32(0xf0, 11, true);
  writeMatrix(gv, 0x100, 4, 5, 6);
  gv.setInt32(0x180, 1, true);
  gv.setInt32(0x190, 20, true);
  writeMatrix(gv, 0x1a0, -1, -2, -3);

  const index = new Uint8Array(0x300);
  const iv = new DataView(index.buffer);
  const assets = new Uint8Array(0x800);
  const tieClasses = { count: 2, offset: 0x100 };
  iv.setInt32(0x100, 0x200, true); iv.setInt32(0x104, 10, true);
  iv.setInt32(0x120, 0x300, true); iv.setInt32(0x124, 11, true);
  const shrubClasses = { count: 1, offset: 0x140 };
  iv.setInt32(0x140, 0x400, true); iv.setInt32(0x144, 20, true);

  const publicFields = {
    gsRam: { count: 0, offset: 0 }, tfrags: 0, occlusion: 0, sky: 0, collision: 0,
    mobyClasses: { count: 0, offset: 0 }, tieClasses, shrubClasses,
    tfragTextures: { count: 0, offset: 0 }, mobyTextures: { count: 0, offset: 0 },
    tieTextures: { count: 0, offset: 0 }, shrubTextures: { count: 0, offset: 0 },
    partTextures: { count: 0, offset: 0 }, fxTextures: { count: 0, offset: 0 },
    texturesBaseOffset: 0, partBankOffset: 0, fxBankOffset: 0, partDefsOffset: 0,
    soundRemapOffset: 0, unknown74: 0, ratchetSeqsRac123: 0, sceneViewSize: 0,
    indexIntoSome1TexsRac2Maybe3: 0, mobyGsStashCountRac23Dl: 0,
    assetsCompressedSize: 0, assetsDecompressedSize: assets.length,
    chromeMapTexture: 0, chromeMapPalette: 0, glassMapTexture: 0, glassMapPalette: 0,
    unknownA0: 0, heightmapOffset: 0, occlusionOctOffset: 0, mobyGsStashList: 0,
    occlusionRadOffset: 0, mobySoundRemapOffset: 0, occlusionRad2Offset: 0,
  };
  return { gameplay, decodedCore: { coreIndex: index, assets }, publicFields };
}

test("independently validates full TIE and shrub instance block spans", () => {
  const f = fixture();
  const tie = censusUyaInstanceBlockPrerequisites(f.gameplay, "tie");
  assert.equal(tie.declaredCount, 2);
  assert.equal(tie.fullDeclaredSpanWithinGameplay, true);
  assert.equal(tie.fullStructReadableCount, 2);
  assert.equal(tie.completeFiniteEntryCount, 2);
  assert.equal(tie.nonFiniteMatrixComponentCount, 0);

  const shrub = censusUyaInstanceBlockPrerequisites(f.gameplay, "shrub");
  assert.equal(shrub.declaredCount, 1);
  assert.equal(shrub.fullDeclaredSpanWithinGameplay, true);
  assert.equal(shrub.completeFiniteEntryCount, 1);
});

test("unchanged GC instance parser returns every declared entry and resolves class oClasses", () => {
  const f = fixture();
  const result = probeUyaGcInstancePlacementCompatibilityPublicLead(f.gameplay, f.decodedCore, f.publicFields);
  assert.equal(result.tie.parserDecodedInstanceCount, 2);
  assert.equal(result.tie.parserDecodedAllDeclaredEntries, true);
  assert.deepEqual(result.tie.oClassesMissingFromCandidateClassTable, []);
  assert.deepEqual(result.tie.translationBounds, { min: [1,2,3], max: [4,5,6] });
  assert.equal(result.shrub.parserDecodedInstanceCount, 1);
  assert.equal(result.shrub.parserDecodedAllDeclaredEntries, true);
  assert.deepEqual(result.shrub.oClassesMissingFromCandidateClassTable, []);
});

test("truncated declared instance spans are visible instead of silently accepted", () => {
  const f = fixture();
  const gv = new DataView(f.gameplay.buffer);
  gv.setInt32(0x80, 20, true);
  const census = censusUyaInstanceBlockPrerequisites(f.gameplay, "tie");
  assert.equal(census.declaredCount, 20);
  assert.equal(census.fullDeclaredSpanWithinGameplay, false);
  assert.ok(census.fullStructReadableCount < 20);
});
