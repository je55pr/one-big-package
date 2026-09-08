import type { RandomAccessReader } from "../../importer-common/src/index.js";

const ELF32_HEADER_SIZE = 52;
const ELF32_PROGRAM_HEADER_SIZE = 32;
const ELF32_ADDRESS_SPACE_SIZE = 0x1_0000_0000;
const ELF_MAGIC = [0x7f, 0x45, 0x4c, 0x46] as const;

export const ELF_MACHINE_MIPS = 8 as const;
export const ELF_TYPE_EXECUTABLE = 2 as const;
export const ELF_PROGRAM_TYPE_LOAD = 1 as const;

export interface Elf32Header {
  readonly endian: "little" | "big";
  readonly osAbi: number;
  readonly abiVersion: number;
  readonly type: number;
  readonly machine: number;
  readonly version: number;
  readonly entry: number;
  readonly programHeaderOffset: number;
  readonly sectionHeaderOffset: number;
  readonly flags: number;
  readonly headerSize: number;
  readonly programHeaderEntrySize: number;
  readonly programHeaderCount: number;
  readonly sectionHeaderEntrySize: number;
  readonly sectionHeaderCount: number;
  readonly sectionNameStringTableIndex: number;
}

export interface Elf32ProgramHeader {
  readonly index: number;
  readonly type: number;
  readonly offset: number;
  readonly virtualAddress: number;
  readonly physicalAddress: number;
  readonly fileSize: number;
  readonly memorySize: number;
  readonly flags: number;
  readonly alignment: number;
}

export interface Elf32ProgramHeaderOptions {
  /** Safety cap for malformed or unexpected executables. */
  readonly maxProgramHeaders?: number;
}

export interface Elf32VirtualRangeMapping {
  readonly segmentIndex: number;
  readonly virtualAddress: number;
  readonly fileOffset: number;
  readonly length: number;
}

/** Read only the fixed 52-byte ELF32 header. ELF payloads and tables remain untouched. */
export async function readElf32Header(reader: RandomAccessReader): Promise<Elf32Header> {
  if (reader.size < ELF32_HEADER_SIZE) throw new Error(`ELF32 source '${reader.name}' is smaller than the ${ELF32_HEADER_SIZE}-byte header.`);
  const bytes = await reader.read(0, ELF32_HEADER_SIZE);
  if (bytes.length !== ELF32_HEADER_SIZE) throw new Error(`Short ELF32 header read from '${reader.name}'.`);

  for (let index = 0; index < ELF_MAGIC.length; index++) {
    if (bytes[index] !== ELF_MAGIC[index]) throw new Error(`Invalid ELF magic in '${reader.name}'.`);
  }
  if (bytes[4] !== 1) throw new Error(`Unsupported ELF class ${bytes[4] ?? "missing"}; expected ELF32.`);
  const dataEncoding = bytes[5];
  if (dataEncoding !== 1 && dataEncoding !== 2) throw new Error(`Unsupported ELF data encoding ${dataEncoding ?? "missing"}.`);
  if (bytes[6] !== 1) throw new Error(`Unsupported ELF identification version ${bytes[6] ?? "missing"}.`);

  const littleEndian = dataEncoding === 1;
  const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
  const header: Elf32Header = {
    endian: littleEndian ? "little" : "big",
    osAbi: bytes[7] ?? 0,
    abiVersion: bytes[8] ?? 0,
    type: view.getUint16(16, littleEndian),
    machine: view.getUint16(18, littleEndian),
    version: view.getUint32(20, littleEndian),
    entry: view.getUint32(24, littleEndian),
    programHeaderOffset: view.getUint32(28, littleEndian),
    sectionHeaderOffset: view.getUint32(32, littleEndian),
    flags: view.getUint32(36, littleEndian),
    headerSize: view.getUint16(40, littleEndian),
    programHeaderEntrySize: view.getUint16(42, littleEndian),
    programHeaderCount: view.getUint16(44, littleEndian),
    sectionHeaderEntrySize: view.getUint16(46, littleEndian),
    sectionHeaderCount: view.getUint16(48, littleEndian),
    sectionNameStringTableIndex: view.getUint16(50, littleEndian),
  };

  if (header.version !== 1) throw new Error(`Unsupported ELF32 version ${header.version}.`);
  if (header.headerSize < ELF32_HEADER_SIZE) throw new Error(`Invalid ELF32 header size ${header.headerSize}; expected at least ${ELF32_HEADER_SIZE}.`);
  if (header.programHeaderCount > 0 && header.programHeaderEntrySize < ELF32_PROGRAM_HEADER_SIZE) {
    throw new Error(`Invalid ELF32 program-header entry size ${header.programHeaderEntrySize}; expected at least ${ELF32_PROGRAM_HEADER_SIZE}.`);
  }
  validateTableRange(reader, "program-header", header.programHeaderOffset, header.programHeaderEntrySize, header.programHeaderCount);
  if (header.sectionHeaderCount > 0) {
    if (header.sectionHeaderEntrySize === 0) throw new Error("ELF32 section-header count is nonzero but entry size is zero.");
    validateTableRange(reader, "section-header", header.sectionHeaderOffset, header.sectionHeaderEntrySize, header.sectionHeaderCount);
  }

  return header;
}

