import { assertReadRange } from "../../importer-common/src/index.js";
import type { RandomAccessReader } from "../../importer-common/src/index.js";

const VOLUME_DESCRIPTOR_START_LBA = 16;
const VOLUME_DESCRIPTOR_BYTES = 2048;
const ISO_STANDARD_IDENTIFIER = "CD001";

export interface Iso9660DirectoryEntry {
  readonly rawIdentifier: string;
  readonly name: string;
  readonly version?: number;
  readonly extentLba: number;
  readonly dataLength: number;
  readonly flags: number;
  readonly isDirectory: boolean;
  readonly isSpecial: boolean;
}

export interface Iso9660PrimaryVolumeDescriptor {
  readonly descriptorLba: number;
  readonly systemIdentifier: string;
  readonly volumeIdentifier: string;
  readonly volumeSpaceSize: number;
  readonly logicalBlockSize: number;
  readonly rootDirectory: Iso9660DirectoryEntry;
}

export interface Iso9660WalkEntry extends Iso9660DirectoryEntry {
  /** Absolute normalized ISO path, without a trailing slash. */
  readonly path: string;
  /** Depth relative to the requested walk root; direct children are depth 1. */
  readonly depth: number;
}

export interface Iso9660WalkOptions {
  /** Include directory entries in yielded results. Directories are still traversed when false. */
  readonly includeDirectories?: boolean;
  /** Maximum relative depth to inspect. A value of 1 yields only direct children. */
  readonly maxDepth?: number;
  /** Hard safety cap on encountered non-special entries, including directories. */
  readonly maxEntries?: number;
}

/** Reads ISO-9660 volume descriptors one 2048-byte sector at a time. */
export async function readPrimaryVolumeDescriptor(
  reader: RandomAccessReader,
  maxDescriptors = 64,
): Promise<Iso9660PrimaryVolumeDescriptor> {
  if (!Number.isSafeInteger(maxDescriptors) || maxDescriptors <= 0) {
    throw new RangeError(`Invalid maxDescriptors: ${maxDescriptors}`);
  }

  for (let index = 0; index < maxDescriptors; index++) {
    const descriptorLba = VOLUME_DESCRIPTOR_START_LBA + index;
    const offset = descriptorLba * VOLUME_DESCRIPTOR_BYTES;
    if (offset + VOLUME_DESCRIPTOR_BYTES > reader.size) break;

    const sector = await reader.read(offset, VOLUME_DESCRIPTOR_BYTES);
    if (sector.length !== VOLUME_DESCRIPTOR_BYTES) {
      throw new Error(`Short ISO volume descriptor read at LBA ${descriptorLba}.`);
    }

    const identifier = ascii(sector, 1, 5);
    if (identifier !== ISO_STANDARD_IDENTIFIER) {
      throw new Error(`Invalid ISO-9660 standard identifier '${identifier}' at LBA ${descriptorLba}.`);
    }
    if (sector[6] !== 1) throw new Error(`Unsupported ISO-9660 descriptor version ${sector[6]} at LBA ${descriptorLba}.`);

    const type = sector[0];
    if (type === 255) break;
    if (type !== 1) continue;

    const volumeSpaceSize = bothEndianUint32(sector, 80, 84, "volume space size");
    const logicalBlockSize = bothEndianUint16(sector, 128, 130, "logical block size");
    if (logicalBlockSize <= 0) throw new Error("ISO-9660 logical block size must be positive.");

    const rootDirectory = parseDirectoryRecord(sector, 156);
    if (!rootDirectory.isDirectory) throw new Error("ISO-9660 primary volume descriptor root record is not a directory.");

    return {
      descriptorLba,
      systemIdentifier: ascii(sector, 8, 32).trimEnd(),
      volumeIdentifier: ascii(sector, 40, 32).trimEnd(),
      volumeSpaceSize,
      logicalBlockSize,
      rootDirectory,
    };
  }

  throw new Error("ISO-9660 primary volume descriptor was not found.");
}

