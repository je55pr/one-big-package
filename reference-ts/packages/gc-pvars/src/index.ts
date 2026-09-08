/**
 * Strict archaeology reader for Going Commando gameplay Moby/PVar structures.
 *
 * This intentionally complements gc-instances rather than replacing its world-
 * instance decoder. It preserves raw/native fields needed for gameplay research
 * and validates the PVar indirection used by static Mobies.
 */

export interface GcPvarEntry {
  readonly index: number;
  /** Offset relative to the GC PVar data block. */
  readonly offset: number;
  readonly size: number;
  /** Absolute offset in the decompressed gameplay lump. */
  readonly dataOffset: number;
}

export interface GcPvarFixup {
  readonly pvarIndex: number;
  /** Byte offset within that PVar. */
  readonly offset: number;
}

export interface GcGameplayMobyPvarInstance {
  readonly index: number;
  readonly oClass: number;
  readonly pvarIndex: number;
  readonly modeBits: number;
  readonly uid: number;
  /** Native field at +0x14. Public tools call this `bolts`; retail semantics are not asserted here. */
  readonly raw0x14: number;
  readonly pvar: GcPvarEntry | null;
}

export interface GcGameplayMobyPvarData {
  /** Class IDs listed by the native Moby-class block at gameplay header +0x48. */
  readonly mobyClasses: readonly number[];
  readonly mobies: readonly GcGameplayMobyPvarInstance[];
  /** Unique PVar table entries referenced by static Mobies, sorted by table index. */
  readonly mobyPvars: readonly GcPvarEntry[];
  /** Entire native Moby-link fixup block, terminated by pvarIndex < 0. */
  readonly pvarMobyLinks: readonly GcPvarFixup[];
  /** Entire native relative-pointer fixup block, terminated by pvarIndex < 0. */
  readonly pvarRelativePointers: readonly GcPvarFixup[];
  readonly pvarDataBlockOffset: number | null;
}

const GC_MOBY_CLASSES_PTR = 0x48;
const GC_MOBY_INSTANCES_PTR = 0x4c;
const GC_PVAR_MOBY_LINKS_PTR = 0x58;
const GC_PVAR_TABLE_PTR = 0x5c;
const GC_PVAR_DATA_PTR = 0x60;
const GC_PVAR_RELATIVE_POINTERS_PTR = 0x64;
const GC_MOBY_INSTANCE_SIZE = 0x88;
const MAX_MOBIES = 200_000;
const MAX_CLASSES = 100_000;
const MAX_FIXUPS = 1_000_000;

/**
 * Parse the GC-specific Moby/PVar slice from one *decompressed* gameplay lump.
 *
 * Structural provenance for the offsets is documented in research/GC_PVARS.md.
 * The parser is deliberately strict: malformed pointers/counts throw rather than
 * silently shrinking retail evidence into a plausible-looking result.
 */
