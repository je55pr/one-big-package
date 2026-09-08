import type { RandomAccessReader } from "../../importer-common/src/index.js";

/** PS2 logical sector size used by the retail trilogy disc loaders. */
export const RAC234_DISC_SECTOR_BYTES = 0x800;

/**
 * Candidate hidden table-of-contents location for GC/UYA/DL.
 *
 * Status: public-source lead from chaoticgd/wrench e67ea46e5e60ec52a0c31fcde1e3f46b20aa4cfb.
 * GC independently exposes the same sector through RC2.HDR; UYA retail confirmation is intentionally
 * tracked separately in research/UYA_DISC_LAYOUT.md rather than represented as proven by this constant.
 */
export const RAC234_PUBLIC_LEAD_TOC_LBA = 1001;

/** Maximum bounded window used by the pinned public implementation when discovering the hidden ToC. */
export const RAC234_PUBLIC_LEAD_TOC_MAX_BYTES = 0x200000;

/** Maximum prefix searched for the resident global-header stream / level table. */
export const RAC234_PUBLIC_LEAD_INDEX_MAX_BYTES = 0x10000;

/** A level table entry is three physical sector-range pairs (24 bytes). */
export const RAC234_LEVEL_TABLE_ENTRY_BYTES = 0x18;

/**
 * Public UYA lead for the physical level-table part order and header sizes.
 * These values are deliberately labelled as a lead until checked against the authority retail bytes.
 */
export const UYA_PUBLIC_LEAD_LEVEL_PARTS = [
  { kind: "audio", headerSize: 0x1818 },
  { kind: "level", headerSize: 0x0060 },
  { kind: "scene", headerSize: 0x26f0 },
] as const;

export type UyaPublicLeadLevelPartKind = (typeof UYA_PUBLIC_LEAD_LEVEL_PARTS)[number]["kind"];

export interface Rac234SectorRangePair {
  readonly headerLba: number;
  readonly sizeSectors: number;
}

export interface Rac234ResidentHeader {
  readonly offset: number;
  readonly headerSize: number;
  readonly fileLba: number;
}

export interface Rac234LevelTableEntry {
  readonly index: number;
  readonly offset: number;
  /** Physical on-disc order. Do not assign game semantics without independent evidence. */
  readonly parts: readonly [Rac234SectorRangePair, Rac234SectorRangePair, Rac234SectorRangePair];
}

export interface Rac234TocStructuralCandidate {
  readonly tableOffset: number;
  readonly firstTwoEntries: readonly [Rac234LevelTableEntry, Rac234LevelTableEntry];
  readonly pointedHeaderSizes: readonly [number, number, number, number, number, number];
}

export interface UyaPublicLeadLevelEntry {
  readonly index: number;
  readonly offset: number;
  readonly audio: Rac234SectorRangePair;
  readonly level: Rac234SectorRangePair;
  readonly scene: Rac234SectorRangePair;
}

/**
 * Read only the bounded hidden-ToC archaeology window. This never hashes or buffers the full disc.
 */
export async function readRac234TocProbeWindow(
  source: RandomAccessReader,
  options: { readonly tocLba?: number; readonly maxBytes?: number } = {},
): Promise<Uint8Array> {
  const tocLba = options.tocLba ?? RAC234_PUBLIC_LEAD_TOC_LBA;
  const maxBytes = options.maxBytes ?? RAC234_PUBLIC_LEAD_TOC_MAX_BYTES;
  requireSafeNonNegativeInteger(tocLba, "tocLba");
  requireSafePositiveInteger(maxBytes, "maxBytes");

  const offset = checkedMultiply(tocLba, RAC234_DISC_SECTOR_BYTES, "ToC byte offset");
  if (offset >= source.size) {
    throw new RangeError(`ToC candidate LBA ${tocLba} starts past ${source.name} (size ${source.size}).`);
  }
  const length = Math.min(maxBytes, source.size - offset);
  const bytes = await source.read(offset, length);
  if (bytes.length !== length) {
    throw new Error(`Short ToC probe read from ${source.name} @ ${offset}+${length}: got ${bytes.length} bytes.`);
  }
  return bytes;
}

