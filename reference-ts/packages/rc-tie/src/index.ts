export const RAC1_TIE_CLASS_HEADER_SIZE = 0x70;
export const GC_UYA_DL_TIE_CLASS_HEADER_SIZE = 0x80;
export const RC_TIE_CLASS_ENTRY_SIZE = 0x20;

export interface RcTieClassLayout {
  readonly name: string;
  readonly headerSize: number;
  readonly packetCountOffset: number;
  readonly scaleOffset: number;
}

/** R&C1 uses the older 0x70-byte class header but the same decoded packet grammar. */
export const RAC1_TIE_CLASS_LAYOUT: RcTieClassLayout = {
  name: "rac1",
  headerSize: RAC1_TIE_CLASS_HEADER_SIZE,
  packetCountOffset: 0x20,
  scaleOffset: 0x40,
};

/** Going Commando / UYA / Deadlocked class-header generation. */
export const GC_UYA_DL_TIE_CLASS_LAYOUT: RcTieClassLayout = {
  name: "gc-uya-dl",
  headerSize: GC_UYA_DL_TIE_CLASS_HEADER_SIZE,
  packetCountOffset: 0x0c,
  scaleOffset: 0x40,
};

export interface RcTieMesh {
  /** Flat class-local positions (native Z-up), 3 per vertex. */
  readonly positions: Float64Array;
  readonly uvs: Float32Array;
  readonly indices: Uint32Array;
  /** Class-local texture/material slot, one per recovered triangle. */
  readonly triangleMaterialSlots: Int32Array;
  readonly scale: number;
}

interface RcTieVertex {
  readonly x: number;
  readonly y: number;
  readonly z: number;
  readonly ofs: number;
  readonly s: number;
  readonly t: number;
}

interface RcTiePrimitive {
  readonly material: number;
  readonly winding: number;
  readonly verts: RcTieVertex[];
}

/**
 * Decode one RC tie class using an explicitly selected class-header generation.
 *
 * Retail R&C1 validation covers all 1,804 authority classes (25,385 packets,
 * 127,762 strips). The packet/event walk is shared with GC; the important
 * generation difference is the class header: R&C1 packet counts live at 0x20,
 * while GC/UYA/DL moved them to 0x0c. See research/RAC1_CLASS_FAMILIES.md.
 */
