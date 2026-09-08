import type { RandomAccessReader } from "../../importer-common/src/index.js";
import {
  UYA_PUBLIC_MAX_LEVEL_ROWS_HINT,
  UYA_PUBLIC_TOC_LBA_HINT,
  UYA_PUBLIC_TOC_WINDOW_BYTES_HINT,
  probeUyaDiscToc,
} from "../../uya-disc-toc/src/index.js";
import type {
  UyaDiscTocProbeResult,
  UyaTocLevelRowCandidate,
  UyaTocPartCandidate,
} from "../../uya-disc-toc/src/index.js";
import {
  openUyaTocPayloadPublicLead,
  readUyaLevelDataHeaderPublicLead,
  readUyaLevelWadHeaderPublicLead,
} from "../../uya-level-wad/src/index.js";
import type {
  UyaLevelDataHeaderPublicLead,
  UyaLevelWadHeaderPublicLead,
} from "../../uya-level-wad/src/index.js";
import {
  readUyaCoreDataWadLzHeaderPublicLead,
  readUyaLevelCoreHeaderPublicLead,
} from "../../uya-level-core/src/index.js";
import type {
  UyaCoreDataWadLzHeaderPublicLead,
  UyaLevelCoreHeaderPublicLead,
} from "../../uya-level-core/src/index.js";

export interface UyaWorldCandidateProbePublicLeadOptions {
  /** Sparse native table index, deliberately not equated with engine level ID. */
  readonly tableIndex: number;
  readonly tocLba?: number;
  readonly tocWindowBytes?: number;
  readonly maxLevelRows?: number;
}

export interface UyaWorldCandidateProbePublicLeadResult {
  readonly evidenceStatus: "public-format-leads-applied-to-observed-bytes";
  readonly disc: {
    readonly sourceName: string;
    readonly sizeBytes: number;
  };
  readonly toc: UyaDiscTocProbeResult;
  readonly selectedTableIndex: number;
  readonly selectedRow: UyaTocLevelRowCandidate;
  readonly selectedMainPart: UyaTocPartCandidate;
  readonly levelPayload: {
    readonly sourceName: string;
    readonly sizeBytes: number;
    readonly outer: UyaLevelWadHeaderPublicLead;
    readonly data: UyaLevelDataHeaderPublicLead;
    readonly core: UyaLevelCoreHeaderPublicLead;
    readonly coreDataWadLz: UyaCoreDataWadLzHeaderPublicLead;
    readonly compatibilityChecks: {
      /** Public Wrench packing code assigns this core field from the compressed WAD byte size. */
      readonly coreCompressedSizeMatchesWadHeader: boolean;
    };
  };
}

/**
 * Compose the cheapest current UYA vertical archaeology path without decompression:
 *
 * retail/random-access disc -> candidate resident ToC -> one sparse table row -> proposed main-level
 * payload -> 0x60 outer header -> 0x58 data header -> 0xbc core header -> 0x10 WAD-LZ header.
 *
 * Semantic classifications are still pinned-public-source leads. The bytes observed by a caller are
 * real evidence, but this function intentionally does not promote any lead to native truth by itself.
 */
export async function probeUyaWorldCandidatePublicLead(
  disc: RandomAccessReader,
  options: UyaWorldCandidateProbePublicLeadOptions,
): Promise<UyaWorldCandidateProbePublicLeadResult> {
  const tableIndex = requireNonNegativeSafeInteger(options.tableIndex, "tableIndex");
  const toc = await probeUyaDiscToc(disc, {
    tocLba: options.tocLba ?? UYA_PUBLIC_TOC_LBA_HINT,
    windowBytes: options.tocWindowBytes ?? UYA_PUBLIC_TOC_WINDOW_BYTES_HINT,
    maxLevelRows: options.maxLevelRows ?? UYA_PUBLIC_MAX_LEVEL_ROWS_HINT,
  });

  const selectedRow = toc.levelRows.find((row) => row.index === tableIndex);
  if (!selectedRow) {
    throw new Error(`UYA candidate ToC has no observed non-zero row at table index ${tableIndex}.`);
  }
  const mainParts = selectedRow.parts.filter((part) => part.publicFormatHint?.label === "level");
  if (mainParts.length !== 1) {
    throw new Error(`UYA table index ${tableIndex} has ${mainParts.length} parts matching the public 0x60 main-level header lead; expected exactly one.`);
  }
  const selectedMainPart = mainParts[0]!;
  if (selectedMainPart.issues.length !== 0) {
    throw new Error(`UYA table index ${tableIndex} public main-level part has unresolved ToC issues: ${selectedMainPart.issues.join("; ")}`);
  }

  const levelReader = openUyaTocPayloadPublicLead(disc, selectedMainPart, `uya-table-${tableIndex}-candidate-level`);
  const outer = await readUyaLevelWadHeaderPublicLead(levelReader);
  const data = await readUyaLevelDataHeaderPublicLead(levelReader, outer);
  const core = await readUyaLevelCoreHeaderPublicLead(levelReader, outer, data);
  const coreDataWadLz = await readUyaCoreDataWadLzHeaderPublicLead(levelReader, outer, data);

  return {
    evidenceStatus: "public-format-leads-applied-to-observed-bytes",
    disc: {
      sourceName: disc.name,
      sizeBytes: disc.size,
    },
    toc,
    selectedTableIndex: tableIndex,
    selectedRow,
    selectedMainPart,
    levelPayload: {
      sourceName: levelReader.name,
      sizeBytes: levelReader.size,
      outer,
      data,
      core,
      coreDataWadLz,
      compatibilityChecks: {
        coreCompressedSizeMatchesWadHeader: core.publicFields.assetsCompressedSize === coreDataWadLz.compressedSize,
      },
    },
  };
}

function requireNonNegativeSafeInteger(value: number, label: string): number {
  if (!Number.isSafeInteger(value) || value < 0) throw new RangeError(`${label} must be a non-negative safe integer.`);
  return value;
}
