import type { GcLevelCore } from "../../gc-level-core/src/index.js";
import {
  RC_LEVEL_TEXTURE_ENTRY_SIZE,
  readRcLevelTextureTable,
} from "../../rc-level-textures/src/index.js";
import type { RcLevelTexture, RcLevelTextureOptions } from "../../rc-level-textures/src/index.js";

/** Legacy Going Commando name retained for API compatibility. */
export const TEXTURE_ENTRY_SIZE = RC_LEVEL_TEXTURE_ENTRY_SIZE;

export type GcTextureTable = "tfrag" | "moby" | "tie" | "shrub" | "part" | "fx";
export type GcLevelTexture = RcLevelTexture;
export type GcLevelTextureOptions = RcLevelTextureOptions;

/**
 * Going Commando wrapper over the now retail-validated shared RC texture-table
 * decoder. See `packages/rc-level-textures` and `research/RAC1_TEXTURES.md`.
 */
export function readGcLevelTextures(
  core: GcLevelCore,
  table: GcTextureTable = "tfrag",
  options: GcLevelTextureOptions = {},
): GcLevelTexture[] {
  const range = {
    tfrag: core.coreHeader.tfragTextures,
    moby: core.coreHeader.mobyTextures,
    tie: core.coreHeader.tieTextures,
    shrub: core.coreHeader.shrubTextures,
    part: core.coreHeader.partTextures,
    fx: core.coreHeader.fxTextures,
  }[table];

  return readRcLevelTextureTable(
    {
      index: core.index,
      assets: core.assets,
      gsRam: core.gsRam,
      texturesBaseOffset: core.coreHeader.texturesBaseOffset,
    },
    range,
    options,
  );
}
