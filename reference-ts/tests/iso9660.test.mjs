import test from "node:test";
import assert from "node:assert/strict";
import { Iso9660ExtentReader, Iso9660Filesystem, normalizeIso9660Path, readDirectoryEntries, readPrimaryVolumeDescriptor } from "../.build/packages/iso9660/src/index.js";

const BLOCK = 2048;

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

async function collect(iterable) {
  const values = [];
  for await (const value of iterable) values.push(value);
  return values;
}

function writeBoth16(bytes, offset, value) {
  const view = new DataView(bytes.buffer);
  view.setUint16(offset, value, true);
  view.setUint16(offset + 2, value, false);
}
function writeBoth32(bytes, offset, value) {
  const view = new DataView(bytes.buffer);
  view.setUint32(offset, value, true);
  view.setUint32(offset + 4, value, false);
}
function writeAscii(bytes, offset, length, value) {
  bytes.fill(0x20, offset, offset + length);
  for (let i = 0; i < Math.min(length, value.length); i++) bytes[offset + i] = value.charCodeAt(i);
}
function writeDirectoryRecord(bytes, offset, { identifier, extentLba, dataLength, directory = false }) {
  const id = typeof identifier === "number" ? Uint8Array.of(identifier) : Uint8Array.from(identifier, (char) => char.charCodeAt(0));
  const length = 33 + id.length + (id.length % 2 === 0 ? 1 : 0);
  bytes[offset] = length;
  bytes[offset + 1] = 0;
  writeBoth32(bytes, offset + 2, extentLba);
  writeBoth32(bytes, offset + 10, dataLength);
  bytes[offset + 25] = directory ? 0x02 : 0;
  bytes[offset + 26] = 0;
  bytes[offset + 27] = 0;
  writeBoth16(bytes, offset + 28, 1);
  bytes[offset + 32] = id.length;
  bytes.set(id, offset + 33);
  return length;
}
function syntheticIso() {
  const bytes = new Uint8Array(26 * BLOCK);
  const pvd = 16 * BLOCK;
  bytes[pvd] = 1;
  writeAscii(bytes, pvd + 1, 5, "CD001");
  bytes[pvd + 6] = 1;
  writeAscii(bytes, pvd + 8, 32, "OBP TEST SYSTEM");
  writeAscii(bytes, pvd + 40, 32, "OBP SYNTHETIC");
  writeBoth32(bytes, pvd + 80, 26);
  writeBoth16(bytes, pvd + 128, BLOCK);
  writeDirectoryRecord(bytes, pvd + 156, { identifier: 0, extentLba: 20, dataLength: BLOCK, directory: true });

  const terminator = 17 * BLOCK;
  bytes[terminator] = 255;
  writeAscii(bytes, terminator + 1, 5, "CD001");
  bytes[terminator + 6] = 1;

  let root = 20 * BLOCK;
  root += writeDirectoryRecord(bytes, root, { identifier: 0, extentLba: 20, dataLength: BLOCK, directory: true });
  root += writeDirectoryRecord(bytes, root, { identifier: 1, extentLba: 20, dataLength: BLOCK, directory: true });
  root += writeDirectoryRecord(bytes, root, { identifier: "SYSTEM.CNF;1", extentLba: 21, dataLength: 16 });
  root += writeDirectoryRecord(bytes, root, { identifier: "SCUS_971.99;1", extentLba: 22, dataLength: 8 });
  root += writeDirectoryRecord(bytes, root, { identifier: "DATA", extentLba: 23, dataLength: BLOCK, directory: true });
  bytes.set(Uint8Array.from("BOOT2 = cdrom0:\\SCUS", (char) => char.charCodeAt(0)).subarray(0, 16), 21 * BLOCK);

  let data = 23 * BLOCK;
  data += writeDirectoryRecord(bytes, data, { identifier: 0, extentLba: 23, dataLength: BLOCK, directory: true });
  data += writeDirectoryRecord(bytes, data, { identifier: 1, extentLba: 20, dataLength: BLOCK, directory: true });
  data += writeDirectoryRecord(bytes, data, { identifier: "PLANET.WAD;1", extentLba: 24, dataLength: 6 });
  bytes.set(Uint8Array.from("planet", (char) => char.charCodeAt(0)), 24 * BLOCK);
  return bytes;
}

