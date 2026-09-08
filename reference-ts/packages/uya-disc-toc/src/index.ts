import type { RandomAccessReader } from "../../importer-common/src/index.js";
import {
  filterUyaLevelTableCandidatesByPublicLead,
  scanRac234LevelTableStructuralCandidates,
} from "../../uya-disc/src/index.js";

/** PS2 DVD logical-sector size used by the retail trilogy images. */
export const UYA_DISC_SECTOR_BYTES = 2048;

/**
 * Public archaeology lead only, NOT retail-confirmed by this package.
 *
 * Wrench commit e67ea46e5e60ec52a0c31fcde1e3f46b20aa4cfb models GC/UYA/DL
 * as having a resident table of contents beginning at LBA 1001. OBP keeps the
 * address explicitly labelled as a lead until a deterministic retail probe
 * records the bytes from the supported UYA authority image.
 */
export const UYA_PUBLIC_TOC_LBA_HINT = 1001;

/** Same bounded inspection window used by the pinned public implementation. */
export const UYA_PUBLIC_TOC_WINDOW_BYTES_HINT = 0x200000;

/** Upper bound used only to reject nonsensical candidate resident headers. */
export const UYA_TOC_MAX_HEADER_BYTES = 0xffff;

/** Public implementation scans at most 100 level-table rows. */
export const UYA_PUBLIC_MAX_LEVEL_ROWS_HINT = 100;

export const UYA_TOC_LEVEL_ROW_BYTES = 24;

export const UYA_WRENCH_SOURCE = {
  repository: "chaoticgd/wrench",
  commit: "e67ea46e5e60ec52a0c31fcde1e3f46b20aa4cfb",
  tableOfContentsPath: "src/iso/table_of_contents.cpp",
  wadIdentifierPath: "src/iso/wad_identifier.cpp",
} as const;

export interface UyaTocProbeOptions {
  /** Candidate absolute disc LBA. Defaults to the pinned public lead, not a retail claim. */
  readonly tocLba?: number;
  /** Maximum number of bytes to request from the source. */
  readonly windowBytes?: number;
  /** Maximum number of 24-byte candidate level rows to inspect. */
  readonly maxLevelRows?: number;
}

export interface UyaTocGlobalHeaderCandidate {
  readonly index: number;
  readonly offsetBytes: number;
  readonly headerSize: number;
  /** Raw u32 stored at header + 4. Meaning is intentionally not asserted here. */
  readonly rawWordAt0x04: number;
}

export interface UyaPublicFormatHint {
  /** Public-tool interpretation only; never emitted as a retail/native type. */
  readonly label: string;
  readonly gameHint: "gc-or-uya" | "uya" | "unknown";
  readonly source: typeof UYA_WRENCH_SOURCE;
}

export interface UyaTocPartCandidate {
  readonly slot: 0 | 1 | 2;
  readonly headerLba: number;
  readonly sizeSectors: number;
  readonly headerOffsetInWindowBytes?: number;
  readonly headerSize?: number;
  /** Raw u32 stored at pointed header + 4. Public tooling treats this as a file LBA; OBP does not yet. */
  readonly rawHeaderWordAt0x04?: number;
  readonly publicFormatHint?: UyaPublicFormatHint;
  readonly issues: readonly string[];
}

export interface UyaTocLevelRowCandidate {
  readonly index: number;
  readonly offsetBytes: number;
  readonly parts: readonly [UyaTocPartCandidate, UyaTocPartCandidate, UyaTocPartCandidate];
}

export interface UyaTocWindowAnalysis {
  readonly tocLba: number;
  readonly windowBytes: number;
  readonly globalHeaders: readonly UyaTocGlobalHeaderCandidate[];
  /** How the candidate level-table boundary was selected. */
  readonly levelTableDiscovery: "structural-public-lead" | "fallback-leading-header-chain";
  readonly levelTableOffsetBytes: number;
  /** Sparse rows containing at least one non-zero range. Index is preserved. */
  readonly levelRows: readonly UyaTocLevelRowCandidate[];
  readonly warnings: readonly string[];
}