/**
 * Reads a directory extent one logical block at a time. ISO directory records never cross a
 * logical-block boundary, so no whole-directory allocation is required.
 */
export async function readDirectoryEntries(
  reader: RandomAccessReader,
  directory: Iso9660DirectoryEntry,
  logicalBlockSize: number,
  options: { includeSpecial?: boolean } = {},
): Promise<readonly Iso9660DirectoryEntry[]> {
  if (!directory.isDirectory) throw new Error(`ISO-9660 entry '${directory.name}' is not a directory.`);
  if (!Number.isSafeInteger(logicalBlockSize) || logicalBlockSize <= 0) {
    throw new RangeError(`Invalid ISO-9660 logical block size: ${logicalBlockSize}`);
  }

  const extentOffset = directory.extentLba * logicalBlockSize;
  if (!Number.isSafeInteger(extentOffset) || extentOffset < 0 || directory.dataLength < 0 || extentOffset > reader.size || directory.dataLength > reader.size - extentOffset) {
    throw new RangeError(`Directory extent '${directory.name}' lies outside ${reader.name}.`);
  }

  const entries: Iso9660DirectoryEntry[] = [];
  let consumed = 0;
  while (consumed < directory.dataLength) {
    const readLength = Math.min(logicalBlockSize, directory.dataLength - consumed);
    const block = await reader.read(extentOffset + consumed, readLength);
    if (block.length !== readLength) throw new Error(`Short ISO-9660 directory read at ${extentOffset + consumed}.`);

    let offset = 0;
    while (offset < block.length) {
      const recordLength = block[offset];
      if (recordLength === undefined || recordLength === 0) break;
      if (offset + recordLength > block.length) {
        throw new Error(`ISO-9660 directory record crosses a logical-block boundary at ${extentOffset + consumed + offset}.`);
      }
      const entry = parseDirectoryRecord(block, offset);
      if (options.includeSpecial || !entry.isSpecial) entries.push(entry);
      offset += recordLength;
    }

    consumed += readLength;
  }

  return entries;
}

/** Range-limited view of one ISO-9660 file extent. */
export class Iso9660ExtentReader implements RandomAccessReader {
  readonly name: string;
  readonly size: number;
  private readonly start: number;

  constructor(
    private readonly source: RandomAccessReader,
    entry: Iso9660DirectoryEntry,
    logicalBlockSize: number,
    name = entry.name,
  ) {
    if (entry.isDirectory) throw new Error(`ISO-9660 entry '${entry.name}' is a directory, not a file extent.`);
    if (!Number.isSafeInteger(logicalBlockSize) || logicalBlockSize <= 0) {
      throw new RangeError(`Invalid ISO-9660 logical block size: ${logicalBlockSize}`);
    }
    const start = entry.extentLba * logicalBlockSize;
    if (!Number.isSafeInteger(start) || start < 0 || start > source.size || entry.dataLength > source.size - start) {
      throw new RangeError(`File extent '${entry.name}' lies outside ${source.name}.`);
    }
    this.name = name;
    this.size = entry.dataLength;
    this.start = start;
  }

  async read(offset: number, length: number): Promise<Uint8Array> {
    assertReadRange(this, offset, length);
    if (length === 0) return new Uint8Array();
    const data = await this.source.read(this.start + offset, length);
    if (data.length !== length) throw new Error(`Short ISO-9660 extent read from ${this.name} @ ${offset}+${length}.`);
    return data;
  }
}

/**
 * Small path-oriented facade over ISO-9660. It caches only the primary descriptor; each directory
 * traversal stays block-bounded and file contents remain range-readable.
 */
export class Iso9660Filesystem {
  private constructor(
    private readonly source: RandomAccessReader,
    readonly volume: Iso9660PrimaryVolumeDescriptor,
  ) {}

  static async open(source: RandomAccessReader): Promise<Iso9660Filesystem> {
    return new Iso9660Filesystem(source, await readPrimaryVolumeDescriptor(source));
  }

