import type { RandomAccessReader } from "../../importer-common/src/index.js";

/**
 * Decompressor for the "WAD" LZ container used by the PS2 Ratchet & Clank games
 * for level chunk data, level-core asset blocks, and similar.
 *
 * Container header (16 bytes):
 *
 * ```text
 * 0x00  'W' 'A' 'D'        magic
 * 0x03  s32  compressedSize   (little-endian, at the unaligned offset 3; counts from 0x00)
 * 0x07  u8[9]               name / padding (informational; retail values vary, sometimes garbage)
 * 0x10  compressed stream   ... up to 0x00 + compressedSize
 * ```
 *
 * The stream is a byte-oriented LZ77 with a leading flag byte per packet:
 *
 * - `flag < 0x10`  literal run. `flag != 0` → `flag + 3` bytes; `flag == 0` →
 *   `next + 18` bytes. A literal packet may not be followed by another literal.
 * - `flag 0x10..0x1f` far match. `size = flag & 7` (`0` → `next + 7`); read `b0`,
 *   `b1`; `lookback = out - ((flag & 8) << 11) - (b1 << 6) - (b0 >> 2)`. If
 *   `lookback != out`: `size += 2`, `lookback -= 0x4000`. Else if `size != 1`:
 *   skip stream bytes to the next 0x1000 boundary and end the packet.
 * - `flag 0x20..0x3f` medium/big match. `size = flag & 0x1f` (`0` → `next + 0x1f`)
 *   `+ 2`; read `b1`, `b2`; `lookback = out - (b2 << 6) - (b1 >> 2) - 1`.
 * - `flag 0x40..0xff` little match. read `b1`; `lookback = out - (b1 << 3) -
 *   ((flag >> 2) & 7) - 1`; `size = (flag >> 5) + 1`.
 *
 * After every match, `(secondToLastByteRead & 3)` trailing literal bytes are
 * copied inline. Match copies are byte-by-byte so overlapping runs expand.
 *
 * Implemented from the format description; not derived from any external
 * implementation's code. Verified against retail Going Commando chunk data
 * (see research/GC_COLLISION.md).
 */

export const WAD_LZ_HEADER_SIZE = 0x10;
const DEFAULT_MAX_OUTPUT_BYTES = 128 * 1024 * 1024;

export interface WadLzHeader {
  readonly compressedSize: number;
  /** Bytes 0x07..0x0f as latin1, trailing NULs trimmed. Informational only. */
  readonly name: string;
}

export interface WadLzResult extends WadLzHeader {
  readonly data: Uint8Array;
}

export interface WadLzOptions {
  /** Hard cap on decompressed size. Default 128 MiB. */
  readonly maxOutputBytes?: number;
}

/** True when `bytes` begins with a structurally plausible WAD LZ header. */
export function isWadLz(bytes: Uint8Array): boolean {
  if (bytes.length < WAD_LZ_HEADER_SIZE) return false;
  if (bytes[0] !== 0x57 || bytes[1] !== 0x41 || bytes[2] !== 0x44) return false;
  const compressedSize = readInt32LEUnaligned(bytes, 3);
  return compressedSize >= WAD_LZ_HEADER_SIZE && compressedSize <= bytes.length;
}

export function readWadLzHeader(bytes: Uint8Array): WadLzHeader {
  if (bytes.length < WAD_LZ_HEADER_SIZE) throw new Error("WAD LZ source shorter than the 16-byte header.");
  if (bytes[0] !== 0x57 || bytes[1] !== 0x41 || bytes[2] !== 0x44) throw new Error("WAD LZ magic 'WAD' not found.");
  const compressedSize = readInt32LEUnaligned(bytes, 3);
  if (compressedSize < WAD_LZ_HEADER_SIZE) throw new Error(`WAD LZ compressed size ${compressedSize} is smaller than the header.`);
  let name = "";
  for (let i = 7; i < 16; i++) {
    const byte = bytes[i] ?? 0;
    if (byte === 0) break;
    name += String.fromCharCode(byte);
  }
  return { compressedSize, name };
}

