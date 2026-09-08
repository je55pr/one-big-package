import type { RandomAccessReader } from "../../importer-common/src/index.js";
import { Sha256 } from "../../hashing/src/index.js";
import { WAD_LZ_HEADER_SIZE, decompressWad, readWadLzHeader } from "../../wad-lz/src/index.js";

export const UYA_PACKED_ELF_WRENCH_SOURCE = {
  repository: "chaoticgd/wrench",
  commit: "e67ea46e5e60ec52a0c31fcde1e3f46b20aa4cfb",
  loadingDoc: "docs/file_loading.md",
  unpackerPath: "src/wrenchbuild/common/elf_asset.cpp",
  ratchetExecutablePath: "src/core/elf.cpp",
} as const;

export const UYA_PUBLIC_LEAD_RATCHET_SECTION_HEADER_BYTES = 0x10;
export const UYA_LOADER_ANCHOR_CANDIDATE_LBA = 1001;
export const UYA_LOADER_ANCHOR_CANDIDATE_BYTE_OFFSET = UYA_LOADER_ANCHOR_CANDIDATE_LBA * 2048;
const DEFAULT_MAX_BOOT_EXECUTABLE_BYTES = 16 * 1024 * 1024;

export interface UyaPackedWadCandidatePublicLead {
  readonly offset: number;
  readonly compressedSize: number;
  readonly name: string;
}

export interface UyaRatchetSectionPublicLead {
  readonly index: number;
  readonly headerOffset: number;
  readonly dataOffset: number;
  readonly destAddressU32: number;
  readonly copySize: number;
  readonly sectionTypeU32: number;
  readonly entryPointU32: number;
  readonly dataSha256: string;
}

export interface UyaRatchetExecutableStreamPublicLead {
  readonly evidenceStatus: "public-format-lead-applied-to-observed-bytes";
  readonly byteLength: number;
  readonly sha256: string;
  readonly entryPointU32?: number;
  readonly sections: readonly UyaRatchetSectionPublicLead[];
  readonly consumedBytes: number;
  readonly terminatorOffset?: number;
  readonly trailingBytes: number;
  readonly warnings: readonly string[];
  readonly publicSource: typeof UYA_PACKED_ELF_WRENCH_SOURCE;
}

export type UyaLoaderAnchorHitKindPublicLead =
  | "aligned-u32-lba-1001"
  | "mips-direct-load-lba-1001"
  | "mips-lui-low-pair-byte-offset-0x001f4800";

export interface UyaLoaderAnchorHitPublicLead {
  readonly kind: UyaLoaderAnchorHitKindPublicLead;
  readonly sectionIndex: number;
  readonly sectionTypeU32: number;
  readonly sectionOffset: number;
  readonly streamOffset: number;
  readonly virtualAddressU32: number;
  readonly wordsU32: readonly number[];
}

export interface UyaLoaderAnchorScanPublicLead {
  readonly evidenceStatus: "candidate-constant-sites-only-not-loader-proof";
  readonly candidateLba: number;
  readonly candidateByteOffset: number;
  readonly hits: readonly UyaLoaderAnchorHitPublicLead[];
  readonly caveat: string;
}

export interface UyaDecodedPackedWadPublicLead {
  readonly candidate: UyaPackedWadCandidatePublicLead;
  readonly decompressedBytes: number;
  readonly decompressedSha256: string;
  readonly ratchetExecutable: UyaRatchetExecutableStreamPublicLead;
  readonly loaderAnchorScan: UyaLoaderAnchorScanPublicLead;
}

export interface UyaPackedBootExecutableProbeOptions {
  readonly maxExecutableBytes?: number;
  /** Optional caller-known exact identity. Checked before any embedded-format semantic scan/decode. */
  readonly expectedSha256?: string;
  /** Decode the embedded WAD only when exactly one structurally plausible candidate is found. */
  readonly decodeUniqueCandidate?: boolean;
  readonly maxDecompressedBytes?: number;
}

export interface UyaPackedBootExecutableProbePublicLead {
  readonly evidenceStatus: "retail-or-caller-bytes-observed-public-packed-elf-semantics-unconfirmed";
  readonly sourceName: string;
  readonly sizeBytes: number;
  readonly sha256: string;
  readonly wadCandidates: readonly UyaPackedWadCandidatePublicLead[];
  readonly decodedUniqueCandidate?: UyaDecodedPackedWadPublicLead;
  readonly warnings: readonly string[];
  readonly publicSource: typeof UYA_PACKED_ELF_WRENCH_SOURCE;
}

