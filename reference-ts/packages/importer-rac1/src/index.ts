import type { OBPImporter } from "../../importer-common/src/index.js";
import { PRIMARY_DISC_SOURCE_KEY, probePs2PrimaryAuthority } from "../../importer-ps2-common/src/index.js";
import { importRac1WorldSlice } from "../../rac1-world/src/index.js";

export const RAC1_PRIMARY_AUTHORITY = {
  game: "rac1",
  buildId: "rac1-ntscu-original",
  serial: "SCUS-97199",
  region: "NTSC-U",
  revision: "original retail",
  sha256: "ab849fe7cc9cc81c487d61b0d3ea15b5849943481b6a6ebf4d9aa9cf7bc40d9d",
} as const;

export const rac1Importer: OBPImporter = {
  id: "rac1",
  game: "rac1",
  probe: (source, context) => probePs2PrimaryAuthority(source, RAC1_PRIMARY_AUTHORITY, context),
  async importWorld(source, request, context) {
    if (source.identity.game !== "rac1" || source.identity.buildId !== RAC1_PRIMARY_AUTHORITY.buildId) {
      throw new Error(
        `R&C1 world import currently supports only authority build '${RAC1_PRIMARY_AUTHORITY.buildId}', got '${source.identity.game}:${source.identity.buildId}'.`,
      );
    }
    const disc = source.files.get(PRIMARY_DISC_SOURCE_KEY);
    if (!disc) throw new Error(`R&C1 world import requires the '${PRIMARY_DISC_SOURCE_KEY}' random-access disc source.`);

    context.log({ level: "info", message: `Importing R&C1 native level ${request.levelId} evidence-backed world slice.`, sourcePath: disc.name });
    const result = await importRac1WorldSlice(disc, source.identity.buildId, request.levelId);
    context.log({
      level: "info",
      message: `R&C1 level ${result.level.levelId}: ${result.world.meshes.length} render meshes, ${result.world.collisionMeshes.length} collision mesh, ${result.world.materials.length} materials; placements tie=${result.tieInstanceCount} shrub=${result.shrubInstanceCount} moby=${result.mobyInstanceCount}.`,
      sourcePath: disc.name,
      offset: result.level.headerLba * 0x800,
    });
    return result.world;
  },
};
