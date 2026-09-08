import test from "node:test";
import assert from "node:assert/strict";
import { parseGcGameplayInstances, transformPoint, matrixFromPosRotScale } from "../.build/packages/gc-instances/src/index.js";

/** Build a gameplay buffer: pointer table, then a tie-instance block. */
function buildGameplay(tieInstances = [], shrubInstances = []) {
  const headerSize = 0x100;
  const parts = [new Uint8Array(headerSize)];
  const header = new DataView(parts[0].buffer);

  const addBlock = (ptrOffset, instances, structSize) => {
    const blockStart = parts.reduce((n, p) => n + p.length, 0);
    header.setInt32(ptrOffset, blockStart, true);
    const block = new Uint8Array(0x10 + instances.length * structSize);
    const bv = new DataView(block.buffer);
    bv.setInt32(0, instances.length, true);
    instances.forEach((inst, i) => {
      const at = 0x10 + i * structSize;
      bv.setInt32(at, inst.oClass, true);
      for (let m = 0; m < 16; m++) bv.setFloat32(at + 0x10 + m * 4, inst.matrix[m] ?? 0, true);
    });
    parts.push(block);
  };

  if (tieInstances.length) addBlock(0x34, tieInstances, 0x60);
  if (shrubInstances.length) addBlock(0x40, shrubInstances, 0x70);

  const total = parts.reduce((n, p) => n + p.length, 0);
  const out = new Uint8Array(total);
  let o = 0;
  for (const p of parts) { out.set(p, o); o += p.length; }
  return out;
}

const identity = [1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 10, 20, 30, 0];

test("parses tie and shrub instance blocks", () => {
  const data = buildGameplay(
    [{ oClass: 2262, matrix: identity }, { oClass: 2263, matrix: [0, -1, 0, 0, 1, 0, 0, 0, 0, 0, 1, 0, 5, 6, 7, 0] }],
    [{ oClass: 100, matrix: identity }],
  );
  const g = parseGcGameplayInstances(data);
  assert.equal(g.tieInstances.length, 2);
  assert.equal(g.shrubInstances.length, 1);
  assert.equal(g.tieInstances[0].oClass, 2262);
  assert.deepEqual(g.tieInstances[0].matrix.slice(12, 15), [10, 20, 30]);
  assert.equal(g.tieInstances[1].oClass, 2263);
});

test("a missing block pointer yields an empty list", () => {
  const g = parseGcGameplayInstances(buildGameplay([{ oClass: 1, matrix: identity }]));
  assert.equal(g.tieInstances.length, 1);
  assert.deepEqual(g.shrubInstances, []);
});

test("an implausible instance count is rejected", () => {
  const data = buildGameplay([{ oClass: 1, matrix: identity }]);
  new DataView(data.buffer).setInt32(new DataView(data.buffer).getInt32(0x34, true), 9_999_999, true);
  assert.deepEqual(parseGcGameplayInstances(data).tieInstances, []);
});

test("transformPoint applies a column-major Mat4", () => {
  // 90deg about Z: x' = -y, y' = x  (column-major: [0,4,8]=col0 ...)
  const m = [0, 1, 0, 0, -1, 0, 0, 0, 0, 0, 1, 0, 100, 200, 300, 0];
  assert.deepEqual(transformPoint(m, 1, 0, 0), [100, 201, 300]);
  assert.deepEqual(transformPoint(m, 0, 1, 0), [99, 200, 300]);
  assert.deepEqual(transformPoint(m, 0, 0, 2), [100, 200, 302]);
});

test("parses moby instances and builds a transform from pos/rot/scale", () => {
  // one moby block: MobyBlockHeader (0x10) then a GcUyaMobyInstance (0x88)
  const data = new Uint8Array(0x200);
  const dv = new DataView(data.buffer);
  const blockOffset = 0x80;
  dv.setInt32(0x4c, blockOffset, true); // GC moby instances pointer
  dv.setInt32(blockOffset, 1, true); // staticCount
  const at = blockOffset + 0x10;
  dv.setInt32(at, 0x88, true); // size
  dv.setInt32(at + 0x28, 4828, true); // oClass
  dv.setFloat32(at + 0x2c, 1.0, true); // scale
  dv.setFloat32(at + 0x40, 300, true); // pos x
  dv.setFloat32(at + 0x44, 585, true); // pos y
  dv.setFloat32(at + 0x48, 110, true); // pos z

  const g = parseGcGameplayInstances(data);
  assert.equal(g.mobyInstances.length, 1);
  assert.equal(g.mobyInstances[0].oClass, 4828);
  assert.deepEqual(g.mobyInstances[0].position, [300, 585, 110]);

  const m = matrixFromPosRotScale([300, 585, 110], [0, 0, 0], 2);
  assert.deepEqual(transformPoint(m, 0, 0, 0), [300, 585, 110]); // origin -> translation
  assert.deepEqual(transformPoint(m, 1, 0, 0), [302, 585, 110]); // scaled x axis
});

test("a moby entry with a bad size field ends the list", () => {
  const data = new Uint8Array(0x200);
  const dv = new DataView(data.buffer);
  dv.setInt32(0x4c, 0x80, true);
  dv.setInt32(0x80, 3, true); // claims 3
  dv.setInt32(0x90, 0x88, true); // entry 0 ok
  dv.setInt32(0x90 + 0x88, 0x11, true); // entry 1 bad size
  assert.equal(parseGcGameplayInstances(data).mobyInstances.length, 1);
});
