import fs from 'node:fs';
import crypto from 'node:crypto';
import path from 'node:path';

const EXPECTED_ISO_SHA256 = 'ab849fe7cc9cc81c487d61b0d3ea15b5849943481b6a6ebf4d9aa9cf7bc40d9d';
const EXPECTED_EXE_SHA256 = 'e050581032e4bb3f20341307da5b69b76f1574910519155380ea771e55c3c0c9';
const SELECTOR_HELPERS = new Set([0x212ed8, 0x212f90, 0x2130d8]);
const ISO_SECTOR_SIZE = 2048;

function arg(name, fallback = null) {
  const i = process.argv.indexOf(name);
  return i >= 0 && i + 1 < process.argv.length ? process.argv[i + 1] : fallback;
}
async function sha256(file) {
  const hash = crypto.createHash('sha256');
  for await (const chunk of fs.createReadStream(file)) hash.update(chunk);
  return hash.digest('hex');
}
function bufferSha256(buffer) {
  return crypto.createHash('sha256').update(buffer).digest('hex');
}
function hex(value) { return `0x${value.toString(16)}`; }
function signed16(value) { return value & 0x8000 ? value - 0x10000 : value; }
function readAt(fd, position, length) {
  const buffer = Buffer.alloc(length);
  const bytes = fs.readSync(fd, buffer, 0, length, position);
  if (bytes !== length) throw new Error(`short read at ${position}: ${bytes}/${length}`);
  return buffer;
}
function readRootIsoFile(isoPath, wantedName) {
  const fd = fs.openSync(isoPath, 'r');
  try {
    const pvd = readAt(fd, 16 * ISO_SECTOR_SIZE, ISO_SECTOR_SIZE);
    if (pvd[0] !== 1 || pvd.toString('ascii', 1, 6) !== 'CD001') {
      throw new Error('ISO primary volume descriptor not found at sector 16');
    }
    const rootLength = pvd[156];
    const root = pvd.subarray(156, 156 + rootLength);
    const extent = root.readUInt32LE(2);
    const dataLength = root.readUInt32LE(10);
    const directory = readAt(fd, extent * ISO_SECTOR_SIZE, dataLength);
    for (let offset = 0; offset < directory.length;) {
      const recordLength = directory[offset];
      if (recordLength === 0) {
        offset = Math.ceil((offset + 1) / ISO_SECTOR_SIZE) * ISO_SECTOR_SIZE;
        continue;
      }
      const nameLength = directory[offset + 32];
      const name = directory.toString('ascii', offset + 33, offset + 33 + nameLength);
      const normalized = name.replace(/;\d+$/, '');
      if (normalized.toUpperCase() === wantedName.toUpperCase()) {
        const fileExtent = directory.readUInt32LE(offset + 2);
        const fileLength = directory.readUInt32LE(offset + 10);
        return readAt(fd, fileExtent * ISO_SECTOR_SIZE, fileLength);
      }
      offset += recordLength;
    }
    throw new Error(`root ISO file not found: ${wantedName}`);
  } finally {
    fs.closeSync(fd);
  }
}

const isoPath = arg('--iso', 'C:\\ChatGPT\\ISOs\\Ratchet & Clank (USA) (En,Fr,De,Es,It).iso');
const outputPath = arg('--out');
const traceOutputPath = arg('--trace-out');
const traceDir = arg('--trace-dir');
if (!fs.existsSync(isoPath)) throw new Error(`ISO not found: ${isoPath}`);
const isoSha256 = await sha256(isoPath);
if (isoSha256 !== EXPECTED_ISO_SHA256) {
  throw new Error(`unexpected ISO sha256 ${isoSha256}`);
}
const exe = readRootIsoFile(isoPath, 'SCUS_971.99');
const exeSha256 = bufferSha256(exe);
if (exeSha256 !== EXPECTED_EXE_SHA256) {
  throw new Error(`unexpected executable sha256 ${exeSha256}`);
}
if (exe.readUInt32LE(0) !== 0x464c457f) throw new Error('SCUS_971.99 is not ELF');

