import test from "node:test";
import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { BlobRandomAccessReader } from "../.build/packages/importer-common/src/index.js";
import {
  decodeUyaPackedWadCandidatePublicLead,
  parseUyaRatchetExecutableStreamPublicLead,
  probeUyaPackedBootExecutablePublicLead,
  scanUyaLoaderAnchorCandidatesPublicLead,
  scanUyaPackedExecutableWadCandidatesPublicLead,
} from "../.build/packages/uya-packed-elf/src/index.js";

function u32(view, at, value) {
  view.setUint32(at, value >>> 0, true);
}

function ratchetStream() {
  const bytes = new Uint8Array(40);
  const view = new DataView(bytes.buffer);
  u32(view, 0x00, 0x00100000);
  u32(view, 0x04, 4);
  u32(view, 0x08, 1);
  u32(view, 0x0c, 0x00123456);
  bytes.set([1, 2, 3, 4], 0x10);
  u32(view, 0x14, 0x00200000);
  u32(view, 0x18, 4);
  u32(view, 0x1c, 8);
  u32(view, 0x20, 0x00123456);
  bytes.set([5, 6, 7, 8], 0x24);
  return bytes;
}

function loaderAnchorStream() {
  const bytes = new Uint8Array(0x20);
  const view = new DataView(bytes.buffer);
  u32(view, 0x00, 0x00300000);
  u32(view, 0x04, 0x10);
  u32(view, 0x08, 1);
  u32(view, 0x0c, 0x00123456);
  u32(view, 0x10, 1001); // aligned scalar candidate
  u32(view, 0x14, (0x09 << 26) | (8 << 16) | 1001); // addiu $t0,$zero,1001
  u32(view, 0x18, (0x0f << 26) | (9 << 16) | 0x001f); // lui $t1,0x001f
  u32(view, 0x1c, (0x0d << 26) | (9 << 21) | (9 << 16) | 0x4800); // ori $t1,$t1,0x4800
  return bytes;
}

function literalWad(payload, name = "boot") {
  assert.ok(payload.length >= 18 && payload.length <= 273);
  const body = new Uint8Array(2 + payload.length);
  body[0] = 0;
  body[1] = payload.length - 18;
  body.set(payload, 2);
  const bytes = new Uint8Array(0x10 + body.length);
  bytes.set([0x57, 0x41, 0x44], 0);
  new DataView(bytes.buffer).setInt32(3, bytes.length, true);
  for (let i = 0; i < name.length && i < 9; i++) bytes[7 + i] = name.charCodeAt(i);
  bytes.set(body, 0x10);
  return bytes;
}

function sha256(bytes) {
  return createHash("sha256").update(bytes).digest("hex");
}

test("public Ratchet executable stream parses fixed headers and section hashes without donor metadata", () => {
  const parsed = parseUyaRatchetExecutableStreamPublicLead(ratchetStream());
  assert.equal(parsed.entryPointU32, 0x00123456);
  assert.equal(parsed.sections.length, 2);
  assert.deepEqual(parsed.sections.map((s) => [s.destAddressU32, s.copySize, s.sectionTypeU32]), [
    [0x00100000, 4, 1],
    [0x00200000, 4, 8],
  ]);
  assert.equal(parsed.consumedBytes, 40);
  assert.equal(parsed.trailingBytes, 0);
  assert.match(parsed.sections[0].dataSha256, /^[0-9a-f]{64}$/);
});

test("differing entry point terminates the public stream before consuming the candidate header", () => {
  const base = ratchetStream();
  const bytes = new Uint8Array(base.length + 16);
  bytes.set(base);
  const view = new DataView(bytes.buffer);
  u32(view, 40 + 0x00, 0x00300000);
  u32(view, 40 + 0x04, 0);
  u32(view, 40 + 0x08, 1);
  u32(view, 40 + 0x0c, 0x00999999);
  const parsed = parseUyaRatchetExecutableStreamPublicLead(bytes);
  assert.equal(parsed.sections.length, 2);
  assert.equal(parsed.terminatorOffset, 40);
  assert.equal(parsed.trailingBytes, 16);
});

