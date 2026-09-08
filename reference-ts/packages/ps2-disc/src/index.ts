import type { RandomAccessReader } from "../../importer-common/src/index.js";
import {
  ELF_MACHINE_MIPS,
  ELF_TYPE_EXECUTABLE,
  readElf32Header,
  readElf32ProgramHeaders,
  type Elf32Header,
  type Elf32ProgramHeader,
  type Elf32ProgramHeaderOptions,
} from "../../elf32/src/index.js";
import {
  Iso9660ExtentReader,
  Iso9660Filesystem,
  normalizeIso9660Path,
} from "../../iso9660/src/index.js";

const DEFAULT_MAX_SYSTEM_CNF_BYTES = 64 * 1024;

export interface Ps2BootInfo {
  readonly volumeIdentifier: string;
  readonly bootKey: "BOOT2" | "BOOT";
  readonly bootPath: string;
  readonly executableIsoPath: string;
  readonly executableName: string;
  readonly executableSize: number;
  readonly executableEntryPoint: number;
  readonly executableProgramHeaderCount: number;
  readonly serial?: string;
  readonly config: Readonly<Record<string, string>>;
}

/** Open PS2 disc metadata while keeping the referenced boot executable range-readable. */
export interface Ps2Disc {
  readonly filesystem: Iso9660Filesystem;
  readonly boot: Ps2BootInfo;
  readonly bootExecutable: Iso9660ExtentReader;
  readonly bootExecutableHeader: Elf32Header;
}

/**
 * Open just enough ISO-9660 metadata, SYSTEM.CNF bytes, and the fixed ELF32 header to identify a PS2
 * disc and validate its boot target. Program headers and executable segment payloads are not read.
 */
export async function openPs2Disc(
  reader: RandomAccessReader,
  options: { maxSystemCnfBytes?: number } = {},
): Promise<Ps2Disc> {
  const maxSystemCnfBytes = options.maxSystemCnfBytes ?? DEFAULT_MAX_SYSTEM_CNF_BYTES;
  if (!Number.isSafeInteger(maxSystemCnfBytes) || maxSystemCnfBytes <= 0) {
    throw new RangeError(`Invalid maxSystemCnfBytes: ${maxSystemCnfBytes}`);
  }

  const filesystem = await Iso9660Filesystem.open(reader);
  const rootEntries = await filesystem.list("/");
  if (!rootEntries) throw new Error("ISO-9660 root directory was not found.");

  const systemCnfEntry = rootEntries.find((entry) => !entry.isDirectory && entry.name.toUpperCase() === "SYSTEM.CNF");
  if (!systemCnfEntry) throw new Error("PS2 SYSTEM.CNF was not found in the ISO-9660 root directory.");
  if (systemCnfEntry.dataLength > maxSystemCnfBytes) {
    throw new Error(`PS2 SYSTEM.CNF is unexpectedly large: ${systemCnfEntry.dataLength} bytes.`);
  }

  const systemCnf = new Iso9660ExtentReader(reader, systemCnfEntry, filesystem.volume.logicalBlockSize, "SYSTEM.CNF");
  const bytes = await systemCnf.read(0, systemCnf.size);
  const config = parseSystemCnf(ascii(bytes).replace(/\0+$/g, ""));
  const bootKey = config.BOOT2 ? "BOOT2" : config.BOOT ? "BOOT" : undefined;
  if (!bootKey) throw new Error("PS2 SYSTEM.CNF does not contain BOOT2 or BOOT.");

  const bootPath = config[bootKey];
  if (!bootPath) throw new Error(`PS2 SYSTEM.CNF ${bootKey} value is empty.`);
  const executableIsoPath = ps2BootPathToIso9660Path(bootPath);
  const executableName = executableNameFromBootPath(bootPath);
  const serial = parsePs2ExecutableSerial(executableName);
  const bootExecutable = await filesystem.openFile(executableIsoPath);
  if (!bootExecutable) {
    throw new Error(`PS2 boot executable '${executableIsoPath}' referenced by ${bootKey} was not found in the ISO-9660 filesystem.`);
  }

  const bootExecutableHeader = await readElf32Header(bootExecutable);
  validatePs2BootElfHeader(bootExecutableHeader);

  const boot: Ps2BootInfo = {
    volumeIdentifier: filesystem.volume.volumeIdentifier,
    bootKey,
    bootPath,
    executableIsoPath,
    executableName,
    executableSize: bootExecutable.size,
    executableEntryPoint: bootExecutableHeader.entry,
    executableProgramHeaderCount: bootExecutableHeader.programHeaderCount,
    ...(serial ? { serial } : {}),
    config,
  };

  return { filesystem, boot, bootExecutable, bootExecutableHeader };
}

