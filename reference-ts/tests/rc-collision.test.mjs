import test from "node:test";
import assert from "node:assert/strict";
import { readRcCollision, toObpCollisionMesh, rcToObpPosition, obpCollisionBounds, RC_COLLISION_OCTANT_UNITS } from "../.build/packages/rc-collision/src/index.js";
import { validateWorld } from "../.build/packages/core/src/index.js";

/**
 * Build a minimal but structurally real collision blob: 8-byte header padded to
 * 0x40, one z/y/x octree node each, and `octants` at ascending offsets.
 *
 * octant = { grid:[dx,dy,dz], vertices:[[rx,ry,rz]...] (raw units), faces:[[v0,v1,v2,type,(v3)]...] }
 */
function buildCollision(octants, { heroGroups } = {}) {
  const mesh = 0x40;
  const parts = [];
  // header + pad
  const head = new Uint8Array(mesh);
  new DataView(head.buffer).setInt32(0, mesh, true);
  parts.push(head);

  // We lay out: zNode, yNode, xNode, then octant bodies. Compute offsets first.
  const zNodeOff = mesh + 8; // after z header (coord,count) + 1 u16 offset -> pad to 4
  // z header: s16 zCoord, u16 zCount, u16 zOffsets[1]  => 6 bytes, pad to 8
  // zNode (== z child): s16 yCoord, u16 yCount, u32 yOffsets[1] => 8 bytes
  const yNodeOff = zNodeOff + 8;
  // yNode: s16 xCoord, u16 xCount, u32 xOffsets[n] => 4 + 4n
  const xTableBytes = 4 + 4 * octants.length;
  let cursor = yNodeOff + ((xTableBytes + 3) & ~3);

  const octantOffsets = [];
  const octantBlobs = [];
  for (const oct of octants) {
    octantOffsets.push(cursor);
    const faceCount = oct.faces.length;
    const quadCount = oct.faces.filter((f) => f.length === 5).length;
    const size = 4 + oct.vertices.length * 4 + faceCount * 4 + quadCount;
    const b = new Uint8Array((size + 3) & ~3);
    const dv = new DataView(b.buffer);
    dv.setUint16(0, faceCount, true);
    b[2] = oct.vertices.length;
    b[3] = quadCount;
    let p = 4;
    for (const [rx, ry, rz] of oct.vertices) {
      const packed = ((rz & 0xfff) >>> 0) * 0x100000 + ((ry & 0x3ff) << 10) + (rx & 0x3ff);
      dv.setUint32(p, packed >>> 0, true);
      p += 4;
    }
    // quads first (they get a v3), then tris
    const ordered = [...oct.faces].sort((a, c) => (c.length === 5 ? 1 : 0) - (a.length === 5 ? 1 : 0));
    for (const f of ordered) {
      b[p] = f[0]; b[p + 1] = f[1]; b[p + 2] = f[2]; b[p + 3] = f[3];
      p += 4;
    }
    for (const f of ordered) if (f.length === 5) b[p++] = f[4];
    octantBlobs.push(b);
    cursor += b.length;
  }

  // z header
  const z = new Uint8Array(8);
  const zd = new DataView(z.buffer);
  zd.setInt16(0, octants[0].grid[2], true);
  zd.setUint16(2, 1, true);
  zd.setUint16(4, (zNodeOff - mesh) / 4, true);
  parts.push(z);

  // zNode -> yNode pointer
  const zn = new Uint8Array(8);
  const znd = new DataView(zn.buffer);
  znd.setInt16(0, octants[0].grid[1], true);
  znd.setUint16(2, 1, true);
  znd.setUint32(4, yNodeOff - mesh, true);
  parts.push(zn);

  // yNode -> octant pointers
  const yn = new Uint8Array((xTableBytes + 3) & ~3);
  const ynd = new DataView(yn.buffer);
  ynd.setInt16(0, octants[0].grid[0], true);
  ynd.setUint16(2, octants.length, true);
  octantOffsets.forEach((off, i) => ynd.setUint32(4 + i * 4, (off - mesh) << 8, true));
  parts.push(yn);

  for (const b of octantBlobs) parts.push(b);

  if (heroGroups !== undefined) {
    const hg = new Uint8Array(16);
    new DataView(hg.buffer).setInt32(0, heroGroups, true);
    parts.push(hg);
    // fix header hero_groups offset
    const totalBefore = parts.slice(0, -1).reduce((s, p) => s + p.length, 0);
    new DataView(head.buffer).setInt32(4, totalBefore, true);
  }

  const total = parts.reduce((s, p) => s + p.length, 0);
  const out = new Uint8Array(total);
  let o = 0;
  for (const p of parts) { out.set(p, o); o += p.length; }
  return out;
}

