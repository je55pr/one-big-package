#!/usr/bin/env python3
"""Build a local original-code oracle and compare the Cannon's managed geometry.

Requires the authorized original UYA ISO, the MIT native-port interpreter at the
revision in THIRD_PARTY_NOTICES, and a C++20 compiler. Original bytes and numeric
traces stay under ignored captures/. No PCSX2, window automation or level solver.
"""
import argparse
import hashlib
import importlib.util
import os
from pathlib import Path
import struct
import subprocess

ROOT = Path(__file__).resolve().parents[1]


def module(name, path):
    spec = importlib.util.spec_from_file_location(name, ROOT / path)
    result = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(result)
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--iso', type=Path, required=True)
    parser.add_argument('--reference-root', type=Path, required=True)
    parser.add_argument('--cxx', type=Path, required=True)
    parser.add_argument('--work-dir', type=Path, default=ROOT / 'captures/uya-shock-cannon-oracle')
    args = parser.parse_args()
    work = args.work_dir.resolve()
    if not work.is_relative_to((ROOT / 'captures').resolve()):
        parser.error('Original code and traces must stay in ignored captures/')
    work.mkdir(parents=True, exist_ok=True)
    overlay_reader = module('uya_cannon_overlay', 'tools/rac1-level-overlay-probe.py')
    wad = module('uya_cannon_wad', 'tools/rac1-ui-assets.py')
    with args.iso.open('rb') as stream:
        length = args.iso.stat().st_size

        def read(at, size):
            if at < 0 or size < 0 or at + size > length:
                raise ValueError('UYA extraction range escapes the disc')
            stream.seek(at)
            result = stream.read(size)
            if len(result) != size:
                raise ValueError('Short disc read')
            return result

        def u32(blob, at=0):
            return struct.unpack_from('<I', blob, at)[0]

        packed = read(1463 * 2048, 771008)
        if hashlib.sha256(packed).hexdigest() != '200068cb4186715521cdc1c20bc6c5a88054d0da54aa336ffdb2022633351c7d':
            raise ValueError('Unsupported UYA boot program')
        boot = wad.decode_wad(packed[0x7080:])
        row = read(1001 * 2048 + 0x7400 + 24, 24)
        header = read(u32(row, 8) * 2048, 0x60)
        level = u32(header, 4) * 2048
        outer = read(level, 0x60)
        primary = level + u32(outer, 0x10) * 2048
        data = read(primary, 8)
        code = read(primary + u32(data), u32(data, 4))
    if hashlib.sha256(code).hexdigest() != '093fb8641cb2bf48073793f6cfac726cb7321654d285b848658f3e957df1dcec':
        raise ValueError('Unsupported TABLE1 overlay')
    ram = bytearray(32 * 1024 * 1024)
    at = 0
    while at < len(boot):
        address, size, kind, _ = struct.unpack_from('<4I', boot, at)
        at += 16
        if at + size > len(boot) or address + size > len(ram):
            raise ValueError('Unsupported decoded boot section')
        if kind in (1, 8):
            ram[address:address + size] = boot[at:at + size]
        at += size
    for section in overlay_reader.Overlay(code).sections:
        address, size, offset = section['address'], section['size'], section['dataOffset']
        ram[address:address + size] = code[offset:offset + size]
    # UYA math MPG is at a different boot address from the R&C1 interpreter's
    # defaults. The harness explicitly uploads this original VIF0 chain.
    if u32(ram, 0x10f1ac) != 0x4a600190:
        raise ValueError('Unsupported UYA VU0 math upload')
    image = work / 'ee.bin'
    image.write_bytes(ram)
    program = work / 'cannon-oracle.exe'
    reference = args.reference_root.resolve()
    revision = subprocess.check_output(['git', '-C', str(reference), 'rev-parse', 'HEAD'], text=True).strip()
    if revision != '3fd14c12e4dff2fd4b6661a6e54187faea3f76c8':
        raise ValueError('Use the MIT interpreter revision pinned in THIRD_PARTY_NOTICES')
    compiler = [str(args.cxx.resolve())] + (['c++'] if args.cxx.stem.lower() == 'zig' else [])
    command = compiler + ['-std=c++20', '-O2', '-ffp-contract=off', '-Wno-nullability-completeness', '-I', str(reference),
                          str(ROOT / 'tools/uya-shock-cannon-native-oracle.cpp'),
                          str(reference / 'runtime/ee/ee_interpreter.cpp'), str(reference / 'runtime/vu/vu_interpreter.cpp'),
                          '-o', str(program)]
    subprocess.run(command, check=True)
    environment = dict(os.environ, OBP_UYA_CANNON_ORACLE=str(program), OBP_UYA_CANNON_RAM=str(image))
    subprocess.run(['dotnet', 'test', str(ROOT / 'tests/OBP.Tests'), '-c', 'Release', '--nologo',
                    '--filter', 'FullyQualifiedName~UyaShockCannonNativeOracleTests'], env=environment, check=True)


if __name__ == '__main__':
    main()
