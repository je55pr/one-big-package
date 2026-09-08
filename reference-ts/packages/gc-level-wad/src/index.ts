import { SubRangeReader } from "../../importer-common/src/index.js";
import type { RandomAccessReader } from "../../importer-common/src/index.js";

/**
 * Outer container header of a Going Commando `/G/LEVEL<n>.WAD` file.
 *
 * Confirmed directly against the retail NTSC-U v1.01 build (`SCUS-97268`,
 * payload SHA-256 `9db2e33e…a9b1ce5`) across all 27 `LEVEL0.WAD`..`LEVEL26.WAD`
 * files. See `research/GC_LEVEL_WAD.md` for the full byte evidence.
 *
 * Layout (all fields u32 little-endian):
 *
 * ```text
 * 0x00  headerSize     always 0x60 on this build
 * 0x04  unknown0x04     always 0 observed
 * 0x08  levelId         native level id self-reported by the WAD
 * 0x0c  unknown0x0c     observed {0, 3, 4}; role not established
 * 0x10  lump[0..9]      10 positional { u32 offsetSectors; u32 sizeSectors }
 * ```
 *
 * Each present lump's byte range is `offsetSectors * 2048 .. + sizeSectors * 2048`.
 * On the retail build the present lumps tile the file contiguously from sector 1
 * (sector 0 holds this header) and the final lump's sector-rounded end overruns
 * the real file size by less than one sector. Neither property is assumed here:
 * this parser preserves the raw fields and only rejects genuinely impossible
 * geometry. Lump *slot indices are treated as field identities*, not disc order —
 * on disc, slot 1 precedes slot 0.
 *
 * This parser is deliberately Going-Commando-scoped. Wrench models Going Commando
 * and UYA level WADs through one `GcUyaLevelWadHeader` / `unpack_gc_uya_level_wad`
 * path and also describes a `headerSize == 0x68` Going Commando branch with
 * separate NTSC/PAL gameplay ranges; this retail NTSC-U v1.01 build is `0x60` and
 * neither UYA nor the `0x68` variant is verified from our discs yet.
 */

export const GC_LEVEL_WAD_HEADER_SIZE = 0x60;
export const GC_LEVEL_WAD_LUMP_COUNT = 10;
export const GC_LEVEL_WAD_SECTOR_BYTES = 2048;

export interface GcLevelWadLump {
  /** 0..9. Positional identity of the lump within the header table. */
  readonly slot: number;
  readonly offsetSectors: number;
  readonly sizeSectors: number;
  readonly offsetBytes: number;
  /** `sizeSectors * 2048`. May exceed the remaining source bytes for the tail lump. */
  readonly sizeBytes: number;
  /** `sizeBytes` clamped to the bytes actually available in the source. */
  readonly availableBytes: number;
  /** True when either raw field is non-zero. Absent lumps are `{0, 0}`. */
  readonly present: boolean;
}

export interface GcLevelWadHeader {
  readonly headerSize: number;
  readonly unknown0x04: number;
  readonly levelId: number;
  readonly unknown0x0c: number;
  /** Always length {@link GC_LEVEL_WAD_LUMP_COUNT}; indexed by slot. */
  readonly lumps: readonly GcLevelWadLump[];
  readonly sourceSize: number;
}

export interface GcLevelWadTiling {
  /** Present lumps, sorted by on-disc sector offset, cover [1, end) with no gaps or overlaps. */
  readonly contiguousFromSectorOne: boolean;
  /** Sector-rounded end of the last present lump, minus the real source size. */
  readonly tailOverrunBytes: number;
  readonly gaps: readonly { readonly afterSlot: number; readonly gapSectors: number }[];
  readonly overlaps: readonly { readonly slotA: number; readonly slotB: number }[];
}

