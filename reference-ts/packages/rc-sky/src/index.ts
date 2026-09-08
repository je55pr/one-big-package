import {
  SKY_CLUSTER_HEADER_SIZE,
  SKY_HEADER_SIZE,
  SKY_TEXTURE_ENTRY_SIZE,
  readGcSky,
} from "../../gc-sky/src/index.js";
import type { GcSky, GcSkyShell, GcSkyTexture } from "../../gc-sky/src/index.js";

/**
 * Cross-game sky codec for the PS2 Ratchet & Clank engine line.
 *
 * This parser originally lived under `gc-sky`. R&C1 retail archaeology has now
 * independently validated the same 0x40 header, RAC/GC shell header, 0x20
 * cluster records, vertex/ST/face layouts and 0x10 paletted texture definitions
 * across all 19 authority levels. `rc-sky` is therefore the neutral contract;
 * `gc-sky` remains the implementation home to avoid unrelated GC churn.
 */
export const RC_SKY_HEADER_SIZE = SKY_HEADER_SIZE;
export const RC_SKY_CLUSTER_HEADER_SIZE = SKY_CLUSTER_HEADER_SIZE;
export const RC_SKY_TEXTURE_ENTRY_SIZE = SKY_TEXTURE_ENTRY_SIZE;

export type RcSky = GcSky;
export type RcSkyShell = GcSkyShell;
export type RcSkyTexture = GcSkyTexture;

export function readRcSky(bytes: Uint8Array): RcSky {
  return readGcSky(bytes);
}
