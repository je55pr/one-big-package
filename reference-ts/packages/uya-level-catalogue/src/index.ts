import type {
  UyaTocPartCandidate,
  UyaTocWindowAnalysis,
} from "../../uya-disc-toc/src/index.js";

export interface UyaPublicLeadCataloguePart {
  readonly slot: 0 | 1 | 2;
  readonly headerLba: number;
  readonly sizeSectors: number;
  readonly publicKindHint?: "level" | "audio" | "scene";
}

export interface UyaPublicLeadCatalogueEntry {
  readonly tableIndex: number;
  /**
   * Provisional only. For a classified main-level header this is raw u32 @ header + 0x08,
   * following pinned Wrench behaviour. Index 38 may use Wrench's explicit no-main-WAD fallback.
   */
  readonly publicNativeLevelIdHint?: number;
  readonly publicNativeLevelIdEvidence?: "main-header-word-0x08" | "wrench-index-38-special-case";
  readonly parts: readonly UyaPublicLeadCataloguePart[];
  readonly warnings: readonly string[];
}

export interface UyaPublicLeadCatalogue {
  readonly evidenceStatus: "public-format-lead-applied-to-observed-bytes";
  readonly entries: readonly UyaPublicLeadCatalogueEntry[];
  readonly warnings: readonly string[];
}

/**
 * Build a catalogue without equating table index with native level ID.
 *
 * The supplied ToC analysis may ultimately come from retail bytes, but every semantic interpretation
 * performed here remains a public-source lead until separately promoted by UYA retail/executable evidence.
 */
export function buildUyaLevelCataloguePublicLead(
  tocWindow: Uint8Array,
  analysis: UyaTocWindowAnalysis,
): UyaPublicLeadCatalogue {
  const view = new DataView(tocWindow.buffer, tocWindow.byteOffset, tocWindow.byteLength);
  const catalogueWarnings: string[] = [];
  const entries = analysis.levelRows.map((row): UyaPublicLeadCatalogueEntry => {
    const warnings: string[] = [];
    const parts = row.parts.map((part): UyaPublicLeadCataloguePart => ({
      slot: part.slot,
      headerLba: part.headerLba,
      sizeSectors: part.sizeSectors,
      ...publicKindForPart(part),
    }));

    const mainParts = row.parts.filter((part) => part.publicFormatHint?.label === "level");
    let publicNativeLevelIdHint: number | undefined;
    let publicNativeLevelIdEvidence: UyaPublicLeadCatalogueEntry["publicNativeLevelIdEvidence"];

    if (mainParts.length === 1) {
      const main = mainParts[0]!;
      const headerOffset = main.headerOffsetInWindowBytes;
      if (headerOffset === undefined || headerOffset + 12 > tocWindow.length) {
        warnings.push("Public main-level header hint is not fully available through +0x08 in the bounded ToC window.");
      } else {
        publicNativeLevelIdHint = view.getUint32(headerOffset + 8, true);
        publicNativeLevelIdEvidence = "main-header-word-0x08";
      }
    } else if (mainParts.length > 1) {
      warnings.push(`Multiple (${mainParts.length}) parts have the public main-level header signature; native ID hint withheld.`);
    } else if (row.index === 38) {
      // Exact special case present in pinned Wrench iso_unpacker.cpp. Keep it visibly provisional.
      publicNativeLevelIdHint = 38;
      publicNativeLevelIdEvidence = "wrench-index-38-special-case";
    }

    const counts = new Map<string, number>();
    for (const part of parts) {
      if (!part.publicKindHint) continue;
      counts.set(part.publicKindHint, (counts.get(part.publicKindHint) ?? 0) + 1);
    }
    for (const [kind, count] of counts) {
      if (count > 1) warnings.push(`Multiple (${count}) parts classify as '${kind}' under public header-size hints.`);
    }

    return {
      tableIndex: row.index,
      ...(publicNativeLevelIdHint !== undefined ? { publicNativeLevelIdHint } : {}),
      ...(publicNativeLevelIdEvidence !== undefined ? { publicNativeLevelIdEvidence } : {}),
      parts,
      warnings,
    };
  });

  const nativeIds = new Map<number, number[]>();
  for (const entry of entries) {
    if (entry.publicNativeLevelIdHint === undefined) continue;
    const list = nativeIds.get(entry.publicNativeLevelIdHint) ?? [];
    list.push(entry.tableIndex);
    nativeIds.set(entry.publicNativeLevelIdHint, list);
  }
  for (const [id, indices] of nativeIds) {
    if (indices.length > 1) {
      catalogueWarnings.push(`Public native-level ID hint ${id} occurs at multiple table indices: ${indices.join(", ")}.`);
    }
  }

  return {
    evidenceStatus: "public-format-lead-applied-to-observed-bytes",
    entries,
    warnings: catalogueWarnings,
  };
}

function publicKindForPart(part: UyaTocPartCandidate): { readonly publicKindHint?: "level" | "audio" | "scene" } {
  switch (part.publicFormatHint?.label) {
    case "level": return { publicKindHint: "level" };
    case "level-audio": return { publicKindHint: "audio" };
    case "level-scene": return { publicKindHint: "scene" };
    default: return {};
  }
}
