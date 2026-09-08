import {
  SHRUB_CLASS_ENTRY_SIZE,
  SHRUB_CLASS_HEADER_SIZE,
  readGcShrubClass,
} from "../../gc-shrub/src/index.js";
import type { GcShrubMesh } from "../../gc-shrub/src/index.js";

/**
 * Cross-game shrub class facade.
 *
 * R&C1 NTSC-U original validates the same class header and VIF packet grammar on
 * all 551 authority classes (5,374 packets). GC remains the implementation home
 * for now to avoid unnecessary churn; callers that intentionally span games
 * should use these `Rc*` names.
 */
export const RC_SHRUB_CLASS_HEADER_SIZE = SHRUB_CLASS_HEADER_SIZE;
export const RC_SHRUB_CLASS_ENTRY_SIZE = SHRUB_CLASS_ENTRY_SIZE;
export type RcShrubMesh = GcShrubMesh;

export function readRcShrubClass(buf: Uint8Array): RcShrubMesh {
  return readGcShrubClass(buf);
}
