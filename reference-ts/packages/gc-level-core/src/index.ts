import { SubRangeReader } from "../../importer-common/src/index.js";
import type { RandomAccessReader } from "../../importer-common/src/index.js";
import { readWadLz } from "../../wad-lz/src/index.js";

/**
 * The Going Commando / UYA level "core": the `data` lump (slot 0 of the level WAD)
 * holds a `GcUyaLevelDataHeader`, which points at an uncompressed *index* block
 * (a `LevelCoreHeader` plus class/texture tables) and a WAD-LZ compressed *asset*
 * blob. Section offsets in the `LevelCoreHeader` are byte offsets into the
 * decompressed asset blob; a section's end is the next section offset above it.
 *
 * Verified against retail Going Commando (SCUS-97268 v1.01): the decompressed
 * asset blob length matches `assetsDecompressedSize` for all 27 levels, and the
 * derived collision section parses through `rc-collision` for all 27.
 * See research/GC_LEVEL_CORE.md.
 *
 * ```text
 * GcUyaLevelDataHeader @ 0 (in the data lump)
 *   0x00 ByteRange overlay        { s32 offset; s32 size }
 *   0x08 ByteRange coreIndex      uncompressed
 *   0x10 ByteRange gsRam
 *   0x18 ByteRange hudHeader
 *   0x20 ByteRange hudBanks[5]
 *   0x48 ByteRange coreData       WAD-LZ compressed asset blob
 *   0x50 ByteRange transitionTextures
 *
 * LevelCoreHeader @ 0 (in coreIndex), 0xbc bytes, all s32 LE
 *   0x00 ArrayRange gsRam         { s32 count; s32 offset }   offsets below index coreIndex
 *   0x08 tfrags        0x0c occlusion   0x10 sky      0x14 collision      (offsets into the asset blob)
 *   0x18 ArrayRange mobyClasses   0x20 tieClasses    0x28 shrubClasses
 *   0x30..0x5c  tfrag/moby/tie/shrub/part/fx texture ArrayRanges
 *   0x60 texturesBaseOffset
 *   0x88 assetsCompressedSize     0x8c assetsDecompressedSize
 *   0xb4 mobySoundRemapOffset
 *   ... (other fields preserved as `raw`)
 * ```
 */

export const GC_LEVEL_CORE_HEADER_SIZE = 0xbc;
const DEFAULT_MAX_DECOMPRESSED_BYTES = 128 * 1024 * 1024;

export interface ByteRange {
  readonly offset: number;
  readonly size: number;
}

export interface ArrayRange {
  readonly count: number;
  readonly offset: number;
}

export interface GcLevelDataHeader {
  readonly overlay: ByteRange;
  readonly coreIndex: ByteRange;
  readonly gsRam: ByteRange;
  readonly hudHeader: ByteRange;
  readonly hudBanks: readonly ByteRange[];
  readonly coreData: ByteRange;
  readonly transitionTextures: ByteRange;
}

export interface GcLevelCoreHeader {
  readonly gsRam: ArrayRange;
  /** Section byte offsets into the decompressed asset blob. `0` = absent. */
  readonly tfrags: number;
  readonly occlusion: number;
  readonly sky: number;
  readonly collision: number;
  readonly mobyClasses: ArrayRange;
  readonly tieClasses: ArrayRange;
  readonly shrubClasses: ArrayRange;
  readonly tfragTextures: ArrayRange;
  readonly mobyTextures: ArrayRange;
  readonly tieTextures: ArrayRange;
  readonly shrubTextures: ArrayRange;
  readonly partTextures: ArrayRange;
  readonly fxTextures: ArrayRange;
  readonly texturesBaseOffset: number;
  readonly partBankOffset: number;
  readonly fxBankOffset: number;
  readonly soundRemapOffset: number;
  readonly ratchetSeqsOffset: number;
  readonly assetsCompressedSize: number;
  readonly assetsDecompressedSize: number;
  readonly mobySoundRemapOffset: number;
  /** All 47 raw u32 words of the header, for fields not surfaced above. */
  readonly raw: readonly number[];
}

