import test from "node:test";
import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import {
  createNumberedSplitBlobReader,
  createNumberedSplitBlobReaderFromManifest,
  orderNumberedSplitParts,
  readRawIsoSplitManifestContract,
  splitSourceOptionsFromManifest,
} from "../.build/packages/input-sources/src/index.js";

function part(name, bytes) {
  return { name, blob: new Blob([Uint8Array.from(bytes)]) };
}

async function readManifest(name) {
  return JSON.parse(await readFile(new URL(`../../research/manifests/${name}`, import.meta.url), "utf8"));
}

function testManifest() {
  return {
    schemaVersion: 2,
    game: "rac2",
    buildId: "test-build",
    region: "NTSC-U",
    serial: "SCUS-97268",
    revision: "test revision",
    authority: "primary",
    payload: { filename: "disc.iso", sizeBytes: 5, sha256: "a".repeat(64) },
    transport: {
      format: "raw-iso-split",
      chunking: {
        scheme: "binary-concat",
        chunks: [
          { file: "disc.iso.001", sizeBytes: 3 },
          { file: "disc.iso.002", sizeBytes: 2 },
        ],
      },
    },
  };
}

test("numbered split source sorts arbitrary selection order without reading whole parts", async () => {
  const parts = [
    part("disc.iso.003", [6, 7]),
    part("disc.iso.001", [1, 2, 3]),
    part("disc.iso.002", [4, 5]),
  ];
  const reader = createNumberedSplitBlobReader(parts, { expectedSizeBytes: 7 });
  assert.equal(reader.name, "disc.iso");
  assert.equal(reader.size, 7);
  assert.deepEqual([...(await reader.read(2, 4))], [3, 4, 5, 6]);
});

test("split source can validate manifest-provided names and sizes from metadata only", () => {
  const parts = [part("disc.iso.002", [4, 5]), part("disc.iso.001", [1, 2, 3])];
  const reader = createNumberedSplitBlobReader(parts, {
    logicalName: "authority.iso",
    expectedParts: [
      { name: "disc.iso.001", sizeBytes: 3 },
      { name: "disc.iso.002", sizeBytes: 2 },
    ],
    expectedSizeBytes: 5,
  });
  assert.equal(reader.name, "authority.iso");
  assert.equal(reader.size, 5);
});

test("canonical trilogy manifests produce strict browser split-selection contracts", async () => {
  const cases = [
    ["rac1-ntscu.json", "rac1", "rac1-ntscu-original", "SCUS-97199", 9, 4214095872],
    ["rac2-ntscu-v1.01.json", "rac2", "rac2-ntscu-v1.01", "SCUS-97268", 8, 3828350976],
    ["rac3-ntscu.json", "rac3", "rac3-ntscu-original", "SCUS-97353", 9, 4379377664],
  ];

  for (const [filename, game, buildId, serial, partCount, sizeBytes] of cases) {
    const manifest = await readManifest(filename);
    const contract = readRawIsoSplitManifestContract(manifest);
    const options = splitSourceOptionsFromManifest(manifest);
    assert.equal(contract.schemaVersion, 2);
    assert.equal(contract.game, game);
    assert.equal(contract.buildId, buildId);
    assert.equal(contract.serial, serial);
    assert.equal(contract.payload.sizeBytes, sizeBytes);
    assert.equal(contract.transport.chunking.chunks.length, partCount);
    assert.equal(options.expectedParts.length, partCount);
    assert.equal(options.expectedSizeBytes, sizeBytes);
    assert.equal(options.logicalName, contract.payload.filename);
    assert.equal(options.expectedParts[0].name, `${contract.payload.filename}.001`);
  }
});

test("manifest-bound split reader validates selected names and sizes before byte reads", () => {
  const manifest = testManifest();
  const reader = createNumberedSplitBlobReaderFromManifest(
    [part("disc.iso.002", [4, 5]), part("disc.iso.001", [1, 2, 3])],
    manifest,
  );
  assert.equal(reader.name, "disc.iso");
  assert.equal(reader.size, 5);

  assert.throws(
    () => createNumberedSplitBlobReaderFromManifest([part("disc.iso.001", [1, 2, 3])], manifest),
    /part count mismatch/,
  );
  assert.throws(
    () => createNumberedSplitBlobReaderFromManifest(
      [part("disc.iso.001", [1, 2]), part("disc.iso.002", [3, 4, 5])],
      manifest,
    ),
    /size mismatch/,
  );
});

test("raw split manifest validation rejects inconsistent or unsafe transport metadata", () => {
  const valid = testManifest();

  assert.throws(
    () => readRawIsoSplitManifestContract({ ...valid, payload: { ...valid.payload, sizeBytes: 6 } }),
    /sum to 5 bytes, but payload declares 6/,
  );
  assert.throws(
    () => readRawIsoSplitManifestContract({ ...valid, transport: { ...valid.transport, format: "7z" } }),
    /raw-iso-split/,
  );
  assert.throws(
    () => readRawIsoSplitManifestContract({ ...valid, payload: { ...valid.payload, sha256: "not-a-hash" } }),
    /SHA-256/,
  );
  assert.throws(
    () => readRawIsoSplitManifestContract({ ...valid, schemaVersion: 3 }),
    /schemaVersion 3/,
  );
  assert.throws(
    () => readRawIsoSplitManifestContract({ ...valid, authority: "secondary" }),
    /authority must be 'primary'/,
  );
  assert.throws(
    () => readRawIsoSplitManifestContract({
      ...valid,
      transport: {
        ...valid.transport,
        chunking: {
          ...valid.transport.chunking,
          chunks: [
            { file: "disc.iso.001", sizeBytes: 3 },
            { file: "disc.iso.003", sizeBytes: 2 },
          ],
        },
      },
    }),
    /not contiguous/,
  );
});

test("split source rejects gaps, duplicate positions, mixed stems and wrong manifest metadata", () => {
  assert.throws(
    () => orderNumberedSplitParts([part("disc.iso.001", [1]), part("disc.iso.003", [3])]),
    /not contiguous/,
  );
  assert.throws(
    () => orderNumberedSplitParts([part("disc.iso.001", [1]), part("disc.iso.001", [2])]),
    /not contiguous/,
  );
  assert.throws(
    () => orderNumberedSplitParts([part("a.iso.001", [1]), part("b.iso.002", [2])]),
    /filename stem/,
  );
  assert.throws(
    () => createNumberedSplitBlobReader([part("disc.iso.001", [1])], { expectedParts: [{ name: "disc.iso.001", sizeBytes: 2 }] }),
    /size mismatch/,
  );
  assert.throws(
    () => createNumberedSplitBlobReader([part("disc.iso.001", [1])], { expectedSizeBytes: 2 }),
    /source size mismatch/,
  );
});