  async find(path: string): Promise<Iso9660DirectoryEntry | undefined> {
    const segments = normalizeIso9660Path(path);
    let current = this.volume.rootDirectory;
    if (segments.length === 0) return current;

    for (const [index, segment] of segments.entries()) {
      if (!current.isDirectory) return undefined;
      const entries = await readDirectoryEntries(this.source, current, this.volume.logicalBlockSize);
      const next = entries.find((entry) => isoIdentifierMatches(entry, segment));
      if (!next) return undefined;
      if (index < segments.length - 1 && !next.isDirectory) return undefined;
      current = next;
    }

    return current;
  }

  async list(path = "/"): Promise<readonly Iso9660DirectoryEntry[] | undefined> {
    const directory = await this.find(path);
    if (!directory) return undefined;
    if (!directory.isDirectory) throw new Error(`ISO-9660 path '${path}' is not a directory.`);
    return readDirectoryEntries(this.source, directory, this.volume.logicalBlockSize);
  }

  async openFile(path: string): Promise<Iso9660ExtentReader | undefined> {
    const entry = await this.find(path);
    if (!entry) return undefined;
    if (entry.isDirectory) throw new Error(`ISO-9660 path '${path}' is a directory, not a file.`);
    return new Iso9660ExtentReader(this.source, entry, this.volume.logicalBlockSize, normalizeIso9660Path(path).join("/"));
  }

  /**
   * Depth-first incremental directory walk. Only directory extents are read; file contents remain
   * untouched. Depth and entry caps bound malformed or unexpectedly huge trees, while extent-based
   * cycle detection prevents directory aliases from recursing forever.
   */
  async *walk(path = "/", options: Iso9660WalkOptions = {}): AsyncGenerator<Iso9660WalkEntry> {
    const maxDepth = options.maxDepth ?? 64;
    const maxEntries = options.maxEntries ?? 100_000;
    const includeDirectories = options.includeDirectories ?? true;
    if (!Number.isSafeInteger(maxDepth) || maxDepth < 0) throw new RangeError(`Invalid ISO-9660 walk maxDepth: ${maxDepth}`);
    if (!Number.isSafeInteger(maxEntries) || maxEntries <= 0) throw new RangeError(`Invalid ISO-9660 walk maxEntries: ${maxEntries}`);

    const start = await this.find(path);
    if (!start) return;
    if (!start.isDirectory) throw new Error(`ISO-9660 path '${path}' is not a directory.`);
    if (maxDepth === 0) return;

    const source = this.source;
    const logicalBlockSize = this.volume.logicalBlockSize;
    const baseSegments = normalizeIso9660Path(path);
    const visitedDirectories = new Set<string>();
    let encounteredEntries = 0;

    const walkDirectory = async function* (
      directory: Iso9660DirectoryEntry,
      parentSegments: readonly string[],
      parentDepth: number,
    ): AsyncGenerator<Iso9660WalkEntry> {
      const directoryKey = `${directory.extentLba}:${directory.dataLength}`;
      if (visitedDirectories.has(directoryKey)) return;
      visitedDirectories.add(directoryKey);

      const entries = await readDirectoryEntries(source, directory, logicalBlockSize);
      for (const entry of entries) {
        encounteredEntries += 1;
        if (encounteredEntries > maxEntries) {
          throw new Error(`ISO-9660 walk exceeded maxEntries ${maxEntries}.`);
        }

        const depth = parentDepth + 1;
        const segments = [...parentSegments, entry.name];
        const walked: Iso9660WalkEntry = { ...entry, path: `/${segments.join("/")}`, depth };
        if (!entry.isDirectory || includeDirectories) yield walked;

        if (entry.isDirectory && depth < maxDepth) {
          yield* walkDirectory(entry, segments, depth);
        }
      }
    };

    yield* walkDirectory(start, baseSegments, 0);
  }
}