export interface GcLevelCore {
  readonly dataHeader: GcLevelDataHeader;
  readonly coreHeader: GcLevelCoreHeader;
  /** Uncompressed index block (`LevelCoreHeader` + class / texture tables). */
  readonly index: Uint8Array;
  /** Decompressed asset blob. Section offsets are relative to its start. */
  readonly assets: Uint8Array;
  /** Uncompressed GS RAM block from the data lump (texture palettes live here). */
  readonly gsRam: Uint8Array;
  /** Ascending, de-duplicated section-boundary offsets within {@link assets}. */
  readonly sectionBoundaries: readonly number[];
}

export interface GcLevelCoreOptions {
  readonly maxDecompressedBytes?: number;
}

/** Parse a level's core from its `data` lump (slot 0 of the level WAD). */
export async function openGcLevelCore(dataLump: RandomAccessReader, options: GcLevelCoreOptions = {}): Promise<GcLevelCore> {
  if (dataLump.size < 0x58) throw new Error(`GC level data lump '${dataLump.name}' is too small for a GcUyaLevelDataHeader.`);
  const headerBytes = await dataLump.read(0, 0x58);
  const hv = new DataView(headerBytes.buffer, headerBytes.byteOffset, headerBytes.byteLength);
  const range = (at: number): ByteRange => ({ offset: hv.getInt32(at, true), size: hv.getInt32(at + 4, true) });

  const dataHeader: GcLevelDataHeader = {
    overlay: range(0x00),
    coreIndex: range(0x08),
    gsRam: range(0x10),
    hudHeader: range(0x18),
    hudBanks: [range(0x20), range(0x28), range(0x30), range(0x38), range(0x40)],
    coreData: range(0x48),
    transitionTextures: range(0x50),
  };

  assertRangeWithin(dataHeader.coreIndex, dataLump.size, "coreIndex", dataLump.name);
  assertRangeWithin(dataHeader.coreData, dataLump.size, "coreData", dataLump.name);
  assertRangeWithin(dataHeader.gsRam, dataLump.size, "gsRam", dataLump.name);
  if (dataHeader.coreIndex.size < GC_LEVEL_CORE_HEADER_SIZE) {
    throw new Error(`GC level coreIndex is smaller than the ${GC_LEVEL_CORE_HEADER_SIZE}-byte LevelCoreHeader.`);
  }

  const index = await dataLump.read(dataHeader.coreIndex.offset, dataHeader.coreIndex.size);
  const iv = new DataView(index.buffer, index.byteOffset, index.byteLength);
  const s32 = (at: number): number => iv.getInt32(at, true);
  const arr = (at: number): ArrayRange => ({ count: s32(at), offset: s32(at + 4) });
  const raw: number[] = [];
  for (let i = 0; i < GC_LEVEL_CORE_HEADER_SIZE; i += 4) raw.push(iv.getUint32(i, true));

  const coreHeader: GcLevelCoreHeader = {
    gsRam: arr(0x00),
    tfrags: s32(0x08),
    occlusion: s32(0x0c),
    sky: s32(0x10),
    collision: s32(0x14),
    mobyClasses: arr(0x18),
    tieClasses: arr(0x20),
    shrubClasses: arr(0x28),
    tfragTextures: arr(0x30),
    mobyTextures: arr(0x38),
    tieTextures: arr(0x40),
    shrubTextures: arr(0x48),
    partTextures: arr(0x50),
    fxTextures: arr(0x58),
    texturesBaseOffset: s32(0x60),
    partBankOffset: s32(0x64),
    fxBankOffset: s32(0x68),
    soundRemapOffset: s32(0x70),
    ratchetSeqsOffset: s32(0x78),
    assetsCompressedSize: s32(0x88),
    assetsDecompressedSize: s32(0x8c),
    mobySoundRemapOffset: s32(0xb4),
    raw,
  };

  const maxDecompressedBytes = options.maxDecompressedBytes ?? DEFAULT_MAX_DECOMPRESSED_BYTES;
  const coreDataReader = new SubRangeReader(dataLump, dataHeader.coreData.offset, dataHeader.coreData.size, `${dataLump.name}#coreData`);
  const { data: assets } = await readWadLz(coreDataReader, 0, { maxOutputBytes: maxDecompressedBytes });

  if (coreHeader.assetsDecompressedSize > 0 && assets.length !== coreHeader.assetsDecompressedSize) {
    throw new Error(
      `GC level core: decompressed asset blob is ${assets.length} bytes but the header declares ${coreHeader.assetsDecompressedSize}.`,
    );
  }

  const gsRam = dataHeader.gsRam.size > 0
    ? await dataLump.read(dataHeader.gsRam.offset, dataHeader.gsRam.size)
    : new Uint8Array();

  const sectionBoundaries = enumerateSectionBoundaries(coreHeader, iv, assets.length);
  return { dataHeader, coreHeader, index, assets, gsRam, sectionBoundaries };
}

