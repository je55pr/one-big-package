import test from "node:test";
import assert from "node:assert/strict";
import {
  decodeVifCode,
  vifUnpackElementSize,
  vifPacketSize,
  readVifCommandList,
  filterVifUnpacks,
  readUnpackS16,
  readUnpackU8,
  VIF_UNPACK,
} from "../.build/packages/ps2-vif/src/index.js";

/** Encode an UNPACK VIF code word. */
function unpackCode(vnvl, num, addr, { unsigned = false } = {}) {
  const cmd = 0x60 | (vnvl & 0x0f);
  return ((cmd & 0x7f) << 24) | ((num & 0xff) << 16) | ((unsigned ? 1 : 0) << 14) | (addr & 0x3ff);
}

function u32le(value) {
  return new Uint8Array([value & 0xff, (value >> 8) & 0xff, (value >> 16) & 0xff, (value >>> 24) & 0xff]);
}

test("element sizes for the VN/VL codes used by tfrags", () => {
  assert.equal(vifUnpackElementSize(VIF_UNPACK.V3_16), 6);
  assert.equal(vifUnpackElementSize(VIF_UNPACK.V4_16), 8);
  assert.equal(vifUnpackElementSize(VIF_UNPACK.V4_8), 4);
  assert.equal(vifUnpackElementSize(VIF_UNPACK.V4_32), 16);
});

test("decodeVifCode splits an UNPACK code", () => {
  const code = decodeVifCode(unpackCode(VIF_UNPACK.V3_16, 41, 90));
  assert.equal(code.isUnpack, true);
  assert.equal(code.vnvl, VIF_UNPACK.V3_16);
  assert.equal(code.num, 41);
  assert.equal(code.addr, 90);
});

test("num of 0 decodes as 256", () => {
  assert.equal(decodeVifCode(unpackCode(VIF_UNPACK.V4_8, 0, 0)).num, 256);
});

test("vifPacketSize rounds unpack data up to a word and adds the code word", () => {
  // V3_16 num 3 -> 18 bytes -> padded to 20 -> +4 for the code = 24
  const code = decodeVifCode(unpackCode(VIF_UNPACK.V3_16, 3, 0));
  assert.equal(vifPacketSize(code), 24);
  // STROW is always 5 words
  assert.equal(vifPacketSize(decodeVifCode(0x30 << 24)), 20);
  // NOP is 1 word
  assert.equal(vifPacketSize(decodeVifCode(0)), 4);
});

test("readVifCommandList walks a mixed stream and filterVifUnpacks selects the unpacks", () => {
  const parts = [];
  parts.push(u32le(0)); // NOP
  // STROW + 4 data words
  parts.push(u32le(0x30 << 24), u32le(100), u32le(200), u32le(300), u32le(0));
  // UNPACK V3_16 num 2 -> 12 bytes padded to 12
  parts.push(u32le(unpackCode(VIF_UNPACK.V3_16, 2, 5)));
  parts.push(new Uint8Array([1, 0, 2, 0, 3, 0, 4, 0, 5, 0, 6, 0]));
  // UNPACK V4_8 num 3 -> 12 bytes
  parts.push(u32le(unpackCode(VIF_UNPACK.V4_8, 3, 9, { unsigned: true })));
  parts.push(new Uint8Array([10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21]));

  const bytes = new Uint8Array(parts.reduce((n, p) => n + p.length, 0));
  let o = 0;
  for (const p of parts) { bytes.set(p, o); o += p.length; }

  const list = readVifCommandList(bytes);
  assert.equal(list.length, 4);
  assert.equal(list[1].code.cmd, 0x30); // STROW at index 1
  assert.deepEqual([...new Int32Array(list[1].data.buffer, list[1].data.byteOffset, 4)], [100, 200, 300, 0]);

  const unpacks = filterVifUnpacks(list);
  assert.equal(unpacks.length, 2);
  assert.deepEqual([...readUnpackS16(unpacks[0], 3)], [1, 2, 3, 4, 5, 6]);
  assert.deepEqual([...readUnpackU8(unpacks[1])], [10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21]);
});

test("a truncated tail stops the walk cleanly", () => {
  const bytes = new Uint8Array([...u32le(unpackCode(VIF_UNPACK.V3_16, 10, 0)), 1, 2, 3]); // claims 10 elems, 3 bytes follow
  assert.deepEqual(readVifCommandList(bytes), []);
});
