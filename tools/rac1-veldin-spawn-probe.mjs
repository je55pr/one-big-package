import fs from 'node:fs';
import crypto from 'node:crypto';
import path from 'node:path';

const EXPECTED_ISO_SHA256 = 'ab849fe7cc9cc81c487d61b0d3ea15b5849943481b6a6ebf4d9aa9cf7bc40d9d';
const EXPECTED_EXE_SHA256 = 'e050581032e4bb3f20341307da5b69b76f1574910519155380ea771e55c3c0c9';
const isoPath = path.resolve(process.argv[2] ?? 'C:\\ChatGPT\\ISOs\\Ratchet & Clank (USA) (En,Fr,De,Es,It).iso');
const outPath = path.resolve(process.argv[3] ?? 'research/generated/rac1-veldin-spawn/executable-evidence.json');

async function sha256File(file) {
  const hash = crypto.createHash('sha256');
  for await (const chunk of fs.createReadStream(file)) hash.update(chunk);
  return hash.digest('hex');
}

function readAt(fd, offset, size) {
  const out = Buffer.alloc(size);
  const count = fs.readSync(fd, out, 0, size, offset);
  if (count !== size) throw new Error(`short read at ${offset}: ${count}/${size}`);
  return out;
}

function extractRootFile(fd, pvd, wanted) {
  const rootLength = pvd[156];
  const root = pvd.subarray(156, 156 + rootLength);
  const extent = root.readUInt32LE(2);
  const size = root.readUInt32LE(10);
  const directory = readAt(fd, extent * 2048, size);
  for (let cursor = 0; cursor < directory.length;) {
    const recordLength = directory[cursor];
    if (recordLength === 0) {
      cursor = Math.ceil((cursor + 1) / 2048) * 2048;
      continue;
    }
    const nameLength = directory[cursor + 32];
    const name = directory.subarray(cursor + 33, cursor + 33 + nameLength).toString('ascii').split(';')[0];
    if (name.toUpperCase() === wanted.toUpperCase()) {
      const fileExtent = directory.readUInt32LE(cursor + 2);
      const fileSize = directory.readUInt32LE(cursor + 10);
      return readAt(fd, fileExtent * 2048, fileSize);
    }
    cursor += recordLength;
  }
  throw new Error(`${wanted} not found in ISO root`);
}

function elfWord(exe, address) {
  const phoff = exe.readUInt32LE(28);
  const phentsize = exe.readUInt16LE(42);
  const phnum = exe.readUInt16LE(44);
  for (let i = 0; i < phnum; i++) {
    const at = phoff + i * phentsize;
    if (exe.readUInt32LE(at) !== 1) continue;
    const fileOffset = exe.readUInt32LE(at + 4);
    const vaddr = exe.readUInt32LE(at + 8);
    const fileSize = exe.readUInt32LE(at + 16);
    if (address >= vaddr && address + 4 <= vaddr + fileSize)
      return exe.readUInt32LE(fileOffset + address - vaddr);
  }
  throw new Error(`ELF address 0x${address.toString(16)} is not file-backed`);
}

if (!fs.existsSync(isoPath)) throw new Error(`ISO not found: ${isoPath}`);
const isoSha256 = await sha256File(isoPath);
if (isoSha256 !== EXPECTED_ISO_SHA256) throw new Error(`unexpected ISO sha256 ${isoSha256}`);
const fd = fs.openSync(isoPath, 'r');
let exe;
try {
  const pvd = readAt(fd, 16 * 2048, 2048);
  exe = extractRootFile(fd, pvd, 'SCUS_971.99');
} finally {
  fs.closeSync(fd);
}
const exeSha256 = crypto.createHash('sha256').update(exe).digest('hex');
if (exeSha256 !== EXPECTED_EXE_SHA256) throw new Error(`unexpected executable sha256 ${exeSha256}`);

const signatures = [
  [0x20c538, 0x26100100, 'live-Moby scan advances s0 by 0x100'],
  [0x20c550, 0x26100100, 'second live-Moby scan branch advances s0 by 0x100'],
  [0x20c5f4, 0x24060100, 'live-Moby initializer supplies size 0x100'],
  [0x20c60c, 0x0c07e5fa, 'initializer calls the retail clear/copy helper at 0x1f97e8'],
];
for (const [address, expected] of signatures) {
  const actual = elfWord(exe, address);
  if (actual !== expected) throw new Error(`signature drift at 0x${address.toString(16)}: 0x${actual.toString(16)}`);
}

const report = {
  schema: 1,
  authority: { build: 'rac1-ntscu-original', serial: 'SCUS-97199', isoSha256, executableSha256: exeSha256 },
  executableEvidence: signatures.map(([address, word, meaning]) => ({
    address: `0x${address.toString(16)}`,
    word: `0x${word.toString(16).padStart(8, '0')}`,
    meaning,
  })),
  conclusion: 'Retail live Mobies use a 0x100-byte record. This probe does not claim the authored 0x78-byte class-0 placement is copied into that record; that link remains to be proven.',
};
fs.mkdirSync(path.dirname(outPath), { recursive: true });
fs.writeFileSync(outPath, `${JSON.stringify(report, null, 2)}\n`);
console.log(`wrote ${outPath}`);