/**
 * Find every structurally plausible embedded WAD header instead of blindly trusting the first
 * ASCII "WAD" occurrence. This mirrors the public packing lead conservatively: classification is
 * withheld when there are zero or multiple candidates.
 */
export function scanUyaPackedExecutableWadCandidatesPublicLead(bytes: Uint8Array): readonly UyaPackedWadCandidatePublicLead[] {
  const candidates: UyaPackedWadCandidatePublicLead[] = [];
  for (let offset = 0; offset + WAD_LZ_HEADER_SIZE <= bytes.length; offset++) {
    if (bytes[offset] !== 0x57 || bytes[offset + 1] !== 0x41 || bytes[offset + 2] !== 0x44) continue;
    const headerBytes = bytes.subarray(offset, offset + WAD_LZ_HEADER_SIZE);
    try {
      const header = readWadLzHeader(headerBytes);
      if (header.compressedSize > bytes.length - offset) continue;
      candidates.push({ offset, compressedSize: header.compressedSize, name: header.name });
    } catch {
      // Raw magic with an invalid header remains intentionally unclassified.
    }
  }
  return candidates;
}

/**
 * Parse the public 16-byte Ratchet executable section stream without donor ELF metadata.
 * The first entry point becomes the stream entry point; a later differing entry point is treated
 * as the same terminator condition documented in the pinned Wrench reader.
 */
export function parseUyaRatchetExecutableStreamPublicLead(bytes: Uint8Array): UyaRatchetExecutableStreamPublicLead {
  const sections: UyaRatchetSectionPublicLead[] = [];
  const warnings: string[] = [];
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  let cursor = 0;
  let entryPointU32: number | undefined;
  let terminatorOffset: number | undefined;

  while (cursor + UYA_PUBLIC_LEAD_RATCHET_SECTION_HEADER_BYTES <= bytes.length) {
    const headerOffset = cursor;
    const destAddressU32 = view.getUint32(cursor + 0x00, true);
    const copySizeSigned = view.getInt32(cursor + 0x04, true);
    const sectionTypeU32 = view.getUint32(cursor + 0x08, true);
    const sectionEntryPointU32 = view.getUint32(cursor + 0x0c, true);

    if (entryPointU32 === undefined) entryPointU32 = sectionEntryPointU32;
    else if (sectionEntryPointU32 !== entryPointU32) {
      terminatorOffset = headerOffset;
      break;
    }
    if (copySizeSigned < 0) throw new Error(`UYA Ratchet executable candidate section ${sections.length} has negative copy_size ${copySizeSigned}.`);

    const dataOffset = cursor + UYA_PUBLIC_LEAD_RATCHET_SECTION_HEADER_BYTES;
    if (copySizeSigned > bytes.length - dataOffset) {
      throw new Error(`UYA Ratchet executable candidate section ${sections.length} data ${dataOffset}+${copySizeSigned} exceeds ${bytes.length} bytes.`);
    }
    const data = bytes.subarray(dataOffset, dataOffset + copySizeSigned);
    sections.push({
      index: sections.length,
      headerOffset,
      dataOffset,
      destAddressU32,
      copySize: copySizeSigned,
      sectionTypeU32,
      entryPointU32: sectionEntryPointU32,
      dataSha256: sha256(data),
    });
    cursor = dataOffset + copySizeSigned;
  }

  if (sections.length === 0) warnings.push("No complete public Ratchet executable sections were parsed.");
  if (terminatorOffset === undefined && cursor < bytes.length) {
    warnings.push(`Stream ended with ${bytes.length - cursor} bytes insufficient for another 0x10-byte public section header.`);
  }
  const consumedBytes = terminatorOffset ?? cursor;
  return {
    evidenceStatus: "public-format-lead-applied-to-observed-bytes",
    byteLength: bytes.length,
    sha256: sha256(bytes),
    ...(entryPointU32 !== undefined ? { entryPointU32 } : {}),
    sections,
    consumedBytes,
    ...(terminatorOffset !== undefined ? { terminatorOffset } : {}),
    trailingBytes: bytes.length - consumedBytes,
    warnings,
    publicSource: UYA_PACKED_ELF_WRENCH_SOURCE,
  };
}

