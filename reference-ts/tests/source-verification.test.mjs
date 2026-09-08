import test from "node:test";
import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { readFile } from "node:fs/promises";
import { BlobRandomAccessReader } from "../.build/packages/importer-common/src/index.js";
import { RAC1_PRIMARY_AUTHORITY } from "../.build/packages/importer-rac1/src/index.js";
import { RAC2_PRIMARY_AUTHORITY } from "../.build/packages/importer-rac2/src/index.js";
import { RAC3_PRIMARY_AUTHORITY } from "../.build/packages/importer-rac3/src/index.js";
import {
  createVerifiedDiscProbeSource,
  verifyRawIsoSplitSource,
} from "../.build/packages/source-verification/src/index.js";
import { readRawIsoSplitManifestContract } from "../.build/packages/input-sources/src/index.js";

function sha256(bytes) {
  return createHash("sha256").update(bytes).digest("hex");
}

function manifestFor(bytes, overrides = {}) {
  return {
    schemaVersion: 2,
    game: "rac2",
    buildId: "test-build",
    region: "NTSC-U",
    serial: "SCUS-97268",
    revision: "test revision",
    authority: "primary",
    payload: { filename: "disc.iso", sizeBytes: bytes.length, sha256: sha256(bytes) },
    transport: {
      format: "raw-iso-split",
      chunking: {
        scheme: "binary-concat",
        chunks: [{ file: "disc.iso.001", sizeBytes: bytes.length }],
      },
    },
    ...overrides,
  };
}

async function readManifest(name) {
  return JSON.parse(await readFile(new URL(`../../research/manifests/${name}`, import.meta.url), "utf8"));
}

test("verified raw split source resolves exact OBP build identity only after bounded hash verification", async () => {
  const bytes = Uint8Array.from({ length: 4097 }, (_, index) => (index * 31 + 11) & 0xff);
  const reader = new BlobRandomAccessReader(new Blob([bytes]), "disc.iso");
  const progress = [];
  const verified = await verifyRawIsoSplitSource(reader, manifestFor(bytes), {
    chunkSize: 127,
    onProgress(value) { progress.push(value); },
  });

  assert.deepEqual(verified.identity, {
    game: "rac2",
    buildId: "test-build",
    region: "NTSC-U",
    serial: "SCUS-97268",
    revision: "test revision",
    sha256: sha256(bytes),
  });
  assert.equal(verified.sizeBytes, bytes.length);
  assert.deepEqual(progress.at(-1), { bytesRead: bytes.length, totalBytes: bytes.length });

  const probeSource = createVerifiedDiscProbeSource(reader, verified);
  assert.equal(probeSource.files.get("disc"), reader);
  assert.equal(probeSource.identityHint.sha256, sha256(bytes));
});

test("verification rejects wrong size before hashing and wrong SHA after hashing", async () => {
  const bytes = Uint8Array.from([1, 2, 3, 4]);
  let reads = 0;
  const reader = {
    name: "disc.iso",
    size: bytes.length,
    async read(offset, length) { reads += 1; return bytes.slice(offset, offset + length); },
  };

  const wrongSize = manifestFor(bytes);
  wrongSize.payload = { ...wrongSize.payload, sizeBytes: bytes.length + 1 };
  wrongSize.transport = {
    ...wrongSize.transport,
    chunking: {
      ...wrongSize.transport.chunking,
      chunks: [{ file: "disc.iso.001", sizeBytes: bytes.length + 1 }],
    },
  };
  await assert.rejects(() => verifyRawIsoSplitSource(reader, wrongSize), /Source size mismatch/);
  assert.equal(reads, 0);

  const wrongHash = manifestFor(bytes);
  wrongHash.payload = { ...wrongHash.payload, sha256: "0".repeat(64) };
  await assert.rejects(() => verifyRawIsoSplitSource(reader, wrongHash, { chunkSize: 2 }), /SHA-256 mismatch/);
  assert.ok(reads > 0);
});

test("canonical trilogy manifests stay synchronized with importer primary authority constants", async () => {
  const cases = [
    ["rac1-ntscu.json", RAC1_PRIMARY_AUTHORITY],
    ["rac2-ntscu-v1.01.json", RAC2_PRIMARY_AUTHORITY],
    ["rac3-ntscu.json", RAC3_PRIMARY_AUTHORITY],
  ];

  for (const [filename, authority] of cases) {
    const manifest = readRawIsoSplitManifestContract(await readManifest(filename));
    assert.deepEqual(
      {
        game: manifest.game,
        buildId: manifest.buildId,
        region: manifest.region,
        serial: manifest.serial,
        revision: manifest.revision,
        sha256: manifest.payload.sha256,
      },
      authority,
      filename,
    );
  }
});