/**
 * Structurally scan for possible six-part (two-entry) RAC234 level-table starts.
 *
 * This intentionally does not identify WAD types. A candidate only proves that six non-zero sector
 * references point back into this bounded ToC window and each target begins with a sane header size.
 * False positives are therefore possible and expected; game-specific interpretation is a later step.
 */
export function scanRac234LevelTableStructuralCandidates(
  window: Uint8Array,
  options: {
    readonly tocLba?: number;
    readonly maxScanBytes?: number;
    readonly maxHeaderSize?: number;
  } = {},
): readonly Rac234TocStructuralCandidate[] {
  const tocLba = options.tocLba ?? RAC234_PUBLIC_LEAD_TOC_LBA;
  const maxScanBytes = Math.min(options.maxScanBytes ?? RAC234_PUBLIC_LEAD_INDEX_MAX_BYTES, window.length);
  const maxHeaderSize = options.maxHeaderSize ?? 0xffff;
  requireSafeNonNegativeInteger(tocLba, "tocLba");
  requireSafeNonNegativeInteger(maxScanBytes, "maxScanBytes");
  requireSafePositiveInteger(maxHeaderSize, "maxHeaderSize");

  const candidates: Rac234TocStructuralCandidate[] = [];
  const requiredTableBytes = RAC234_LEVEL_TABLE_ENTRY_BYTES * 2;
  for (let offset = 0; offset + requiredTableBytes <= maxScanBytes; offset += 4) {
    const entries = [
      parseRac234LevelTableEntry(window, offset, 0),
      parseRac234LevelTableEntry(window, offset, 1),
    ] as const;

    const headerSizes: number[] = [];
    let valid = true;
    for (const entry of entries) {
      for (const part of entry.parts) {
        if (part.headerLba === 0) {
          valid = false;
          break;
        }
        const headerOffset = sectorDeltaToByteOffset(part.headerLba, tocLba);
        if (headerOffset <= 0 || headerOffset + 8 > window.length) {
          valid = false;
          break;
        }
        const headerSize = readU32Le(window, headerOffset);
        if (headerSize < 8 || headerSize > maxHeaderSize || headerOffset + headerSize > window.length) {
          valid = false;
          break;
        }
        headerSizes.push(headerSize);
      }
      if (!valid) break;
    }

    if (valid && headerSizes.length === 6) {
      candidates.push({
        tableOffset: offset,
        firstTwoEntries: entries,
        pointedHeaderSizes: headerSizes as unknown as Rac234TocStructuralCandidate["pointedHeaderSizes"],
      });
    }
  }
  return candidates;
}

/**
 * Narrow structural candidates using the pinned Wrench UYA lead only.
 * A match is evidence that the bytes agree with that public interpretation; it is not itself retail semantics proof.
 */
export function filterUyaLevelTableCandidatesByPublicLead(
  candidates: readonly Rac234TocStructuralCandidate[],
): readonly Rac234TocStructuralCandidate[] {
  const expected = [
    UYA_PUBLIC_LEAD_LEVEL_PARTS[0].headerSize,
    UYA_PUBLIC_LEAD_LEVEL_PARTS[1].headerSize,
    UYA_PUBLIC_LEAD_LEVEL_PARTS[2].headerSize,
    UYA_PUBLIC_LEAD_LEVEL_PARTS[0].headerSize,
    UYA_PUBLIC_LEAD_LEVEL_PARTS[1].headerSize,
    UYA_PUBLIC_LEAD_LEVEL_PARTS[2].headerSize,
  ] as const;
  return candidates.filter((candidate) => candidate.pointedHeaderSizes.every((value, index) => value === expected[index]));
}

/** Interpret one physical entry using the provisional UYA public order: audio, level, scene. */
export function interpretUyaLevelTableEntryPublicLead(entry: Rac234LevelTableEntry): UyaPublicLeadLevelEntry {
  return {
    index: entry.index,
    offset: entry.offset,
    audio: entry.parts[0],
    level: entry.parts[1],
    scene: entry.parts[2],
  };
}

