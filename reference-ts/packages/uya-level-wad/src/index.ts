import { SubRangeReader } from "../../importer-common/src/index.js";
import type { RandomAccessReader } from "../../importer-common/src/index.js";
import type { UyaTocPartCandidate } from "../../uya-disc-toc/src/index.js";

/**
 * Public-source layout lead from Wrench e67ea46e5e60ec52a0c31fcde1e3f46b20aa4cfb.
 * These constants are validation targets, not retail-confirmed UYA declarations.
 */
export const UYA_PUBLIC_LEAD_LEVEL_WAD_HEADER_BYTES = 0x60;
export const UYA_PUBLIC_LEAD_LEVEL_DATA_HEADER_BYTES = 0x58;
export const UYA_DISC_SECTOR_BYTES = 0x800;

/** Names follow the pinned Wrench GC/UYA 0x60 file-header documentation. */
export const UYA_PUBLIC_LEAD_LEVEL_RANGE_LABELS = [
  "primary",
  "core-bank",
  "gameplay",
  "occlusion",
  "chunk-0",
  "chunk-1",
  "chunk-2",
  "chunk-sound-bank-0",
  "chunk-sound-bank-1",
  "chunk-sound-bank-2",
] as const;

export type UyaPublicLeadLevelRangeLabel = (typeof UYA_PUBLIC_LEAD_LEVEL_RANGE_LABELS)[number];

export interface UyaPublicLeadSectorRange {
  readonly slot: number;
  readonly publicLabel: UyaPublicLeadLevelRangeLabel;
  readonly offsetSectors: number;
  readonly sizeSectors: number;
  readonly offsetBytes: number;
  readonly sizeBytes: number;
  readonly present: boolean;
}

export interface UyaLevelWadHeaderPublicLead {
  readonly headerSize: number;
  /** Raw payload-header word @ +0x04. Do not equate this with the resident ToC copy's file LBA. */
  readonly rawWord0x04: number;
  /** Public Wrench interpretation of raw word @ +0x08. */
  readonly publicLevelIdHint: number;
  /** Public Wrench interpretation of raw word @ +0x0c. */
  readonly publicReverbHint: number;
  readonly ranges: readonly UyaPublicLeadSectorRange[];
}

export interface UyaPublicLeadByteRange {
  readonly offset: number;
  readonly size: number;
  readonly present: boolean;
}

export interface UyaLevelDataHeaderPublicLead {
  readonly overlay: UyaPublicLeadByteRange;
  readonly coreIndex: UyaPublicLeadByteRange;
  readonly gsRam: UyaPublicLeadByteRange;
  readonly hudHeader: UyaPublicLeadByteRange;
  readonly hudBanks: readonly UyaPublicLeadByteRange[];
  readonly coreData: UyaPublicLeadByteRange;
  readonly transitionTextures: UyaPublicLeadByteRange;
}

export interface UyaLevelLayoutValidationPublicLead {
  readonly sourceName: string;
  readonly sourceSize: number;
  readonly outer: UyaLevelWadHeaderPublicLead;
  readonly dataHeader?: UyaLevelDataHeaderPublicLead;
  readonly warnings: readonly string[];
}

/**
 * Turn a ToC part into a bounded payload view using only the explicitly public interpretation
 * of pointed-header +0x04 as payload LBA and the table's size in sectors.
 */
export function openUyaTocPayloadPublicLead(
  disc: RandomAccessReader,
  part: UyaTocPartCandidate,
  label = "uya-candidate-level-wad",
): RandomAccessReader {
  if (part.rawHeaderWordAt0x04 === undefined) {
    throw new Error("UYA ToC part has no resident header word @ +0x04; public payload LBA cannot be resolved.");
  }
  const payloadLba = requireNonNegativeSafeInteger(part.rawHeaderWordAt0x04, "public payload LBA hint");
  const sizeSectors = requireNonNegativeSafeInteger(part.sizeSectors, "payload size sectors");
  if (sizeSectors === 0) throw new Error("UYA ToC part has zero payload size sectors.");
  const offsetBytes = checkedSectorBytes(payloadLba, "public payload byte offset");
  const sizeBytes = checkedSectorBytes(sizeSectors, "public payload byte size");
  if (offsetBytes > disc.size || sizeBytes > disc.size - offsetBytes) {
    throw new RangeError(`Public UYA payload range ${offsetBytes}+${sizeBytes} lies outside '${disc.name}' (${disc.size} bytes).`);
  }
  return new SubRangeReader(disc, offsetBytes, sizeBytes, `${disc.name}#${label}`);
}

