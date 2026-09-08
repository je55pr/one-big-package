/**
 * Conservative parser for the Ratchet executable-section stream observed in UYA level overlays.
 * Section labels and the 12-byte Moby dispatch-table interpretation are pinned Wrench/GC-lineage
 * leads applied to retail UYA bytes; callers must not treat these names as recovered native symbols.
 */
export const UYA_LEVEL_OVERLAY_WRENCH_SOURCE = {
  repository: "chaoticgd/wrench",
  commit: "e67ea46e5e60ec52a0c31fcde1e3f46b20aa4cfb",
  loadingDoc: "docs/file_loading.md",
  ratchetExecutablePath: "src/core/elf.cpp",
} as const;

export const UYA_PUBLIC_LEAD_OVERLAY_SECTION_HEADER_BYTES = 0x10;
export const UYA_PUBLIC_LEAD_MOBY_DISPATCH_RECORD_BYTES = 0x0c;
export const UYA_PUBLIC_LEAD_LEVEL_SECTION_LABELS = [
  ".lit", ".bss", ".data", "lvl.vtbl", "lvl.camvtbl", "lvl.sndvtbl", ".text",
] as const;

export interface UyaLevelOverlaySectionPublicLead {
  readonly index: number;
  readonly publicLabel?: string;
  readonly headerOffset: number;
  readonly dataOffset: number;
  readonly destAddressU32: number;
  readonly copySize: number;
  readonly sectionTypeU32: number;
  readonly entryPointU32: number;
}

export interface UyaMobyDispatchRecordPublicLead {
  readonly index: number;
  readonly oClassGcCompatibility: number;
  readonly updateAddressU32: number;
  readonly auxAddressU32: number;
  readonly recordAddressU32: number;
}

export interface UyaMobyDispatchTablePublicLead {
  readonly evidenceStatus: "public-layout-lead-retail-structure-compatible";
  readonly sectionIndex: number;
  readonly sectionDestAddressU32: number;
  readonly sectionBytes: number;
  readonly records: readonly UyaMobyDispatchRecordPublicLead[];
  readonly terminatorOffset: number;
  readonly terminatorUpdateAddressU32: number;
  readonly terminatorAuxAddressU32: number;
}

export interface UyaLevelOverlayPublicLead {
  readonly evidenceStatus: "public-ratchet-section-lead-applied-to-observed-bytes";
  readonly entryPointU32?: number;
  readonly sections: readonly UyaLevelOverlaySectionPublicLead[];
  readonly consumedBytes: number;
  readonly trailingBytes: number;
  readonly mobyDispatch?: UyaMobyDispatchTablePublicLead;
  readonly publicSource: typeof UYA_LEVEL_OVERLAY_WRENCH_SOURCE;
}

export function parseUyaLevelOverlayPublicLead(bytes: Uint8Array): UyaLevelOverlayPublicLead {
  const sections = parseSections(bytes);
  const consumedBytes = sections.length === 0
    ? 0
    : sections.at(-1)!.dataOffset + sections.at(-1)!.copySize;
  const mobyDispatch = sections.length > 3
    ? parseUyaMobyDispatchTablePublicLead(bytes, sections[3]!)
    : undefined;
  return {
    evidenceStatus: "public-ratchet-section-lead-applied-to-observed-bytes",
    ...(sections[0] ? { entryPointU32: sections[0].entryPointU32 } : {}),
    sections,
    consumedBytes,
    trailingBytes: bytes.length - consumedBytes,
    ...(mobyDispatch ? { mobyDispatch } : {}),
    publicSource: UYA_LEVEL_OVERLAY_WRENCH_SOURCE,
  };
}

