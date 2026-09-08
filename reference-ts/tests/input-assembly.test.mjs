import test from "node:test";
import assert from "node:assert/strict";
import { createHash } from "node:crypto";
import { mkdtemp, readFile, writeFile, access } from "node:fs/promises";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { assembleChunks, assemblyPlanFromManifest } from "../tools/input-assembly.mjs";

const sha256 = (data) => createHash("sha256").update(data).digest("hex");

test("chunk assembly streams parts in manifest order and verifies hashes", async () => {
  const dir = await mkdtemp(join(tmpdir(), "obp-assembly-"));
  const pieces = [Buffer.from("one-"), Buffer.from("big-"), Buffer.from("package")];
  const chunks = [];
  for (let i = 0; i < pieces.length; i++) {
    const path = join(dir, `part${i + 1}`);
    await writeFile(path, pieces[i]);
    chunks.push({ path, sizeBytes: pieces[i].length, sha256: sha256(pieces[i]) });
  }
  const combined = Buffer.concat(pieces);
  const outputPath = join(dir, "assembled.iso");
  const report = await assembleChunks({
    chunks,
    outputPath,
    expectedSizeBytes: combined.length,
    expectedSha256: sha256(combined),
  });
  assert.deepEqual(await readFile(outputPath), combined);
  assert.equal(report.sizeBytes, combined.length);
  assert.equal(report.sha256, sha256(combined));
  assert.equal(report.chunks.length, 3);
});

test("raw ISO split manifests verify the reconstructed payload identity", () => {
  const plan = assemblyPlanFromManifest({
    payload: { filename: "disc.iso", sizeBytes: 1234, sha256: "a".repeat(64) },
    transport: {
      format: "raw-iso-split",
      chunking: {
        scheme: "binary-concat",
        chunks: [{ file: "disc.iso.001", sizeBytes: 1000 }, { file: "disc.iso.002", sizeBytes: 234 }],
      },
    },
  });

  assert.equal(plan.format, "raw-iso-split");
  assert.equal(plan.expectedSizeBytes, 1234);
  assert.equal(plan.expectedSha256, "a".repeat(64));
  assert.equal(plan.chunking.chunks.length, 2);
});

test("failed verification never promotes a partial output", async () => {
  const dir = await mkdtemp(join(tmpdir(), "obp-assembly-bad-"));
  const part = join(dir, "part1");
  const outputPath = join(dir, "assembled.iso");
  await writeFile(part, Buffer.from("wrong"));
  await assert.rejects(
    assembleChunks({ chunks: [{ path: part }], outputPath, expectedSha256: "0".repeat(64) }),
    /SHA-256 mismatch/,
  );
  await assert.rejects(access(outputPath));
  await assert.rejects(access(`${outputPath}.partial`));
});