/** Read only the proposed fixed 0x60-byte GC/UYA outer level header. */
export async function readUyaLevelWadHeaderPublicLead(reader: RandomAccessReader): Promise<UyaLevelWadHeaderPublicLead> {
  if (reader.size < UYA_PUBLIC_LEAD_LEVEL_WAD_HEADER_BYTES) {
    throw new Error(`UYA candidate '${reader.name}' is smaller than the public 0x60-byte level-header lead.`);
  }
  const bytes = await reader.read(0, UYA_PUBLIC_LEAD_LEVEL_WAD_HEADER_BYTES);
  if (bytes.length !== UYA_PUBLIC_LEAD_LEVEL_WAD_HEADER_BYTES) throw new Error(`Short UYA candidate level-header read from '${reader.name}'.`);
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  const headerSize = view.getInt32(0x00, true);
  if (headerSize !== UYA_PUBLIC_LEAD_LEVEL_WAD_HEADER_BYTES) {
    throw new Error(`UYA candidate '${reader.name}' header size 0x${(headerSize >>> 0).toString(16)} does not match public GC/UYA 0x60 lead.`);
  }

  const ranges: UyaPublicLeadSectorRange[] = [];
  for (let slot = 0; slot < UYA_PUBLIC_LEAD_LEVEL_RANGE_LABELS.length; slot++) {
    const at = 0x10 + slot * 8;
    const offsetSectors = view.getInt32(at, true);
    const sizeSectors = view.getInt32(at + 4, true);
    const present = offsetSectors !== 0 || sizeSectors !== 0;
    if (present && (offsetSectors <= 0 || sizeSectors <= 0)) {
      throw new Error(`UYA candidate '${reader.name}' public ${UYA_PUBLIC_LEAD_LEVEL_RANGE_LABELS[slot]} range has invalid sectors ${offsetSectors}+${sizeSectors}.`);
    }
    const offsetBytes = present ? checkedSectorBytes(offsetSectors, `level range ${slot} offset`) : 0;
    const sizeBytes = present ? checkedSectorBytes(sizeSectors, `level range ${slot} size`) : 0;
    if (present && (offsetBytes > reader.size || sizeBytes > reader.size - offsetBytes)) {
      throw new RangeError(`UYA candidate '${reader.name}' public ${UYA_PUBLIC_LEAD_LEVEL_RANGE_LABELS[slot]} range ${offsetBytes}+${sizeBytes} lies outside ${reader.size}-byte payload extent.`);
    }
    ranges.push({
      slot,
      publicLabel: UYA_PUBLIC_LEAD_LEVEL_RANGE_LABELS[slot]!,
      offsetSectors,
      sizeSectors,
      offsetBytes,
      sizeBytes,
      present,
    });
  }

  return {
    headerSize,
    rawWord0x04: view.getInt32(0x04, true),
    publicLevelIdHint: view.getInt32(0x08, true),
    publicReverbHint: view.getInt32(0x0c, true),
    ranges,
  };
}

/** Read the proposed 0x58-byte GC/UYA data header from a previously validated primary range. */
export async function readUyaLevelDataHeaderPublicLead(
  levelReader: RandomAccessReader,
  outer: UyaLevelWadHeaderPublicLead,
): Promise<UyaLevelDataHeaderPublicLead> {
  const primary = outer.ranges[0];
  if (!primary?.present) throw new Error("UYA candidate outer header has no public primary range in slot 0.");
  if (primary.sizeBytes < UYA_PUBLIC_LEAD_LEVEL_DATA_HEADER_BYTES) {
    throw new Error(`UYA candidate primary range is smaller than the public 0x58-byte GcUyaLevelDataHeader lead.`);
  }
  const bytes = await levelReader.read(primary.offsetBytes, UYA_PUBLIC_LEAD_LEVEL_DATA_HEADER_BYTES);
  if (bytes.length !== UYA_PUBLIC_LEAD_LEVEL_DATA_HEADER_BYTES) throw new Error(`Short UYA candidate level-data header read from '${levelReader.name}'.`);
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  const range = (at: number): UyaPublicLeadByteRange => {
    const offset = view.getInt32(at, true);
    const size = view.getInt32(at + 4, true);
    const present = size !== 0;
    if (present && (offset < 0 || size < 0 || offset > primary.sizeBytes || size > primary.sizeBytes - offset)) {
      throw new RangeError(`UYA candidate data-header range @0x${at.toString(16)} ${offset}+${size} lies outside ${primary.sizeBytes}-byte primary section.`);
    }
    return { offset, size, present };
  };

  return {
    overlay: range(0x00),
    coreIndex: range(0x08),
    gsRam: range(0x10),
    hudHeader: range(0x18),
    hudBanks: [range(0x20), range(0x28), range(0x30), range(0x38), range(0x40)],
    coreData: range(0x48),
    transitionTextures: range(0x50),
  };
}

/**
 * Cheap two-header validation. This reads 0x60 bytes plus, when slot 0 is present, exactly 0x58 more bytes.
 * It deliberately stops before WAD-LZ decompression or any large level payload read.
 */
export async function validateUyaLevelLayoutPublicLead(reader: RandomAccessReader): Promise<UyaLevelLayoutValidationPublicLead> {
  const warnings: string[] = [];
  const outer = await readUyaLevelWadHeaderPublicLead(reader);
  let dataHeader: UyaLevelDataHeaderPublicLead | undefined;
  if (outer.ranges[0]?.present) {
    dataHeader = await readUyaLevelDataHeaderPublicLead(reader, outer);
    if (!dataHeader.coreIndex.present) warnings.push("Public coreIndex range is absent.");
    if (!dataHeader.coreData.present) warnings.push("Public coreData range is absent.");
    if (!dataHeader.gsRam.present) warnings.push("Public gsRam range is absent.");
  } else {
    warnings.push("Public primary range (outer slot 0) is absent; data-header validation skipped.");
  }
  return {
    sourceName: reader.name,
    sourceSize: reader.size,
    outer,
    ...(dataHeader ? { dataHeader } : {}),
    warnings,
  };
}

function checkedSectorBytes(sectors: number, label: string): number {
  const value = requireNonNegativeSafeInteger(sectors, label) * UYA_DISC_SECTOR_BYTES;
  if (!Number.isSafeInteger(value)) throw new RangeError(`${label} exceeds JavaScript safe integer range.`);
  return value;
}

function requireNonNegativeSafeInteger(value: number, label: string): number {
  if (!Number.isSafeInteger(value) || value < 0) throw new RangeError(`${label} must be a non-negative safe integer.`);
  return value;
}