test("loader-anchor census reports only candidate sites with section virtual addresses", () => {
  const bytes = loaderAnchorStream();
  const scan = scanUyaLoaderAnchorCandidatesPublicLead(bytes);
  assert.equal(scan.evidenceStatus, "candidate-constant-sites-only-not-loader-proof");
  assert.equal(scan.candidateLba, 1001);
  assert.equal(scan.candidateByteOffset, 0x001f4800);
  assert.deepEqual(scan.hits.map((hit) => hit.kind), [
    "aligned-u32-lba-1001",
    "mips-direct-load-lba-1001",
    "mips-lui-low-pair-byte-offset-0x001f4800",
  ]);
  assert.deepEqual(scan.hits.map((hit) => hit.virtualAddressU32), [
    0x00300000,
    0x00300004,
    0x00300008,
  ]);
  assert.deepEqual(scan.hits.map((hit) => hit.sectionOffset), [0, 4, 8]);
  assert.match(scan.caveat, /not.*loader|promote.*only/i);
});

test("loader-anchor MIPS scan does not treat a nonzero source register as a direct LBA load", () => {
  const bytes = new Uint8Array(0x14);
  const view = new DataView(bytes.buffer);
  u32(view, 0x00, 0x00400000);
  u32(view, 0x04, 4);
  u32(view, 0x08, 1);
  u32(view, 0x0c, 0x00123456);
  u32(view, 0x10, (0x09 << 26) | (7 << 21) | (8 << 16) | 1001); // addiu $t0,$a3,1001
  assert.deepEqual(scanUyaLoaderAnchorCandidatesPublicLead(bytes).hits, []);
});

test("packed executable scanner retains all structurally plausible WAD candidates and ignores invalid raw magic", () => {
  const good = literalWad(ratchetStream());
  const bytes = new Uint8Array(512);
  bytes.set([0x57, 0x41, 0x44, 1, 0, 0, 0], 3); // invalid compressed size
  bytes.set(good, 100);
  bytes.set(good, 300);
  const candidates = scanUyaPackedExecutableWadCandidatesPublicLead(bytes);
  assert.deepEqual(candidates.map((c) => c.offset), [100, 300]);
});

test("existing WAD-LZ decoder can be used as an explicit compatibility test on a selected packed candidate", () => {
  const wad = literalWad(ratchetStream(), "boot");
  const executable = new Uint8Array(64 + wad.length);
  executable.set(wad, 64);
  const candidate = scanUyaPackedExecutableWadCandidatesPublicLead(executable)[0];
  assert.ok(candidate);
  const decoded = decodeUyaPackedWadCandidatePublicLead(executable, candidate);
  assert.equal(decoded.decompressedBytes, 40);
  assert.equal(decoded.ratchetExecutable.sections.length, 2);
  assert.equal(decoded.ratchetExecutable.entryPointU32, 0x00123456);
  assert.equal(decoded.loaderAnchorScan.evidenceStatus, "candidate-constant-sites-only-not-loader-proof");
});

test("bounded boot probe returns exact executable SHA and only decodes a unique candidate when requested", async () => {
  const wad = literalWad(ratchetStream());
  const executable = new Uint8Array(80 + wad.length);
  executable.set(wad, 80);
  const reader = new BlobRandomAccessReader(new Blob([executable]), "SCUS_973.53");
  const result = await probeUyaPackedBootExecutablePublicLead(reader, {
    expectedSha256: sha256(executable),
    decodeUniqueCandidate: true,
  });
  assert.equal(result.sizeBytes, executable.length);
  assert.equal(result.sha256, sha256(executable));
  assert.equal(result.wadCandidates.length, 1);
  assert.equal(result.wadCandidates[0].offset, 80);
  assert.equal(result.decodedUniqueCandidate.ratchetExecutable.sections.length, 2);
  assert.equal(result.decodedUniqueCandidate.loaderAnchorScan.candidateLba, 1001);
});

test("packed semantic scan is refused when the caller-known executable hash mismatches", async () => {
  const executable = new Uint8Array(128);
  // Include raw WAD magic to prove mismatch happens before it can become a semantic candidate.
  executable.set([0x57, 0x41, 0x44], 32);
  const reader = new BlobRandomAccessReader(new Blob([executable]), "wrong-SCUS_973.53");
  await assert.rejects(
    probeUyaPackedBootExecutablePublicLead(reader, {
      expectedSha256: "f".repeat(64),
      decodeUniqueCandidate: true,
    }),
    /identity mismatch.*Refusing to apply packed-executable semantic probes/i,
  );
});
