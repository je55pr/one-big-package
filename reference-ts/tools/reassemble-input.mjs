import { readFile } from "node:fs/promises";
import { resolve } from "node:path";
import { assembleChunks, assemblyPlanFromManifest } from "./input-assembly.mjs";

const [manifestArg, chunksDirArg, outputArg] = process.argv.slice(2);
if (!manifestArg || !chunksDirArg || !outputArg) {
  console.error("Usage: node tools/reassemble-input.mjs <manifest.json> <chunks-dir> <output-file>");
  process.exit(2);
}

const manifestPath = resolve(manifestArg);
const chunksDir = resolve(chunksDirArg);
const outputPath = resolve(outputArg);
const manifest = JSON.parse(await readFile(manifestPath, "utf8"));
const plan = assemblyPlanFromManifest(manifest);

const chunks = plan.chunking.chunks.map((chunk, index) => {
  if (!chunk.file) throw new Error(`Manifest chunk ${index} is missing 'file'.`);
  return {
    path: resolve(chunksDir, chunk.file),
    ...(chunk.sizeBytes == null ? {} : { sizeBytes: chunk.sizeBytes }),
    ...(chunk.sha256 ? { sha256: chunk.sha256 } : {}),
  };
});

const report = await assembleChunks({
  chunks,
  outputPath,
  expectedSizeBytes: plan.expectedSizeBytes,
  expectedSha256: plan.expectedSha256,
  onProgress: ({ index, count, totalBytes }) => {
    if (process.stderr.isTTY) process.stderr.write(`\rchunk ${index + 1}/${count} · ${totalBytes.toLocaleString()} bytes`);
  },
});
if (process.stderr.isTTY) process.stderr.write("\n");
console.log(JSON.stringify({ transportFormat: plan.format, ...report }, null, 2));
