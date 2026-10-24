import test from 'node:test';
import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import os from 'node:os';
import { execFileSync } from 'node:child_process';
import { inspectUids, main } from './sync-godot-uids.mjs';

function fixture(fn) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), 'obp-uid-test-'));
  fs.mkdirSync(path.join(root, 'game/scripts'), { recursive: true });
  fs.mkdirSync(path.join(root, 'game/diagnostics'), { recursive: true });
  execFileSync('git', ['init', '-q', root]);
  try { fn(root); } finally { fs.rmSync(root, { force: true, recursive: true }); }
}
function expectFailure(fn, message) {
  const previous = console.error, messages = [];
  console.error = (...parts) => messages.push(parts.join(' '));
  try { assert.equal(fn(), 1); }
  finally { console.error = previous; }
  assert.match(messages.join('\n'), message);
}
function add(root, relative, content) {
  const absolute = path.join(root, relative);
  fs.mkdirSync(path.dirname(absolute), { recursive: true });
  fs.writeFileSync(absolute, content);
  return absolute;
}

test('accepts unique valid IDs and preserves byte-for-byte values on no-op sync', () => fixture(root => {
  const script = add(root, 'game/scripts/A.cs', 'public class A {}');
  const uid = add(root, 'game/scripts/A.cs.uid', 'uid://cafefeed\n\n');
  add(root, 'game/diagnostics/B.cs', 'public class B {}');
  add(root, 'game/diagnostics/B.cs.uid', 'uid://abcdef123\n');
  execFileSync('git', ['-c', 'core.autocrlf=false', '-C', root, 'add', 'game']);
  const before = fs.readFileSync(uid);
  assert.deepEqual(inspectUids(root).errors, []);
  assert.equal(main(['--check'], root), 0);
  assert.equal(main(['--sync'], root), 0);
  assert.deepEqual(fs.readFileSync(uid), before);
  assert.ok(fs.existsSync(script));
}));

test('missing sidecars fail check with guidance', () => fixture(root => {
  add(root, 'game/scripts/New.cs', 'public class New {}');
  assert.equal(inspectUids(root).missing.length, 1);
  expectFailure(() => main(['--check'], root), /Missing UID game\/scripts\/New.cs.uid/);
}));

test('sidecar needs to be tracked in Git', () => fixture(root => {
  add(root, 'game/scripts/A.cs', 'public class A {}');
  add(root, 'game/scripts/A.cs.uid', 'uid://abc123\n');
  expectFailure(() => main(['--check'], root), /Untracked UID game\/scripts\/A.cs.uid/);
  execFileSync('git', ['-c', 'core.autocrlf=false', '-C', root, 'add', 'game']);
  assert.equal(main(['--check'], root), 0);
}));

test('detects malformed, duplicate, and orphaned IDs with exact paths', () => fixture(root => {
  add(root, 'game/scripts/A.cs', 'public class A {}');
  add(root, 'game/scripts/A.cs.uid', 'uid://abc');
  add(root, 'game/diagnostics/B.cs', 'public class B {}');
  add(root, 'game/diagnostics/B.cs.uid', 'uid://abc');
  add(root, 'game/scripts/C.cs', 'public class C {}');
  add(root, 'game/scripts/C.cs.uid', 'UID://BAD');
  add(root, 'game/diagnostics/Gone.cs.uid', 'uid://lost');
  const report = inspectUids(root).errors.join('\n');
  assert.match(report, /Duplicate UID.*B\.cs\.uid.*A\.cs\.uid/s);
  assert.match(report, /Malformed UID.*C\.cs\.uid/);
  assert.match(report, /Orphan UID.*Gone\.cs\.uid/);
  expectFailure(() => main(['--sync'], root), /Orphan UID.*Duplicate UID.*Malformed UID/s);
}));

test('moving a script and its sidecar retains the exact identity', () => fixture(root => {
  const oldSource = add(root, 'game/scripts/Old.cs', 'public class Old {}');
  const oldUid = add(root, 'game/scripts/Old.cs.uid', 'uid://permanent123\n');
  const before = fs.readFileSync(oldUid);
  fs.renameSync(oldSource, path.join(root, 'game/scripts/New.cs'));
  fs.renameSync(oldUid, path.join(root, 'game/scripts/New.cs.uid'));
  assert.deepEqual(inspectUids(root).errors, []);
  assert.deepEqual(inspectUids(root).missing, []);
  assert.deepEqual(fs.readFileSync(path.join(root, 'game/scripts/New.cs.uid')), before);
}));

test('detects scripts in nested folders without generating any custom IDs', () => fixture(root => {
  add(root, 'game/scripts/sub/New.cs', 'public class New {}');
  const scanned = inspectUids(root);
  assert.equal(scanned.sources.length, 1);
  assert.equal(scanned.missing.length, 1);
  assert.equal(scanned.errors.length, 0);
}));
