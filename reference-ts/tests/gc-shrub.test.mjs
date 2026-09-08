import test from "node:test";
import assert from "node:assert/strict";
import { readGcShrubClass, readGcShrubClasses, SHRUB_CLASS_HEADER_SIZE } from "../.build/packages/gc-shrub/src/index.js";
import { VIF_UNPACK } from "../.build/packages/ps2-vif/src/index.js";

const cat = (...parts) => {
  const total = parts.reduce((n, p) => n + p.length, 0);
  const out = new Uint8Array(total);
  let o = 0;
  for (const p of parts) { out.set(p, o); o += p.length; }
  return out;
};
const u32 = (v) => new Uint8Array([v & 0xff, (v >> 8) & 0xff, (v >> 16) & 0xff, (v >>> 24) & 0xff]);
const s16buf = (vals) => {
  const b = new Uint8Array(vals.length * 2);
  const d = new DataView(b.buffer);
  vals.forEach((v, i) => d.setInt16(i * 2, v, true));
  return b;
};
function unpackPacket(vnvl, num, data) {
  const cmd = 0x60 | (vnvl & 0x0f);
  const code = ((cmd & 0x7f) << 24) | ((num & 0xff) << 16);
  const pad = (4 - (data.length % 4)) % 4;
  return cat(u32(code), data, new Uint8Array(pad));
}

/** One-packet shrub class: a 3-vertex triangle strip, 1 GIF tag (strip), no AD-GIFs. */
function buildShrubClass() {
  // unpack[0]: ShrubPacketHeader(textureCount=0, gifTagCount=1, vertexCount=3) + one ShrubVertexGifTag
  const gifTag = new Uint8Array(0x10);
  // bits 47..49 of the 64-bit tag == 0b100 (TRIANGLE_STRIP): set bit 17 of the high dword
  new DataView(gifTag.buffer).setUint32(4, 1 << 17, true);
  new DataView(gifTag.buffer).setInt32(0x0c, 0, true); // gs_packet_offset 0
  const header = cat(u32(0), u32(1), u32(3), u32(0), gifTag);

  // unpack[1]: ShrubVertexPart1[3]  {s16 x,y,z, s16 gsOffset}
  const part1 = cat(s16buf([0, 0, 0, 1]), s16buf([1024, 0, 0, 4]), s16buf([0, 1024, 0, 7]));
  // unpack[2]: ShrubVertexPart2[3]  {s16 s,t,h, s16 n}
  const part2 = cat(s16buf([0, 0, 0, 0]), s16buf([4096, 0, 0, 0]), s16buf([0, 4096, 0, 0]));

  const packet = cat(
    unpackPacket(VIF_UNPACK.V4_32, header.length / 16, header),
    unpackPacket(VIF_UNPACK.V4_16, 3, part1),
    unpackPacket(VIF_UNPACK.V4_16, 3, part2),
  );

  const classHeader = new Uint8Array(SHRUB_CLASS_HEADER_SIZE);
  const dv = new DataView(classHeader.buffer);
  dv.setFloat32(0x20, 1.0, true); // scale
  dv.setInt16(0x28, 1, true); // packetCount

  const packetEntry = cat(u32(SHRUB_CLASS_HEADER_SIZE + 8), u32(packet.length)); // offset, size
  return cat(classHeader, packetEntry, packet);
}

test("decodes a one-strip shrub class", () => {
  const mesh = readGcShrubClass(buildShrubClass());
  assert.equal(mesh.positions.length / 3, 3);
  assert.equal(mesh.indices.length / 3, 1);
  assert.equal(mesh.scale, 1);
  // vertex 1 = (1024,0,0) * (scale/1024) = (1,0,0)
  assert.deepEqual([...mesh.positions.slice(3, 6)], [1, 0, 0]);
  assert.ok(Math.abs(mesh.uvs[2] - 1) < 1e-6); // uv.s of vertex 1
});

test("truncated shrub class buffer is rejected", () => {
  assert.throws(() => readGcShrubClass(new Uint8Array(0x10)), /shorter than the 0x40 header/);
});

test("readGcShrubClasses maps material slots via ShrubClassEntry.textures[]", () => {
  const classBuf = buildShrubClass();
  const assets = new Uint8Array(0x800 + classBuf.length);
  const classOffset = 0x200;
  assets.set(classBuf, classOffset);

  const index = new Uint8Array(0x200);
  const iv = new DataView(index.buffer);
  const tableOffset = 0x20;
  iv.setInt32(tableOffset, classOffset, true);
  iv.setInt32(tableOffset + 4, 555, true); // oClass
  index[tableOffset + 0x10] = 7; // textures[0] -> level shrub texture 7

  const core = {
    coreHeader: { shrubClasses: { count: 1, offset: tableOffset } },
    index,
    assets,
    sectionBoundaries: [classOffset, assets.length],
  };

  const cls = readGcShrubClasses(core).get(555);
  assert.ok(cls);
  assert.deepEqual([...cls.triangleTextureIds], [7]);
});