test("parses a one-octant collision blob into world-space geometry", () => {
  const bytes = buildCollision([
    {
      grid: [1, 2, 3],
      vertices: [[0, 0, 0], [16, 0, 0], [16, 16, 0], [0, 16, 0]],
      faces: [[0, 1, 2, 42], [0, 1, 2, 42, 3]], // one tri, one quad
    },
  ]);
  const mesh = readRcCollision(bytes);

  assert.equal(mesh.meshOffset, 0x40);
  assert.equal(mesh.octants.length, 1);
  const oct = mesh.octants[0];
  assert.deepEqual([oct.gridX, oct.gridY, oct.gridZ], [1, 2, 3]);
  assert.deepEqual([oct.originX, oct.originY, oct.originZ], [1 * 4 + 2, 2 * 4 + 2, 3 * 4 + 2]);
  assert.equal(oct.vertexCount, 4);

  // vertex 0 sits at the octant origin; vertex 1 is +1.0 in x (raw 16 / 16).
  assert.deepEqual([...mesh.positions.slice(0, 3)], [6, 10, 14]);
  assert.deepEqual([...mesh.positions.slice(3, 6)], [7, 10, 14]);

  // tri -> 1 triangle, quad -> 2 triangles
  assert.equal(mesh.triangles.length, 3);
  assert.deepEqual(mesh.materialIds, [42]);
  assert.equal(mesh.triangles.every((t) => t.materialId === 42), true);
  assert.equal(mesh.triangles[1].fromQuad, true);
  assert.equal(RC_COLLISION_OCTANT_UNITS, 4);
});

test("hero group count is read when present", () => {
  const bytes = buildCollision(
    [{ grid: [0, 0, 0], vertices: [[0, 0, 0], [16, 0, 0], [0, 16, 0]], faces: [[0, 1, 2, 5]] }],
    { heroGroups: 7 },
  );
  const mesh = readRcCollision(bytes);
  assert.notEqual(mesh.heroGroupsOffset, 0);
  assert.equal(mesh.heroGroupCount, 7);
});

test("multiple octants accumulate vertices with correct per-octant base", () => {
  const bytes = buildCollision([
    { grid: [0, 0, 0], vertices: [[0, 0, 0], [16, 0, 0], [0, 16, 0]], faces: [[0, 1, 2, 1]] },
    { grid: [1, 0, 0], vertices: [[0, 0, 0], [16, 0, 0], [0, 16, 0]], faces: [[0, 1, 2, 2]] },
  ]);
  const mesh = readRcCollision(bytes);
  assert.equal(mesh.octants.length, 2);
  assert.equal(mesh.octants[1].firstVertex, 3);
  // second octant's triangle references vertices 3,4,5
  const t = mesh.triangles[1];
  assert.deepEqual([t.a, t.b, t.c], [3, 4, 5]);
  assert.deepEqual(mesh.materialIds, [1, 2]);
});

test("rejects an out-of-range mesh offset", () => {
  const bytes = buildCollision([{ grid: [0, 0, 0], vertices: [[0, 0, 0]], faces: [] }]);
  new DataView(bytes.buffer).setInt32(0, bytes.length + 100, true);
  assert.throws(() => readRcCollision(bytes), /meshOffset/);
});

test("rejects a face vertex index past the octant vertex count", () => {
  const bytes = buildCollision([
    { grid: [0, 0, 0], vertices: [[0, 0, 0], [16, 0, 0], [0, 16, 0]], faces: [[0, 1, 9, 1]] },
  ]);
  assert.throws(() => readRcCollision(bytes), /face vertex index/);
});

test("enforces the octant cap", () => {
  const bytes = buildCollision([
    { grid: [0, 0, 0], vertices: [[0, 0, 0], [16, 0, 0], [0, 16, 0]], faces: [[0, 1, 2, 1]] },
    { grid: [1, 0, 0], vertices: [[0, 0, 0], [16, 0, 0], [0, 16, 0]], faces: [[0, 1, 2, 1]] },
  ]);
  assert.throws(() => readRcCollision(bytes, { maxOctants: 1 }), /maxOctants/);
});

test("toObpCollisionMesh produces a valid OBP collision mesh with native material ids", () => {
  const bytes = buildCollision([
    { grid: [1, 1, 1], vertices: [[0, 0, 0], [16, 0, 0], [16, 16, 0], [0, 16, 0]], faces: [[0, 1, 2, 12, 3]] },
  ]);
  const parsed = readRcCollision(bytes);
  const source = { game: "rac2", buildId: "rac2-ntscu-v1.01", levelId: "1", assetKind: "collision", originalOffset: 0 };
  const obp = toObpCollisionMesh(parsed, source, "rac2-1-collision", "LEVEL1 collision");

  assert.equal(obp.geometry.indices.length, 6); // quad -> 2 tris
  assert.deepEqual(obp.triangleMaterialIds, [12, 12]);
  assert.equal(obp.source.game, "rac2");
  assert.ok(obp.source.notes.some((n) => n.includes("meshOffset=0x40")));

  // native RC is Z-up; OBP is Y-up -> (x, y, z) -> (x, z, y)
  assert.deepEqual(rcToObpPosition(3, 5, 7), [3, 7, 5]);
  const nativeV0 = [parsed.positions[0], parsed.positions[1], parsed.positions[2]];
  assert.deepEqual([obp.geometry.positions[0], obp.geometry.positions[1], obp.geometry.positions[2]], [nativeV0[0], nativeV0[2], nativeV0[1]]);
  const b = obpCollisionBounds(parsed);
  assert.equal(b.min.y, parsed.bounds.min.z);
  assert.equal(b.max.z, parsed.bounds.max.y);

  const world = {
    schemaVersion: 1,
    id: "w",
    displayName: "w",
    source,
    bounds: obpCollisionBounds(parsed),
    materials: [],
    meshes: [],
    collisionMeshes: [obp],
    instances: [],
    splines: [],
    volumes: [],
    spawnPoints: [],
  };
  assert.deepEqual(validateWorld(world), []);
});
