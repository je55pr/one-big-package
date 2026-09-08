import test from "node:test";
import assert from "node:assert/strict";
import {
  ELF_MACHINE_MIPS,
  ELF_TYPE_EXECUTABLE,
  mapElf32VirtualRange,
  readElf32Header,
  readElf32ProgramHeaders,
  readElf32VirtualRange,
} from "../.build/packages/elf32/src/index.js";

class RecordingReader {
  constructor(name, bytes) {
    this.name = name;
    this.bytes = bytes;
    this.size = bytes.length;
    this.reads = [];
  }
  async read(offset, length) {
    this.reads.push([offset, length]);
    return this.bytes.slice(offset, offset + length);
  }
}

function syntheticElf32({ machine = ELF_MACHINE_MIPS, count = 2 } = {}) {
  const bytes = new Uint8Array(0x300);
  bytes.set([0x7f, 0x45, 0x4c, 0x46, 1, 1, 1, 0, 0], 0);
  const view = new DataView(bytes.buffer);
  view.setUint16(16, ELF_TYPE_EXECUTABLE, true);
  view.setUint16(18, machine, true);
  view.setUint32(20, 1, true);
  view.setUint32(24, 0x00100008, true);
  view.setUint32(28, 52, true);
  view.setUint32(32, 0, true);
  view.setUint32(36, 0x20924001, true);
  view.setUint16(40, 52, true);
  view.setUint16(42, 32, true);
  view.setUint16(44, count, true);
  view.setUint16(46, 40, true);
  view.setUint16(48, 0, true);
  view.setUint16(50, 0, true);

  for (let index = 0; index < count; index++) {
    const offset = 52 + index * 32;
    view.setUint32(offset + 0, 1, true);
    view.setUint32(offset + 4, 0x100 + index * 0x40, true);
    view.setUint32(offset + 8, 0x00100000 + index * 0x1000, true);
    view.setUint32(offset + 12, 0x00100000 + index * 0x1000, true);
    view.setUint32(offset + 16, 0x20, true);
    view.setUint32(offset + 20, 0x30, true);
    view.setUint32(offset + 24, index === 0 ? 5 : 6, true);
    view.setUint32(offset + 28, 0x10, true);
    for (let byte = 0; byte < 0x20; byte++) bytes[0x100 + index * 0x40 + byte] = (index * 64 + byte + 1) & 0xff;
  }
  return bytes;
}

test("ELF32 parser reads only the fixed header and exposes PS2-relevant metadata", async () => {
  const reader = new RecordingReader("SCUS_972.68", syntheticElf32());
  const header = await readElf32Header(reader);
  assert.deepEqual(
    {
      endian: header.endian,
      type: header.type,
      machine: header.machine,
      entry: header.entry,
      flags: header.flags,
      programHeaderCount: header.programHeaderCount,
    },
    {
      endian: "little",
      type: ELF_TYPE_EXECUTABLE,
      machine: ELF_MACHINE_MIPS,
      entry: 0x00100008,
      flags: 0x20924001,
      programHeaderCount: 2,
    },
  );
  assert.deepEqual(reader.reads, [[0, 52]]);
});

test("ELF32 program headers are range-read individually without touching segment payloads", async () => {
  const reader = new RecordingReader("SCUS_972.68", syntheticElf32());
  const header = await readElf32Header(reader);
  const programHeaders = await readElf32ProgramHeaders(reader, header);
  assert.equal(programHeaders.length, 2);
  assert.deepEqual(programHeaders.map((entry) => [entry.offset, entry.fileSize, entry.memorySize, entry.flags]), [
    [0x100, 0x20, 0x30, 5],
    [0x140, 0x20, 0x30, 6],
  ]);
  assert.deepEqual(reader.reads, [[0, 52], [52, 32], [84, 32]]);
});

test("ELF32 virtual-address mapping translates only file-backed PT_LOAD ranges", async () => {
  const reader = new RecordingReader("SCUS_972.68", syntheticElf32());
  const header = await readElf32Header(reader);
  const programHeaders = await readElf32ProgramHeaders(reader, header);
  const mapping = mapElf32VirtualRange(programHeaders, 0x00100004, 8);
  assert.deepEqual(mapping, {
    segmentIndex: 0,
    virtualAddress: 0x00100004,
    fileOffset: 0x104,
    length: 8,
  });
  const bytes = await readElf32VirtualRange(reader, programHeaders, 0x00100004, 8);
  assert.deepEqual([...bytes], [5, 6, 7, 8, 9, 10, 11, 12]);
  assert.deepEqual(reader.reads.at(-1), [0x104, 8]);
});

test("ELF32 virtual mapping distinguishes BSS from completely unmapped addresses", async () => {
  const reader = new RecordingReader("SCUS_972.68", syntheticElf32());
  const header = await readElf32Header(reader);
  const programHeaders = await readElf32ProgramHeaders(reader, header);
  assert.throws(() => mapElf32VirtualRange(programHeaders, 0x00100024, 4), /not fully file-backed.*BSS/);
  assert.throws(() => mapElf32VirtualRange(programHeaders, 0x00200000, 4), /not mapped by any PT_LOAD/);
  assert.throws(() => mapElf32VirtualRange(programHeaders, 0xfffffff0, 32), /exceeds the 32-bit address space/);
});

test("ELF32 parser rejects wrong class, invalid tables and excessive program-header counts", async () => {
  const wrongClass = syntheticElf32();
  wrongClass[4] = 2;
  await assert.rejects(() => readElf32Header(new RecordingReader("elf64", wrongClass)), /expected ELF32/);

  const outside = syntheticElf32();
  new DataView(outside.buffer).setUint32(28, outside.length - 16, true);
  await assert.rejects(() => readElf32Header(new RecordingReader("outside", outside)), /program-header table lies outside/);

  const reader = new RecordingReader("many", syntheticElf32({ count: 3 }));
  const header = await readElf32Header(reader);
  await assert.rejects(() => readElf32ProgramHeaders(reader, header, { maxProgramHeaders: 2 }), /exceeds safety cap 2/);
});

test("ELF32 program-header validation rejects file segments extending past the executable extent", async () => {
  const bytes = syntheticElf32();
  const view = new DataView(bytes.buffer);
  view.setUint32(52 + 4, bytes.length - 4, true);
  view.setUint32(52 + 16, 8, true);
  const reader = new RecordingReader("bad-segment", bytes);
  const header = await readElf32Header(reader);
  await assert.rejects(() => readElf32ProgramHeaders(reader, header), /program segment 0 lies outside/);
});