export interface UyaDiscTocProbeResult extends UyaTocWindowAnalysis {
  readonly sourceName: string;
  readonly sourceSize: number;
  readonly readOffsetBytes: number;
}

/**
 * Perform one bounded raw-disc read and inspect the candidate UYA resident TOC.
 * No ISO reconstruction and no level/container payload reads are performed.
 */
export async function probeUyaDiscToc(
  source: RandomAccessReader,
  options: UyaTocProbeOptions = {},
): Promise<UyaDiscTocProbeResult> {
  const tocLba = checkedNonNegativeInteger(options.tocLba ?? UYA_PUBLIC_TOC_LBA_HINT, "tocLba");
  const requestedWindow = checkedPositiveInteger(options.windowBytes ?? UYA_PUBLIC_TOC_WINDOW_BYTES_HINT, "windowBytes");
  const maxLevelRows = checkedPositiveInteger(options.maxLevelRows ?? UYA_PUBLIC_MAX_LEVEL_ROWS_HINT, "maxLevelRows");
  const readOffsetBytes = checkedProduct(tocLba, UYA_DISC_SECTOR_BYTES, "TOC byte offset");
  if (readOffsetBytes >= source.size) {
    throw new RangeError(`Candidate UYA TOC LBA ${tocLba} begins past ${source.name} (${source.size} bytes).`);
  }

  const windowBytes = Math.min(requestedWindow, source.size - readOffsetBytes);
  const bytes = await source.read(readOffsetBytes, windowBytes);
  if (bytes.length !== windowBytes) {
    throw new Error(`Short UYA TOC probe read from ${source.name}: expected ${windowBytes}, got ${bytes.length}.`);
  }
  const analysis = analyzeUyaTocWindow(bytes, { tocLba, maxLevelRows });
  return {
    ...analysis,
    sourceName: source.name,
    sourceSize: source.size,
    readOffsetBytes,
  };
}

