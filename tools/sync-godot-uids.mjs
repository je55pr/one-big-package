#!/usr/bin/env node
/** Sync C# script IDs using Godot's importer, not a custom UID generator. */
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import { execFileSync, spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';

const scriptPath = fileURLToPath(import.meta.url);
export const repoRoot = path.resolve(path.dirname(scriptPath), '..');
const folders = ['game/scripts', 'game/diagnostics'];
const UID_PATTERN = /^uid:\/\/[a-z0-9]+$/;

function recursiveFiles(directory, suffix) {
  if (!fs.existsSync(directory)) return [];
  return fs.readdirSync(directory, { withFileTypes: true })
    .flatMap(entry => {
      const full = path.join(directory, entry.name);
      if (entry.isDirectory()) return recursiveFiles(full, suffix);
      return entry.isFile() && entry.name.endsWith(suffix) ? [full] : [];
    });
}
function rel(root, file) {
  return path.relative(root, file).split(path.sep).join('/');
}

export function inspectUids(root) {
  const sources = folders.flatMap(folder => recursiveFiles(path.join(root, folder), '.cs')).sort();
  const sidecars = folders.flatMap(folder => recursiveFiles(path.join(root, folder), '.cs.uid')).sort();
  const errors = [], missing = [], identities = new Map(), originals = new Map();
  for (const sidecar of sidecars) {
    if (!fs.existsSync(sidecar.slice(0, -4)))
      errors.push('Orphan UID ' + rel(root, sidecar) + ': restore its .cs script or move the sidecar alongside the renamed script (git mv).');
  }
  for (const source of sources) {
    const sidecar = source + '.uid';
    if (!fs.existsSync(sidecar)) {
      missing.push(sidecar);
      continue;
    }
    const bytes = fs.readFileSync(sidecar);
    originals.set(sidecar, bytes);
    const uid = bytes.toString('utf8').trim();
    if (!UID_PATTERN.test(uid)) {
      errors.push('Malformed UID in ' + rel(root, sidecar) + ': expected uid:// followed by lowercase letters/digits. Restore the committed value or re-import deliberately.');
      continue;
    }
    const previous = identities.get(uid);
    if (previous) {
      errors.push('Duplicate UID ' + uid + ': ' + rel(root, previous) + ' and ' + rel(root, sidecar) + '. Restore the intended original UID; do not auto-regenerate existing IDs.');
    } else {
      identities.set(uid, sidecar);
    }
  }
  return { sources, missing, errors, originals };
}

function trackedSidecarErrors(root, scanned) {
  const tracked = new Set(execFileSync('git', ['-C', root, 'ls-files', '--cached', '-z', '--', ...folders], { encoding: 'utf8' }).split('\0').filter(Boolean));
  return scanned.sources
    .map(source => source + '.uid')
    .filter(sidecar => fs.existsSync(sidecar) && !tracked.has(rel(root, sidecar)))
    .map(sidecar => 'Untracked UID ' + rel(root, sidecar) + ': git add ' + rel(root, sidecar) + ' and commit it.');
}

function pinnedGodot(root) {
  const manifest = JSON.parse(fs.readFileSync(path.join(root, 'tools/godot-toolchain.json'), 'utf8'));
  const platform = os.platform() === 'win32' ? 'win64' : os.platform() === 'linux' && os.arch() === 'x64' ? 'linux_x86_64' : null;
  return {
    executable: platform ? path.join(root, '.tools', 'godot', manifest.godot.artifacts[platform].exe) : null,
    version: manifest.godot.version,
  };
}

function run(command, args, cwd) {
  const result = spawnSync(command, args, { cwd, stdio: 'inherit', shell: false });
  if (result.error) throw result.error;
  if (result.status !== 0) throw new Error(path.basename(command) + ' exited with code ' + (result.status ?? 'unknown'));
}

function help() {
  console.log('Usage: node tools/sync-godot-uids.mjs [--sync|--check] [--godot PATH]\n' +
    '  --sync (default): generate missing sidecars with the pinned Godot .NET editor.\n' +
    '  --check: validate coverage, uniqueness, shape, orphan sidecars and Git tracking without importing.\n' +
    '  --godot: override executable location (must match pinned Godot version).\n' +
    'Bootstrap Godot with tools/bootstrap-godot.ps1 or tools/bootstrap-godot.sh. Commit generated UIDs.');
}

export function main(args = process.argv.slice(2), root = repoRoot) {
  let mode = 'sync', godotOption = null;
  for (let i = 0; i < args.length; i++) {
    if (args[i] === '--check') mode = 'check';
    else if (args[i] === '--sync') mode = 'sync';
    else if (args[i] === '--godot' && i + 1 < args.length) godotOption = args[++i];
    else if (args[i] === '--help' || args[i] === '-h') { help(); return 0; }
    else throw new Error('Unknown option: ' + args[i] + '. Run --help for usage.');
  }
  const first = inspectUids(root);
  for (const issue of first.errors) console.error('ERROR: ' + issue);
  if (first.errors.length) {
    console.error('UID sync refused to modify existing IDs. Correct the reported files and rerun.');
    return 1;
  }
  if (mode === 'check') {
    const problems = [
      ...first.missing.map(file => 'Missing UID ' + rel(root, file) + ': run node tools/sync-godot-uids.mjs --sync and commit the sidecar.'),
      ...trackedSidecarErrors(root, first),
    ];
    problems.forEach(problem => console.error('ERROR: ' + problem));
    if (problems.length) return 1;
    console.log('Godot UID check OK: ' + first.sources.length + ' scripts, unique tracked sidecars, no orphans.');
    return 0;
  }
  if (first.missing.length === 0) {
    console.log('Godot UIDs already synchronized (' + first.sources.length + ' scripts). No Godot import needed.');
    return 0;
  }
  const pinned = pinnedGodot(root);
  const godot = godotOption ? path.resolve(godotOption) : pinned.executable;
  if (!godot) throw new Error('No pinned Godot binary for ' + os.platform() + '/' + os.arch() + '; use --godot PATH to a matching Godot .NET editor.');
  if (!fs.existsSync(godot))
    throw new Error('Pinned Godot .NET editor not found: ' + godot + '\nRun the appropriate tools/bootstrap-godot script first.');
  const versionResult = spawnSync(godot, ['--version'], { encoding: 'utf8' });
  const versionText = ((versionResult.stdout ?? '') + (versionResult.stderr ?? '')).trim();
  if (versionResult.error || versionResult.status !== 0 || !versionText.startsWith(pinned.version.split('-')[0]))
    throw new Error('Expected pinned Godot ' + pinned.version + '; ' + godot + ' reports ' + versionText);
  console.log('Generating ' + first.missing.length + ' missing sidecar(s) using Godot ' + pinned.version + ':');
  first.missing.forEach(file => console.log('  ' + rel(root, file)));
  // Godot needs the built C# host to load its autoload scripts.
  run('dotnet', ['build', 'game/OneBigPackage.csproj', '-c', 'Debug', '--nologo'], root);
  run(godot, ['--headless', '--path', 'game', '--editor', '--import', '--quit'], root);
  let churn = false;
  for (const [file, before] of first.originals) {
    if (!fs.existsSync(file) || !fs.readFileSync(file).equals(before)) {
      fs.writeFileSync(file, before);
      console.error('ERROR: Godot modified a valid existing UID; restored original bytes: ' + rel(root, file));
      churn = true;
    }
  }
  const after = inspectUids(root);
  after.errors.forEach(issue => console.error('ERROR: ' + issue));
  after.missing.forEach(file => console.error('ERROR: Godot did not generate ' + rel(root, file) + '. Check compilation and Godot import output.'));
  if (churn || after.errors.length || after.missing.length) return 1;
  console.log('Generated ' + first.missing.length + ' UID sidecar(s). Run --check after git add, and commit them with the .cs files.');
  return 0;
}

if (process.argv[1] && path.resolve(process.argv[1]) === scriptPath) {
  try { process.exitCode = main(); }
  catch (error) { console.error('UID sync failed: ' + error.message); process.exitCode = 1; }
}