export function readRcTieClass(buf: Uint8Array, layout: RcTieClassLayout): RcTieMesh {
  if (buf.length < layout.headerSize) {
    throw new Error(`RC tie class (${layout.name}) buffer shorter than the 0x${layout.headerSize.toString(16)} header.`);
  }
  const view = new DataView(buf.buffer, buf.byteOffset, buf.byteLength);
  const tableBase = view.getInt32(0x00, true);
  const packetCount = buf[layout.packetCountOffset]!;
  const scale = view.getFloat32(layout.scaleOffset, true);
  if (!Number.isFinite(scale)) throw new Error(`RC tie class (${layout.name}) has non-finite scale.`);

  const primitives: RcTiePrimitive[] = [];
  if (tableBase > 0 && tableBase < buf.length) {
    for (let p = 0; p < packetCount; p++) {
      const phOff = tableBase + p * 0x10;
      if (phOff + 0x10 > buf.length) throw new Error(`RC tie class (${layout.name}) packet ${p} header is out of range.`);
      const packetData = tableBase + view.getInt32(phOff, true);
      const vertOfs = buf[phOff + 0x08]! * 0x10;
      const vertSize = buf[phOff + 0x09]! * 0x10;
      if (packetData <= 0 || packetData + 0x2c > buf.length) {
        throw new Error(`RC tie class (${layout.name}) packet ${p} data is out of range.`);
      }

      const adGifDest = [0, 1, 2, 3].map((i) => view.getInt32(packetData + i * 4, true));
      const adGifSrc = [0, 1, 2, 3].map((i) => view.getInt32(packetData + 0x10 + i * 4, true));
      const stripCount = buf[packetData + 0x23]!;
      const dinkyCount = Math.max(0, (buf[packetData + 0x28]! - 4) >> 1);

      const strips: { readonly gifTagOffset: number; readonly winding: number }[] = [];
      for (let s = 0; s < stripCount; s++) {
        const so = packetData + 0x2c + s * 4;
        if (so + 4 > buf.length) throw new Error(`RC tie class (${layout.name}) packet ${p} strip ${s} is out of range.`);
        strips.push({ gifTagOffset: buf[so + 2]!, winding: buf[so + 3]! });
      }

      const vbase = packetData + vertOfs;
      const raw: RcTieVertex[] = [];
      const push = (x: number, y: number, z: number, ofs: number, s: number, t: number, ofs2: number): void => {
        raw.push({ x, y, z, ofs, s, t });
        if (ofs2 !== 0 && ofs2 !== ofs) raw.push({ x, y, z, ofs: ofs2, s, t });
      };
      for (let v = 0; v < dinkyCount; v++) {
        const o = vbase + v * 0x10;
        if (o + 0x10 > buf.length) throw new Error(`RC tie class (${layout.name}) packet ${p} dinky vertex ${v} is out of range.`);
        push(
          view.getInt16(o, true), view.getInt16(o + 2, true), view.getInt16(o + 4, true),
          view.getUint16(o + 6, true), view.getUint16(o + 8, true), view.getUint16(o + 10, true), view.getUint16(o + 14, true),
        );
      }
      const vend = vbase + vertSize;
      if (vend > buf.length) throw new Error(`RC tie class (${layout.name}) packet ${p} vertex range is out of range.`);
      for (let fo = vbase + dinkyCount * 0x10; fo + 0x18 <= vend; fo += 0x18) {
        push(
          view.getInt16(fo + 8, true), view.getInt16(fo + 10, true), view.getInt16(fo + 12, true),
          view.getUint16(fo + 6, true), view.getUint16(fo + 16, true), view.getUint16(fo + 18, true), view.getUint16(fo + 22, true),
        );
      }

      raw.sort((a, b) => a.ofs - b.ofs);
      const verts: RcTieVertex[] = raw.length ? [raw[0]!] : [];
      for (let i = 1; i < raw.length; i++) if (raw[i]!.ofs !== raw[i - 1]!.ofs) verts.push(raw[i]!);

      let nextStrip = 0;
      let nextVertex = 0;
      let nextAdGif = 1;
      let nextOffset = 6;
      let material = Math.max(0, Math.floor(adGifSrc[0]! / 0x50));
      let prim: RcTiePrimitive | null = null;
      let guard = 0;
      while ((nextStrip < strips.length || nextVertex < verts.length) && guard++ < 200_000) {
        if (nextStrip < strips.length && strips[nextStrip]!.gifTagOffset === nextOffset) {
          prim = { material, winding: strips[nextStrip]!.winding !== 0 ? 1 : 0, verts: [] };
          primitives.push(prim);
          nextStrip++;
          nextOffset += 1;
        } else if (nextVertex < verts.length && verts[nextVertex]!.ofs === nextOffset) {
          if (prim) prim.verts.push(verts[nextVertex]!);
          nextVertex++;
          nextOffset += 3;
        } else if (nextAdGif < adGifSrc.length && adGifDest[nextAdGif - 1] === nextOffset) {
          material = Math.max(0, Math.floor(adGifSrc[nextAdGif]! / 0x50));
          nextAdGif++;
          nextOffset += 6;
        } else {
          throw new Error(`RC tie class (${layout.name}) packet ${p} event walk stalled at GS offset ${nextOffset}.`);
        }
      }
      if (guard >= 200_000) throw new Error(`RC tie class (${layout.name}) packet ${p} event walk exceeded guard.`);
    }
  }

  const positions: number[] = [];
  const uvs: number[] = [];
  const indices: number[] = [];
  const triangleMaterialSlots: number[] = [];
  const k = scale / 1024;
  for (const prim of primitives) {
    if (prim.verts.length < 3) continue;
    const base = positions.length / 3;
    for (const v of prim.verts) {
      positions.push(v.x * k, v.y * k, v.z * k);
      uvs.push(v.s / 4096, v.t / 4096);
    }
    for (let i = 2; i < prim.verts.length; i++) {
      const a = base + i - 2;
      const b = base + i - 1;
      const c = base + i;
      if ((i % 2) === prim.winding) indices.push(a, b, c);
      else indices.push(b, a, c);
      triangleMaterialSlots.push(prim.material);
    }
  }

  return {
    positions: Float64Array.from(positions),
    uvs: Float32Array.from(uvs),
    indices: Uint32Array.from(indices),
    triangleMaterialSlots: Int32Array.from(triangleMaterialSlots),
    scale,
  };
}