/** Analyze an already-read candidate resident-TOC window. */
export function analyzeUyaTocWindow(
  bytes: Uint8Array,
  options: Pick<UyaTocProbeOptions, "tocLba" | "maxLevelRows"> = {},
): UyaTocWindowAnalysis {
  const tocLba = checkedNonNegativeInteger(options.tocLba ?? UYA_PUBLIC_TOC_LBA_HINT, "tocLba");
  const maxLevelRows = checkedPositiveInteger(options.maxLevelRows ?? UYA_PUBLIC_MAX_LEVEL_ROWS_HINT, "maxLevelRows");
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  const warnings: string[] = [];

  // Do not infer the level-table boundary merely by chaining plausible header-size words.
  // A table's first header LBA can itself look like a sane header size (the first CI fixture
  // exposed exactly that ambiguity). Prefer the pinned public two-row/six-header structure,
  // while keeping that semantic dependence explicit in the result.
  const structuralCandidates = scanRac234LevelTableStructuralCandidates(bytes, { tocLba });
  const publicLeadCandidates = filterUyaLevelTableCandidatesByPublicLead(structuralCandidates);

  let levelTableOffsetBytes: number;
  let levelTableDiscovery: UyaTocWindowAnalysis["levelTableDiscovery"];
  if (publicLeadCandidates.length === 1) {
    levelTableOffsetBytes = publicLeadCandidates[0]!.tableOffset;
    levelTableDiscovery = "structural-public-lead";
  } else {
    if (publicLeadCandidates.length > 1) {
      warnings.push(`Multiple (${publicLeadCandidates.length}) level-table offsets match the provisional public UYA header-size sequence; using conservative leading-header fallback.`);
    } else {
      warnings.push("No level-table offset matched the provisional public UYA two-row header-size sequence; using conservative leading-header fallback.");
    }
    levelTableOffsetBytes = findConservativeLeadingHeaderBoundary(bytes, view, tocLba);
    levelTableDiscovery = "fallback-leading-header-chain";
  }

  const globalHeaders: UyaTocGlobalHeaderCandidate[] = [];
  let offset = 0;
  while (offset < levelTableOffsetBytes) {
    if (offset + 8 > levelTableOffsetBytes) {
      warnings.push(`Resident header chain leaves ${levelTableOffsetBytes - offset} bytes before candidate level table.`);
      break;
    }
    const headerSize = view.getInt32(offset, true);
    if (headerSize < 8 || headerSize > UYA_TOC_MAX_HEADER_BYTES || offset + headerSize > levelTableOffsetBytes) {
      warnings.push(`Resident header at 0x${offset.toString(16)} cannot tile cleanly to candidate level table 0x${levelTableOffsetBytes.toString(16)}.`);
      break;
    }
    globalHeaders.push({
      index: globalHeaders.length,
      offsetBytes: offset,
      headerSize,
      rawWordAt0x04: view.getUint32(offset + 4, true),
    });
    offset += headerSize;
  }

  if (globalHeaders.length === 0 && levelTableOffsetBytes !== 0) {
    warnings.push("No plausible resident global headers were parsed before the candidate level table.");
  }
  if ((levelTableOffsetBytes & 3) !== 0) {
    warnings.push(`Candidate level-table offset 0x${levelTableOffsetBytes.toString(16)} is not 4-byte aligned.`);
  }

  const availableRows = Math.floor(Math.max(0, bytes.length - levelTableOffsetBytes) / UYA_TOC_LEVEL_ROW_BYTES);
  const rowsToInspect = Math.min(maxLevelRows, availableRows);
  const levelRows: UyaTocLevelRowCandidate[] = [];

  for (let index = 0; index < rowsToInspect; index++) {
    const rowOffset = levelTableOffsetBytes + index * UYA_TOC_LEVEL_ROW_BYTES;
    const raw: Array<{ headerLba: number; sizeSectors: number }> = [];
    let anyNonZero = false;
    for (let slot = 0; slot < 3; slot++) {
      const partOffset = rowOffset + slot * 8;
      const headerLba = view.getUint32(partOffset, true);
      const sizeSectors = view.getUint32(partOffset + 4, true);
      if (headerLba !== 0 || sizeSectors !== 0) anyNonZero = true;
      raw.push({ headerLba, sizeSectors });
    }
    if (!anyNonZero) continue;

    const parts = raw.map((part, slot) => inspectPart(bytes, view, tocLba, slot as 0 | 1 | 2, part.headerLba, part.sizeSectors));
    levelRows.push({
      index,
      offsetBytes: rowOffset,
      parts: [parts[0]!, parts[1]!, parts[2]!],
    });
  }

  if (levelRows.length === 0) {
    warnings.push("No non-zero 3-range rows were found at the candidate table offset.");
  }

  return {
    tocLba,
    windowBytes: bytes.length,
    globalHeaders,
    levelTableDiscovery,
    levelTableOffsetBytes,
    levelRows,
    warnings,
  };
}

function findConservativeLeadingHeaderBoundary(bytes: Uint8Array, view: DataView, tocLba: number): number {
  let offset = 0;
  while (offset + 8 <= bytes.length) {
    const headerSize = view.getInt32(offset, true);
    const wordAt04 = view.getUint32(offset + 4, true);
    if (headerSize < 8 || headerSize > UYA_TOC_MAX_HEADER_BYTES || offset + headerSize > bytes.length) break;
    // Public layout lead says +4 is an absolute file LBA. Requiring it to point beyond the
    // resident ToC is only a fallback discriminator; the result is labelled accordingly.
    if (wordAt04 <= tocLba) break;
    offset += headerSize;
  }
  return offset;
}

