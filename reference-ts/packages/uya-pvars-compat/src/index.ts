import { parseGcGameplayMobyPvars } from "../../gc-pvars/src/index.js";
import type { GcGameplayMobyPvarData } from "../../gc-pvars/src/index.js";
import { Sha256 } from "../../hashing/src/index.js";

const HEADER_BYTES = 0x68;
const MOBY_CLASSES_PTR = 0x48;
const MOBY_INSTANCES_PTR = 0x4c;
const PVAR_MOBY_LINKS_PTR = 0x58;
const PVAR_TABLE_PTR = 0x5c;
const PVAR_DATA_PTR = 0x60;
const PVAR_RELATIVE_POINTERS_PTR = 0x64;
const MOBY_INSTANCE_BYTES = 0x88;
const MAX_CLASSES = 100_000;
const MAX_MOBIES = 200_000;
const MAX_FIXUPS = 1_000_000;

export interface UyaGameplayPointerCensus {
  readonly headerOffset: number;
  readonly rawValue: number;
  readonly present: boolean;
}

export interface UyaPvarFixupCensusEntry {
  readonly index: number;
  readonly pvarIndex: number;
  readonly offset: number;
}
export interface UyaReferencedPvarEntryCensus {
  readonly index: number;
  readonly offset: number;
  readonly size: number;
  readonly dataOffset: number;
  readonly referencedByMoby: boolean;
  readonly referencedByMobyLink: boolean;
  readonly referencedByRelativePointer: boolean;
}

export interface UyaPvarPrerequisiteCensus {
  readonly gameplayBytes: number;
  readonly gameplaySha256: string;
  readonly pointers: Readonly<Record<
    "mobyClasses" | "mobyInstances" | "pvarMobyLinks" | "pvarTable" | "pvarData" | "pvarRelativePointers",
    UyaGameplayPointerCensus
  >>;
  readonly classCount: number;
  readonly mobyCount: number;
  readonly mobiesWithPvar: number;
  readonly mobyReferencedPvarCount: number;
  readonly allReferencedPvarCount: number;
  readonly maxReferencedPvarIndex: number | null;
  readonly referencedPvars: readonly UyaReferencedPvarEntryCensus[];
  readonly pvarMobyLinks: readonly UyaPvarFixupCensusEntry[];
  readonly pvarRelativePointers: readonly UyaPvarFixupCensusEntry[];
}
export interface UyaGcPvarCompatibilityResult {
  readonly evidenceStatus: "existing-gc-pvar-parser-applied-to-uya-bytes-with-independent-prerequisite-census";
  readonly prerequisiteCensus: UyaPvarPrerequisiteCensus;
  readonly parser: GcGameplayMobyPvarData;
}

/**
 * Independently validate the structural prerequisites used by the GC gameplay/PVar reader,
 * then apply that reader unchanged. Acceptance proves layout compatibility, not UYA-native
 * semantic names for the fields exposed by the GC implementation.
 */
export function probeUyaGcPvarCompatibility(gameplay: Uint8Array): UyaGcPvarCompatibilityResult {
  const prerequisiteCensus = censusUyaGameplayPvarPrerequisites(gameplay);
  const parser = parseGcGameplayMobyPvars(gameplay);

  if (parser.mobyClasses.length !== prerequisiteCensus.classCount) {
    throw new Error(`GC PVar parser class count ${parser.mobyClasses.length} disagrees with independent UYA census ${prerequisiteCensus.classCount}.`);
  }
  if (parser.mobies.length !== prerequisiteCensus.mobyCount) {
    throw new Error(`GC PVar parser Moby count ${parser.mobies.length} disagrees with independent UYA census ${prerequisiteCensus.mobyCount}.`);
  }
  if (parser.pvarMobyLinks.length !== prerequisiteCensus.pvarMobyLinks.length) {
    throw new Error("GC PVar parser Moby-link fixup count disagrees with independent UYA census.");
  }
  if (parser.pvarRelativePointers.length !== prerequisiteCensus.pvarRelativePointers.length) {
    throw new Error("GC PVar parser relative-pointer fixup count disagrees with independent UYA census.");
  }
  if (parser.mobyPvars.length !== prerequisiteCensus.mobyReferencedPvarCount) {
    throw new Error(`GC PVar parser referenced-PVar count ${parser.mobyPvars.length} disagrees with independent UYA Moby reference census ${prerequisiteCensus.mobyReferencedPvarCount}.`);
  }
  const independentMobyPvars = prerequisiteCensus.referencedPvars.filter((entry) => entry.referencedByMoby);
  for (const expected of independentMobyPvars) {
    const actual = parser.mobyPvars.find((entry) => entry.index === expected.index);
    if (!actual || actual.offset !== expected.offset || actual.size !== expected.size || actual.dataOffset !== expected.dataOffset) {
      throw new Error(`GC PVar parser entry ${expected.index} disagrees with the independent UYA table/data census.`);
    }
  }
  return {
    evidenceStatus: "existing-gc-pvar-parser-applied-to-uya-bytes-with-independent-prerequisite-census",
    prerequisiteCensus,
    parser,
  };
}

