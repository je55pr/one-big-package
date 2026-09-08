import { createHash } from "node:crypto";
import { createReadStream, createWriteStream } from "node:fs";
import { mkdir, rename, rm, stat } from "node:fs/promises";
import { dirname } from "node:path";
import { once } from "node:events";

export async function hashFile(path) {
  const hash = createHash("sha256");
  let sizeBytes = 0;
  for await (const chunk of createReadStream(path, { highWaterMark: 8 * 1024 * 1024 })) {
    hash.update(chunk);
    sizeBytes += chunk.length;
  }
  return { sizeBytes, sha256: hash.digest("hex") };
}

export function assemblyPlanFromManifest(manifest) {
  const transport = manifest?.transport ?? manifest;
  const chunking = transport?.chunking;
  if (!chunking || chunking.scheme !== "binary-concat" || !Array.isArray(chunking.chunks) || chunking.chunks.length === 0) {
    throw new Error("Manifest transport must contain chunking.scheme='binary-concat' and a non-empty chunking.chunks array.");
  }

  const verification = transport.format === "raw-iso-split" && manifest?.payload ? manifest.payload : transport;
  return {
    transport,
    chunking,
    format: transport.format ?? "raw",
    expectedSizeBytes: verification.sizeBytes ?? undefined,
    expectedSha256: verification.sha256 ?? undefined,
  };
}

export async function assembleChunks({ chunks, outputPath, expectedSizeBytes, expectedSha256, onProgress = () => {} }) {
  if (!Array.isArray(chunks) || chunks.length === 0) throw new Error("At least one input chunk is required.");
  await mkdir(dirname(outputPath), { recursive: true });
  const partialPath = `${outputPath}.partial`;
  await rm(partialPath, { force: true });

  const output = createWriteStream(partialPath, { flags: "wx" });
  const overallHash = createHash("sha256");
  const report = [];
  let totalBytes = 0;

  try {
    for (let index = 0; index < chunks.length; index++) {
      const chunk = chunks[index];
      if (!chunk?.path) throw new Error(`Chunk ${index} has no path.`);
      const info = await stat(chunk.path);
      if (!info.isFile()) throw new Error(`Chunk is not a file: ${chunk.path}`);
      if (chunk.sizeBytes != null && info.size !== chunk.sizeBytes) {
        throw new Error(`Chunk size mismatch for ${chunk.path}: expected ${chunk.sizeBytes}, got ${info.size}`);
      }

      const chunkHash = createHash("sha256");
      let chunkBytes = 0;
      for await (const data of createReadStream(chunk.path, { highWaterMark: 8 * 1024 * 1024 })) {
        chunkHash.update(data);
        overallHash.update(data);
        chunkBytes += data.length;
        totalBytes += data.length;
        if (!output.write(data)) await once(output, "drain");
        onProgress({ index, count: chunks.length, chunkBytes, totalBytes });
      }

      const sha256 = chunkHash.digest("hex");
      if (chunk.sha256 && sha256.toLowerCase() !== String(chunk.sha256).toLowerCase()) {
        throw new Error(`Chunk SHA-256 mismatch for ${chunk.path}: expected ${chunk.sha256}, got ${sha256}`);
      }
      report.push({ path: chunk.path, sizeBytes: chunkBytes, sha256 });
    }

    output.end();
    await once(output, "close");

    const sha256 = overallHash.digest("hex");
    if (expectedSizeBytes != null && totalBytes !== expectedSizeBytes) {
      throw new Error(`Assembled size mismatch: expected ${expectedSizeBytes}, got ${totalBytes}`);
    }
    if (expectedSha256 && sha256.toLowerCase() !== String(expectedSha256).toLowerCase()) {
      throw new Error(`Assembled SHA-256 mismatch: expected ${expectedSha256}, got ${sha256}`);
    }

    await rm(outputPath, { force: true });
    await rename(partialPath, outputPath);
    return { outputPath, sizeBytes: totalBytes, sha256, chunks: report };
  } catch (error) {
    output.destroy();
    await rm(partialPath, { force: true });
    throw error;
  }
}
