import test from "node:test";
import assert from "node:assert/strict";
import { BlobRandomAccessReader } from "../.build/packages/importer-common/src/index.js";
import { decompressWad, isWadLz, readWadLzHeader, readWadLz, WAD_LZ_HEADER_SIZE } from "../.build/packages/wad-lz/src/index.js";

/** Wrap a raw packet stream in a valid 16-byte WAD LZ container header. */
function container(body, { name = "", compressedSizeOverride } = {}) {
  const header = new Uint8Array(WAD_LZ_HEADER_SIZE);
  header[0] = 0x57;
  header[1] = 0x41;
  header[2] = 0x44;
  const size = compressedSizeOverride ?? WAD_LZ_HEADER_SIZE + body.length;
  header[3] = size & 0xff;
  header[4] = (size >> 8) & 0xff;
  header[5] = (size >> 16) & 0xff;
  header[6] = (size >> 24) & 0xff;
  for (let i = 0; i < name.length && i < 9; i++) header[7 + i] = name.charCodeAt(i);
  return new Uint8Array([...header, ...body]);
}

test("small and big literal packets", () => {
  // flag 0x05 -> literal run of 0x05 + 3 = 8 bytes.
  assert.deepEqual([...decompressWad(container([0x05, 1, 2, 3, 4, 5, 6, 7, 8])).data], [1, 2, 3, 4, 5, 6, 7, 8]);
  // flag 0x00 -> big literal, size = next + 18.
  const big = decompressWad(container([0x00, 0x02, ...Array(20).fill(0xab)])).data;
  assert.equal(big.length, 20);
  assert.ok(big.every((b) => b === 0xab));
});

test("little match packet copies from the output with overlap, plus trailing inline literal", () => {
  // literal "ABCD" (flag 0x01 -> 4 bytes), then flag 0x62 little match:
  //   b1 = 0x00; matchSize = (0x62 >> 5) + 1 = 4; lookback = 4 - 0 - ((0x62>>2)&7) - 1 = 4 - 0 - 0 - 1 = 3
  //   copy out[3..] x4 with overlap -> D D D D ; trailing literals = (secondToLastByte 0x62) & 3 = 2 -> "XY"
  const out = decompressWad(container([0x01, 65, 66, 67, 68, 0x62, 0x00, 88, 89])).data;
  assert.equal(String.fromCharCode(...out), "ABCDDDDDXY");
});

test("medium match packet", () => {
  // literal 8 bytes 0..7 (flag 0x05), then medium match flag 0x21:
  //   matchSize = (0x21 & 0x1f) + 2 = 3 ; b1 = 0x04, b2 = 0x00
  //   lookback = 8 - (0*0x40) - (0x04 >> 2) - 1 = 8 - 0 - 1 - 1 = 6 ; copy out[6],out[7],out[8|overlap]
  //   trailing literals = (b1 0x04) & 3 = 0
  const out = decompressWad(container([0x05, 0, 1, 2, 3, 4, 5, 6, 7, 0x21, 0x04, 0x00])).data;
  assert.deepEqual([...out], [0, 1, 2, 3, 4, 5, 6, 7, 6, 7, 6]);
});

test("far match padding-sync special case ends the packet and skips to a 0x1000 boundary", () => {
  // literal 4 bytes, then flag 0x11 with size field 1 and lookback == out (b0=b1=0, flag&8=0):
  //   size = 0x11 & 7 = 1 ; lookback == out and size == 1 -> falls through, no copy
  //   Actually to hit the *sync* branch we need size != 1, so use flag 0x12 (size 2).
  const body = [0x01, 1, 2, 3, 4, 0x12, 0x00, 0x00, /* padding to 0x1000 */ ...Array(0x1000 - 8).fill(0)];
  const out = decompressWad(container(body)).data;
  assert.deepEqual([...out], [1, 2, 3, 4]);
});

test("two literal packets in a row are rejected", () => {
  assert.throws(() => decompressWad(container([0x01, 1, 2, 3, 4, 0x01, 5, 6, 7, 8])), /two literal packets/);
});

test("a match pointing before the buffer is rejected", () => {
  // flag 0x62 little match with b1 large -> negative lookback
  assert.throws(() => decompressWad(container([0x01, 1, 2, 3, 4, 0x62, 0xff, 0x00])), /outside the output buffer/);
});

test("truncated stream is rejected", () => {
  // header claims a literal run of 8 but only 3 bytes follow
  assert.throws(() => decompressWad(container([0x05, 1, 2, 3])), /past the stream|end of stream/);
});

test("output cap is enforced", () => {
  // 273-byte literal run decompresses fine by default...
  assert.equal(decompressWad(container([0x00, 0xff, ...Array(273).fill(1)])).data.length, 273);
  // ...but not under a tight cap.
  assert.throws(() => decompressWad(container([0x00, 0xff, ...Array(273).fill(1)]), { maxOutputBytes: 100 }), /exceeds cap/);
});

test("header parsing and magic detection", () => {
  const c = container([0x01, 1, 2, 3, 4], { name: "chunkcoll" });
  assert.equal(isWadLz(c), true);
  assert.equal(isWadLz(new Uint8Array([1, 2, 3, 4])), false);
  const h = readWadLzHeader(c);
  assert.equal(h.name, "chunkcoll");
  assert.equal(h.compressedSize, WAD_LZ_HEADER_SIZE + 5);
  assert.throws(() => readWadLzHeader(new Uint8Array([0x58, 0x41, 0x44, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0])), /magic/);
});

test("readWadLz reads only the block's own bytes from a reader", async () => {
  const block = container([0x05, 9, 9, 9, 9, 9, 9, 9, 9]);
  const padded = new Uint8Array(block.length + 4096);
  padded.set(block, 128);
  const reader = new BlobRandomAccessReader(new Blob([padded]), "level.wad");
  const result = await readWadLz(reader, 128);
  assert.deepEqual([...result.data], [9, 9, 9, 9, 9, 9, 9, 9]);
});