export function censusUyaGameplayPvarPrerequisites(gameplay: Uint8Array): UyaPvarPrerequisiteCensus {
  if (gameplay.byteLength < HEADER_BYTES) throw new Error(`UYA gameplay lump is too small (${gameplay.byteLength} bytes).`);
  const view = new DataView(gameplay.buffer, gameplay.byteOffset, gameplay.byteLength);
  const pointer = (at: number, label: string): UyaGameplayPointerCensus => {
    const rawValue = view.getInt32(at, true);
    if (rawValue < 0 || rawValue >= gameplay.byteLength) {
      if (rawValue !== 0) throw new Error(`${label} pointer 0x${(rawValue >>> 0).toString(16)} lies outside ${gameplay.byteLength}-byte UYA gameplay data.`);
    }
    return { headerOffset: at, rawValue, present: rawValue !== 0 };
  };
  const pointers = {
    mobyClasses: pointer(MOBY_CLASSES_PTR, "Moby classes"),
    mobyInstances: pointer(MOBY_INSTANCES_PTR, "Moby instances"),
    pvarMobyLinks: pointer(PVAR_MOBY_LINKS_PTR, "PVar Moby-link fixups"),
    pvarTable: pointer(PVAR_TABLE_PTR, "PVar table"),
    pvarData: pointer(PVAR_DATA_PTR, "PVar data"),
    pvarRelativePointers: pointer(PVAR_RELATIVE_POINTERS_PTR, "PVar relative-pointer fixups"),
  } as const;

  let classCount = 0;
  if (pointers.mobyClasses.present) {
    ensureRange(gameplay, pointers.mobyClasses.rawValue, 4, "UYA Moby class count");
    classCount = view.getInt32(pointers.mobyClasses.rawValue, true);
    if (classCount < 0 || classCount > MAX_CLASSES) throw new Error(`Implausible UYA Moby class count ${classCount}.`);
    ensureRange(gameplay, pointers.mobyClasses.rawValue + 4, classCount * 4, "UYA Moby class table");
  }

  const mobyPvarIndices: number[] = [];
  let mobyCount = 0;
  let mobiesWithPvar = 0;
  if (pointers.mobyInstances.present) {
    const block = pointers.mobyInstances.rawValue;
    ensureRange(gameplay, block, 0x10, "UYA Moby block header");
    mobyCount = view.getInt32(block, true);
    if (mobyCount < 0 || mobyCount > MAX_MOBIES) throw new Error(`Implausible UYA static Moby count ${mobyCount}.`);
    ensureRange(gameplay, block + 0x10, mobyCount * MOBY_INSTANCE_BYTES, "UYA Moby instance table");
    for (let i = 0; i < mobyCount; i++) {
      const at = block + 0x10 + i * MOBY_INSTANCE_BYTES;
      const size = view.getInt32(at, true);
      if (size !== MOBY_INSTANCE_BYTES) {
        throw new Error(`UYA Moby ${i} has size 0x${(size >>> 0).toString(16)}; expected 0x88.`);
      }
      const pvarIndex = view.getInt32(at + 0x68, true);
      if (pvarIndex >= 0) {
        mobiesWithPvar++;
        mobyPvarIndices.push(pvarIndex);
      }
    }
  }

  const pvarMobyLinks = readFixupCensus(gameplay, view, pointers.pvarMobyLinks, "UYA PVar Moby-link fixups");
  const pvarRelativePointers = readFixupCensus(gameplay, view, pointers.pvarRelativePointers, "UYA PVar relative-pointer fixups");
  const mobyRefs = new Set(mobyPvarIndices);
  const linkRefs = new Set(pvarMobyLinks.map((entry) => entry.pvarIndex));
  const relativeRefs = new Set(pvarRelativePointers.map((entry) => entry.pvarIndex));
  const allRefs = [...new Set([...mobyRefs, ...linkRefs, ...relativeRefs])].sort((a, b) => a - b);

  if (allRefs.length > 0 && (!pointers.pvarTable.present || !pointers.pvarData.present)) {
    throw new Error("UYA gameplay references PVars but the candidate PVar table/data blocks are absent.");
  }

  const referencedPvars: UyaReferencedPvarEntryCensus[] = [];
  for (const index of allRefs) {
    const tableAt = pointers.pvarTable.rawValue + index * 8;
    ensureRange(gameplay, tableAt, 8, `UYA PVar table entry ${index}`);
    const offset = view.getInt32(tableAt, true);
    const size = view.getInt32(tableAt + 4, true);
    if (offset < 0 || size < 0) throw new Error(`UYA PVar table entry ${index} has negative offset/size (${offset}, ${size}).`);
    const dataOffset = pointers.pvarData.rawValue + offset;
    ensureRange(gameplay, dataOffset, size, `UYA PVar ${index} data`);
    referencedPvars.push({
      index,
      offset,
      size,
      dataOffset,
      referencedByMoby: mobyRefs.has(index),
      referencedByMobyLink: linkRefs.has(index),
      referencedByRelativePointer: relativeRefs.has(index),
    });
  }

  const byPvar = new Map(referencedPvars.map((entry) => [entry.index, entry]));
  validateFixupTargets(pvarMobyLinks, byPvar, "UYA PVar Moby-link fixups");
  validateFixupTargets(pvarRelativePointers, byPvar, "UYA PVar relative-pointer fixups");

  return {
    gameplayBytes: gameplay.byteLength,
    gameplaySha256: sha256(gameplay),
    pointers,
    classCount,
    mobyCount,
    mobiesWithPvar,
    mobyReferencedPvarCount: mobyRefs.size,
    allReferencedPvarCount: allRefs.length,
    maxReferencedPvarIndex: allRefs.length ? allRefs.at(-1)! : null,
    referencedPvars,
    pvarMobyLinks,
    pvarRelativePointers,
  };
}