const phoff = exe.readUInt32LE(28);
const phentsize = exe.readUInt16LE(42);
const phnum = exe.readUInt16LE(44);
const loads = [];
for (let i = 0; i < phnum; i++) {
  const at = phoff + i * phentsize;
  if (exe.readUInt32LE(at) !== 1) continue;
  loads.push({
    offset: exe.readUInt32LE(at + 4),
    vaddr: exe.readUInt32LE(at + 8),
    size: exe.readUInt32LE(at + 16),
    flags: exe.readUInt32LE(at + 24),
  });
}
const code = loads.find(x => (x.flags & 1) !== 0);
if (!code) throw new Error('no executable PT_LOAD code segment');
const words = [];
for (let off = 0; off + 4 <= code.size; off += 4) {
  words.push({ pc: code.vaddr + off, word: exe.readUInt32LE(code.offset + off) });
}
const selectorStores = [];
const helperCalls = [];
for (let i = 0; i < words.length; i++) {
  const { pc, word } = words[i];
  const opcode = word >>> 26;
  if (opcode === 0x28 && (word & 0xffff) === 0x53) {
    selectorStores.push({
      address: hex(pc),
      baseRegister: (word >>> 21) & 31,
      sourceRegister: (word >>> 16) & 31,
    });
  }
  if (opcode !== 3) continue;
  const target = (((pc + 4) & 0xf0000000) | ((word & 0x03ffffff) << 2)) >>> 0;
  if (!SELECTOR_HELPERS.has(target)) continue;
  let immediateSequence = null;
  for (let j = Math.max(0, i - 8); j < i; j++) {
    const prior = words[j].word;
    if ((prior >>> 26) === 9 && ((prior >>> 21) & 31) === 0 && ((prior >>> 16) & 31) === 5) {
      immediateSequence = signed16(prior & 0xffff);
    }
  }
  helperCalls.push({
    callSite: hex(pc),
    helper: hex(target),
    immediateA1Sequence: immediateSequence,
  });
}
const expectedStores = ['0x2048ec','0x212f40','0x213074','0x2131c4','0x22f018','0x22f098','0x230524'];
const actualStores = selectorStores.map(x => x.address);
if (JSON.stringify(actualStores) !== JSON.stringify(expectedStores)) {
  throw new Error(`selector-store census drifted: ${JSON.stringify(actualStores)}`);
}
const authority = {
  build: 'R&C1 NTSC-U SCUS-97199',
  isoSha256: EXPECTED_ISO_SHA256,
  executableSha256: EXPECTED_EXE_SHA256,
};
const clip = (state, sequenceId, status, evidence, frameCount, transitionRate, notes = null) => ({
  state, sequenceId, status, evidence, frameCount,
  rateMode: transitionRate === 0 ? 'variable' : 'constant',
  transitionRate: transitionRate || null,
  fps: transitionRate ? transitionRate * 60 : null,
  ...(notes ? { notes } : {}),
});
const admissions = [
  clip('standing', 0, 'admit', 'live+asset', 10, 0.125),
  clip('idle_fidget_a', 1, 'admit-neutral-only', 'live+asset', 77, 0),
  clip('idle_fidget_b', 2, 'admit-neutral-only', 'live+asset', 77, 0),
  clip('locomotion_start', 3, 'admit-transition', 'controlled-live+asset', 33, 0.25),
  clip('sustained_locomotion', 4, 'admit', 'controlled-live+asset', 23, 0.5),
  clip('locomotion_stop_variant_a', 5, 'admit-transition-only', 'controlled-live+asset', 13, 0.25, 'Observed after left/right stick-only locomotion release; context-specific.'),
  clip('locomotion_stop_variant_b', 6, 'admit-transition-only', 'controlled-live+asset', 13, 0.25, 'Observed after forward and moving-jump locomotion release; context-specific.'),
  clip('stationary_jump', 7, 'admit', 'controlled-live+asset', 29, 0, 'No apex/fall selector split witnessed.'),
  clip('moving_jump', 8, 'admit', 'controlled-live+asset', 16, 0, 'Returns to sequence 4 while movement remains held.'),
  clip('crouch', 13, 'admit', 'controlled-live+asset', 15, 0.25),
  clip('crouch_turn_right', 14, 'admit', 'controlled-live+asset', 7, 0.25),
  clip('crouch_turn_left', 15, 'admit', 'controlled-live+asset', 7, 0.25),
  clip('square_wrench_attack', 23, 'admit', 'controlled-live+asset', 21, 0),
  clip('bind_linear_anchor', 122, 'diagnostic-only', 'asset', 21, 0.5),
];
const unresolvedStates = ['distinct_apex_or_fall_clip', 'distinct_landing_clip', 'stop_variant_5_vs_6_selection_rule'];
const negativeControls = [{
  sequenceId: 6,
  callSite: '0x22478c',
  gate: 'oClass 0x25f at 0x224760..0x22476c',
  conclusion: 'That static call site remains invalid as Ratchet proof; sequence 6 is admitted only from later independent class-0 live traces.',
}];
function seqPath(samples) {
  const out = [];
  for (const row of samples) if (out.at(-1) !== row['53']) out.push(row['53']);
  return out;
}
function phaseTransitions(samples) {
  const out = [];
  for (const row of samples) {
    const item = `${row.phase}:${row['53']}`;
    if (out.at(-1) !== item) out.push(item);
  }
  return out;
}
const expectedTracePaths = {
  'final-forward-1.json': [2,3,4,6,0], 'final-forward-2.json': [2,3,4,6,0],
  'final-jump-1.json': [2,7,0], 'final-jump-2.json': [2,7,0],
  'final-wrench-1.json': [2,23,0], 'final-wrench-2.json': [2,23,0],
  'final-crouch-1.json': [2,13,0], 'final-crouch-2.json': [2,13,0],
  'final-crouch-left-1.json': [2,13,15,0], 'final-crouch-left-2.json': [2,13,15,0],
  'final-crouch-right-1.json': [2,13,14,0], 'final-crouch-right-2.json': [2,13,14,0],
  'final-turn-left-1.json': [2,3,4,5,0], 'final-turn-right-1.json': [2,3,4,5,0],
  'final-runjump-1.json': [2,3,4,8,4,6,0], 'final-runjump-2.json': [2,3,4,8,4,6,0],
};
function readTraceSet(dir) {
  if (!dir) return { status: 'not-reverified', trials: [] };
  const trials = [];
  for (const [name, expected] of Object.entries(expectedTracePaths)) {
    const file = path.join(dir, name);
    if (!fs.existsSync(file)) throw new Error(`missing controlled trace ${name}`);
    const raw = fs.readFileSync(file);
    const parsed = JSON.parse(raw.toString('utf8'));
    if (parsed.moby.toLowerCase() !== '0x01845e80') throw new Error(`wrong Moby target in ${name}`);
    const actual = seqPath(parsed.samples);
    if (JSON.stringify(actual) !== JSON.stringify(expected)) {
      throw new Error(`trace path drifted for ${name}: ${JSON.stringify(actual)}`);
    }
    trials.push({ file: name, sha256: bufferSha256(raw), sequencePath: actual, phaseTransitions: phaseTransitions(parsed.samples) });
  }
  return { status: 'captured-and-verified', trialCount: trials.length, trials };
}
const traceCapture = readTraceSet(traceDir);
const stateReport = {
  schema: 3, authority,
  playerSelector: { currentSequenceOffset: 'Moby+0x53', nextSequenceOffset: 'Moby+0x52' },
  playerMoby: '0x01845e80', selectorStores,
  selectorHelpers: [...SELECTOR_HELPERS].map(hex), helperCalls, negativeControls,
  admissions, unresolvedStates, liveTraceRequired: false,
  controlledTrace: { artifact: 'research/generated/rac1-ratchet-animation-trace.json', note: 'Hash-pinned reduced live evidence; use --trace-dir to reverify raw local traces.' },
  controllerOverlay: 'time-varying post-animation pass; no frozen correction admitted',
};
const action = (name, files, admission) => ({ action: name, trials: files, admission });
const traceReport = {
  schema: 2, authority,
  mode: 'controlled read-only PCSX2/PINE class-0 selector trace',
  target: { level: 'Veldin', playerClass: 0, instanceIndex: 0, moby: '0x01845e80' },
  capture: traceCapture,
  sampleFields: ['Moby+0x20','Moby+0x50','Moby+0x51','Moby+0x52','Moby+0x53','Moby+0x54','Moby+0x5c','Moby+0x74'],
  actions: [
    action('forward_locomotion', ['final-forward-1.json','final-forward-2.json'], '3=start, 4=sustained; release chose 6 in both forward trials'),
    action('stationary_jump', ['final-jump-1.json','final-jump-2.json'], '7 throughout observed airborne interval; no separate apex/fall selector'),
    action('moving_jump', ['final-runjump-1.json','final-runjump-2.json'], '8 airborne, then returns to 4 while movement remains held'),
    action('square_wrench_attack', ['final-wrench-1.json','final-wrench-2.json'], '23, then standing 0'),
    action('turning_left_right', ['final-turn-left-1.json','final-turn-right-1.json'], 'same 3->4 locomotion family; no distinct turn-in-place selector witnessed'),
    action('crouch', ['final-crouch-1.json','final-crouch-2.json'], '13'),
    action('crouch_turn_left', ['final-crouch-left-1.json'], '13->15'),
    action('crouch_turn_right', ['final-crouch-right-1.json'], '13->14'),
    action('landing', ['final-jump-1.json','final-jump-2.json','final-runjump-1.json','final-runjump-2.json'], 'no distinct landing selector witnessed; returns directly to 0 or 4'),
  ],
  neutralWitnessAlreadyEstablished: { selectorCycle: [0,2,0,1], evidence: 'prior untouched Veldin class-0 PINE witness preserved in RAC1_MOBY_SKINNING.md' },
  admissionRule: 'Only admit mappings repeatedly witnessed on class-0 Ratchet in isolated labelled trials; static helper immediates remain insufficient.',
  negativeControls,
  controllerOverlay: 'Sequence selection does not include the time-varying post-animation controller chain at Moby+0x64.',
};

function writeJson(file, value) {
  if (!file) return;
  fs.mkdirSync(path.dirname(file), { recursive: true });
  fs.writeFileSync(file, `${JSON.stringify(value, null, 2)}\n`);
}
writeJson(outputPath, stateReport);
writeJson(traceOutputPath, traceReport);
if (!outputPath && !traceOutputPath) process.stdout.write(`${JSON.stringify(stateReport, null, 2)}\n`);
