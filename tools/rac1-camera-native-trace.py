#!/usr/bin/env python3
"""Replay the original NTSC-U camera functions in the native-port's R5900 interpreter.

Requires a local MIT native-port checkout and C++20 compiler (Zig's c++ driver is
supported). Private EE memory/binaries stay under captures/. The output contains
only derived numeric observations, input plans and code hashes, never retail bytes.
Collision helpers return clear space; this proves the upright follow pipeline,
not authored camera volumes or retail collision internals.
"""
import argparse
import hashlib
import importlib.util
import json
import math
import struct
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


def module(name, path):
    spec = importlib.util.spec_from_file_location(name, ROOT / path)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


def plans():
    result = []
    for axis in range(2):
        for sign in [-1, 1]:
            for inverted in [False, True]:
                options = [1, 1, 1]
                options[axis] = 0 if inverted else 1
                stick = [0, 0]
                stick[axis] = sign
                result.append(dict(name=f"{'horizontal' if axis == 0 else 'vertical'}-{'negative' if sign < 0 else 'positive'}-{'inverted' if inverted else 'normal'}",
                                   options=options, segments=[dict(frames=60, move=[0, 0, 0], stick=stick), dict(frames=180, move=[0, 0, 0], stick=[0, 0])]))
    for speed in [0, 2]:
        result.append(dict(name=f"horizontal-speed-{speed}", options=[1, 1, speed],
                           segments=[dict(frames=36, move=[0, 0, 0], stick=[1, 0]), dict(frames=100, move=[0, 0, 0], stick=[0, 0])]))
    result.append(dict(name="partial-stick-diagonal", options=[1, 1, 1],
                       segments=[dict(frames=48, move=[0, 0, 0], stick=[.65, -.72]), dict(frames=180, move=[0, 0, 0], stick=[0, 0])]))
    for name, segments in [
        ("sideways-follow", [(60, [0, .1, 0]), (90, [0, 0, 0])]),
        ("forward-back-follow", [(60, [.1, 0, 0]), (60, [-.1, 0, 0]), (90, [0, 0, 0])]),
        ("jump-follow", [(20, [0, 0, .12]), (20, [0, 0, -.12]), (120, [0, 0, 0])]),
    ]:
        result.append(dict(name=name, options=[1, 1, 1], segments=[dict(frames=n, move=m, stick=[0, 0]) for n, m in segments]))
    return result


def condition(raw):
    return math.copysign(min(1.0, max(0.0, abs(raw) * 128 - 48) / 76), raw)


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--iso', type=Path, required=True)
    ap.add_argument('--reference-root', type=Path, required=True)
    ap.add_argument('--cxx', type=Path, required=True)
    ap.add_argument('--work-dir', type=Path, default=ROOT / 'captures/camera-native-oracle')
    ap.add_argument('--out', type=Path, required=True)
    args = ap.parse_args()
    if not args.work_dir.resolve().is_relative_to((ROOT / 'captures').resolve()):
        ap.error('Private work directory must stay under ignored captures/')
    args.work_dir.mkdir(parents=True, exist_ok=True)
    overlay = module('camera_overlay', 'tools/rac1-level-overlay-probe.py')
    boot = module('camera_boot', 'tools/rac1-movement-probe.py')
    exe = boot.read_root_iso_file(args.iso, 'SCUS_971.99')
    if hashlib.sha256(exe).hexdigest() != boot.EXE_SHA256:
        raise ValueError('Unsupported boot program')
    with args.iso.open('rb') as stream:
        code = overlay.read_veldin_overlay(stream, args.iso.stat().st_size)
    if hashlib.sha256(code).hexdigest() != overlay.OVERLAY_SHA256:
        raise ValueError('Unsupported Veldin overlay')
    loaded = overlay.Overlay(code)
    ram = bytearray(32 * 1024 * 1024)
    for segment in boot.load_segments(exe):
        ram[segment['vaddr']:segment['vaddr'] + segment['size']] = exe[segment['offset']:segment['offset'] + segment['size']]
    for segment in loaded.sections:
        ram[segment['address']:segment['address'] + segment['size']] = code[segment['dataOffset']:segment['dataOffset'] + segment['size']]
    image = args.work_dir / 'ee.bin'
    image.write_bytes(ram)
    program = args.work_dir / 'camera-oracle.exe'
    driver = [str(args.cxx.resolve())]
    if args.cxx.stem.lower() == 'zig':
        driver += ['c++']
    command = driver + ['-std=c++20', '-O2', '-ffp-contract=off', '-Wno-nullability-completeness',
                        '-I', str(args.reference_root.resolve()), str(ROOT / 'tools/rac1-camera-native-oracle.cpp'),
                        str(args.reference_root / 'runtime/ee/ee_interpreter.cpp'),
                        str(args.reference_root / 'runtime/vu/vu_interpreter.cpp'), '-o', str(program)]
    with (args.work_dir / 'build.log').open('w') as log:
        subprocess.run(command, stdout=log, stderr=log, check=True)
    cases = plans()
    for case in cases:
        rows = []
        for segment in case['segments']:
            x, up = map(condition, segment['stick'])
            rows += [' '.join(map(str, segment['move'] + [x, -up] + case['options']))] * segment['frames']
        output = subprocess.run([str(program), str(image)], input='\n'.join(rows) + '\n', text=True,
                                capture_output=True, check=True, timeout=30)
        values = [list(map(float, line.split())) for line in output.stdout.splitlines()]
        if len(values) != len(rows) or any(not all(map(math.isfinite, row)) for row in values):
            raise ValueError('Incomplete or non-finite native trace')
        checkpoints = {1, 6, 12, 24, 36, 48, 60, 72, 90, 120, 180, len(rows)}
        case['samples'] = [dict(frame=i + 1, eye=row[:3], forward=row[3:6], manual=row[6:8], desiredOffset=row[8:11])
                           for i, row in enumerate(values) if i + 1 in checkpoints]
    ranges = [('manualSelector', 0x2e89b0, 0x2e8bb8), ('turn', 0x2e8bb8, 0x2e8f40),
              ('follow', 0x2e8f40, 0x2e9010), ('aim', 0x2e9010, 0x2e9450),
              ('position', 0x2e9518, 0x2e9aa0), ('update', 0x2e9c28, 0x2e9cb8),
              ('projection', 0x1f7848, 0x1f7b00)]
    report = dict(schema=1, authority='SCUS-97199 original NTSC-U', overlaySha256=overlay.OVERLAY_SHA256,
                  execution='Original MIPS camera_track and cam0_update in R5900 interpreter; clear collision helpers',
                  initialState='Original cam0_init, profile radius 6 (retained ordinary Veldin witness), 300 neutral updates',
                  options='Native menu values: horizontal/vertical 1=Normal, 0=Reversed; speed 0/1/2=Slow/Medium/Fast',
                  code=[dict(stage=name, start=hex(start), end=hex(end), sha256=hashlib.sha256(loaded.read(start, end-start)).hexdigest()) for name, start, end in ranges],
                  limits=['Synthetic upright player motion; no authored camera modifiers or collision-volume execution.',
                          'OBP uses double precision; the oracle uses PS2 float rounding and original math approximations.'],
                  cases=cases)
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print(f"Original camera replay: {len(cases)} scenarios, {sum(len(c['samples']) for c in cases)} checkpoints")


if __name__ == '__main__':
    main()
