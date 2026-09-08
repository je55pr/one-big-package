/**
 * Minimal PS2 VIF1 command-list reader.
 *
 * The Ratchet & Clank geometry formats (tfrags, moby / tie / shrub class models)
 * store their vertex data as VIF DMA packet streams: a sequence of VIF codes,
 * each optionally followed by inline data, that in hardware fill VU1 memory.
 * For asset recovery we only need to walk the stream and pull out the `UNPACK`
 * packets (which carry the arrays) and the `STROW` register writes.
 *
 * VIF code (u32, little-endian):
 * ```text
 *   bit 31      interrupt
 *   bits 24..30 cmd (7 bits)
 *   bits 16..23 num  (0 means 256)
 *   ...         command-specific
 * ```
 * `cmd & 0x60 == 0x60` marks an UNPACK; then `cmd & 0x0f` is the VN/VL code,
 * bit 15 = flg, bit 14 = usn, bits 0..9 = VU target address.
 *
 * Implemented from the VIF1 spec, not translated from any external code.
 */

export const VIF_CMD_NOP = 0x00;
export const VIF_CMD_STCYCL = 0x01;
export const VIF_CMD_STMOD = 0x05;
export const VIF_CMD_STMASK = 0x20;
export const VIF_CMD_STROW = 0x30;
export const VIF_CMD_STCOL = 0x31;
export const VIF_CMD_MPG = 0x4a;
export const VIF_CMD_DIRECT = 0x50;
export const VIF_CMD_DIRECTHL = 0x51;

/** VN/VL codes (the low nibble of an UNPACK cmd). */
export const VIF_UNPACK = {
  S_32: 0x0, S_16: 0x1, S_8: 0x2,
  V2_32: 0x4, V2_16: 0x5, V2_8: 0x6,
  V3_32: 0x8, V3_16: 0x9, V3_8: 0xa,
  V4_32: 0xc, V4_16: 0xd, V4_8: 0xe, V4_5: 0xf,
} as const;

export interface VifCode {
  readonly raw: number;
  readonly interrupt: boolean;
  readonly cmd: number;
  readonly num: number;
  readonly isUnpack: boolean;
  /** UNPACK only: VN/VL code (see {@link VIF_UNPACK}). */
  readonly vnvl: number;
  /** UNPACK only: `true` when the data is unsigned. */
  readonly unsigned: boolean;
  /** UNPACK only: VU target address (qwords). */
  readonly addr: number;
}

export interface VifPacket {
  /** Byte offset of the VIF code within the command list. */
  readonly offset: number;
  readonly code: VifCode;
  /** The packet's inline data (empty for register-only commands). */
  readonly data: Uint8Array;
}

/** Bytes per element for an UNPACK VN/VL code. */
export function vifUnpackElementSize(vnvl: number): number {
  const vn = (vnvl & 0b1100) >> 2; // 0..3  -> 1..4 components
  const vl = vnvl & 0b11; // 0..3  -> 32/16/8/(5) bits
  if (vl === 3) {
    // V4_5: a single 16-bit packed RGBA5551 per element.
    return vn === 3 ? 2 : Math.ceil(((32 >> vl) * (vn + 1)) / 8);
  }
  return ((32 >> vl) * (vn + 1)) / 8;
}

export function decodeVifCode(raw: number): VifCode {
  const cmd = (raw >>> 24) & 0x7f;
  let num = (raw >>> 16) & 0xff;
  if (num === 0) num = 256;
  const isUnpack = (cmd & 0x60) === 0x60;
  return {
    raw: raw >>> 0,
    interrupt: (raw >>> 31) !== 0,
    cmd,
    num,
    isUnpack,
    vnvl: isUnpack ? cmd & 0x0f : 0,
    unsigned: isUnpack ? ((raw >>> 14) & 1) !== 0 : false,
    addr: isUnpack ? raw & 0x3ff : 0,
  };
}

/** Total byte size of a VIF packet (code word + inline data), matching PS2 timing rules. */
export function vifPacketSize(code: VifCode): number {
  switch (code.cmd) {
    case VIF_CMD_STMASK:
      return 2 * 4;
    case VIF_CMD_STROW:
    case VIF_CMD_STCOL:
      return 5 * 4;
    case VIF_CMD_MPG:
      return (1 + code.num * 2) * 4;
    case VIF_CMD_DIRECT:
    case VIF_CMD_DIRECTHL: {
      const size = (code.raw & 0xffff) || 0x10000;
      return (1 + size * 4) * 4;
    }
    default:
      if (code.isUnpack) {
        let size = code.num * vifUnpackElementSize(code.vnvl);
        if (size % 4 !== 0) size += 4 - (size % 4);
        return (1 + size / 4) * 4;
      }
      return 1 * 4; // NOP, STCYCL, OFFSET, BASE, ITOP, STMOD, MARK, FLUSH*, MSCAL*, MSCNT
  }
}

export interface VifReadOptions {
  /** Safety cap on the number of packets parsed. */
  readonly maxPackets?: number;
}

/** Walk a VIF command list. Stops cleanly at a truncated / malformed tail. */
export function readVifCommandList(bytes: Uint8Array, options: VifReadOptions = {}): VifPacket[] {
  const maxPackets = options.maxPackets ?? 100_000;
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  const packets: VifPacket[] = [];
  let offset = 0;
  while (offset + 4 <= bytes.length && packets.length < maxPackets) {
    const code = decodeVifCode(view.getUint32(offset, true));
    const size = vifPacketSize(code);
    if (size <= 0 || size > 0x10000 || offset + size > bytes.length) break;
    packets.push({ offset, code, data: bytes.subarray(offset + 4, offset + size) });
    offset += size;
  }
  return packets;
}

/** The UNPACK packets of a command list, in order. */
export function filterVifUnpacks(packets: readonly VifPacket[]): VifPacket[] {
  return packets.filter((packet) => packet.code.isUnpack);
}

/** Read an UNPACK packet's data as `count`-tuples of signed 16-bit ints (V*_16). */
export function readUnpackS16(packet: VifPacket, components: number): Int16Array {
  const view = new DataView(packet.data.buffer, packet.data.byteOffset, packet.data.byteLength);
  const total = packet.code.num * components;
  const out = new Int16Array(total);
  for (let i = 0; i < total && (i + 1) * 2 <= packet.data.length; i++) out[i] = view.getInt16(i * 2, true);
  return out;
}

/** Read an UNPACK packet's data as raw unsigned bytes (V*_8). */
export function readUnpackU8(packet: VifPacket): Uint8Array {
  return packet.data.slice();
}

/** Read an UNPACK packet's data as `count`-tuples of unsigned 32-bit ints (V*_32). */
export function readUnpackU32(packet: VifPacket, components: number): Uint32Array {
  const view = new DataView(packet.data.buffer, packet.data.byteOffset, packet.data.byteLength);
  const total = packet.code.num * components;
  const out = new Uint32Array(total);
  for (let i = 0; i < total && (i + 1) * 4 <= packet.data.length; i++) out[i] = view.getUint32(i * 4, true);
  return out;
}