/**
 * Census only obvious candidate constant sites for the public LBA-1001 lead in the decoded
 * Ratchet section stream. A hit is not evidence that the containing code performs disc loading;
 * it is merely a bounded set of virtual addresses for later disassembly/control-flow archaeology.
 */
export function scanUyaLoaderAnchorCandidatesPublicLead(
  bytes: Uint8Array,
  stream: UyaRatchetExecutableStreamPublicLead = parseUyaRatchetExecutableStreamPublicLead(bytes),
): UyaLoaderAnchorScanPublicLead {
  const hits: UyaLoaderAnchorHitPublicLead[] = [];
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);

  for (const section of stream.sections) {
    for (let sectionOffset = 0; sectionOffset + 4 <= section.copySize; sectionOffset += 4) {
      const streamOffset = section.dataOffset + sectionOffset;
      const word = view.getUint32(streamOffset, true);
      if (word === UYA_LOADER_ANCHOR_CANDIDATE_LBA) {
        hits.push(loaderHit("aligned-u32-lba-1001", section, sectionOffset, streamOffset, [word]));
      }

      const opcode = word >>> 26;
      const rs = (word >>> 21) & 0x1f;
      const immediate = word & 0xffff;
      if ((opcode === 0x09 || opcode === 0x0d) && rs === 0 && immediate === UYA_LOADER_ANCHOR_CANDIDATE_LBA) {
        hits.push(loaderHit("mips-direct-load-lba-1001", section, sectionOffset, streamOffset, [word]));
      }

      if (sectionOffset + 8 <= section.copySize && opcode === 0x0f && immediate === 0x001f) {
        const register = (word >>> 16) & 0x1f;
        const nextWord = view.getUint32(streamOffset + 4, true);
        const nextOpcode = nextWord >>> 26;
        const nextRs = (nextWord >>> 21) & 0x1f;
        const nextRt = (nextWord >>> 16) & 0x1f;
        const nextImmediate = nextWord & 0xffff;
        if ((nextOpcode === 0x09 || nextOpcode === 0x0d) && nextRs === register && nextRt === register && nextImmediate === 0x4800) {
          hits.push(loaderHit("mips-lui-low-pair-byte-offset-0x001f4800", section, sectionOffset, streamOffset, [word, nextWord]));
        }
      }
    }
  }

  return {
    evidenceStatus: "candidate-constant-sites-only-not-loader-proof",
    candidateLba: UYA_LOADER_ANCHOR_CANDIDATE_LBA,
    candidateByteOffset: UYA_LOADER_ANCHOR_CANDIDATE_BYTE_OFFSET,
    hits,
    caveat: "Constant/immediate matches can be unrelated data or code. Promote loader provenance only after disassembly/control-flow or independent native evidence ties a hit to disc reads.",
  };
}

/** Decompress one selected WAD candidate with the existing OBP WAD-LZ codec, then parse the section stream. */
export function decodeUyaPackedWadCandidatePublicLead(
  executableBytes: Uint8Array,
  candidate: UyaPackedWadCandidatePublicLead,
  options: { readonly maxDecompressedBytes?: number } = {},
): UyaDecodedPackedWadPublicLead {
  if (candidate.offset < 0 || candidate.compressedSize < WAD_LZ_HEADER_SIZE || candidate.offset > executableBytes.length || candidate.compressedSize > executableBytes.length - candidate.offset) {
    throw new RangeError("UYA packed executable WAD candidate lies outside the executable bytes.");
  }
  const block = executableBytes.subarray(candidate.offset, candidate.offset + candidate.compressedSize);
  const decompressed = decompressWad(block, {
    ...(options.maxDecompressedBytes !== undefined ? { maxOutputBytes: options.maxDecompressedBytes } : {}),
  }).data;
  const ratchetExecutable = parseUyaRatchetExecutableStreamPublicLead(decompressed);
  return {
    candidate,
    decompressedBytes: decompressed.length,
    decompressedSha256: sha256(decompressed),
    ratchetExecutable,
    loaderAnchorScan: scanUyaLoaderAnchorCandidatesPublicLead(decompressed, ratchetExecutable),
  };
}

