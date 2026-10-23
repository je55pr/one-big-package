#!/usr/bin/env python3
"""Execute bounded Novalis water witnesses using original NTSC-U instructions.

Requires an authorized local ISO, the MIT native-port interpreter and a C++20
compiler. RAM/code stay in ignored captures/. The report contains numeric facts
and hashes only. This is not an emulator playthrough or a complete query solver.
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


def module(name, relative):
    spec = importlib.util.spec_from_file_location(name, ROOT / relative)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--iso', type=Path, required=True)
    ap.add_argument('--reference-root', type=Path, required=True)
    ap.add_argument('--cxx', type=Path, required=True)
    ap.add_argument('--work-dir', type=Path, default=ROOT / 'captures/native-water')
    ap.add_argument('--out', type=Path, required=True)
    args = ap.parse_args()
    if not args.work_dir.resolve().is_relative_to((ROOT / 'captures').resolve()):
        ap.error('Private work directory must stay under ignored captures/')
    args.work_dir.mkdir(parents=True, exist_ok=True)
    overlay = module('water_overlay', 'tools/rac1-level-overlay-probe.py')
    boot = module('water_boot', 'tools/rac1-movement-probe.py')
    exe = boot.read_root_iso_file(args.iso, 'SCUS_971.99')
    if hashlib.sha256(exe).hexdigest() != boot.EXE_SHA256:
        raise ValueError('Unsupported boot program')
    with args.iso.open('rb') as stream:
        code = overlay.read_level_overlay(stream, args.iso.stat().st_size, 1)
    loaded = overlay.Overlay(code)
    # Verify independently retained water instruction/table identities before execution.
    if hashlib.sha256(loaded.read(0x2406b0, 0x238)).hexdigest() != 'e9a815b946299d4135328f6f0dd27138e7ccacb60021e8d39c9a66d0cfbec52f':
        raise ValueError('Unsupported Novalis water code')
    ram = bytearray(32 * 1024 * 1024)
    for segment in boot.load_segments(exe):
        ram[segment['vaddr']:segment['vaddr'] + segment['size']] = exe[segment['offset']:segment['offset'] + segment['size']]
    for segment in loaded.sections:
        ram[segment['address']:segment['address'] + segment['size']] = code[segment['dataOffset']:segment['dataOffset'] + segment['size']]
    image = args.work_dir / 'ee.bin'
    image.write_bytes(ram)
    program = args.work_dir / 'water-oracle.exe'
    driver = [str(args.cxx.resolve())] + (['c++'] if args.cxx.stem.lower() == 'zig' else [])
    command = driver + ['-std=c++20', '-O2', '-ffp-contract=off', '-Wno-nullability-completeness',
                        '-I', str(args.reference_root.resolve()), str(ROOT / 'tools/rac1-water-native-oracle.cpp'),
                        str(args.reference_root / 'runtime/ee/ee_interpreter.cpp'),
                        str(args.reference_root / 'runtime/vu/vu_interpreter.cpp'), '-o', str(program)]
    with (args.work_dir / 'oracle-build.log').open('w') as log:
        subprocess.run(command, stdout=log, stderr=log, check=True)

    def run(mode, rows):
        output = subprocess.run([str(program), str(image), mode], input=''.join(' '.join(map(str, row)) + '\n' for row in rows),
                                text=True, capture_output=True, check=True, timeout=30)
        values = [list(map(float, line.split())) for line in output.stdout.splitlines()]
        if len(values) != len(rows) or any(not all(map(math.isfinite, row)) for row in values):
            raise ValueError('Incomplete or non-finite native witness')
        return values

    # domain,action,apex,diving,oxygen,mask,z,surface,depth,requestZ,contactZ,pitch
    entry = [
        [4, 7, 1, 0, 10000, 0, 55.65, 56, 4, -.1, -.1, 0],
        [4, 7, 0, 0, 10000, 0, 55.65, 56, 4, -.1, -.1, 0],
        [2, 0, 1, 0, 10000, 0, 55.65, 56, .1875, -.1, -.1, 0],
        [4, 7, 1, 0, 10000, 0, 56.12, 56, 4, -.1, -.1, 0],
        [17, 51, 1, 0, 5000, 0, 55.7, 56, 4, .04, .04, -.9],
        [17, 51, 1, 1, 5000, 0, 55.7, 56, 4, .04, .04, -.9],
    ]
    oxygen = [
        [17, 51, 1, 0, 10000, 0, 53, 56, 4, 0, 0, 0],
        [17, 51, 1, 0, 10000, 1, 53, 56, 4, 0, 0, 0],
        [17, 51, 1, 0, 11, 0, 53, 56, 4, 0, 0, 0],
        [17, 51, 1, 0, 11, 0, 54.5, 56, 4, .04, .04, -.9],
        [17, 51, 1, 0, 11, 0, 54.5, 56, 4, .04, .01, -.9],
        [18, 55, 1, 0, 1, 0, 55.88, 56, 4, 0, 0, 0],
        [20, 106, 1, 0, 0, 0, 53, 56, 4, 0, 0, 0],
    ]
    # See the adapter's explicit 30-column seed schema. These are bounded
    # motion facts, not complete level/player snapshots.
    motion = [
        [55, 0, 0, 6, 4, 0, 0, 55.65, 56, 0, 0, 0, 0, 0, 0, 0, -.23, -.037, 0, 56, 0, .04, .01, 0, 0, 0, 0, 0, 0, 0],
        [54, 0, 0, 55, 18, 4, 0, 55.88, 56, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 56, 0, 0, 0, 0, 1, 0, 4, .1, 45, 0],
        [51, 1, 1, 55, 18, 4, 128, 55.8, 56, .02, .3, .2, .01, .005, .1, .01, 0, 0, 0, 56, 0, 0, 0, 0, .7, 1, 12.4, .25, 0, 0],
        [53, 20, 0, 51, 17, 18, 10, 53, 56, .06, .3, -.7, .02, -.01, .05, .01, 0, 0, 0, 56, 0, 0, 0, 0, .6, 1, 4, .25, 0, 0],
        [52, 20, 0, 53, 17, 18, 0, 53, 56, .04, 0, .2, 0, .01, 0, 0, 0, 0, 0, 56, 0, .06, .01, -.02, 0, 0, 15, .1, 0, 0],
    ]
    ranges = [('underwaterMotion', 0x2378bc, 0x23880c), ('surfaceMotion', 0x238a24, 0x238d20),
              ('oxygen', 0x2406b0, 0x2408e8), ('waterEntry', 0x2408e8, 0x240c78),
              ('waterline', 0x240c78, 0x240ed8), ('inputDispatch', 0x2435b8, 0x243c08),
              ('exitImpulse', 0x17c3e0, 0x17c440), ('strokeCurve', 0x17c440, 0x17c4b8)]
    report = dict(schema=1, authority='SCUS-97199 original NTSC-U', levelId=1,
                  overlaySha256=hashlib.sha256(code).hexdigest(),
                  execution='Original Novalis MIPS water motion, entry and oxygen functions in R5900 interpreter',
                  limits=['Conditioned input, static geometry facts and animation time are supplied by the adapter.',
                          'Wall/ceiling queries are clear; effects/audio/camera are stubbed. This is not ordinary gameplay.',
                          'OBP uses host float math/trigonometry, not bit-exact EE/VU math.'],
                  code=[dict(stage=n, start=hex(a), end=hex(e), sha256=hashlib.sha256(loaded.read(a, e-a)).hexdigest()) for n, a, e in ranges],
                  strokeCurve=list(struct.unpack('<30f', loaded.read(0x17c440, 120))))
    for mode, rows in [('entry', entry), ('oxygen', oxygen), ('motion', motion)]:
        report[mode] = [dict(input=row, output=list(map(int, output)) if mode != 'motion' else output)
                        for row, output in zip(rows, run(mode, rows))]
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(report, indent=2) + '\n', encoding='utf-8')
    print(f'Original Novalis water: {len(entry)} entry, {len(oxygen)} oxygen, {len(motion)} motion witnesses')


if __name__ == '__main__':
    main()