/** Read and validate the fixed 0x60-byte outer header through bounded reads only. */
export async function readGcLevelWadHeader(reader: RandomAccessReader): Promise<GcLevelWadHeader> {
  if (reader.size < GC_LEVEL_WAD_HEADER_SIZE) {
    throw new Error(`GC level WAD '${reader.name}' is smaller than the ${GC_LEVEL_WAD_HEADER_SIZE}-byte header.`);
  }

  const bytes = await reader.read(0, GC_LEVEL_WAD_HEADER_SIZE);
  if (bytes.length !== GC_LEVEL_WAD_HEADER_SIZE) throw new Error(`Short GC level WAD header read from '${reader.name}'.`);
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);

  const headerSize = view.getUint32(0x00, true);
  if (headerSize !== GC_LEVEL_WAD_HEADER_SIZE) {
    throw new Error(`GC level WAD '${reader.name}' header size 0x${headerSize.toString(16)} is not 0x${GC_LEVEL_WAD_HEADER_SIZE.toString(16)}.`);
  }

  const lumps: GcLevelWadLump[] = [];
  for (let slot = 0; slot < GC_LEVEL_WAD_LUMP_COUNT; slot++) {
    const base = 0x10 + slot * 8;
    const offsetSectors = view.getUint32(base, true);
    const sizeSectors = view.getUint32(base + 4, true);
    const present = offsetSectors !== 0 || sizeSectors !== 0;

    const offsetBytes = offsetSectors * GC_LEVEL_WAD_SECTOR_BYTES;
    const sizeBytes = sizeSectors * GC_LEVEL_WAD_SECTOR_BYTES;
    if (!Number.isSafeInteger(offsetBytes) || !Number.isSafeInteger(sizeBytes)) {
      throw new RangeError(`GC level WAD '${reader.name}' lump ${slot} sector fields overflow.`);
    }

    if (present) {
      if (offsetSectors === 0) {
        throw new Error(`GC level WAD '${reader.name}' lump ${slot} has size but no offset.`);
      }
      if (offsetBytes < GC_LEVEL_WAD_HEADER_SIZE) {
        throw new Error(`GC level WAD '${reader.name}' lump ${slot} offset ${offsetBytes} overlaps the header.`);
      }
      if (offsetBytes > reader.size) {
        throw new Error(`GC level WAD '${reader.name}' lump ${slot} offset ${offsetBytes} lies past the ${reader.size}-byte source.`);
      }
    }

    const availableBytes = present ? Math.min(sizeBytes, reader.size - offsetBytes) : 0;
    lumps.push({ slot, offsetSectors, sizeSectors, offsetBytes, sizeBytes, availableBytes, present });
  }

  return {
    headerSize,
    unknown0x04: view.getUint32(0x04, true),
    levelId: view.getUint32(0x08, true),
    unknown0x0c: view.getUint32(0x0c, true),
    lumps,
    sourceSize: reader.size,
  };
}

/**
 * Expose one lump as its own range-readable source. Returns `undefined` for an
 * absent lump. The view is clamped to the bytes actually present in the source so
 * a sector-rounded tail lump never reads past the file.
 */
export function openGcLevelWadLump(
  reader: RandomAccessReader,
  header: GcLevelWadHeader,
  slot: number,
): RandomAccessReader | undefined {
  if (!Number.isInteger(slot) || slot < 0 || slot >= GC_LEVEL_WAD_LUMP_COUNT) {
    throw new RangeError(`GC level WAD lump slot ${slot} is out of range 0..${GC_LEVEL_WAD_LUMP_COUNT - 1}.`);
  }
  const lump = header.lumps[slot];
  if (!lump || !lump.present) return undefined;
  return new SubRangeReader(reader, lump.offsetBytes, lump.availableBytes, `${reader.name}#lump${slot}`);
}

/**
 * Research helper: report whether the present lumps tile the file as observed on
 * the retail build. This is descriptive only — {@link readGcLevelWadHeader} does
 * not require it.
 */
export function analyzeGcLevelWadTiling(header: GcLevelWadHeader): GcLevelWadTiling {
  const present = header.lumps.filter((lump) => lump.present).slice().sort((a, b) => a.offsetSectors - b.offsetSectors);
  const gaps: { afterSlot: number; gapSectors: number }[] = [];
  const overlaps: { slotA: number; slotB: number }[] = [];

  let contiguousFromSectorOne = present.length > 0 && present[0]!.offsetSectors === 1;
  for (let i = 1; i < present.length; i++) {
    const previous = present[i - 1]!;
    const current = present[i]!;
    const previousEnd = previous.offsetSectors + previous.sizeSectors;
    if (current.offsetSectors > previousEnd) {
      gaps.push({ afterSlot: previous.slot, gapSectors: current.offsetSectors - previousEnd });
      contiguousFromSectorOne = false;
    } else if (current.offsetSectors < previousEnd) {
      overlaps.push({ slotA: previous.slot, slotB: current.slot });
      contiguousFromSectorOne = false;
    }
  }

  const last = present[present.length - 1];
  const tailOverrunBytes = last ? (last.offsetSectors + last.sizeSectors) * GC_LEVEL_WAD_SECTOR_BYTES - header.sourceSize : 0;
  return { contiguousFromSectorOne, tailOverrunBytes, gaps, overlaps };
}