/**
 * Read program headers one entry at a time. Segment payloads are never read, and a configurable
 * count cap prevents a malformed executable from causing unbounded table traversal.
 */
export async function readElf32ProgramHeaders(
  reader: RandomAccessReader,
  header?: Elf32Header,
  options: Elf32ProgramHeaderOptions = {},
): Promise<readonly Elf32ProgramHeader[]> {
  const resolvedHeader = header ?? await readElf32Header(reader);
  const maxProgramHeaders = options.maxProgramHeaders ?? 256;
  if (!Number.isSafeInteger(maxProgramHeaders) || maxProgramHeaders < 0) {
    throw new RangeError(`Invalid maxProgramHeaders: ${maxProgramHeaders}`);
  }
  if (resolvedHeader.programHeaderCount > maxProgramHeaders) {
    throw new Error(`ELF32 program-header count ${resolvedHeader.programHeaderCount} exceeds safety cap ${maxProgramHeaders}.`);
  }

  const littleEndian = resolvedHeader.endian === "little";
  const entries: Elf32ProgramHeader[] = [];
  for (let index = 0; index < resolvedHeader.programHeaderCount; index++) {
    const offset = resolvedHeader.programHeaderOffset + index * resolvedHeader.programHeaderEntrySize;
    const bytes = await reader.read(offset, resolvedHeader.programHeaderEntrySize);
    if (bytes.length !== resolvedHeader.programHeaderEntrySize) throw new Error(`Short ELF32 program-header read at index ${index}.`);
    const view = new DataView(bytes.buffer, bytes.byteOffset, bytes.byteLength);
    const entry: Elf32ProgramHeader = {
      index,
      type: view.getUint32(0, littleEndian),
      offset: view.getUint32(4, littleEndian),
      virtualAddress: view.getUint32(8, littleEndian),
      physicalAddress: view.getUint32(12, littleEndian),
      fileSize: view.getUint32(16, littleEndian),
      memorySize: view.getUint32(20, littleEndian),
      flags: view.getUint32(24, littleEndian),
      alignment: view.getUint32(28, littleEndian),
    };
    validateSegmentRange(reader, entry);
    entries.push(entry);
  }
  return entries;
}

