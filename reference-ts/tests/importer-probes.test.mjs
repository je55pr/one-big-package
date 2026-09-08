import test from "node:test";
import assert from "node:assert/strict";
import { BlobRandomAccessReader } from "../.build/packages/importer-common/src/index.js";
import { rac1Importer, RAC1_PRIMARY_AUTHORITY } from "../.build/packages/importer-rac1/src/index.js";
import { rac2Importer, RAC2_PRIMARY_AUTHORITY } from "../.build/packages/importer-rac2/src/index.js";
import { rac3Importer, RAC3_PRIMARY_AUTHORITY } from "../.build/packages/importer-rac3/src/index.js";
import { syntheticPs2Iso } from "./helpers/synthetic-ps2.mjs";

const context = { log() {} };
function source(serialExecutable, identityHint, isoOptions) {
  const disc = new BlobRandomAccessReader(new Blob([syntheticPs2Iso(serialExecutable, isoOptions)]), "disc.iso");
  return { files: new Map([["disc", disc]]), ...(identityHint ? { identityHint } : {}) };
}

test("game importers strongly identify matching PS2 serial plus ELF32 MIPS boot structure without claiming exact revisions", async () => {
  for (const [importer, executable] of [
    [rac1Importer, "SCUS_971.99"],
    [rac2Importer, "SCUS_972.68"],
    [rac3Importer, "SCUS_973.53"],
  ]) {
    const result = await importer.probe(source(executable), context);
    assert.equal(result.confidence, "strong");
    assert.match(result.reasons.join(" "), /ELF32 MIPS executable/);
  }
});

test("verified primary payload hashes upgrade matching serial probes to exact", async () => {
  const result = await rac2Importer.probe(source("SCUS_972.68", { sha256: RAC2_PRIMARY_AUTHORITY.sha256 }), context);
  assert.equal(result.confidence, "exact");
  assert.match(result.reasons.join(" "), /primary authority/);
});

test("wrong game serials are rejected and same-serial hash mismatches stay strong", async () => {
  assert.equal((await rac1Importer.probe(source("SCUS_973.53"), context)).confidence, "none");
  const result = await rac1Importer.probe(source("SCUS_971.99", { sha256: "0".repeat(64) }), context);
  assert.equal(result.confidence, "strong");
  assert.match(result.reasons.join(" "), /differs from primary authority/);
});

test("matching SYSTEM.CNF serials with missing or non-MIPS boot targets are rejected", async () => {
  const missing = await rac1Importer.probe(source("SCUS_971.99", undefined, { includeBootExecutable: false }), context);
  assert.equal(missing.confidence, "none");
  assert.match(missing.reasons.join(" "), /boot executable .* was not found/i);

  const wrongMachine = await rac1Importer.probe(source("SCUS_971.99", undefined, { elfMachine: 3 }), context);
  assert.equal(wrongMachine.confidence, "none");
  assert.match(wrongMachine.reasons.join(" "), /not MIPS/);
});

test("authority constants match current primary trilogy identities", () => {
  assert.equal(RAC1_PRIMARY_AUTHORITY.serial, "SCUS-97199");
  assert.equal(RAC2_PRIMARY_AUTHORITY.serial, "SCUS-97268");
  assert.equal(RAC3_PRIMARY_AUTHORITY.serial, "SCUS-97353");
});