export function parseGcGameplayMobyPvars(data: Uint8Array): GcGameplayMobyPvarData {
  if (data.byteLength < 0x68) throw new Error(`GC gameplay lump is too small (${data.byteLength} bytes).`);
  const view = new DataView(data.buffer, data.byteOffset, data.byteLength);

  const blockPointer = (headerOffset: number, label: string): number | null => {
    if (headerOffset + 4 > data.byteLength) throw new Error(`${label} header pointer lies outside gameplay data.`);
    const value = view.getInt32(headerOffset, true);
    if (value === 0) return null;
    if (value < 0 || value >= data.byteLength) {
      throw new Error(`${label} block pointer 0x${(value >>> 0).toString(16)} lies outside ${data.byteLength}-byte gameplay data.`);
    }
    return value;
  };

  const classBlock = blockPointer(GC_MOBY_CLASSES_PTR, "Moby classes");
  const mobyClasses: number[] = [];
  if (classBlock !== null) {
    ensureRange(data, classBlock, 4, "Moby class count");
    const count = view.getInt32(classBlock, true);
    if (count < 0 || count > MAX_CLASSES) throw new Error(`Implausible GC Moby class count ${count}.`);
    ensureRange(data, classBlock + 4, count * 4, "Moby class table");
    for (let i = 0; i < count; i++) mobyClasses.push(view.getInt32(classBlock + 4 + i * 4, true));
  }

  const mobyBlock = blockPointer(GC_MOBY_INSTANCES_PTR, "Moby instances");
  const rawMobies: Omit<GcGameplayMobyPvarInstance, "pvar">[] = [];
  if (mobyBlock !== null) {
    ensureRange(data, mobyBlock, 0x10, "Moby block header");
    const staticCount = view.getInt32(mobyBlock, true);
    if (staticCount < 0 || staticCount > MAX_MOBIES) throw new Error(`Implausible GC static Moby count ${staticCount}.`);
    ensureRange(data, mobyBlock + 0x10, staticCount * GC_MOBY_INSTANCE_SIZE, "Moby instance table");
    for (let i = 0; i < staticCount; i++) {
      const at = mobyBlock + 0x10 + i * GC_MOBY_INSTANCE_SIZE;
      const size = view.getInt32(at, true);
      if (size !== GC_MOBY_INSTANCE_SIZE) {
        throw new Error(`GC Moby ${i} has size 0x${(size >>> 0).toString(16)}; expected 0x88.`);
      }
      rawMobies.push({
        index: i,
        oClass: view.getInt32(at + 0x28, true),
        pvarIndex: view.getInt32(at + 0x68, true),
        modeBits: view.getInt32(at + 0x70, true),
        uid: view.getInt32(at + 0x10, true),
        raw0x14: view.getInt32(at + 0x14, true),
      });
    }
  }

  const pvarTableBlock = blockPointer(GC_PVAR_TABLE_PTR, "PVar table");
  const pvarDataBlock = blockPointer(GC_PVAR_DATA_PTR, "PVar data");
  const referencedPvarIndices = [...new Set(rawMobies.map((m) => m.pvarIndex).filter((index) => index >= 0))].sort((a, b) => a - b);
  const pvarByIndex = new Map<number, GcPvarEntry>();
  if (referencedPvarIndices.length > 0) {
    if (pvarTableBlock === null) throw new Error("Static Mobies reference PVars but the GC PVar table block is absent.");
    if (pvarDataBlock === null) throw new Error("Static Mobies reference PVars but the GC PVar data block is absent.");
    for (const index of referencedPvarIndices) {
      const tableAt = pvarTableBlock + index * 8;
      ensureRange(data, tableAt, 8, `PVar table entry ${index}`);
      const offset = view.getInt32(tableAt, true);
      const size = view.getInt32(tableAt + 4, true);
      if (offset < 0 || size < 0) throw new Error(`PVar table entry ${index} has negative offset/size (${offset}, ${size}).`);
      const dataOffset = pvarDataBlock + offset;
      ensureRange(data, dataOffset, size, `PVar ${index} data`);
      pvarByIndex.set(index, { index, offset, size, dataOffset });
    }
  }

  const mobies: GcGameplayMobyPvarInstance[] = rawMobies.map((moby) => ({
    ...moby,
    pvar: moby.pvarIndex >= 0 ? (pvarByIndex.get(moby.pvarIndex) ?? null) : null,
  }));

  return {
    mobyClasses,
    mobies,
    mobyPvars: [...pvarByIndex.values()],
    pvarMobyLinks: readFixups(data, view, blockPointer(GC_PVAR_MOBY_LINKS_PTR, "PVar Moby-link fixups"), "PVar Moby-link fixups"),
    pvarRelativePointers: readFixups(data, view, blockPointer(GC_PVAR_RELATIVE_POINTERS_PTR, "PVar relative-pointer fixups"), "PVar relative-pointer fixups"),
    pvarDataBlockOffset: pvarDataBlock,
  };
}

function readFixups(
  data: Uint8Array,
  view: DataView,
  block: number | null,
  label: string,
): GcPvarFixup[] {
  if (block === null) return [];
  const out: GcPvarFixup[] = [];
  for (let i = 0; i < MAX_FIXUPS; i++) {
    const at = block + i * 8;
    ensureRange(data, at, 8, `${label} entry ${i}`);
    const pvarIndex = view.getInt32(at, true);
    if (pvarIndex < 0) return out;
    out.push({ pvarIndex, offset: view.getUint32(at + 4, true) });
  }
  throw new Error(`${label} exceeded safety cap ${MAX_FIXUPS} without a terminator.`);
}

function ensureRange(data: Uint8Array, offset: number, length: number, label: string): void {
  if (!Number.isSafeInteger(offset) || !Number.isSafeInteger(length) || offset < 0 || length < 0 || offset > data.byteLength || length > data.byteLength - offset) {
    throw new RangeError(`${label} range ${offset}+${length} lies outside ${data.byteLength}-byte gameplay data.`);
  }
}