test("ISO-9660 probing and directory listing stay range-limited", async () => {
  const reader = new RecordingReader("synthetic.iso", syntheticIso());
  const volume = await readPrimaryVolumeDescriptor(reader);
  assert.equal(volume.descriptorLba, 16);
  assert.equal(volume.volumeIdentifier, "OBP SYNTHETIC");
  assert.equal(volume.logicalBlockSize, BLOCK);
  assert.equal(volume.volumeSpaceSize, 26);
  assert.equal(volume.rootDirectory.extentLba, 20);
  assert.deepEqual(reader.reads, [[16 * BLOCK, BLOCK]]);

  const entries = await readDirectoryEntries(reader, volume.rootDirectory, volume.logicalBlockSize);
  assert.deepEqual(entries.map((entry) => entry.name), ["SYSTEM.CNF", "SCUS_971.99", "DATA"]);
  assert.equal(entries[0].version, 1);
  assert.deepEqual(reader.reads, [[16 * BLOCK, BLOCK], [20 * BLOCK, BLOCK]]);

  const systemCnf = new Iso9660ExtentReader(reader, entries[0], volume.logicalBlockSize);
  assert.equal(systemCnf.size, 16);
  assert.equal(new TextDecoder().decode(await systemCnf.read(0, 5)), "BOOT2");
  assert.deepEqual(reader.reads.at(-1), [21 * BLOCK, 5]);
});

test("ISO-9660 filesystem resolves nested paths with bounded directory and file reads", async () => {
  const reader = new RecordingReader("synthetic.iso", syntheticIso());
  const filesystem = await Iso9660Filesystem.open(reader);
  const file = await filesystem.openFile("data\\planet.wad");
  assert.ok(file);
  assert.equal(file.name, "data/planet.wad");
  assert.equal(file.size, 6);
  assert.equal(new TextDecoder().decode(await file.read(1, 3)), "lan");
  assert.deepEqual(reader.reads, [
    [16 * BLOCK, BLOCK],
    [20 * BLOCK, BLOCK],
    [23 * BLOCK, BLOCK],
    [24 * BLOCK + 1, 3],
  ]);
});

test("ISO-9660 filesystem walks directory metadata incrementally without reading file contents", async () => {
  const reader = new RecordingReader("synthetic.iso", syntheticIso());
  const filesystem = await Iso9660Filesystem.open(reader);
  const entries = await collect(filesystem.walk());
  assert.deepEqual(entries.map((entry) => [entry.path, entry.depth, entry.isDirectory]), [
    ["/SYSTEM.CNF", 1, false],
    ["/SCUS_971.99", 1, false],
    ["/DATA", 1, true],
    ["/DATA/PLANET.WAD", 2, false],
  ]);
  assert.deepEqual(reader.reads, [
    [16 * BLOCK, BLOCK],
    [20 * BLOCK, BLOCK],
    [23 * BLOCK, BLOCK],
  ]);
});

test("ISO-9660 walk depth and entry limits bound discovery work", async () => {
  const shallowReader = new RecordingReader("synthetic.iso", syntheticIso());
  const shallow = await Iso9660Filesystem.open(shallowReader);
  const direct = await collect(shallow.walk("/", { maxDepth: 1, includeDirectories: false }));
  assert.deepEqual(direct.map((entry) => entry.path), ["/SYSTEM.CNF", "/SCUS_971.99"]);
  assert.deepEqual(shallowReader.reads, [[16 * BLOCK, BLOCK], [20 * BLOCK, BLOCK]]);

  const capped = await Iso9660Filesystem.open(new RecordingReader("synthetic.iso", syntheticIso()));
  await assert.rejects(() => collect(capped.walk("/", { maxEntries: 2 })), /exceeded maxEntries 2/);
});

test("ISO-9660 path lookup accepts explicit file versions and rejects traversal", async () => {
  const filesystem = await Iso9660Filesystem.open(new RecordingReader("synthetic.iso", syntheticIso()));
  const file = await filesystem.openFile("/DATA/PLANET.WAD;1");
  assert.ok(file);
  assert.deepEqual(normalizeIso9660Path("//DATA//PLANET.WAD"), ["DATA", "PLANET.WAD"]);
  assert.throws(() => normalizeIso9660Path("DATA/../SYSTEM.CNF"), /traversal/);
});

test("ISO-9660 parser rejects disagreeing endian metadata", async () => {
  const bytes = syntheticIso();
  new DataView(bytes.buffer).setUint32(16 * BLOCK + 84, 25, false);
  await assert.rejects(readPrimaryVolumeDescriptor(new RecordingReader("bad.iso", bytes)), /endian copies disagree/);
});
