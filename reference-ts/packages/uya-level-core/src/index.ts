import type { RandomAccessReader } from "../../importer-common/src/index.js";
import type {
  UyaLevelDataHeaderPublicLead,
  UyaLevelWadHeaderPublicLead,
} from "../../uya-level-wad/src/index.js";
import {
  WAD_LZ_HEADER_SIZE,
  readWadLzHeader,
} from "../../wad-lz/src/index.js";
import type { WadLzHeader } from "../../wad-lz/src/index.js";

/**
 * Public-source layout lead from Wrench e67ea46e5e60ec52a0c31fcde1e3f46b20aa4cfb.
 * The 0xbc size and every semantic field name below remain validation targets until
 * observed against the supported retail UYA authority bytes.
 */
export const UYA_PUBLIC_LEAD_LEVEL_CORE_HEADER_BYTES = 0xbc;

export const UYA_LEVEL_CORE_WRENCH_SOURCE = {
  repository: "chaoticgd/wrench",
  commit: "e67ea46e5e60ec52a0c31fcde1e3f46b20aa4cfb",
  headerPath: "src/wrenchbuild/level/level_core.h",
  unpackPath: "src/wrenchbuild/level/level_core.cpp",
  arrayRangePath: "src/core/util/binary_util.h",
} as const;

/** Pinned public `ArrayRange` layout is `{ s32 count; s32 offset; }`. */
export interface UyaPublicLeadArrayRange {
  readonly count: number;
  readonly offset: number;
}

export interface UyaLevelCoreFieldsPublicLead {
  readonly gsRam: UyaPublicLeadArrayRange;
  readonly tfrags: number;
  readonly occlusion: number;
  readonly sky: number;
  readonly collision: number;
  readonly mobyClasses: UyaPublicLeadArrayRange;
  readonly tieClasses: UyaPublicLeadArrayRange;
  readonly shrubClasses: UyaPublicLeadArrayRange;
  readonly tfragTextures: UyaPublicLeadArrayRange;
  readonly mobyTextures: UyaPublicLeadArrayRange;
  readonly tieTextures: UyaPublicLeadArrayRange;
  readonly shrubTextures: UyaPublicLeadArrayRange;
  readonly partTextures: UyaPublicLeadArrayRange;
  readonly fxTextures: UyaPublicLeadArrayRange;
  readonly texturesBaseOffset: number;
  readonly partBankOffset: number;
  readonly fxBankOffset: number;
  readonly partDefsOffset: number;
  readonly soundRemapOffset: number;
  readonly unknown74: number;
  readonly ratchetSeqsRac123: number;
  readonly sceneViewSize: number;
  readonly indexIntoSome1TexsRac2Maybe3: number;
  readonly mobyGsStashCountRac23Dl: number;
  readonly assetsCompressedSize: number;
  readonly assetsDecompressedSize: number;
  readonly chromeMapTexture: number;
  readonly chromeMapPalette: number;
  readonly glassMapTexture: number;
  readonly glassMapPalette: number;
  readonly unknownA0: number;
  readonly heightmapOffset: number;
  readonly occlusionOctOffset: number;
  readonly mobyGsStashList: number;
  readonly occlusionRadOffset: number;
  readonly mobySoundRemapOffset: number;
  readonly occlusionRad2Offset: number;
}

export interface UyaLevelCoreHeaderPublicLead {
  readonly evidenceStatus: "public-format-lead-applied-to-observed-bytes";
  readonly sourceName: string;
  /** Byte offset of the 0xbc candidate header from the beginning of the candidate level payload. */
  readonly offsetInLevelBytes: number;
  readonly headerBytes: number;
  /** Exact raw little-endian u32 words, retained so semantic labels never discard native evidence. */
  readonly rawWordsU32: readonly number[];
  readonly publicFields: UyaLevelCoreFieldsPublicLead;
  readonly publicSource: typeof UYA_LEVEL_CORE_WRENCH_SOURCE;
}

export interface UyaCoreDataWadLzHeaderPublicLead extends WadLzHeader {
  readonly evidenceStatus: "public-format-lead-applied-to-observed-bytes";
  readonly sourceName: string;
  /** Byte offset of the 16-byte WAD-LZ header from the beginning of the candidate level payload. */
  readonly offsetInLevelBytes: number;
  /** Size of the containing public coreData ByteRange. */
  readonly containingRangeBytes: number;
  readonly publicSource: typeof UYA_LEVEL_CORE_WRENCH_SOURCE;
}

/**
 * Read exactly the proposed 0xbc core-index header from a previously validated GC/UYA-style
 * level/data layout. Wrench reads this header directly from coreIndex; coreData is the section
 * that is WAD-LZ decompressed. This function therefore performs no decompression and no large read.
 */