/** Resolve one fully file-backed ELF32 virtual range through PT_LOAD metadata. */
export function mapElf32VirtualRange(
  programHeaders: readonly Elf32ProgramHeader[],
  virtualAddress: number,
  length: number,
): Elf32VirtualRangeMapping {
  validateVirtualRange(virtualAddress, length);
  const requestEnd = virtualAddress + length;
  let memoryOnlySegment: Elf32ProgramHeader | undefined;

  for (const segment of programHeaders) {
    if (segment.type !== ELF_PROGRAM_TYPE_LOAD) continue;
    const memoryEnd = segment.virtualAddress + segment.memorySize;
    if (!Number.isSafeInteger(memoryEnd) || memoryEnd > ELF32_ADDRESS_SPACE_SIZE) {
      throw new RangeError(`ELF32 PT_LOAD segment ${segment.index} exceeds the 32-bit virtual address space.`);
    }
    if (virtualAddress < segment.virtualAddress || requestEnd > memoryEnd) continue;

    const relative = virtualAddress - segment.virtualAddress;
    if (relative <= segment.fileSize && length <= segment.fileSize - relative) {
      const fileOffset = segment.offset + relative;
      if (!Number.isSafeInteger(fileOffset)) throw new RangeError(`ELF32 virtual mapping overflow in segment ${segment.index}.`);
      return { segmentIndex: segment.index, virtualAddress, fileOffset, length };
    }
    memoryOnlySegment = segment;
  }

  const range = `0x${virtualAddress.toString(16)}+${length}`;
  if (memoryOnlySegment) {
    throw new RangeError(`ELF32 virtual range ${range} lies in PT_LOAD segment ${memoryOnlySegment.index} but is not fully file-backed (BSS/zero-fill).`);
  }
  throw new RangeError(`ELF32 virtual range ${range} is not mapped by any PT_LOAD segment.`);
}

/** Map and read one file-backed virtual range without buffering any surrounding executable data. */
export async function readElf32VirtualRange(
  reader: RandomAccessReader,
  programHeaders: readonly Elf32ProgramHeader[],
  virtualAddress: number,
  length: number,
): Promise<Uint8Array> {
  if (length === 0) {
    validateVirtualRange(virtualAddress, length);
    return new Uint8Array();
  }
  const mapping = mapElf32VirtualRange(programHeaders, virtualAddress, length);
  if (mapping.fileOffset > reader.size || mapping.length > reader.size - mapping.fileOffset) {
    throw new RangeError(`Mapped ELF32 virtual range lies outside '${reader.name}'.`);
  }
  const bytes = await reader.read(mapping.fileOffset, mapping.length);
  if (bytes.length !== mapping.length) throw new Error(`Short ELF32 virtual-range read from '${reader.name}'.`);
  return bytes;
}

function validateTableRange(reader: RandomAccessReader, label: string, offset: number, entrySize: number, count: number): void {
  if (!Number.isSafeInteger(offset) || !Number.isSafeInteger(entrySize) || !Number.isSafeInteger(count)) {
    throw new RangeError(`Invalid ELF32 ${label} table metadata.`);
  }
  if (count === 0) return;
  const size = entrySize * count;
  if (!Number.isSafeInteger(size) || offset > reader.size || size > reader.size - offset) {
    throw new RangeError(`ELF32 ${label} table lies outside '${reader.name}'.`);
  }
}

function validateSegmentRange(reader: RandomAccessReader, entry: Elf32ProgramHeader): void {
  if (entry.fileSize === 0) return;
  if (entry.offset > reader.size || entry.fileSize > reader.size - entry.offset) {
    throw new RangeError(`ELF32 program segment ${entry.index} lies outside '${reader.name}'.`);
  }
}

function validateVirtualRange(virtualAddress: number, length: number): void {
  if (!Number.isSafeInteger(virtualAddress) || virtualAddress < 0 || virtualAddress >= ELF32_ADDRESS_SPACE_SIZE) {
    throw new RangeError(`Invalid ELF32 virtual address: ${virtualAddress}.`);
  }
  if (!Number.isSafeInteger(length) || length < 0) throw new RangeError(`Invalid ELF32 virtual range length: ${length}.`);
  const end = virtualAddress + length;
  if (!Number.isSafeInteger(end) || end > ELF32_ADDRESS_SPACE_SIZE) {
    throw new RangeError(`ELF32 virtual range exceeds the 32-bit address space.`);
  }
}