/**
 * Read and hash the ISO-visible boot executable, optionally require a caller-known exact SHA,
 * census plausible packed-WAD headers, and optionally test the unique candidate with OBP's existing
 * WAD-LZ decoder. The identity check occurs before any packed-format semantic interpretation.
 * The whole-file read is capped because the supported retail UYA boot executable is small
 * (~771 KiB), not a multi-gigabyte disc payload.
 */
export async function probeUyaPackedBootExecutablePublicLead(
  executable: RandomAccessReader,
  options: UyaPackedBootExecutableProbeOptions = {},
): Promise<UyaPackedBootExecutableProbePublicLead> {
  const maxExecutableBytes = options.maxExecutableBytes ?? DEFAULT_MAX_BOOT_EXECUTABLE_BYTES;
  if (!Number.isSafeInteger(maxExecutableBytes) || maxExecutableBytes <= 0) throw new RangeError(`Invalid maxExecutableBytes ${maxExecutableBytes}.`);
  if (executable.size > maxExecutableBytes) {
    throw new Error(`UYA candidate boot executable '${executable.name}' is ${executable.size} bytes, above probe cap ${maxExecutableBytes}.`);
  }
  const bytes = await executable.read(0, executable.size);
  if (bytes.length !== executable.size) throw new Error(`Short UYA boot executable read from '${executable.name}'.`);

  const executableSha256 = sha256(bytes);
  if (options.expectedSha256 !== undefined) {
    const expectedSha256 = normalizeExpectedSha256(options.expectedSha256);
    if (executableSha256 !== expectedSha256) {
      throw new Error(
        `UYA boot executable identity mismatch: expected ${expectedSha256}, got ${executableSha256}. ` +
        "Refusing to apply packed-executable semantic probes to this source.",
      );
    }
  }

  const candidates = scanUyaPackedExecutableWadCandidatesPublicLead(bytes);
  const warnings: string[] = [];
  if (candidates.length === 0) warnings.push("No structurally plausible embedded WAD header found; public packed-ELF lead not corroborated.");
  if (candidates.length > 1) warnings.push(`Found ${candidates.length} structurally plausible embedded WAD headers; no unique packed payload inferred.`);

  let decodedUniqueCandidate: UyaDecodedPackedWadPublicLead | undefined;
  if (options.decodeUniqueCandidate && candidates.length === 1) {
    decodedUniqueCandidate = decodeUyaPackedWadCandidatePublicLead(bytes, candidates[0]!, {
      ...(options.maxDecompressedBytes !== undefined ? { maxDecompressedBytes: options.maxDecompressedBytes } : {}),
    });
  } else if (options.decodeUniqueCandidate && candidates.length !== 1) {
    warnings.push("Unique-candidate decode requested but candidate count is not exactly one; decompression skipped.");
  }

  return {
    evidenceStatus: "retail-or-caller-bytes-observed-public-packed-elf-semantics-unconfirmed",
    sourceName: executable.name,
    sizeBytes: executable.size,
    sha256: executableSha256,
    wadCandidates: candidates,
    ...(decodedUniqueCandidate ? { decodedUniqueCandidate } : {}),
    warnings,
    publicSource: UYA_PACKED_ELF_WRENCH_SOURCE,
  };
}

function loaderHit(
  kind: UyaLoaderAnchorHitKindPublicLead,
  section: UyaRatchetSectionPublicLead,
  sectionOffset: number,
  streamOffset: number,
  wordsU32: readonly number[],
): UyaLoaderAnchorHitPublicLead {
  return {
    kind,
    sectionIndex: section.index,
    sectionTypeU32: section.sectionTypeU32,
    sectionOffset,
    streamOffset,
    virtualAddressU32: (section.destAddressU32 + sectionOffset) >>> 0,
    wordsU32,
  };
}

function normalizeExpectedSha256(value: string): string {
  const normalized = value.trim().toLowerCase();
  if (!/^[0-9a-f]{64}$/.test(normalized)) throw new Error("expectedSha256 must be a 64-digit hexadecimal SHA-256.");
  return normalized;
}

function sha256(bytes: Uint8Array): string {
  return new Sha256().update(bytes).digestHex();
}