export function normalizeIso9660Path(path: string): readonly string[] {
  if (path.includes("\0")) throw new Error("ISO-9660 path contains a NUL byte.");
  const normalized = path.replaceAll("\\", "/").replace(/^\/+|\/+$/g, "");
  if (!normalized) return [];
  const segments = normalized.split("/").filter(Boolean);
  if (segments.some((segment) => segment === "." || segment === "..")) {
    throw new Error(`ISO-9660 path traversal is not allowed: '${path}'.`);
  }
  return segments;
}

function isoIdentifierMatches(entry: Iso9660DirectoryEntry, segment: string): boolean {
  const expected = segment.toUpperCase();
  return entry.name.toUpperCase() === expected || entry.rawIdentifier.toUpperCase() === expected;
}

function parseDirectoryRecord(bytes: Uint8Array, offset: number): Iso9660DirectoryEntry {
  const recordLength = bytes[offset];
  if (recordLength === undefined || recordLength < 34 || offset + recordLength > bytes.length) {
    throw new Error(`Invalid ISO-9660 directory record length ${recordLength ?? "missing"} at offset ${offset}.`);
  }

  const identifierLength = bytes[offset + 32];
  if (identifierLength === undefined || identifierLength === 0 || 33 + identifierLength > recordLength) {
    throw new Error(`Invalid ISO-9660 file identifier length ${identifierLength ?? "missing"} at offset ${offset}.`);
  }

  const extentLba = bothEndianUint32(bytes, offset + 2, offset + 6, "extent location");
  const dataLength = bothEndianUint32(bytes, offset + 10, offset + 14, "data length");
  const flags = bytes[offset + 25];
  if (flags === undefined) throw new Error(`Missing ISO-9660 file flags at offset ${offset}.`);

  const identifierBytes = bytes.subarray(offset + 33, offset + 33 + identifierLength);
  let rawIdentifier: string;
  let name: string;
  let version: number | undefined;
  let isSpecial = false;

  if (identifierLength === 1 && identifierBytes[0] === 0) {
    rawIdentifier = "\\0";
    name = ".";
    isSpecial = true;
  } else if (identifierLength === 1 && identifierBytes[0] === 1) {
    rawIdentifier = "\\1";
    name = "..";
    isSpecial = true;
  } else {
    rawIdentifier = ascii(identifierBytes, 0, identifierBytes.length);
    const match = /^(.*);([0-9]+)$/.exec(rawIdentifier);
    name = match?.[1] ?? rawIdentifier;
    if (match?.[2]) version = Number.parseInt(match[2], 10);
  }

  return {
    rawIdentifier,
    name,
    ...(version === undefined ? {} : { version }),
    extentLba,
    dataLength,
    flags,
    isDirectory: (flags & 0x02) !== 0,
    isSpecial,
  };
}

function bothEndianUint16(bytes: Uint8Array, littleOffset: number, bigOffset: number, label: string): number {
  const little = new DataView(bytes.buffer, bytes.byteOffset + littleOffset, 2).getUint16(0, true);
  const big = new DataView(bytes.buffer, bytes.byteOffset + bigOffset, 2).getUint16(0, false);
  if (little !== big) throw new Error(`ISO-9660 ${label} endian copies disagree: ${little} != ${big}.`);
  return little;
}

function bothEndianUint32(bytes: Uint8Array, littleOffset: number, bigOffset: number, label: string): number {
  const little = new DataView(bytes.buffer, bytes.byteOffset + littleOffset, 4).getUint32(0, true);
  const big = new DataView(bytes.buffer, bytes.byteOffset + bigOffset, 4).getUint32(0, false);
  if (little !== big) throw new Error(`ISO-9660 ${label} endian copies disagree: ${little} != ${big}.`);
  return little;
}

function ascii(bytes: Uint8Array, offset: number, length: number): string {
  let output = "";
  for (let index = 0; index < length; index++) output += String.fromCharCode(bytes[offset + index] ?? 0);
  return output;
}
