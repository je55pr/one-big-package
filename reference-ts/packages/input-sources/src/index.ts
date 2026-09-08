import {
  BlobRandomAccessReader,
  ConcatenatedRandomAccessReader,
} from "../../importer-common/src/index.js";

export interface NamedBlobPart {
  readonly name: string;
  readonly blob: Blob;
}

export interface ExpectedSplitPart {
  readonly name: string;
  readonly sizeBytes?: number;
}

export interface NumberedSplitSourceOptions {
  readonly logicalName?: string;
  readonly expectedParts?: readonly ExpectedSplitPart[];
  readonly expectedSizeBytes?: number;
}

export interface OrderedSplitPart extends NamedBlobPart {
  readonly index: number;
  readonly stem: string;
}

export interface RawIsoSplitManifestChunk {
  readonly file: string;
  readonly sizeBytes: number;
}

export interface RawIsoSplitManifestContract {
  readonly schemaVersion: 2;
  readonly game: string;
  readonly buildId: string;
  readonly region: string;
  readonly serial: string;
  readonly revision: string;
  readonly authority: "primary";
  readonly payload: {
    readonly filename: string;
    readonly sizeBytes: number;
    readonly sha256: string;
  };
  readonly transport: {
    readonly format: "raw-iso-split";
    readonly chunking: {
      readonly scheme: "binary-concat";
      readonly chunks: readonly RawIsoSplitManifestChunk[];
    };
  };
}

/**
 * Validate the subset of an OBP authority manifest needed to bind a browser split-file selection.
 * Unknown extra manifest fields are intentionally ignored so research notes/UI metadata can evolve
 * without coupling the byte-source layer to them. Schema-version changes still require review.
 */
export function splitSourceOptionsFromManifest(manifest: unknown): NumberedSplitSourceOptions {
  const contract = readRawIsoSplitManifestContract(manifest);
  return {
    logicalName: contract.payload.filename,
    expectedSizeBytes: contract.payload.sizeBytes,
    expectedParts: contract.transport.chunking.chunks.map((chunk) => ({
      name: chunk.file,
      sizeBytes: chunk.sizeBytes,
    })),
  };
}

/** Read and validate the runtime-relevant raw split authority contract from arbitrary JSON data. */
export function readRawIsoSplitManifestContract(manifest: unknown): RawIsoSplitManifestContract {
  const root = asRecord(manifest, "manifest");
  const schemaVersion = requiredSafeInteger(root.schemaVersion, "manifest.schemaVersion", 1);
  if (schemaVersion !== 2) throw new Error(`Unsupported raw split manifest schemaVersion ${schemaVersion}; expected 2.`);
  const game = requiredNonEmptyString(root.game, "manifest.game");
  const buildId = requiredNonEmptyString(root.buildId, "manifest.buildId");
  const region = requiredNonEmptyString(root.region, "manifest.region");
  const serial = requiredNonEmptyString(root.serial, "manifest.serial");
  const revision = requiredNonEmptyString(root.revision, "manifest.revision");
  if (root.authority !== "primary") throw new Error(`manifest.authority must be 'primary', got ${describe(root.authority)}.`);

  const payloadRecord = asRecord(root.payload, "manifest.payload");
  const payload = {
    filename: requiredNonEmptyString(payloadRecord.filename, "manifest.payload.filename"),
    sizeBytes: requiredSafeInteger(payloadRecord.sizeBytes, "manifest.payload.sizeBytes", 0),
    sha256: requiredSha256(payloadRecord.sha256, "manifest.payload.sha256"),
  };

  const transportRecord = asRecord(root.transport, "manifest.transport");
  if (transportRecord.format !== "raw-iso-split") {
    throw new Error(`manifest.transport.format must be 'raw-iso-split', got ${describe(transportRecord.format)}.`);
  }
  const chunkingRecord = asRecord(transportRecord.chunking, "manifest.transport.chunking");
  if (chunkingRecord.scheme !== "binary-concat") {
    throw new Error(`manifest.transport.chunking.scheme must be 'binary-concat', got ${describe(chunkingRecord.scheme)}.`);
  }
  if (!Array.isArray(chunkingRecord.chunks) || chunkingRecord.chunks.length === 0) {
    throw new Error("manifest.transport.chunking.chunks must be a non-empty array.");
  }

  const chunks = chunkingRecord.chunks.map((value, index) => {
    const chunk = asRecord(value, `manifest.transport.chunking.chunks[${index}]`);
    return {
      file: requiredNonEmptyString(chunk.file, `manifest.transport.chunking.chunks[${index}].file`),
      sizeBytes: requiredSafeInteger(chunk.sizeBytes, `manifest.transport.chunking.chunks[${index}].sizeBytes`, 0),
    };
  });

  const names = new Set<string>();
  let sum = 0;
  for (const chunk of chunks) {
    if (names.has(chunk.file)) throw new Error(`Manifest contains duplicate split chunk '${chunk.file}'.`);
    names.add(chunk.file);
    sum += chunk.sizeBytes;
    if (!Number.isSafeInteger(sum)) throw new RangeError("Manifest split chunk size sum exceeds JavaScript safe integer range.");
  }
  if (sum !== payload.sizeBytes) {
    throw new Error(`Manifest split chunk sizes sum to ${sum} bytes, but payload declares ${payload.sizeBytes}.`);
  }

  // Reuse the exact numbered-name sequencing rules applied to a real browser selection.
  orderNumberedSplitNames(chunks.map((chunk) => chunk.file));

  return {
    schemaVersion: 2,
    game,
    buildId,
    region,
    serial,
    revision,
    authority: "primary",
    payload,
    transport: {
      format: "raw-iso-split",
      chunking: { scheme: "binary-concat", chunks },
    },
  };
}

