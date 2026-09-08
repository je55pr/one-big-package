export const BLOCK = 2048;

function writeBoth16(bytes, offset, value) { const view = new DataView(bytes.buffer); view.setUint16(offset, value, true); view.setUint16(offset + 2, value, false); }
function writeBoth32(bytes, offset, value) { const view = new DataView(bytes.buffer); view.setUint32(offset, value, true); view.setUint32(offset + 4, value, false); }
function writeAscii(bytes, offset, length, value) { bytes.fill(0x20, offset, offset + length); for (let i = 0; i < Math.min(length, value.length); i++) bytes[offset + i] = value.charCodeAt(i); }
function writeDirectoryRecord(bytes, offset, { identifier, extentLba, dataLength, directory = false }) {
  const id = typeof identifier === "number" ? Uint8Array.of(identifier) : Uint8Array.from(identifier, (char) => char.charCodeAt(0));
  const length = 33 + id.length + (id.length % 2 === 0 ? 1 : 0);
  bytes[offset] = length; bytes[offset + 1] = 0; writeBoth32(bytes, offset + 2, extentLba); writeBoth32(bytes, offset + 10, dataLength);
  bytes[offset + 25] = directory ? 0x02 : 0; writeBoth16(bytes, offset + 28, 1); bytes[offset + 32] = id.length; bytes.set(id, offset + 33); return length;
}

function syntheticPs2Elf({ machine = 8, type = 2 } = {}) {
  const bytes = new Uint8Array(256);
  bytes.set([0x7f, 0x45, 0x4c, 0x46, 1, 1, 1, 0, 0], 0);
  const view = new DataView(bytes.buffer);
  view.setUint16(16, type, true);
  view.setUint16(18, machine, true);
  view.setUint32(20, 1, true);
  view.setUint32(24, 0x00100008, true);
  view.setUint32(28, 52, true);
  view.setUint32(32, 0, true);
  view.setUint32(36, 0x20924001, true);
  view.setUint16(40, 52, true);
  view.setUint16(42, 32, true);
  view.setUint16(44, 1, true);
  view.setUint16(46, 40, true);
  view.setUint16(48, 0, true);
  view.setUint16(50, 0, true);
  view.setUint32(52 + 0, 1, true);
  view.setUint32(52 + 4, 128, true);
  view.setUint32(52 + 8, 0x00100000, true);
  view.setUint32(52 + 12, 0x00100000, true);
  view.setUint32(52 + 16, 16, true);
  view.setUint32(52 + 20, 32, true);
  view.setUint32(52 + 24, 5, true);
  view.setUint32(52 + 28, 16, true);
  for (let index = 0; index < 16; index++) bytes[128 + index] = (index * 13 + 7) & 0xff;
  return bytes;
}

export function syntheticPs2Fixture(serialExecutable, options = {}) {
  const bootPath = options.bootPath ?? `cdrom0:\\${serialExecutable};1`;
  const configText = `BOOT2 = ${bootPath}\r\nVER = 1.00\r\nVMODE = NTSC\r\n`;
  const configBytes = Uint8Array.from(configText, (char) => char.charCodeAt(0));
  const executableBytes = syntheticPs2Elf({ machine: options.elfMachine, type: options.elfType });
  const bytes = new Uint8Array(24 * BLOCK);
  const pvd = 16 * BLOCK;
  bytes[pvd] = 1; writeAscii(bytes, pvd + 1, 5, "CD001"); bytes[pvd + 6] = 1; writeAscii(bytes, pvd + 40, 32, "RATCHET TEST");
  writeBoth32(bytes, pvd + 80, 24); writeBoth16(bytes, pvd + 128, BLOCK);
  writeDirectoryRecord(bytes, pvd + 156, { identifier: 0, extentLba: 20, dataLength: BLOCK, directory: true });
  const terminator = 17 * BLOCK; bytes[terminator] = 255; writeAscii(bytes, terminator + 1, 5, "CD001"); bytes[terminator + 6] = 1;
  let root = 20 * BLOCK;
  root += writeDirectoryRecord(bytes, root, { identifier: 0, extentLba: 20, dataLength: BLOCK, directory: true });
  root += writeDirectoryRecord(bytes, root, { identifier: 1, extentLba: 20, dataLength: BLOCK, directory: true });
  root += writeDirectoryRecord(bytes, root, { identifier: "SYSTEM.CNF;1", extentLba: 21, dataLength: configBytes.length });
  if (options.includeBootExecutable !== false) {
    root += writeDirectoryRecord(bytes, root, { identifier: `${serialExecutable};1`, extentLba: 22, dataLength: executableBytes.length });
    bytes.set(executableBytes, 22 * BLOCK);
  }
  bytes.set(configBytes, 21 * BLOCK);
  return { bytes, configLength: configBytes.length, executableLength: executableBytes.length };
}

export function syntheticPs2Iso(serialExecutable, options = {}) {
  return syntheticPs2Fixture(serialExecutable, options).bytes;
}