function inspectPart(
  bytes: Uint8Array,
  view: DataView,
  tocLba: number,
  slot: 0 | 1 | 2,
  headerLba: number,
  sizeSectors: number,
): UyaTocPartCandidate {
  const issues: string[] = [];
  if (headerLba === 0 && sizeSectors === 0) return { slot, headerLba, sizeSectors, issues };
  if (headerLba === 0 || sizeSectors === 0) {
    issues.push("range has only one of headerLba/sizeSectors set");
    return { slot, headerLba, sizeSectors, issues };
  }
  if (headerLba <= tocLba) {
    issues.push(`header LBA ${headerLba} does not point after candidate TOC LBA ${tocLba}`);
    return { slot, headerLba, sizeSectors, issues };
  }

  const relativeSectors = headerLba - tocLba;
  const headerOffsetInWindowBytes = checkedProduct(relativeSectors, UYA_DISC_SECTOR_BYTES, "candidate header offset");
  if (headerOffsetInWindowBytes + 8 > bytes.length) {
    issues.push(`pointed header is outside the ${bytes.length}-byte probe window`);
    return { slot, headerLba, sizeSectors, headerOffsetInWindowBytes, issues };
  }

  const headerSize = view.getInt32(headerOffsetInWindowBytes, true);
  const rawHeaderWordAt0x04 = view.getUint32(headerOffsetInWindowBytes + 4, true);
  if (headerSize < 8 || headerSize > UYA_TOC_MAX_HEADER_BYTES) {
    issues.push(`pointed header size 0x${(headerSize >>> 0).toString(16)} is outside the conservative 8..0xffff range`);
    return { slot, headerLba, sizeSectors, headerOffsetInWindowBytes, headerSize, rawHeaderWordAt0x04, issues };
  }
  if (headerOffsetInWindowBytes + headerSize > bytes.length) {
    issues.push(`pointed ${headerSize}-byte header is truncated by the probe window`);
  }

  const publicFormatHint = publicWrenchFormatHint(headerSize);
  return {
    slot,
    headerLba,
    sizeSectors,
    headerOffsetInWindowBytes,
    headerSize,
    rawHeaderWordAt0x04,
    ...(publicFormatHint ? { publicFormatHint } : {}),
    issues,
  };
}

/**
 * Header-size labels copied only as public corroboration leads from the pinned
 * Wrench snapshot. Callers must not treat these labels as retail/native truth.
 */
export function publicWrenchFormatHint(headerSize: number): UyaPublicFormatHint | undefined {
  const source = UYA_WRENCH_SOURCE;
  switch (headerSize) {
    case 0x0060: return { label: "level", gameHint: "gc-or-uya", source };
    case 0x1818: return { label: "level-audio", gameHint: "uya", source };
    case 0x26f0: return { label: "level-scene", gameHint: "unknown", source };
    case 0x0048: return { label: "misc", gameHint: "uya", source };
    case 0x03c8: return { label: "gadget", gameHint: "gc-or-uya", source };
    case 0x0648: return { label: "mpeg", gameHint: "uya", source };
    case 0x0bf0: return { label: "bonus", gameHint: "uya", source };
    case 0x0c30: return { label: "space", gameHint: "uya", source };
    case 0x0398: return { label: "armor", gameHint: "uya", source };
    case 0x2340: return { label: "audio", gameHint: "uya", source };
    case 0x2ab0: return { label: "hud", gameHint: "uya", source };
    default: return undefined;
  }
}

function checkedNonNegativeInteger(value: number, label: string): number {
  if (!Number.isSafeInteger(value) || value < 0) throw new RangeError(`${label} must be a non-negative safe integer.`);
  return value;
}

function checkedPositiveInteger(value: number, label: string): number {
  if (!Number.isSafeInteger(value) || value <= 0) throw new RangeError(`${label} must be a positive safe integer.`);
  return value;
}

function checkedProduct(a: number, b: number, label: string): number {
  const value = a * b;
  if (!Number.isSafeInteger(value)) throw new RangeError(`${label} exceeds JavaScript safe integer range.`);
  return value;
}
