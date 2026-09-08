import test from "node:test";
import assert from "node:assert/strict";
import {
  censusUyaStaticClassPrerequisites,
  probeUyaGcStaticGeometryCompatibilityPublicLead,
} from "../.build/packages/uya-static-geometry-compat/src/index.js";

function fixture() {
  const index = new Uint8Array(0x400);
  const iv = new DataView(index.buffer);
  const assets = new Uint8Array(0x800);

  const tieClasses = { count: 1, offset: 0x100 };
  iv.setInt32(0x100, 0x200, true);
  iv.setInt32(0x104, 42, true);
  // Empty but structurally valid 0x80-byte TIE class: LOD0 table offset/count remain zero.
  new DataView(assets.buffer).setFloat32(0x200 + 0x40, 1, true);

  const shrubClasses = { count: 1, offset: 0x140 };
  iv.setInt32(0x140, 0x400, true);
  iv.setInt32(0x144, 77, true);
  // Empty but structurally valid 0x40-byte shrub class: packet count remains zero.
  new DataView(assets.buffer).setFloat32(0x400 + 0x20, 1, true);

  const publicFields = {
    gsRam: { count: 0, offset: 0 },
    tfrags: 0,
    occlusion: 0,
    sky: 0,
    collision: 0,
    mobyClasses: { count: 0, offset: 0 },
    tieClasses,
    shrubClasses,
    tfragTextures: { count: 0, offset: 0 },
    mobyTextures: { count: 0, offset: 0 },
    tieTextures: { count: 4, offset: 0x180 },
    shrubTextures: { count: 3, offset: 0x1c0 },
    partTextures: { count: 0, offset: 0 },
    fxTextures: { count: 0, offset: 0 },
    texturesBaseOffset: 0x600,
    partBankOffset: 0,
    fxBankOffset: 0,
    partDefsOffset: 0,
    soundRemapOffset: 0,
    unknown74: 0,
    ratchetSeqsRac123: 0,
    sceneViewSize: 0,
    indexIntoSome1TexsRac2Maybe3: 0,
    mobyGsStashCountRac23Dl: 0,
    assetsCompressedSize: 0,
    assetsDecompressedSize: assets.length,
    chromeMapTexture: 0,
    chromeMapPalette: 0,
    glassMapTexture: 0,
    glassMapPalette: 0,
    unknownA0: 0,
    heightmapOffset: 0,
    occlusionOctOffset: 0,
    mobyGsStashList: 0,
    occlusionRadOffset: 0,
    mobySoundRemapOffset: 0,
    occlusionRad2Offset: 0,
  };
  return { decoded: { coreIndex: index, assets }, publicFields, index, assets, tieClasses, shrubClasses };
}

test("censuses TIE and shrub class table prerequisites independently", () => {
  const f = fixture();
  const tie = censusUyaStaticClassPrerequisites(f.index, f.assets, f.tieClasses, "tie");
  assert.equal(tie.tableWithinCoreIndex, true);
  assert.equal(tie.candidateEntryCount, 1);
  assert.equal(tie.candidateUniqueOClassCount, 1);
  assert.equal(tie.invalidAssetOffsetCount, 0);
  assert.equal(tie.classHeaderOutsideAssetCount, 0);

  const shrub = censusUyaStaticClassPrerequisites(f.index, f.assets, f.shrubClasses, "shrub");
  assert.equal(shrub.candidateEntryCount, 1);
  assert.equal(shrub.candidateUniqueOClassCount, 1);
});

test("unchanged GC TIE and shrub readers decode every synthetic candidate oClass", () => {
  const f = fixture();
  const result = probeUyaGcStaticGeometryCompatibilityPublicLead(f.decoded, f.publicFields);
  assert.equal(result.tie.parserDecodedClassCount, 1);
  assert.equal(result.tie.parserDecodedAllCandidateOClasses, true);
  assert.equal(result.tie.nonFinitePositionCount, 0);
  assert.equal(result.tie.totalTriangles, 0);
  assert.equal(result.shrub.parserDecodedClassCount, 1);
  assert.equal(result.shrub.parserDecodedAllCandidateOClasses, true);
  assert.equal(result.shrub.nonFinitePositionCount, 0);
  assert.ok(result.sectionBoundaries.includes(0x200));
  assert.ok(result.sectionBoundaries.includes(0x400));
});

test("invalid class asset offsets are visible rather than silently promoted", () => {
  const f = fixture();
  const iv = new DataView(f.index.buffer);
  iv.setInt32(f.tieClasses.offset, f.assets.length + 1, true);
  const census = censusUyaStaticClassPrerequisites(f.index, f.assets, f.tieClasses, "tie");
  assert.equal(census.invalidAssetOffsetCount, 1);
  assert.equal(census.candidateEntryCount, 0);
});