export function parseUyaMobyDispatchTablePublicLead(
  overlayBytes: Uint8Array,
  section: UyaLevelOverlaySectionPublicLead,
): UyaMobyDispatchTablePublicLead {
  if (section.dataOffset < 0 || section.copySize < 0 || section.dataOffset > overlayBytes.length || section.copySize > overlayBytes.length - section.dataOffset) {
    throw new RangeError(`UYA public Moby dispatch section ${section.index} lies outside the overlay bytes.`);
  }
  const bytes = overlayBytes.subarray(section.dataOffset, section.dataOffset + section.copySize);
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  const records: UyaMobyDispatchRecordPublicLead[] = [];
  const seen = new Set<number>();
  for (let at = 0; at + UYA_PUBLIC_LEAD_MOBY_DISPATCH_RECORD_BYTES <= bytes.length; at += UYA_PUBLIC_LEAD_MOBY_DISPATCH_RECORD_BYTES) {
    const oClass = view.getInt32(at, true);
    const update = view.getUint32(at + 4, true);
    const aux = view.getUint32(at + 8, true);
    if (oClass < 0) {
      if (oClass !== -1) throw new Error(`UYA public Moby dispatch lead found unexpected negative class ${oClass} at 0x${at.toString(16)}.`);
      return {
        evidenceStatus: "public-layout-lead-retail-structure-compatible",
        sectionIndex: section.index,
        sectionDestAddressU32: section.destAddressU32,
        sectionBytes: section.copySize,
        records,
        terminatorOffset: at,
        terminatorUpdateAddressU32: update,
        terminatorAuxAddressU32: aux,
      };
    }
    if (seen.has(oClass)) throw new Error(`UYA public Moby dispatch lead contains duplicate class ${oClass}.`);
    seen.add(oClass);
    records.push({
      index: records.length,
      oClassGcCompatibility: oClass,
      updateAddressU32: update,
      auxAddressU32: aux,
      recordAddressU32: (section.destAddressU32 + at) >>> 0,
    });
  }
  throw new Error("UYA public Moby dispatch lead has no -1 class terminator within its section extent.");
}

export function classifyUyaLevelOverlayAddressPublicLead(
  overlay: Pick<UyaLevelOverlayPublicLead, "sections">,
  addressU32: number,
): string {
  if ((addressU32 >>> 0) === 0) return "zero";
  const address = addressU32 >>> 0;
  const section = overlay.sections.find((candidate) => address >= candidate.destAddressU32 && address < candidate.destAddressU32 + candidate.copySize);
  return section?.publicLabel ?? (section ? `section${section.index}` : "external");
}

function parseSections(bytes: Uint8Array): UyaLevelOverlaySectionPublicLead[] {
  const sections: UyaLevelOverlaySectionPublicLead[] = [];
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  let cursor = 0;
  let entryPointU32: number | undefined;
  while (cursor + UYA_PUBLIC_LEAD_OVERLAY_SECTION_HEADER_BYTES <= bytes.length) {
    const headerOffset = cursor;
    const destAddressU32 = view.getUint32(cursor, true);
    const copySize = view.getUint32(cursor + 4, true);
    const sectionTypeU32 = view.getUint32(cursor + 8, true);
    const sectionEntryPointU32 = view.getUint32(cursor + 12, true);
    if (entryPointU32 === undefined) entryPointU32 = sectionEntryPointU32;
    else if (sectionEntryPointU32 !== entryPointU32) break;
    const dataOffset = cursor + UYA_PUBLIC_LEAD_OVERLAY_SECTION_HEADER_BYTES;
    if (copySize > bytes.length - dataOffset) throw new Error(`UYA public overlay section ${sections.length} overruns the supplied bytes.`);
    sections.push({
      index: sections.length,
      ...(UYA_PUBLIC_LEAD_LEVEL_SECTION_LABELS[sections.length] ? { publicLabel: UYA_PUBLIC_LEAD_LEVEL_SECTION_LABELS[sections.length] } : {}),
      headerOffset,
      dataOffset,
      destAddressU32,
      copySize,
      sectionTypeU32,
      entryPointU32: sectionEntryPointU32,
    });
    cursor = dataOffset + copySize;
  }
  return sections;
}