/**
 * Validate and order numbered files such as disc.iso.001, disc.iso.002, ... without reading bytes.
 * The sequence must start at 1, remain contiguous, and use one common filename stem.
 */
export function orderNumberedSplitParts(parts: readonly NamedBlobPart[]): readonly OrderedSplitPart[] {
  if (parts.length === 0) throw new Error("At least one numbered split part is required.");
  const orderedNames = orderNumberedSplitNames(parts.map((part) => part.name));
  const byName = new Map(parts.map((part) => [part.name, part]));
  return orderedNames.map(({ name, index, stem }) => {
    const part = byName.get(name);
    if (!part) throw new Error(`Split part '${name}' disappeared during ordering.`);
    return { ...part, index, stem };
  });
}

function orderNumberedSplitNames(names: readonly string[]): readonly { name: string; index: number; stem: string }[] {
  if (names.length === 0) throw new Error("At least one numbered split part is required.");
  const parsed = names.map((name) => {
    const match = /^(.*)\.([0-9]{3,})$/.exec(name);
    if (!match?.[1] || !match[2]) throw new Error(`Split part '${name}' does not end in a numeric suffix such as .001.`);
    const index = Number.parseInt(match[2], 10);
    if (!Number.isSafeInteger(index) || index <= 0) throw new Error(`Split part '${name}' has an invalid part number.`);
    return { name, index, stem: match[1] };
  });

  const stem = parsed[0]?.stem;
  if (!stem) throw new Error("Split source has no filename stem.");
  for (const part of parsed) {
    if (part.stem !== stem) throw new Error(`Split parts do not share one filename stem: '${stem}' vs '${part.stem}'.`);
  }

  parsed.sort((a, b) => a.index - b.index);
  for (let position = 0; position < parsed.length; position++) {
    const part = parsed[position];
    const expectedIndex = position + 1;
    if (!part || part.index !== expectedIndex) {
      const actual = part?.index ?? "missing";
      throw new Error(`Split part sequence is not contiguous: expected part ${expectedIndex}, got ${actual}.`);
    }
  }
  return parsed;
}

