import fs from 'node:fs';
import crypto from 'node:crypto';
import path from 'node:path';


const EXPECTED_ISO_SHA256 = 'ab849fe7cc9cc81c487d61b0d3ea15b5849943481b6a6ebf4d9aa9cf7bc40d9d';
const EXPECTED_EXE_SHA256 = 'e050581032e4bb3f20341307da5b69b76f1574910519155380ea771e55c3c0c9';
const args = process.argv.slice(2);
const isoPath = path.resolve(args.shift() ?? 'C:\\ChatGPT\\ISOs\\Ratchet & Clank (USA) (En,Fr,De,Es,It).iso');
let outPath = path.resolve('research/generated/rac1-veldin-spawn/executable-evidence.json');
let eeMemoryPath = null;
while (args.length > 0) {
  const arg = args.shift();
  if (arg === '--out') outPath = path.resolve(args.shift() ?? '');
  else if (arg === '--ee-memory') {
    const value = args.shift();
    if (!value) throw new Error('--ee-memory requires a path or - for stdin');
    eeMemoryPath = value === '-' ? '-' : path.resolve(value);
  }
  else throw new Error(`unknown argument ${arg}`);
}

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
  [0x20c538, 0x26100100, 'boot live-Moby scan advances s0 by 0x100'],
  [0x20c550, 0x26100100, 'second boot live-Moby scan branch advances s0 by 0x100'],
  [0x20c5f4, 0x24060100, 'boot live-Moby initializer supplies size 0x100'],
  [0x20c60c, 0x0c07e5fa, 'boot initializer calls the retail clear/copy helper at 0x1f97e8'],
];
for (const [address, expected] of signatures) {
  const actual = elfWord(exe, address);
  if (actual !== expected) throw new Error(`signature drift at 0x${address.toString(16)}: 0x${actual.toString(16)}`);
}

// These words are from the retail Veldin loaded EE image, not the boot ELF: the
// level overlay replaces this population path after load. Supplying --ee-memory
// makes the probe re-verify the read-only 32 MiB EE capture without committing it.
const overlaySignatures = [
  [0x242828, 0x8ca20044, 'population reads gameplay Moby block pointer at gameplay +0x44'],
  [0x242838, 0x8e300000, 'population reads authored Moby count from the block header'],
  [0x242878, 0x8e220000, 'population reads the authored record declared size'],
  [0x24288c, 0x02221021, 'population computes next record as current + declared size'],
  [0x242a54, 0x8fa90018, 'population loads the emitted live-Moby index'],
  [0x242a5c, 0x00091a00, 'population multiplies live-Moby index by 0x100'],
  [0x242a68, 0x8c42ffd8, 'population loads the live-Moby pool base'],
  [0x242a6c, 0x8e250000, 'population reads authored oClass at record +0x18'],
  [0x242a7c, 0x0c093a4c, 'population calls the loaded live-Moby initializer at 0x24e930'],
  [0x24e934, 0x24060100, 'loaded initializer supplies live record size 0x100'],
  [0x24e9b8, 0xa63000a6, 'loaded initializer writes authored class to live +0xa6'],
  [0x242aac, 0xc6210000, 'population reads authored scale at record +0x1c'],
  [0x242ab4, 0xe640002c, 'population writes authored scale to live +0x2c'],
  [0x242ad4, 0xc6200000, 'population reads authored position X at record +0x30'],
  [0x242ad8, 0xe6400010, 'population writes position X to live +0x10'],
  [0x242adc, 0xc6210004, 'population reads authored position Y at record +0x34'],
  [0x242ae0, 0xe6410014, 'population writes position Y to live +0x14'],
  [0x242ae4, 0xc6220008, 'population reads authored position Z at record +0x38'],
  [0x242aec, 0xe6420018, 'population writes position Z to live +0x18'],
  [0x242af0, 0xc6200000, 'population reads authored rotation X at record +0x3c'],
  [0x242af4, 0xe6400040, 'population writes rotation X to live +0x40'],
  [0x242af8, 0xc6210004, 'population reads authored rotation Y at record +0x40'],
  [0x242afc, 0xe6410044, 'population writes rotation Y to live +0x44'],
  [0x242b00, 0xc6200008, 'population reads authored rotation Z at record +0x44'],
  [0x242b08, 0xe6400048, 'population writes rotation Z to live +0x48'],
  [0x242c48, 0x8fb1001c, 'population advances the authored cursor to the computed record end'],
];
let overlayVerifiedFromInput = false;
if (eeMemoryPath) {
  if (eeMemoryPath !== '-' && !fs.existsSync(eeMemoryPath)) throw new Error(`EE memory capture not found: ${eeMemoryPath}`);
  const ee = eeMemoryPath === '-' ? fs.readFileSync(0) : fs.readFileSync(eeMemoryPath);
  if (ee.length !== 32 * 1024 * 1024) throw new Error(`expected 32 MiB EE memory, got ${ee.length}`);
  for (const [address, expected] of overlaySignatures) {
    const actual = ee.readUInt32LE(address);
    if (actual !== expected) throw new Error(`loaded-overlay signature drift at 0x${address.toString(16)}: 0x${actual.toString(16)}`);
  }
  overlayVerifiedFromInput = true;
}

const report = {
  schema: 2,
  authority: { build: 'rac1-ntscu-original', serial: 'SCUS-97199', isoSha256, executableSha256: exeSha256 },
  executableEvidence: signatures.map(([address, word, meaning]) => ({
    address: `0x${address.toString(16)}`,
    word: `0x${word.toString(16).padStart(8, '0')}`,
    meaning,
  })),
  loadedVeldinOverlayEvidence: {
    source: 'read-only PCSX2 EE-memory witness from untouched retail Veldin gameplay',
    verifiedFromInput: overlayVerifiedFromInput,
    signatures: overlaySignatures.map(([address, word, meaning]) => ({
      address: `0x${address.toString(16)}`,
      word: `0x${word.toString(16).padStart(8, '0')}`,
      meaning,
    })),
    mapping: {
      authoredRecordSize: '0x78',
      liveRecordSize: '0x100',
      oClass: 'authored +0x18 -> live +0xa6',
      scale: 'authored +0x1c -> live +0x2c',
      position: 'authored +0x30..+0x38 -> live +0x10..+0x18',
      rotation: 'authored +0x3c..+0x44 -> live +0x40..+0x48',
    },
  },
  conclusion: 'The loaded retail Veldin population path consumes each declared-size authored Moby record, derives a 0x100-strided live slot from its index, initializes it with the authored class, and copies scale/position/rotation directly. This closes the authored class-0 placement -> live Ratchet start-transform bridge.',
};
fs.mkdirSync(path.dirname(outPath), { recursive: true });
fs.writeFileSync(outPath, `${JSON.stringify(report, null, 2)}\n`);
console.log(`wrote ${outPath}`);
