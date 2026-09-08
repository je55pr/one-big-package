import type { OBPSourceGame, OBPWorld } from "../../core/src/index.js";

/** Random-access byte source. Native game files, File handles and OPFS files can all adapt to this. */
export interface RandomAccessReader {
  readonly name: string;
  readonly size: number;
  read(offset: number, length: number): Promise<Uint8Array>;
}

/** Browser File/Blob adapter. Reads only the requested byte range; the whole source is never materialized. */
export class BlobRandomAccessReader implements RandomAccessReader {
  readonly name: string;
  readonly size: number;

  constructor(private readonly blob: Blob, name?: string) {
    this.name = name ?? ("name" in blob && typeof blob.name === "string" ? blob.name : "blob");
    this.size = blob.size;
  }

  async read(offset: number, length: number): Promise<Uint8Array> {
    assertReadRange(this, offset, length);
    if (length === 0) return new Uint8Array();
    return new Uint8Array(await this.blob.slice(offset, offset + length).arrayBuffer());
  }
}

/**
 * Presents ordered split files as one logical random-access source without reassembling them.
 * Reads crossing a part boundary request only the intersecting ranges from each underlying reader.
 */
export class ConcatenatedRandomAccessReader implements RandomAccessReader {
  readonly name: string;
  readonly size: number;
  private readonly starts: readonly number[];

  constructor(private readonly parts: readonly RandomAccessReader[], name = "concatenated") {
    if (parts.length === 0) throw new Error("At least one source part is required.");

    const starts: number[] = [];
    let size = 0;
    for (const part of parts) {
      if (!Number.isSafeInteger(part.size) || part.size < 0) {
        throw new RangeError(`Invalid source size for ${part.name}: ${part.size}`);
      }
      starts.push(size);
      size += part.size;
      if (!Number.isSafeInteger(size)) throw new RangeError("Concatenated source exceeds JavaScript safe integer range.");
    }

    this.name = name;
    this.size = size;
    this.starts = starts;
  }

  async read(offset: number, length: number): Promise<Uint8Array> {
    assertReadRange(this, offset, length);
    if (length === 0) return new Uint8Array();

    const output = new Uint8Array(length);
    let outputOffset = 0;
    let sourceOffset = offset;
    let partIndex = this.findPartIndex(sourceOffset);

    while (outputOffset < length) {
      const part = this.parts[partIndex];
      const partStart = this.starts[partIndex];
      if (!part || partStart === undefined) throw new Error(`No source part covers offset ${sourceOffset}.`);

      const localOffset = sourceOffset - partStart;
      const readLength = Math.min(length - outputOffset, part.size - localOffset);
      if (readLength <= 0) {
        partIndex += 1;
        continue;
      }

      const data = await part.read(localOffset, readLength);
      if (data.length !== readLength) {
        throw new Error(`Short read from ${part.name} @ ${localOffset}+${readLength}: got ${data.length} bytes.`);
      }
      output.set(data, outputOffset);
      outputOffset += readLength;
      sourceOffset += readLength;
      partIndex += 1;
    }

    return output;
  }

  private findPartIndex(offset: number): number {
    let low = 0;
    let high = this.parts.length - 1;
    while (low <= high) {
      const middle = (low + high) >>> 1;
      const start = this.starts[middle];
      const part = this.parts[middle];
      if (start === undefined || !part) break;
      const end = start + part.size;
      if (offset < start) high = middle - 1;
      else if (offset >= end) low = middle + 1;
      else return middle;
    }
    throw new RangeError(`No source part covers offset ${offset}.`);
  }
}

export interface MappedRandomAccessExtent {
  /** Logical byte offset at which this mapped extent begins. */
  readonly start: number;
  readonly reader: RandomAccessReader;
  /** Byte offset within `reader`; defaults to zero. */
  readonly readerOffset?: number;
  /** Number of bytes exposed from `reader`; defaults to the remaining reader bytes. */
  readonly length?: number;
}

