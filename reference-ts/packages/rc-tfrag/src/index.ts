import {
  TFRAG_HEADER_SIZE,
  readGcTfrags,
  toObpTfragMeshes,
} from "../../gc-tfrag/src/index.js";
import type {
  GcTfragTexture,
  GcTfragMesh,
  GcTfragOptions,
} from "../../gc-tfrag/src/index.js";
import type { OBPMesh, OBPSourceRef } from "../../core/src/index.js";

/**
 * Cross-game static-terrain tfrag codec for the PS2 Ratchet & Clank engine line.
 *
 * The implementation originally entered OBP under the Going Commando-specific
 * `gc-tfrag` package. Retail R&C1 archaeology has now independently validated
 * the same 0x40 table/header layout, VIF command-list grammar, position/UV
 * scaling and strip recovery across all 19 authority levels (20,016 tfrags),
 * with recovered triangle totals matching every native per-fragment declaration.
 *
 * This neutral facade is therefore the shared contract. The legacy GC package
 * remains the implementation home for now so this archaeology checkpoint does
 * not churn the already-verified GC importer; callers should prefer the `Rc*`
 * names when code is intentionally cross-game.
 */
export const RC_TFRAG_HEADER_SIZE = TFRAG_HEADER_SIZE;

export type RcTfragTexture = GcTfragTexture;
export type RcTfragMesh = GcTfragMesh;
export type RcTfragOptions = GcTfragOptions;

export function readRcTfrags(blob: Uint8Array, options: RcTfragOptions = {}): RcTfragMesh {
  return readGcTfrags(blob, options);
}

export function toObpRcTfragMeshes(
  mesh: RcTfragMesh,
  source: OBPSourceRef,
  meshIdPrefix: string,
  materialIdPrefix: string = meshIdPrefix,
): OBPMesh[] {
  return toObpTfragMeshes(mesh, source, meshIdPrefix, materialIdPrefix);
}
