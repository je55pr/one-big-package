#!/usr/bin/env python3
"""Build the original-instruction skeleton oracle and run the retail comparisons.

Requires OBP_RAC1_ISO, OBP_GC_ISO and OBP_UYA_ISO. The tests extract their
bounded inputs locally under ignored captures/ and remove them on completion.
No original code, animation channels, meshes or matrices are distributed.
"""
import argparse
import os
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parents[1]
REVISION = '3fd14c12e4dff2fd4b6661a6e54187faea3f76c8'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--reference-root', type=Path, required=True)
    parser.add_argument('--cxx', type=Path, required=True)
    args = parser.parse_args()
    for name in ('OBP_RAC1_ISO', 'OBP_GC_ISO', 'OBP_UYA_ISO'):
        if not os.environ.get(name) or not Path(os.environ[name]).is_file():
            parser.error(f'{name} must name the supported authorized source')
    reference = args.reference_root.resolve()
    revision = subprocess.check_output(['git', '-C', str(reference), 'rev-parse', 'HEAD'], text=True).strip()
    if revision != REVISION:
        parser.error('Use the MIT interpreter revision pinned in THIRD_PARTY_NOTICES.md')
    work = ROOT / 'captures/trilogy-skeleton-oracle'
    work.mkdir(parents=True, exist_ok=True)
    program = work / ('skeleton-oracle.exe' if os.name == 'nt' else 'skeleton-oracle')
    compiler = [str(args.cxx.resolve())] + (['c++'] if args.cxx.stem.lower() == 'zig' else [])
    subprocess.run(compiler + ['-std=c++20', '-O2', '-ffp-contract=off', '-Wno-nullability-completeness',
                              '-I', str(reference), str(ROOT / 'tools/trilogy-skeleton-native-oracle.cpp'),
                              str(reference / 'runtime/ee/ee_interpreter.cpp'), str(reference / 'runtime/vu/vu_interpreter.cpp'),
                              '-o', str(program)], check=True)
    environment = dict(os.environ, OBP_SKELETON_ORACLE=str(program))
    subprocess.run(['dotnet', 'test', str(ROOT / 'tests/OBP.Tests'), '-c', 'Release', '--nologo',
                    '--filter', 'FullyQualifiedName~TrilogySkeletalRetailTests'], env=environment, check=True)


if __name__ == '__main__':
    main()
