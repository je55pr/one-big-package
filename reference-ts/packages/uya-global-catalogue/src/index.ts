import {
  publicWrenchFormatHint,
  type UyaPublicFormatHint,
  type UyaTocWindowAnalysis,
} from "../../uya-disc-toc/src/index.js";

export const UYA_PUBLIC_GLOBAL_ORDER = [
  "mpeg",
  "misc",
  "bonus",
  "space",
  "armor",
  "audio",
  "gadget",
  "hud",
] as const;

export const UYA_GLOBAL_ORDER_WRENCH_SOURCE = {
  repository: "chaoticgd/wrench",
  commit: "e67ea46e5e60ec52a0c31fcde1e3f46b20aa4cfb",
  path: "src/iso/iso_packer.cpp",
} as const;

export type UyaPublicGlobalKind = typeof UYA_PUBLIC_GLOBAL_ORDER[number];

export interface UyaPublicLeadGlobalCatalogueEntry {
  readonly index: number;
  readonly offsetBytes: number;
  readonly headerSize: number;
  readonly rawWordAt0x04: number;
  readonly expectedPublicKindAtIndex?: UyaPublicGlobalKind;
  readonly headerSizePublicHint?: UyaPublicFormatHint;
  readonly publicKindAgreesWithExpectedOrder?: boolean;
}

export interface UyaPublicLeadGlobalCatalogue {
  readonly evidenceStatus: "public-format-leads-applied-to-observed-bytes";
  readonly entries: readonly UyaPublicLeadGlobalCatalogueEntry[];
  readonly publicExpectedOrder: readonly UyaPublicGlobalKind[];
  readonly exactExpectedCount: boolean;
  readonly allClassifiedEntriesAgreeWithExpectedOrder: boolean;
  readonly warnings: readonly string[];
  readonly publicSource: typeof UYA_GLOBAL_ORDER_WRENCH_SOURCE;
}

/**
 * Overlay pinned Wrench's UYA global-container order and header-size labels onto an observed raw
 * resident-header census. The underlying ToC analysis deliberately remains semantic-free.
 */
export function buildUyaGlobalCataloguePublicLead(analysis: UyaTocWindowAnalysis): UyaPublicLeadGlobalCatalogue {
  const warnings: string[] = [];
  const entries = analysis.globalHeaders.map((header): UyaPublicLeadGlobalCatalogueEntry => {
    const expectedPublicKindAtIndex = UYA_PUBLIC_GLOBAL_ORDER[header.index];
    const headerSizePublicHint = publicWrenchFormatHint(header.headerSize);
    const hintedKind = asUyaGlobalKind(headerSizePublicHint?.label);
    const publicKindAgreesWithExpectedOrder = expectedPublicKindAtIndex !== undefined && hintedKind !== undefined
      ? expectedPublicKindAtIndex === hintedKind
      : undefined;
    if (publicKindAgreesWithExpectedOrder === false) {
      warnings.push(`Resident global index ${header.index}: public header-size hint '${hintedKind}' conflicts with public expected order '${expectedPublicKindAtIndex}'.`);
    }
    if (!headerSizePublicHint) {
      warnings.push(`Resident global index ${header.index}: header size 0x${header.headerSize.toString(16)} has no pinned UYA public format hint.`);
    }
    return {
      index: header.index,
      offsetBytes: header.offsetBytes,
      headerSize: header.headerSize,
      rawWordAt0x04: header.rawWordAt0x04,
      ...(expectedPublicKindAtIndex !== undefined ? { expectedPublicKindAtIndex } : {}),
      ...(headerSizePublicHint ? { headerSizePublicHint } : {}),
      ...(publicKindAgreesWithExpectedOrder !== undefined ? { publicKindAgreesWithExpectedOrder } : {}),
    };
  });

  const exactExpectedCount = entries.length === UYA_PUBLIC_GLOBAL_ORDER.length;
  if (!exactExpectedCount) {
    warnings.push(`Observed ${entries.length} resident global headers; pinned UYA public order expects ${UYA_PUBLIC_GLOBAL_ORDER.length}.`);
  }
  const allClassifiedEntriesAgreeWithExpectedOrder = entries.every((entry) => entry.publicKindAgreesWithExpectedOrder !== false);

  return {
    evidenceStatus: "public-format-leads-applied-to-observed-bytes",
    entries,
    publicExpectedOrder: UYA_PUBLIC_GLOBAL_ORDER,
    exactExpectedCount,
    allClassifiedEntriesAgreeWithExpectedOrder,
    warnings,
    publicSource: UYA_GLOBAL_ORDER_WRENCH_SOURCE,
  };
}

function asUyaGlobalKind(label: string | undefined): UyaPublicGlobalKind | undefined {
  if (!label) return undefined;
  return (UYA_PUBLIC_GLOBAL_ORDER as readonly string[]).includes(label) ? label as UyaPublicGlobalKind : undefined;
}
