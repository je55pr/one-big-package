import type { OBPImporter } from "../../importer-common/src/index.js";
import { PRIMARY_DISC_SOURCE_KEY, probePs2PrimaryAuthority } from "../../importer-ps2-common/src/index.js";
import { importRac3World } from "../../rac3-world/src/index.js";

export const RAC3_PRIMARY_AUTHORITY = {
  game: "rac3",
  buildId: "rac3-ntscu-original",
  serial: "SCUS-97353",
  region: "NTSC-U",
  revision: "original retail",
  sha256: "d2bb15c7c5b2205db868713fc0362c2b10e87751ca5bcc4e96c1e244a8c42444",
} as const;

export const rac3Importer: OBPImporter = {
  id: "rac3",
  game: "rac3",
  probe: (source, context) => probePs2PrimaryAuthority(source, RAC3_PRIMARY_AUTHORITY, context),
  async importWorld(source, request, context) {
    if (source.identity.game !== "rac3" || source.identity.buildId !== RAC3_PRIMARY_AUTHORITY.buildId) {
      throw new Error(`RAC3 world import supports only authority build '${RAC3_PRIMARY_AUTHORITY.buildId}', got '${source.identity.game}:${source.identity.buildId}'.`);
    }
    const disc = source.files.get(PRIMARY_DISC_SOURCE_KEY);
    if (!disc) throw new Error(`RAC3 world import requires the '${PRIMARY_DISC_SOURCE_KEY}' random-access disc source.`);

    context.log({ level: "info", message: `Importing RAC3 retail level/table ${request.levelId} through the evidence-backed world path.`, sourcePath: disc.name });
    const result = await importRac3World(disc, source.identity.buildId, request.levelId);
    context.log({
      level: "info",
      message: `RAC3 level ${result.publicNativeLevelIdHint}: ${result.world.meshes.length} world meshes, ${result.mobyModelCount} Moby models, ${result.world.instances.length} authored Moby instances (${result.mobiesWithPvar} with PVar); tie=${result.tieInstanceCount} shrub=${result.shrubInstanceCount}.`,
      sourcePath: disc.name,
    });
    return result.world;
  },
};
