import { rac1Importer } from "../../importer-rac1/src/index.js";
import { rac2Importer } from "../../importer-rac2/src/index.js";
import { rac3Importer } from "../../importer-rac3/src/index.js";

/** Primary Stage-0 probes for the three original PS2 Ratchet & Clank games. */
export const TRILOGY_IMPORTERS = [rac1Importer, rac2Importer, rac3Importer] as const;