/**
 * Byte range of the section that starts at `offset` in the asset blob: from
 * `offset` to the next larger section boundary. Returns `null` for `offset <= 0`
 * (absent section) or when no boundary lies above it.
 */
export function gcLevelCoreSectionRange(core: GcLevelCore, offset: number): ByteRange | null {
  if (offset <= 0) return null;
  let next = -1;
  for (const bound of core.sectionBoundaries) {
    if (bound > offset && (next === -1 || bound < next)) next = bound;
  }
  if (next === -1) return null;
  return { offset, size: next - offset };
}

/** The raw (already-decompressed, not individually compressed) collision section bytes, or `null`. */
export function gcLevelCoreCollision(core: GcLevelCore): Uint8Array | null {
  const range = gcLevelCoreSectionRange(core, core.coreHeader.collision);
  if (!range) return null;
  return core.assets.subarray(range.offset, range.offset + range.size);
}

function enumerateSectionBoundaries(header: GcLevelCoreHeader, index: DataView, assetsLength: number): readonly number[] {
  const bounds = new Set<number>();
  const add = (value: number): void => {
    if (Number.isSafeInteger(value) && value > 0 && value <= assetsLength) bounds.add(value);
  };

  add(header.tfrags);
  add(header.occlusion);
  add(header.sky);
  add(header.collision);
  add(header.texturesBaseOffset);
  add(header.assetsDecompressedSize || assetsLength);
  add(assetsLength);

  const addClassOffsets = (table: ArrayRange, entrySize: number): void => {
    for (let i = 0; i < table.count; i++) {
      const at = table.offset + i * entrySize;
      if (at < 0 || at + 4 > index.byteLength) break;
      add(index.getInt32(at, true)); // offset_in_asset_wad is the first field of every *ClassEntry
    }
  };
  addClassOffsets(header.mobyClasses, 0x20);
  addClassOffsets(header.tieClasses, 0x20);
  addClassOffsets(header.shrubClasses, 0x30);

  add(header.mobySoundRemapOffset);
  if (header.ratchetSeqsOffset > 0) {
    for (let i = 0; i < 256; i++) {
      const at = header.ratchetSeqsOffset + i * 4;
      if (at + 4 > index.byteLength) break;
      add(index.getInt32(at, true));
    }
  }

  return [...bounds].sort((a, b) => a - b);
}

function assertRangeWithin(range: ByteRange, size: number, label: string, name: string): void {
  if (
    !Number.isSafeInteger(range.offset) || !Number.isSafeInteger(range.size) ||
    range.offset < 0 || range.size < 0 || range.offset > size || range.size > size - range.offset
  ) {
    throw new Error(`GC level data ${label} range ${range.offset}+${range.size} lies outside '${name}' (${size} bytes).`);
  }
}
