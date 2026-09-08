import test from "node:test";
import assert from "node:assert/strict";
import {
  openPs2Disc,
  parsePs2ExecutableSerial,
  ps2BootPathToIso9660Path,
  readPs2BootInfo,
  readPs2BootProgramHeaders,
} from "../.build/packages/ps2-disc/src/index.js";
import { BLOCK, syntheticPs2Fixture } from "./helpers/synthetic-ps2.mjs";

class RecordingReader {
  constructor(name, bytes) { this.name = name; this.bytes = bytes; this.size = bytes.length; this.reads = []; }
  async read(offset, length) { this.reads.push([offset, length]); return this.bytes.slice(offset, offset + length); }
}

test("PS2 boot probe identifies serial and validates a little-endian ELF32 MIPS boot target through bounded reads", async () => {
  const fixture = syntheticPs2Fixture("SCUS_971.99");
  const reader = new RecordingReader("rac1.iso", fixture.bytes);
  const info = await readPs2BootInfo(reader);
  assert.equal(info.volumeIdentifier, "RATCHET TEST");
  assert.equal(info.bootKey, "BOOT2");
  assert.equal(info.executableIsoPath, "SCUS_971.99;1");
  assert.equal(info.executableName, "SCUS_971.99");
  assert.equal(info.executableSize, fixture.executableLength);
  assert.equal(info.executableEntryPoint, 0x00100008);
  assert.equal(info.executableProgramHeaderCount, 1);
  assert.equal(info.serial, "SCUS-97199");
  assert.equal(info.config.VMODE, "NTSC");
  assert.deepEqual(reader.reads, [
    [16 * BLOCK, BLOCK],
    [20 * BLOCK, BLOCK],
    [21 * BLOCK, fixture.configLength],
    [20 * BLOCK, BLOCK],
    [22 * BLOCK, 52],
  ]);
});

test("openPs2Disc exposes the boot ELF extent and program headers without reading segment payloads", async () => {
  const fixture = syntheticPs2Fixture("SCUS_971.99");
  const reader = new RecordingReader("rac1.iso", fixture.bytes);
  const disc = await openPs2Disc(reader);
  assert.equal(disc.bootExecutable.size, fixture.executableLength);
  assert.equal(disc.bootExecutableHeader.machine, 8);
  const programHeaders = await readPs2BootProgramHeaders(disc);
  assert.equal(programHeaders.length, 1);
  assert.deepEqual(
    [programHeaders[0].offset, programHeaders[0].fileSize, programHeaders[0].memorySize],
    [128, 16, 32],
  );
  assert.deepEqual(reader.reads.at(-1), [22 * BLOCK + 52, 32]);
  assert.equal(reader.reads.some(([offset]) => offset === 22 * BLOCK + 128), false);
});

test("PS2 boot probe rejects missing targets and non-MIPS boot executables", async () => {
  const missing = syntheticPs2Fixture("SCUS_971.99", { includeBootExecutable: false });
  await assert.rejects(() => readPs2BootInfo(new RecordingReader("truncated.iso", missing.bytes)), /boot executable .* was not found/i);

  const wrongMachine = syntheticPs2Fixture("SCUS_971.99", { elfMachine: 3 });
  await assert.rejects(() => readPs2BootInfo(new RecordingReader("wrong-machine.iso", wrongMachine.bytes)), /machine 3 is not MIPS/);

  const wrongType = syntheticPs2Fixture("SCUS_971.99", { elfType: 3 });
  await assert.rejects(() => readPs2BootInfo(new RecordingReader("wrong-type.iso", wrongType.bytes)), /not ET_EXEC/);
});

test("PS2 cdrom0 boot paths normalize to ISO-9660 paths and reject unsupported devices", () => {
  assert.equal(ps2BootPathToIso9660Path("cdrom0:\\SCUS_972.68;1"), "SCUS_972.68;1");
  assert.equal(ps2BootPathToIso9660Path("CDROM0:/BOOT/SCUS_973.53;1"), "BOOT/SCUS_973.53;1");
  assert.throws(() => ps2BootPathToIso9660Path("host0:\\SCUS_971.99"), /Unsupported PS2 boot device/);
  assert.throws(() => ps2BootPathToIso9660Path("cdrom0:\\..\\SCUS_971.99"), /path traversal/i);
});

test("Ratchet trilogy NTSC-U executable names normalize to authority serials", () => {
  assert.equal(parsePs2ExecutableSerial("SCUS_971.99"), "SCUS-97199");
  assert.equal(parsePs2ExecutableSerial("SCUS_972.68"), "SCUS-97268");
  assert.equal(parsePs2ExecutableSerial("SCUS_973.53"), "SCUS-97353");
  assert.equal(parsePs2ExecutableSerial("not-a-ps2-serial"), undefined);
});
