import test from "node:test";
import assert from "node:assert/strict";
import { BlobRandomAccessReader } from "../.build/packages/importer-common/src/index.js";
import { probeAndSelectImporter, probeImporters, selectImporter } from "../.build/packages/importer-registry/src/index.js";
import { RAC2_PRIMARY_AUTHORITY } from "../.build/packages/importer-rac2/src/index.js";
import { TRILOGY_IMPORTERS } from "../.build/packages/trilogy-importers/src/index.js";
import { syntheticPs2Iso } from "./helpers/synthetic-ps2.mjs";

const context = { log() {} };

class RecordingReader {
  constructor(inner) {
    this.inner = inner;
    this.name = inner.name;
    this.size = inner.size;
    this.reads = [];
  }
  async read(offset, length) {
    this.reads.push([offset, length]);
    return this.inner.read(offset, length);
  }
}

function source(serialExecutable, identityHint, { recordReads = false } = {}) {
  const inner = new BlobRandomAccessReader(new Blob([syntheticPs2Iso(serialExecutable)]), "disc.iso");
  const disc = recordReads ? new RecordingReader(inner) : inner;
  return {
    source: { files: new Map([["disc", disc]]), ...(identityHint ? { identityHint } : {}) },
    disc,
  };
}

test("trilogy registry selects the unique matching serial", async () => {
  const fixture = source("SCUS_972.68");
  const selection = await probeAndSelectImporter(TRILOGY_IMPORTERS, fixture.source, context);
  assert.equal(selection.status, "match");
  assert.equal(selection.candidate.importer.id, "rac2");
  assert.equal(selection.candidate.result.confidence, "strong");
  assert.deepEqual(selection.candidates.map((candidate) => candidate.importer.id), ["rac2", "rac1", "rac3"]);
});

test("trilogy registry shares one bounded PS2 boot probe across all importers", async () => {
  const fixture = source("SCUS_972.68", undefined, { recordReads: true });
  const selection = await probeAndSelectImporter(TRILOGY_IMPORTERS, fixture.source, context);
  assert.equal(selection.status, "match");
  assert.equal(selection.candidate.importer.id, "rac2");
  assert.equal(fixture.disc.reads.length, 5);
  assert.deepEqual(fixture.disc.reads.map(([, length]) => length), [2048, 2048, 57, 2048, 52]);
});

test("verified primary hash produces an exact selected match", async () => {
  const fixture = source("SCUS_972.68", { sha256: RAC2_PRIMARY_AUTHORITY.sha256 });
  const selection = await probeAndSelectImporter(TRILOGY_IMPORTERS, fixture.source, context);
  assert.equal(selection.status, "match");
  assert.equal(selection.candidate.importer.id, "rac2");
  assert.equal(selection.candidate.result.confidence, "exact");
});

test("unknown serial yields no importer match", async () => {
  const fixture = source("SCUS_999.99");
  const selection = await probeAndSelectImporter(TRILOGY_IMPORTERS, fixture.source, context);
  assert.equal(selection.status, "none");
  assert.ok(selection.candidates.every((candidate) => candidate.result.confidence === "none"));
});

test("equal best confidence is ambiguous instead of registration-order dependent", () => {
  const importer = (id) => ({ id, game: "rac1", async probe() { return { confidence: "strong", reasons: [] }; }, async importWorld() { throw new Error("unused"); } });
  const selection = selectImporter([
    { importer: importer("b"), result: { confidence: "strong", reasons: ["b"] } },
    { importer: importer("a"), result: { confidence: "strong", reasons: ["a"] } },
    { importer: importer("c"), result: { confidence: "possible", reasons: ["c"] } },
  ]);
  assert.equal(selection.status, "ambiguous");
  assert.deepEqual(selection.tied.map((candidate) => candidate.importer.id), ["a", "b"]);
});

test("registry rejects duplicate importer ids before probing", async () => {
  const duplicate = [TRILOGY_IMPORTERS[0], TRILOGY_IMPORTERS[0]];
  const fixture = source("SCUS_971.99");
  await assert.rejects(probeImporters(duplicate, fixture.source, context), /Duplicate OBP importer id/);
});