/** Decompress a complete in-memory WAD LZ block. */
export function decompressWad(bytes: Uint8Array, options: WadLzOptions = {}): WadLzResult {
  const header = readWadLzHeader(bytes);
  const maxOutputBytes = options.maxOutputBytes ?? DEFAULT_MAX_OUTPUT_BYTES;
  if (!Number.isSafeInteger(maxOutputBytes) || maxOutputBytes <= 0) throw new RangeError(`Invalid maxOutputBytes: ${maxOutputBytes}`);
  if (header.compressedSize > bytes.length) throw new Error(`WAD LZ compressed size ${header.compressedSize} exceeds the ${bytes.length}-byte source.`);

  const end = header.compressedSize;
  let pos = WAD_LZ_HEADER_SIZE;

  let out = new Uint8Array(Math.min(maxOutputBytes, Math.max(0x1000, header.compressedSize * 4)));
  let outLen = 0;

  const ensure = (extra: number): void => {
    if (outLen + extra > maxOutputBytes) throw new Error(`WAD LZ output exceeds cap ${maxOutputBytes}.`);
    if (outLen + extra <= out.length) return;
    let next = out.length;
    while (next < outLen + extra) next = Math.min(maxOutputBytes, next * 2);
    const grown = new Uint8Array(next);
    grown.set(out.subarray(0, outLen));
    out = grown;
  };

  const read8 = (): number => {
    if (pos >= end) throw new Error("WAD LZ: unexpected end of stream.");
    return bytes[pos++]!;
  };

  const copyLiteral = (count: number): void => {
    if (count === 0) return;
    if (pos + count > end) throw new Error("WAD LZ: literal run runs past the stream.");
    ensure(count);
    out.set(bytes.subarray(pos, pos + count), outLen);
    outLen += count;
    pos += count;
  };

  let sawLiteralPacket = false;

  while (pos < end) {
    const flag = read8();

    if (flag < 0x10) {
      if (sawLiteralPacket) throw new Error("WAD LZ: two literal packets in a row.");
      const size = flag !== 0 ? flag + 3 : read8() + 18;
      copyLiteral(size);
      sawLiteralPacket = true;
      continue;
    }
    sawLiteralPacket = false;

    let matchSize: number;
    let lookback: number;
    let ended = false;

    if (flag < 0x20) {
      matchSize = flag & 7;
      if (matchSize === 0) matchSize = read8() + 7;
      const b0 = read8();
      const b1 = read8();
      lookback = outLen - ((flag & 8) << 11) - (b1 << 6) - (b0 >> 2);
      if (lookback !== outLen) {
        matchSize += 2;
        lookback -= 0x4000;
      } else if (matchSize !== 1) {
        // Padding sync: skip to the next 0x1000 boundary within the stream body.
        const body = pos - WAD_LZ_HEADER_SIZE;
        const aligned = (body + 0xfff) & ~0xfff;
        pos = WAD_LZ_HEADER_SIZE + aligned;
        ended = true;
      }
    } else if (flag < 0x40) {
      matchSize = flag & 0x1f;
      if (matchSize === 0) matchSize = read8() + 0x1f;
      matchSize += 2;
      const b1 = read8();
      const b2 = read8();
      lookback = outLen - (b2 << 6) - (b1 >> 2) - 1;
    } else {
      const b1 = read8();
      lookback = outLen - (b1 << 3) - ((flag >> 2) & 7) - 1;
      matchSize = (flag >> 5) + 1;
    }

    if (ended) continue;

    if (matchSize !== 1) {
      if (lookback < 0 || lookback >= outLen) throw new Error("WAD LZ: match points outside the output buffer.");
      ensure(matchSize);
      for (let i = 0; i < matchSize; i++) out[outLen + i] = out[lookback + i]!;
      outLen += matchSize;
    }

    // Trailing inline literals: low 2 bits of the packet's second-to-last byte.
    copyLiteral((bytes[pos - 2]! & 3));
  }

  return { data: out.subarray(0, outLen), compressedSize: header.compressedSize, name: header.name };
}

/**
 * Read a WAD LZ block from `reader` starting at `offset` and decompress it.
 * Only the block's own bytes are read.
 */
export async function readWadLz(
  reader: RandomAccessReader,
  offset: number,
  options: WadLzOptions = {},
): Promise<WadLzResult> {
  if (!Number.isSafeInteger(offset) || offset < 0 || offset + WAD_LZ_HEADER_SIZE > reader.size) {
    throw new RangeError(`WAD LZ offset ${offset} lies outside '${reader.name}'.`);
  }
  const headerBytes = await reader.read(offset, WAD_LZ_HEADER_SIZE);
  const header = readWadLzHeader(headerBytes);
  if (offset + header.compressedSize > reader.size) {
    throw new Error(`WAD LZ block at ${offset} (compressed size ${header.compressedSize}) runs past '${reader.name}'.`);
  }
  const block = await reader.read(offset, header.compressedSize);
  return decompressWad(block, options);
}

function readInt32LEUnaligned(bytes: Uint8Array, offset: number): number {
  return (
    (bytes[offset]! | (bytes[offset + 1]! << 8) | (bytes[offset + 2]! << 16) | (bytes[offset + 3]! << 24))
  ) | 0;
}
