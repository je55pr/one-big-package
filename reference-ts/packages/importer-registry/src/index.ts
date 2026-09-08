import {
  ImportProbeCache,
  type ImportContext,
  type ImportProbeResult,
  type OBPImporter,
  type ProbeSourceSet,
} from "../../importer-common/src/index.js";

export interface ImporterProbeCandidate {
  readonly importer: OBPImporter;
  readonly result: ImportProbeResult;
}

export type ImporterSelection =
  | {
      readonly status: "match";
      readonly candidate: ImporterProbeCandidate;
      readonly candidates: readonly ImporterProbeCandidate[];
    }
  | {
      readonly status: "none";
      readonly candidates: readonly ImporterProbeCandidate[];
    }
  | {
      readonly status: "ambiguous";
      readonly candidates: readonly ImporterProbeCandidate[];
      readonly tied: readonly ImporterProbeCandidate[];
    };

const CONFIDENCE_RANK: Readonly<Record<ImportProbeResult["confidence"], number>> = {
  none: 0,
  possible: 1,
  strong: 2,
  exact: 3,
};

/**
 * Probe importers sequentially so diagnostic logging and source reads remain deterministic.
 * One per-run cache is shared across all importers, allowing common bounded archaeology (such as
 * PS2 boot metadata) to be performed once for the same reader. Results are ordered by confidence
 * first and importer id second.
 */
export async function probeImporters(
  importers: readonly OBPImporter[],
  source: ProbeSourceSet,
  context: ImportContext,
): Promise<readonly ImporterProbeCandidate[]> {
  assertUniqueImporterIds(importers);
  const candidates: ImporterProbeCandidate[] = [];
  const probeCache = context.probeCache ?? new ImportProbeCache();
  const sharedContext: ImportContext = {
    log: (event) => context.log(event),
    probeCache,
    ...(context.signal ? { signal: context.signal } : {}),
  };

  for (const importer of importers) {
    sharedContext.signal?.throwIfAborted();
    const result = await importer.probe(source, sharedContext);
    candidates.push({ importer, result });
  }

  return candidates.sort(compareCandidates);
}

/**
 * Select the unique highest-confidence non-none candidate. A tie is reported explicitly rather
 * than resolved by registration order, preventing an arbitrary importer from claiming a source.
 */
export function selectImporter(candidates: readonly ImporterProbeCandidate[]): ImporterSelection {
  if (candidates.length === 0) return { status: "none", candidates: [] };

  const ordered = [...candidates].sort(compareCandidates);
  const best = ordered[0];
  if (!best || best.result.confidence === "none") return { status: "none", candidates: ordered };

  const bestRank = CONFIDENCE_RANK[best.result.confidence];
  const tied = ordered.filter((candidate) => CONFIDENCE_RANK[candidate.result.confidence] === bestRank);
  if (tied.length > 1) return { status: "ambiguous", candidates: ordered, tied };
  return { status: "match", candidate: best, candidates: ordered };
}

export async function probeAndSelectImporter(
  importers: readonly OBPImporter[],
  source: ProbeSourceSet,
  context: ImportContext,
): Promise<ImporterSelection> {
  return selectImporter(await probeImporters(importers, source, context));
}

function compareCandidates(a: ImporterProbeCandidate, b: ImporterProbeCandidate): number {
  const confidence = CONFIDENCE_RANK[b.result.confidence] - CONFIDENCE_RANK[a.result.confidence];
  return confidence || a.importer.id.localeCompare(b.importer.id);
}

function assertUniqueImporterIds(importers: readonly OBPImporter[]): void {
  const seen = new Set<string>();
  for (const importer of importers) {
    if (seen.has(importer.id)) throw new Error(`Duplicate OBP importer id '${importer.id}'.`);
    seen.add(importer.id);
  }
}