/** Read PS2 boot identity while validating that the referenced target is an ELF32 MIPS executable. */
export async function readPs2BootInfo(
  reader: RandomAccessReader,
  options: { maxSystemCnfBytes?: number } = {},
): Promise<Ps2BootInfo> {
  return (await openPs2Disc(reader, options)).boot;
}

/** Read only the program-header table of an already-opened PS2 boot executable. */
export async function readPs2BootProgramHeaders(
  disc: Ps2Disc,
  options: Elf32ProgramHeaderOptions = {},
): Promise<readonly Elf32ProgramHeader[]> {
  return readElf32ProgramHeaders(disc.bootExecutable, disc.bootExecutableHeader, options);
}

export function parseSystemCnf(text: string): Readonly<Record<string, string>> {
  const config: Record<string, string> = {};
  for (const line of text.split(/\r?\n/)) {
    const match = /^\s*([A-Za-z0-9_]+)\s*=\s*(.*?)\s*$/.exec(line);
    if (!match?.[1]) continue;
    config[match[1].toUpperCase()] = match[2] ?? "";
  }
  return config;
}

/** Convert a PS2 cdrom0 boot path into an ISO-9660 path suitable for Iso9660Filesystem. */
export function ps2BootPathToIso9660Path(bootPath: string): string {
  const cleaned = bootPath.trim().replace(/^['"]|['"]$/g, "");
  const deviceMatch = /^([A-Za-z][A-Za-z0-9]*):/.exec(cleaned);
  let path = cleaned;
  if (deviceMatch?.[1]) {
    if (deviceMatch[1].toLowerCase() !== "cdrom0") {
      throw new Error(`Unsupported PS2 boot device '${deviceMatch[1]}:'.`);
    }
    path = cleaned.slice(deviceMatch[0].length);
  }

  const segments = normalizeIso9660Path(path);
  if (segments.length === 0) throw new Error("PS2 boot path does not name an ISO-9660 file.");
  return segments.join("/");
}

export function executableNameFromBootPath(bootPath: string): string {
  const cleaned = bootPath.trim().replace(/^['"]|['"]$/g, "");
  const leaf = cleaned.split(/[\\/]/).at(-1) ?? cleaned;
  return leaf.replace(/;[0-9]+$/i, "");
}

/** Converts PS2 executable names such as SCUS_971.99 to canonical disc serials such as SCUS-97199. */
export function parsePs2ExecutableSerial(executableName: string): string | undefined {
  const match = /(?:^|[^A-Z0-9])([A-Z]{4})[_-]([0-9]{3})\.([0-9]{2})(?:$|[^A-Z0-9])/i.exec(executableName);
  if (!match?.[1] || !match[2] || !match[3]) return undefined;
  return `${match[1].toUpperCase()}-${match[2]}${match[3]}`;
}

function validatePs2BootElfHeader(header: Elf32Header): void {
  if (header.endian !== "little") throw new Error(`PS2 boot executable must be little-endian ELF32, got ${header.endian}-endian.`);
  if (header.type !== ELF_TYPE_EXECUTABLE) throw new Error(`PS2 boot executable ELF type ${header.type} is not ET_EXEC (${ELF_TYPE_EXECUTABLE}).`);
  if (header.machine !== ELF_MACHINE_MIPS) throw new Error(`PS2 boot executable machine ${header.machine} is not MIPS (${ELF_MACHINE_MIPS}).`);
}

function ascii(bytes: Uint8Array): string {
  let output = "";
  for (const byte of bytes) output += String.fromCharCode(byte);
  return output;
}