/** Build one logical random-access source from validated browser Blob/File parts. */
export function createNumberedSplitBlobReader(
  parts: readonly NamedBlobPart[],
  options: NumberedSplitSourceOptions = {},
): ConcatenatedRandomAccessReader {
  const ordered = orderNumberedSplitParts(parts);
  validateExpectedParts(ordered, options.expectedParts);

  const totalSize = ordered.reduce((sum, part) => sum + part.blob.size, 0);
  if (!Number.isSafeInteger(totalSize)) throw new RangeError("Split source size exceeds JavaScript safe integer range.");
  if (options.expectedSizeBytes !== undefined && totalSize !== options.expectedSizeBytes) {
    throw new Error(`Split source size mismatch: expected ${options.expectedSizeBytes}, got ${totalSize}.`);
  }

  const readers = ordered.map((part) => new BlobRandomAccessReader(part.blob, part.name));
  return new ConcatenatedRandomAccessReader(readers, options.logicalName ?? ordered[0]?.stem ?? "split-source");
}

/** Build a logical reader from browser parts bound directly to a validated raw-split authority manifest. */
export function createNumberedSplitBlobReaderFromManifest(
  parts: readonly NamedBlobPart[],
  manifest: unknown,
): ConcatenatedRandomAccessReader {
  return createNumberedSplitBlobReader(parts, splitSourceOptionsFromManifest(manifest));
}

/** Browser convenience wrapper; no file bytes are read during construction. */
export function createNumberedSplitFileReader(
  files: Iterable<File>,
  options: NumberedSplitSourceOptions = {},
): ConcatenatedRandomAccessReader {
  const parts = [...files].map((file) => ({ name: file.name, blob: file }));
  return createNumberedSplitBlobReader(parts, options);
}

/** Browser convenience wrapper bound directly to a validated raw-split authority manifest. */
export function createNumberedSplitFileReaderFromManifest(
  files: Iterable<File>,
  manifest: unknown,
): ConcatenatedRandomAccessReader {
  return createNumberedSplitFileReader(files, splitSourceOptionsFromManifest(manifest));
}

function validateExpectedParts(
  ordered: readonly OrderedSplitPart[],
  expected: readonly ExpectedSplitPart[] | undefined,
): void {
  if (!expected) return;
  if (ordered.length !== expected.length) {
    throw new Error(`Split part count mismatch: expected ${expected.length}, got ${ordered.length}.`);
  }

  for (let index = 0; index < expected.length; index++) {
    const actual = ordered[index];
    const wanted = expected[index];
    if (!actual || !wanted) throw new Error(`Missing split part metadata at position ${index + 1}.`);
    if (actual.name !== wanted.name) {
      throw new Error(`Split part name mismatch at position ${index + 1}: expected '${wanted.name}', got '${actual.name}'.`);
    }
    if (wanted.sizeBytes !== undefined && actual.blob.size !== wanted.sizeBytes) {
      throw new Error(`Split part size mismatch for '${actual.name}': expected ${wanted.sizeBytes}, got ${actual.blob.size}.`);
    }
  }
}

function asRecord(value: unknown, label: string): Readonly<Record<string, unknown>> {
  if (!value || typeof value !== "object" || Array.isArray(value)) throw new Error(`${label} must be an object.`);
  return value as Readonly<Record<string, unknown>>;
}

function requiredNonEmptyString(value: unknown, label: string): string {
  if (typeof value !== "string" || value.trim().length === 0) throw new Error(`${label} must be a non-empty string.`);
  return value;
}

function requiredSafeInteger(value: unknown, label: string, minimum: number): number {
  if (typeof value !== "number" || !Number.isSafeInteger(value) || value < minimum) {
    throw new Error(`${label} must be a safe integer >= ${minimum}.`);
  }
  return value;
}

function requiredSha256(value: unknown, label: string): string {
  if (typeof value !== "string" || !/^[0-9a-f]{64}$/i.test(value)) throw new Error(`${label} must be a 64-digit hexadecimal SHA-256.`);
  return value.toLowerCase();
}

function describe(value: unknown): string {
  return typeof value === "string" ? `'${value}'` : String(value);
}
