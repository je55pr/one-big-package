import type { OBPImporter } from "../../importer-common/src/index.js";
import { probePs2PrimaryAuthority } from "../../importer-ps2-common/src/index.js";

export const RAC2_PRIMARY_AUTHORITY = {
  game: "rac2",
  buildId: "rac2-ntscu-v1.01",
  serial: "SCUS-97268",
  region: "NTSC-U",
  revision: "1.01 original retail",
  sha256: "9db2e33e276133cc283647fa3279b37911955e123d6199d10065547eaa9b1ce5",
} as const;

export const rac2Importer: OBPImporter = {
  id: "rac2",
  game: "rac2",
  probe: (source, context) => probePs2PrimaryAuthority(source, RAC2_PRIMARY_AUTHORITY, context),
  async importWorld() {
    throw new Error("Going Commando retail world decoding exists in dedicated GC packages/tools, but integration with the OBPImporter importWorld() contract is not complete yet.");
  },
};
