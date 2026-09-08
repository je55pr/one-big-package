import type { ImportContext, ImportProbeResult, ProbeSourceSet } from "../../importer-common/src/index.js";
import type { OBPSourceGame } from "../../core/src/index.js";
import { readPs2BootInfo } from "../../ps2-disc/src/index.js";

export const PRIMARY_DISC_SOURCE_KEY = "disc";
const PS2_BOOT_INFO_CACHE_KEY = "ps2-disc:boot-info:v1";

export interface Ps2PrimaryAuthority {
  readonly game: Exclude<OBPSourceGame, "synthetic">;
  readonly buildId: string;
  readonly serial: string;
  readonly region: string;
  readonly revision: string;
  readonly sha256: string;
}

export async function probePs2PrimaryAuthority(
  source: ProbeSourceSet,
  authority: Ps2PrimaryAuthority,
  context: ImportContext,
): Promise<ImportProbeResult> {
  const disc = source.files.get(PRIMARY_DISC_SOURCE_KEY);
  if (!disc) {
    return { confidence: "none", reasons: [`Missing required '${PRIMARY_DISC_SOURCE_KEY}' source reader.`] };
  }

  let boot;
  try {
    boot = context.probeCache
      ? await context.probeCache.getOrCreate(disc, PS2_BOOT_INFO_CACHE_KEY, () => readPs2BootInfo(disc))
      : await readPs2BootInfo(disc);
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    context.log({ level: "debug", message: `PS2 boot probe failed: ${message}`, sourcePath: disc.name });
    return { confidence: "none", reasons: [`PS2 boot probe failed: ${message}`] };
  }

  if (boot.serial !== authority.serial) {
    return {
      confidence: "none",
      reasons: [`SYSTEM.CNF serial ${boot.serial ?? "unknown"} does not match ${authority.serial}.`],
    };
  }

  const entryPoint = `0x${boot.executableEntryPoint.toString(16).padStart(8, "0")}`;
  const reasons = [
    `SYSTEM.CNF serial matches ${authority.serial}.`,
    `Referenced boot target is a valid little-endian ELF32 MIPS executable (entry ${entryPoint}).`,
  ];
  const hintedSha = source.identityHint?.sha256?.toLowerCase();
  if (hintedSha) {
    if (hintedSha === authority.sha256.toLowerCase()) {
      reasons.push(`Verified payload SHA-256 matches primary authority ${authority.buildId}.`);
      return { confidence: "exact", reasons };
    }
    reasons.push(`Verified payload SHA-256 differs from primary authority ${authority.buildId}; serial/ELF structure still match.`);
  } else {
    reasons.push("No verified payload SHA-256 hint was supplied; exact revision is not claimed.");
  }

  return { confidence: "strong", reasons };
}
