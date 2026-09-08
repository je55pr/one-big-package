import test from "node:test";
import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { Sha256, hashRandomAccessReaderSha256 } from "../.build/packages/hashing/src/index.js";

function nodeSha256(bytes) {
  return createHash("sha256").update(bytes).digest("hex");
}

class RecordingReader {
  constructor(name, bytes) {
    this.name = name;
    this.bytes = bytes;
    this.size = bytes.length;
    this.reads = [];
  }
  async read(offset, length) {
    this.reads.push([offset, length]);
    return this.bytes.slice(offset, offset + length);
  }
}

test("incremental SHA-256 matches standard vectors", () => {
  const vectors = [
    ["", "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"],
    ["abc", "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"],
    ["The quick brown fox jumps over the lazy dog", "d7a8fbb307d7809469ca9abcb0082e4f8d5651e46d3cdb762d02d0bf37c9e592"],
  ];
  for (const [text, expected] of vectors) {
    const bytes = new TextEncoder().encode(text);
    const hash = new Sha256();
    for (let offset = 0; offset < bytes.length; offset += 2) hash.update(bytes.subarray(offset, offset + 2));
    assert.equal(hash.digestHex(), expected);
    assert.equal(hash.digestHex(), expected);
    assert.throws(() => hash.update(Uint8Array.of(1)), /finalized/);
  }
});

test("incremental SHA-256 matches Node across padding and block boundaries", () => {
  for (const length of [1, 7, 55, 56, 57, 63, 64, 65, 127, 128, 129, 1024, 4097]) {
    const bytes = Uint8Array.from({ length }, (_, index) => (index * 73 + 19) & 0xff);
    const hash = new Sha256();
    let offset = 0;
    const pieces = [1, 3, 17, 64, 5, 91];
    let piece = 0;
    while (offset < bytes.length) {
      const lengthHere = Math.min(pieces[piece % pieces.length], bytes.length - offset);
      hash.update(bytes.subarray(offset, offset + lengthHere));
      offset += lengthHere;
      piece += 1;
    }
    assert.equal(hash.digestHex(), nodeSha256(bytes), `length ${length}`);
  }
});

test("random-access SHA-256 hashes bounded chunks without reconstructing the source", async () => {
  const bytes = Uint8Array.from({ length: 257 }, (_, index) => (index * 29 + 7) & 0xff);
  const reader = new RecordingReader("split-disc", bytes);
  const progress = [];
  const result = await hashRandomAccessReaderSha256(reader, {
    chunkSize: 31,
    onProgress(value) { progress.push(value); },
  });

  assert.deepEqual(result, { sizeBytes: bytes.length, sha256: nodeSha256(bytes) });
  assert.ok(reader.reads.every(([, length]) => length <= 31));
  assert.deepEqual(reader.reads[0], [0, 31]);
  assert.deepEqual(reader.reads.at(-1), [248, 9]);
  assert.deepEqual(progress.at(-1), { bytesRead: 257, totalBytes: 257 });
});

test("random-access SHA-256 handles empty readers without issuing reads", async () => {
  const reader = new RecordingReader("empty", new Uint8Array());
  const result = await hashRandomAccessReaderSha256(reader, { chunkSize: 1 });
  assert.equal(result.sha256, nodeSha256(new Uint8Array()));
  assert.deepEqual(reader.reads, []);
});