function readFixupCensus(
  gameplay: Uint8Array,
  view: DataView,
  pointer: UyaGameplayPointerCensus,
  label: string,
): UyaPvarFixupCensusEntry[] {
  if (!pointer.present) return [];
  const out: UyaPvarFixupCensusEntry[] = [];
  for (let index = 0; index < MAX_FIXUPS; index++) {
    const at = pointer.rawValue + index * 8;
    ensureRange(gameplay, at, 8, `${label} entry ${index}`);
    const pvarIndex = view.getInt32(at, true);
    if (pvarIndex < 0) return out;
    out.push({ index, pvarIndex, offset: view.getUint32(at + 4, true) });
  }
  throw new Error(`${label} exceeded safety cap ${MAX_FIXUPS} without a terminator.`);
}
function validateFixupTargets(
  fixups: readonly UyaPvarFixupCensusEntry[],
  pvars: ReadonlyMap<number, UyaReferencedPvarEntryCensus>,
  label: string,
): void {
  for (const fixup of fixups) {
    const pvar = pvars.get(fixup.pvarIndex);
    if (!pvar) throw new Error(`${label} entry ${fixup.index} references missing PVar ${fixup.pvarIndex}.`);
    if (fixup.offset > pvar.size || 4 > pvar.size - fixup.offset) {
      throw new Error(`${label} entry ${fixup.index} offset ${fixup.offset} does not fit a 4-byte field in PVar ${fixup.pvarIndex} (${pvar.size} bytes).`);
    }
  }
}

function ensureRange(data: Uint8Array, offset: number, length: number, label: string): void {
  if (!Number.isSafeInteger(offset) || !Number.isSafeInteger(length) || offset < 0 || length < 0 || offset > data.byteLength || length > data.byteLength - offset) {
    throw new RangeError(`${label} range ${offset}+${length} lies outside ${data.byteLength}-byte UYA gameplay data.`);
  }
}

function sha256(bytes: Uint8Array): string {
  return new Sha256().update(bytes).digestHex();
}