interface NormalizedMappedRandomAccessExtent {
  readonly start: number;
  readonly end: number;
  readonly reader: RandomAccessReader;
  readonly readerOffset: number;
  readonly length: number;
}

/**
 * Presents selected physical extents at their true logical offsets while leaving all other ranges
 * unmapped. This is useful for archaeology on huge split images when only a small subset of parts
 * can be materialized locally. A read touching an unmapped hole fails rather than synthesizing
 * zeroes or silently shifting later extents.
 */
export class MappedRandomAccessReader implements RandomAccessReader {
  readonly name: string;
  readonly size: number;
  private readonly extents: readonly NormalizedMappedRandomAccessExtent[];

  constructor(extents: readonly MappedRandomAccessExtent[], size: number, name = "mapped") {
    if (!Number.isSafeInteger(size) || size < 0) throw new RangeError(`Invalid mapped source size ${size}.`);
    if (extents.length === 0) throw new Error("At least one mapped source extent is required.");

    const normalized = extents.map((extent): NormalizedMappedRandomAccessExtent => {
      const readerOffset = extent.readerOffset ?? 0;
      const length = extent.length ?? (extent.reader.size - readerOffset);
      if (!Number.isSafeInteger(extent.start) || extent.start < 0) throw new RangeError(`Invalid mapped extent start ${extent.start}.`);
      if (!Number.isSafeInteger(readerOffset) || readerOffset < 0 || readerOffset > extent.reader.size) {
        throw new RangeError(`Invalid mapped reader offset ${readerOffset} for ${extent.reader.name}.`);
      }
      if (!Number.isSafeInteger(length) || length < 0 || length > extent.reader.size - readerOffset) {
        throw new RangeError(`Invalid mapped length ${length} for ${extent.reader.name} @ ${readerOffset}.`);
      }
      const end = extent.start + length;
      if (!Number.isSafeInteger(end) || end > size) {
        throw new RangeError(`Mapped extent ${extent.start}+${length} lies outside logical source size ${size}.`);
      }
      return { start: extent.start, end, reader: extent.reader, readerOffset, length };
    }).sort((a, b) => a.start - b.start);

    for (let i = 1; i < normalized.length; i++) {
      const previous = normalized[i - 1]!;
      const current = normalized[i]!;
      if (current.start < previous.end) {
        throw new RangeError(`Mapped extents overlap at logical offset ${current.start}.`);
      }
    }

    this.name = name;
    this.size = size;
    this.extents = normalized;
  }

  async read(offset: number, length: number): Promise<Uint8Array> {
    assertReadRange(this, offset, length);
    if (length === 0) return new Uint8Array();

    const output = new Uint8Array(length);
    let outputOffset = 0;
    let sourceOffset = offset;
    let extentIndex = this.findExtentIndex(sourceOffset);

    while (outputOffset < length) {
      const extent = this.extents[extentIndex];
      if (!extent || sourceOffset < extent.start || sourceOffset >= extent.end) {
        throw new RangeError(`Unmapped read in ${this.name} at logical offset ${sourceOffset}.`);
      }
      const localOffset = sourceOffset - extent.start;
      const readLength = Math.min(length - outputOffset, extent.length - localOffset);
      const data = await extent.reader.read(extent.readerOffset + localOffset, readLength);
      if (data.length !== readLength) {
        throw new Error(`Short read from mapped extent ${extent.reader.name} @ ${extent.readerOffset + localOffset}+${readLength}: got ${data.length} bytes.`);
      }
      output.set(data, outputOffset);
      outputOffset += readLength;
      sourceOffset += readLength;
      if (outputOffset < length) {
        extentIndex += 1;
        const next = this.extents[extentIndex];
        if (!next || next.start !== sourceOffset) {
          throw new RangeError(`Unmapped read in ${this.name} at logical offset ${sourceOffset}.`);
        }
      }
    }

    return output;
  }