/** Parse one 24-byte entry in physical order without assigning any part semantics. */
export function parseRac234LevelTableEntry(window: Uint8Array, tableOffset: number, index: number): Rac234LevelTableEntry {
  requireSafeNonNegativeInteger(tableOffset, "tableOffset");
  requireSafeNonNegativeInteger(index, "index");
  const offset = tableOffset + checkedMultiply(index, RAC234_LEVEL_TABLE_ENTRY_BYTES, "level table entry offset");
  if (!Number.isSafeInteger(offset) || offset < 0 || offset + RAC234_LEVEL_TABLE_ENTRY_BYTES > window.length) {
    throw new RangeError(`Level table entry ${index} at 0x${offset.toString(16)} lies outside ${window.length}-byte ToC window.`);
  }

  const parts = [0, 1, 2].map((partIndex) => {
    const partOffset = offset + partIndex * 8;
    return {
      headerLba: readU32Le(window, partOffset),
      sizeSectors: readU32Le(window, partOffset + 4),
    };
  }) as unknown as Rac234LevelTableEntry["parts"];

  return { index, offset, parts };
}

/**
 * Parse the resident global-header prefix once a level-table offset has been independently selected.
 * Each header is retained positionally; header-size alone must not be treated as semantic identity.
 */
export function parseRac234ResidentGlobalHeaders(
  window: Uint8Array,
  tableOffset: number,
): readonly Rac234ResidentHeader[] {
  requireSafeNonNegativeInteger(tableOffset, "tableOffset");
  if (tableOffset > window.length) throw new RangeError(`tableOffset 0x${tableOffset.toString(16)} lies outside ToC window.`);

  const headers: Rac234ResidentHeader[] = [];
  let offset = 0;
  while (offset < tableOffset) {
    if (offset + 8 > tableOffset) {
      throw new Error(`Resident global-header stream leaves ${tableOffset - offset} trailing bytes before level table.`);
    }
    const headerSize = readU32Le(window, offset);
    const fileLba = readU32Le(window, offset + 4);
    if (headerSize < 8 || headerSize > 0xffff) {
      throw new Error(`Invalid resident header size 0x${headerSize.toString(16)} at ToC + 0x${offset.toString(16)}.`);
    }
    if (offset + headerSize > tableOffset) {
      throw new Error(`Resident header at 0x${offset.toString(16)} overlaps level table at 0x${tableOffset.toString(16)}.`);
    }
    headers.push({ offset, headerSize, fileLba });
    offset += headerSize;
  }
  return headers;
}

function sectorDeltaToByteOffset(lba: number, baseLba: number): number {
  if (!Number.isSafeInteger(lba) || lba < baseLba) return -1;
  const delta = lba - baseLba;
  return checkedMultiply(delta, RAC234_DISC_SECTOR_BYTES, "sector delta byte offset");
}

function readU32Le(bytes: Uint8Array, offset: number): number {
  if (!Number.isSafeInteger(offset) || offset < 0 || offset + 4 > bytes.length) {
    throw new RangeError(`u32 read at 0x${offset.toString(16)} lies outside ${bytes.length}-byte buffer.`);
  }
  return new DataView(bytes.buffer, bytes.byteOffset + offset, 4).getUint32(0, true);
}

function checkedMultiply(a: number, b: number, label: string): number {
  const value = a * b;
  if (!Number.isSafeInteger(value)) throw new RangeError(`${label} exceeds JavaScript safe integer range.`);
  return value;
}

function requireSafeNonNegativeInteger(value: number, label: string): void {
  if (!Number.isSafeInteger(value) || value < 0) throw new RangeError(`${label} must be a safe non-negative integer.`);
}

function requireSafePositiveInteger(value: number, label: string): void {
  if (!Number.isSafeInteger(value) || value <= 0) throw new RangeError(`${label} must be a safe positive integer.`);
}
