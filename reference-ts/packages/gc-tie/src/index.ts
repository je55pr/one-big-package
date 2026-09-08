import type { GcLevelCore } from "../../gc-level-core/src/index.js";
import {
  GC_UYA_DL_TIE_CLASS_HEADER_SIZE,
  GC_UYA_DL_TIE_CLASS_LAYOUT,
  RC_TIE_CLASS_ENTRY_SIZE,
  readRcTieClass,
} from "../../rc-tie/src/index.js";
import type { RcTieMesh } from "../../rc-tie/src/index.js";

/**
 * Going Commando compatibility facade for the shared RC tie packet codec.
 * R&C1 retail archaeology proved that the packet/event grammar is shared while
 * the class header differs; new cross-game code should use `rc-tie` directly.
 */
export const TIE_CLASS_HEADER_SIZE = GC_UYA_DL_TIE_CLASS_HEADER_SIZE;
export const TIE_CLASS_ENTRY_SIZE = RC_TIE_CLASS_ENTRY_SIZE;
export type GcTieMesh = RcTieMesh;

export function readGcTieClass(buf: Uint8Array): GcTieMesh {
  return readRcTieClass(buf, GC_UYA_DL_TIE_CLASS_LAYOUT);
}

export interface GcTieClass {
  readonly oClass: number;
  readonly assetOffset: number;
  readonly mesh: GcTieMesh;
  readonly triangleTextureIds: Int32Array;
  readonly textureIds: readonly number[];
}

/** Parse every GC tie class and map class-local material slots to level texture ids. */
export function readGcTieClasses(core: GcLevelCore): Map<number, GcTieClass> {
  const table = core.coreHeader.tieClasses;
  const index = new DataView(core.index.buffer, core.index.byteOffset, core.index.byteLength);
  const boundaries = [...core.sectionBoundaries].sort((a, b) => a - b);
  const out = new Map<number, GcTieClass>();

  for (let i = 0; i < table.count; i++) {
    const at = table.offset + i * TIE_CLASS_ENTRY_SIZE;
    if (at < 0 || at + TIE_CLASS_ENTRY_SIZE > core.index.length) break;
    const assetOffset = index.getInt32(at, true);
    const oClass = index.getInt32(at + 4, true);
    if (assetOffset <= 0 || assetOffset >= core.assets.length) continue;

    const textures: number[] = [];
    for (let k = 0; k < 16; k++) textures.push(core.index[at + 0x10 + k]!);

    const end = boundaries.find((b) => b > assetOffset) ?? core.assets.length;
    try {
      const mesh = readGcTieClass(core.assets.subarray(assetOffset, end));
      const triangleTextureIds = Int32Array.from(mesh.triangleMaterialSlots, (slot) =>
        slot >= 0 && slot < 16 ? textures[slot]! : -1,
      );
      out.set(oClass, {
        oClass,
        assetOffset,
        mesh,
        triangleTextureIds,
        textureIds: [...new Set(triangleTextureIds)].filter((id) => id >= 0).sort((a, b) => a - b),
      });
    } catch {
      /* preserve legacy GC behaviour: skip a class that fails to parse */
    }
  }
  return out;
}