  private findExtentIndex(offset: number): number {
    let low = 0;
    let high = this.extents.length - 1;
    while (low <= high) {
      const middle = (low + high) >>> 1;
      const extent = this.extents[middle];
      if (!extent) break;
      if (offset < extent.start) high = middle - 1;
      else if (offset >= extent.end) low = middle + 1;
      else return middle;
    }
    throw new RangeError(`Unmapped read in ${this.name} at logical offset ${offset}.`);
  }
}

/**
 * Fixed-window view of a parent reader. Used to expose a native container's inner
 * section as its own range-readable source without copying or buffering it.
 */
export class SubRangeReader implements RandomAccessReader {
  readonly name: string;
  readonly size: number;

  constructor(
    private readonly source: RandomAccessReader,
    private readonly start: number,
    size: number,
    name?: string,
  ) {
    if (!Number.isSafeInteger(start) || start < 0 || !Number.isSafeInteger(size) || size < 0 || start > source.size || size > source.size - start) {
      throw new RangeError(`Sub-range ${start}+${size} lies outside ${source.name} (size ${source.size}).`);
    }
    this.name = name ?? `${source.name}[${start}..${start + size}]`;
    this.size = size;
  }

  async read(offset: number, length: number): Promise<Uint8Array> {
    assertReadRange(this, offset, length);
    if (length === 0) return new Uint8Array();
    const data = await this.source.read(this.start + offset, length);
    if (data.length !== length) throw new Error(`Short sub-range read from ${this.name} @ ${offset}+${length}.`);
    return data;
  }
}

export interface OBPBuildIdentity {
  game: Exclude<OBPSourceGame, "synthetic">;
  buildId: string;
  region?: string;
  serial?: string;
  revision?: string;
  sha256?: string;
}

export interface ProbeSourceSet {
  files: ReadonlyMap<string, RandomAccessReader>;
  /** Optional caller-supplied metadata. Hashes here must already have been verified by the caller. */
  identityHint?: Readonly<Partial<OBPBuildIdentity>>;
}

export interface ImportSourceSet extends ProbeSourceSet {
  identity: OBPBuildIdentity;
}

export interface ImportProbeResult {
  confidence: "none" | "possible" | "strong" | "exact";
  reasons: readonly string[];
}

export interface ImportLogEvent {
  level: "debug" | "info" | "warning" | "error";
  message: string;
  sourcePath?: string;
  offset?: number;
}

/**
 * Per-probe-run promise cache. Entries are keyed by the actual source object plus a namespaced key,
 * so independent readers with the same filename/size never alias. A rejected probe is cached too:
 * within one deterministic registry run, every importer should observe the same source result.
 */
export class ImportProbeCache {
  private readonly owners = new WeakMap<object, Map<string, Promise<unknown>>>();

  getOrCreate<T>(owner: object, key: string, factory: () => Promise<T>): Promise<T> {
    let entries = this.owners.get(owner);
    if (!entries) {
      entries = new Map();
      this.owners.set(owner, entries);
    }
    const existing = entries.get(key);
    if (existing) return existing as Promise<T>;

    const created = Promise.resolve().then(factory);
    entries.set(key, created);
    return created;
  }
}

export interface ImportContext {
  signal?: AbortSignal;
  /** Optional per-run cache used to share deterministic source archaeology across importer probes. */
  probeCache?: ImportProbeCache;
  log(event: ImportLogEvent): void;
}

export interface WorldImportRequest {
  /** Native level identifier. Exact semantics remain importer-specific. */
  levelId: string | number;
}

export interface OBPImporter {
  readonly id: string;
  readonly game: Exclude<OBPSourceGame, "synthetic">;
  probe(source: ProbeSourceSet, context: ImportContext): Promise<ImportProbeResult>;
  importWorld(source: ImportSourceSet, request: WorldImportRequest, context: ImportContext): Promise<OBPWorld>;
}

export function assertReadRange(reader: RandomAccessReader, offset: number, length: number): void {
  if (!Number.isSafeInteger(offset) || !Number.isSafeInteger(length) || offset < 0 || length < 0 || offset > reader.size || length > reader.size - offset) {
    throw new RangeError(`Invalid read ${reader.name} @ ${offset}+${length} (size ${reader.size})`);
  }
}
