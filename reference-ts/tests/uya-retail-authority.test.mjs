import test from "node:test";
import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { assertUyaAuthorityTocWindow } from "../tools/uya-retail-authority.mjs";

class MemoryReader {
  constructor(bytes, name = "memory") {
    this.bytes = bytes;
    this.name = name;
    this.size = bytes.length;
    this.reads = [];
  }

  async read(offset, length) {
    this.reads.push({ offset, length });
    return this.bytes.slice(offset, offset + length);
  }
}

function sha256(bytes) {
  return createHash("sha256").update(bytes).digest("hex");
}

test("UYA authority guard hashes only the declared bounded window", async () => {
  const sectorBytes = 2048;
  const tocLba = 2;
  const tocWindowBytes = 1024 * 1024 + 137;
  const offsetBytes = tocLba * sectorBytes;
  const source = new Uint8Array(offsetBytes + tocWindowBytes + 4096);
  for (let i = 0; i < tocWindowBytes; i++) source[offsetBytes + i] = (i * 29 + 7) & 0xff;
  const window = source.slice(offsetBytes, offsetBytes + tocWindowBytes);
  const reader = new MemoryReader(source);

  const result = await assertUyaAuthorityTocWindow(reader, {
    label: "synthetic authority",
    serial: "TEST-00000",
    tocLba,
    tocWindowBytes,
    tocWindowSha256: sha256(window),
  });

  assert.equal(result.matched, true);
  assert.equal(result.sha256, sha256(window));
  assert.equal(result.offsetBytes, offsetBytes);
  assert.deepEqual(reader.reads, [
    { offset: offsetBytes, length: 1024 * 1024 },
    { offset: offsetBytes + 1024 * 1024, length: 137 },
  ]);
});

test("UYA authority guard refuses a mismatched source before semantic probing", async () => {
  const source = new Uint8Array(8192);
  const reader = new MemoryReader(source, "wrong-source");

  await assert.rejects(
    assertUyaAuthorityTocWindow(reader, {
      label: "synthetic authority",
      serial: "TEST-00000",
      tocLba: 1,
      tocWindowBytes: 1024,
      tocWindowSha256: "f".repeat(64),
    }),
    /identity mismatch.*Refusing to apply UYA semantic probes/i,
  );
  assert.deepEqual(reader.reads, [{ offset: 2048, length: 1024 }]);
});

test("UYA authority guard rejects a window outside the source without reading", async () => {
  const reader = new MemoryReader(new Uint8Array(4096));
  await assert.rejects(
    assertUyaAuthorityTocWindow(reader, {
      label: "synthetic authority",
      serial: "TEST-00000",
      tocLba: 2,
      tocWindowBytes: 1,
      tocWindowSha256: "0".repeat(64),
    }),
    /lies outside/,
  );
  assert.deepEqual(reader.reads, []);
});
