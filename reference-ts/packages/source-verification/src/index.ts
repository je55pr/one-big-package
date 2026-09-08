import type {
  OBPBuildIdentity,
  ProbeSourceSet,
  RandomAccessReader,
} from "../../importer-common/src/index.js";
import type { OBPSourceGame } from "../../core/src/index.js";
import {
  readRawIsoSplitManifestContract,
  type RawIsoSplitManifestContract,
} from "../../input-sources/src/index.js";
import {
  hashRandomAccessReaderSha256,
  type RandomAccessHashOptions,
} from "../../hashing/src/index.js";

export interface VerifiedRawIsoSplitSource {
  readonly manifest: RawIsoSplitManifestContract;
  readonly identity: OBPBuildIdentity;
  readonly sizeBytes: number;
  readonly sha256: string;
}

/**
 * Verify a complete logical source against its primary raw-split authority manifest. Hashing stays
 * bounded by RandomAccessHashOptions.chunkSize and works directly on virtual concatenated parts.
 * No build identity is returned until both payload size and SHA-256 match the authority manifest.
 */
export async function verifyRawIsoSplitSource(
  reader: RandomAccessReader,
  manifestValue: unknown,
  options: RandomAccessHashOptions = {},
): Promise<VerifiedRawIsoSplitSource> {
  const manifest = readRawIsoSplitManifestContract(manifestValue);
  if (reader.size !== manifest.payload.sizeBytes) {
    throw new Error(`Source size mismatch for ${manifest.buildId}: expected ${manifest.payload.sizeBytes}, got ${reader.size}.`);
  }

  const hash = await hashRandomAccessReaderSha256(reader, options);
  if (hash.sha256.toLowerCase() !== manifest.payload.sha256.toLowerCase()) {
    throw new Error(`Source SHA-256 mismatch for ${manifest.buildId}: expected ${manifest.payload.sha256}, got ${hash.sha256}.`);
  }

  const identity: OBPBuildIdentity = {
    game: requireNativeGame(manifest.game),
    buildId: manifest.buildId,
    region: manifest.region,
    serial: manifest.serial,
    revision: manifest.revision,
    sha256: hash.sha256.toLowerCase(),
  };

  return {
    manifest,
    identity,
    sizeBytes: hash.sizeBytes,
    sha256: hash.sha256.toLowerCase(),
  };
}

/** Create the exact-confidence probe source only from a completed verified-source result. */
export function createVerifiedDiscProbeSource(
  reader: RandomAccessReader,
  verified: VerifiedRawIsoSplitSource,
): ProbeSourceSet {
  if (reader.size !== verified.sizeBytes) {
    throw new Error(`Verified source size ${verified.sizeBytes} no longer matches reader size ${reader.size}.`);
  }
  return {
    files: new Map([["disc", reader]]),
    identityHint: verified.identity,
  };
}

function requireNativeGame(game: string): Exclude<OBPSourceGame, "synthetic"> {
  switch (game) {
    case "rac1":
    case "rac2":
    case "rac3":
    case "deadlocked":
      return game;
    default:
      throw new Error(`Unsupported native OBP source game '${game}' in authority manifest.`);
  }
}