export async function readUyaLevelCoreHeaderPublicLead(
  levelReader: RandomAccessReader,
  outer: UyaLevelWadHeaderPublicLead,
  dataHeader: UyaLevelDataHeaderPublicLead,
): Promise<UyaLevelCoreHeaderPublicLead> {
  const data = requireDataRange(outer);
  const coreIndex = dataHeader.coreIndex;
  if (!coreIndex.present) throw new Error("UYA candidate data header has no public coreIndex range.");
  if (coreIndex.size < UYA_PUBLIC_LEAD_LEVEL_CORE_HEADER_BYTES) {
    throw new Error(`UYA candidate coreIndex range is smaller than the public 0xbc LevelCoreHeader lead (${coreIndex.size} bytes).`);
  }

  const offsetInLevelBytes = checkedAdd(data.offsetBytes, coreIndex.offset, "candidate core-header byte offset");
  assertFixedReadWithin(levelReader, offsetInLevelBytes, UYA_PUBLIC_LEAD_LEVEL_CORE_HEADER_BYTES, "core-header");

  const bytes = await levelReader.read(offsetInLevelBytes, UYA_PUBLIC_LEAD_LEVEL_CORE_HEADER_BYTES);
  if (bytes.length !== UYA_PUBLIC_LEAD_LEVEL_CORE_HEADER_BYTES) {
    throw new Error(`Short UYA candidate core-header read from '${levelReader.name}'.`);
  }
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  const rawWordsU32: number[] = [];
  for (let at = 0; at < bytes.length; at += 4) rawWordsU32.push(view.getUint32(at, true));
  const arrayRange = (at: number): UyaPublicLeadArrayRange => ({
    count: view.getInt32(at, true),
    offset: view.getInt32(at + 4, true),
  });
  const s32 = (at: number): number => view.getInt32(at, true);

  return {
    evidenceStatus: "public-format-lead-applied-to-observed-bytes",
    sourceName: levelReader.name,
    offsetInLevelBytes,
    headerBytes: bytes.length,
    rawWordsU32,
    publicFields: {
      gsRam: arrayRange(0x00),
      tfrags: s32(0x08),
      occlusion: s32(0x0c),
      sky: s32(0x10),
      collision: s32(0x14),
      mobyClasses: arrayRange(0x18),
      tieClasses: arrayRange(0x20),
      shrubClasses: arrayRange(0x28),
      tfragTextures: arrayRange(0x30),
      mobyTextures: arrayRange(0x38),
      tieTextures: arrayRange(0x40),
      shrubTextures: arrayRange(0x48),
      partTextures: arrayRange(0x50),
      fxTextures: arrayRange(0x58),
      texturesBaseOffset: s32(0x60),
      partBankOffset: s32(0x64),
      fxBankOffset: s32(0x68),
      partDefsOffset: s32(0x6c),
      soundRemapOffset: s32(0x70),
      unknown74: s32(0x74),
      ratchetSeqsRac123: s32(0x78),
      sceneViewSize: s32(0x7c),
      indexIntoSome1TexsRac2Maybe3: s32(0x80),
      mobyGsStashCountRac23Dl: s32(0x84),
      assetsCompressedSize: s32(0x88),
      assetsDecompressedSize: s32(0x8c),
      chromeMapTexture: s32(0x90),
      chromeMapPalette: s32(0x94),
      glassMapTexture: s32(0x98),
      glassMapPalette: s32(0x9c),
      unknownA0: s32(0xa0),
      heightmapOffset: s32(0xa4),
      occlusionOctOffset: s32(0xa8),
      mobyGsStashList: s32(0xac),
      occlusionRadOffset: s32(0xb0),
      mobySoundRemapOffset: s32(0xb4),
      occlusionRad2Offset: s32(0xb8),
    },
    publicSource: UYA_LEVEL_CORE_WRENCH_SOURCE,
  };
}

/**
 * Test only the container signature at the proposed compressed coreData range.
 * The existing OBP WAD-LZ codec is GC-retail-verified; this 16-byte read is the first cheap
 * question UYA retail must answer before that codec is reused or any decompression is attempted.
 */
export async function readUyaCoreDataWadLzHeaderPublicLead(
  levelReader: RandomAccessReader,
  outer: UyaLevelWadHeaderPublicLead,
  dataHeader: UyaLevelDataHeaderPublicLead,
): Promise<UyaCoreDataWadLzHeaderPublicLead> {
  const data = requireDataRange(outer);
  const coreData = dataHeader.coreData;
  if (!coreData.present) throw new Error("UYA candidate data header has no public coreData range.");
  if (coreData.size < WAD_LZ_HEADER_SIZE) {
    throw new Error(`UYA candidate coreData range is smaller than the ${WAD_LZ_HEADER_SIZE}-byte WAD-LZ header (${coreData.size} bytes).`);
  }
  const offsetInLevelBytes = checkedAdd(data.offsetBytes, coreData.offset, "candidate coreData byte offset");
  assertFixedReadWithin(levelReader, offsetInLevelBytes, WAD_LZ_HEADER_SIZE, "coreData WAD-LZ header");
  const bytes = await levelReader.read(offsetInLevelBytes, WAD_LZ_HEADER_SIZE);
  if (bytes.length !== WAD_LZ_HEADER_SIZE) throw new Error(`Short UYA candidate coreData WAD-LZ header read from '${levelReader.name}'.`);
  const header = readWadLzHeader(bytes);
  if (header.compressedSize > coreData.size) {
    throw new Error(`UYA candidate WAD-LZ compressed size ${header.compressedSize} exceeds the public coreData range (${coreData.size} bytes).`);
  }
  return {
    evidenceStatus: "public-format-lead-applied-to-observed-bytes",
    sourceName: levelReader.name,
    offsetInLevelBytes,
    containingRangeBytes: coreData.size,
    compressedSize: header.compressedSize,
    name: header.name,
    publicSource: UYA_LEVEL_CORE_WRENCH_SOURCE,
  };
}

function requireDataRange(outer: UyaLevelWadHeaderPublicLead) {
  const data = outer.ranges[0];
  if (!data?.present) throw new Error("UYA candidate outer header has no public data range in slot 0.");
  return data;
}

function assertFixedReadWithin(reader: RandomAccessReader, offset: number, size: number, label: string): void {
  if (offset > reader.size || size > reader.size - offset) {
    throw new RangeError(`UYA candidate ${label} range ${offset}+${size} lies outside '${reader.name}' (${reader.size} bytes).`);
  }
}

function checkedAdd(a: number, b: number, label: string): number {
  const value = a + b;
  if (!Number.isSafeInteger(value) || value < 0) throw new RangeError(`${label} must be a non-negative safe integer.`);
  return value;
}
