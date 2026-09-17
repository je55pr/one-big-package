#!/usr/bin/env python3
import argparse
import ctypes
import hashlib
import json
import struct
import zipfile
from collections import defaultdict
from pathlib import Path

from capstone import Cs, CS_ARCH_MIPS, CS_MODE_LITTLE_ENDIAN, CS_MODE_MIPS32

FLOATS = {
    "ground_error_gain": 0.00800000038,
    "ground_velocity_damping": 0.150000006,
    "ground_maximum_step": 0.165806278,
    "air_error_gain": 0.04,
    "air_velocity_damping": 0.20,
    "air_maximum_step": 0.250163853,
    "snap_step_fraction": 0.01,
}


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def signed16(value):
    return value - 0x10000 if value & 0x8000 else value


def read_zip_entry(path, name, zstd_dll):
    with zipfile.ZipFile(path) as archive:
        info = archive.getinfo(name)
        if info.compress_type != 93:
            return archive.read(name)
    with path.open("rb") as stream:
        stream.seek(info.header_offset)
        header = stream.read(30)
        if header[:4] != b"PK\x03\x04":
            raise RuntimeError("invalid local ZIP header")
        name_len, extra_len = struct.unpack_from("<HH", header, 26)
        stream.seek(name_len + extra_len, 1)
        compressed = stream.read(info.compress_size)
    lib = ctypes.CDLL(str(zstd_dll))
    lib.ZSTD_decompress.restype = ctypes.c_size_t
    lib.ZSTD_decompress.argtypes = [ctypes.c_void_p, ctypes.c_size_t, ctypes.c_void_p, ctypes.c_size_t]
    lib.ZSTD_isError.restype = ctypes.c_uint
    lib.ZSTD_isError.argtypes = [ctypes.c_size_t]
    source = ctypes.create_string_buffer(compressed)
    target = ctypes.create_string_buffer(info.file_size)
    result = lib.ZSTD_decompress(target, info.file_size, source, len(compressed))
    if lib.ZSTD_isError(result):
        raise RuntimeError(f"zstd decompression failed with code {result}")
    return target.raw[:result]


def scan_float_addresses(memory):
    found = defaultdict(list)
    for label, value in FLOATS.items():
        needle = struct.pack("<f", value)
        start = 0
        while True:
            index = memory.find(needle, start)
            if index < 0:
                break
            if index % 4 == 0:
                found[label].append(index)
            start = index + 1
    return found


def scan_lui_lwc1_refs(memory, wanted, start=0x00080000, end=0x01000000):
    refs = []
    stop = min(end, len(memory))
    for pc in range(start, stop - 28, 4):
        word = struct.unpack_from("<I", memory, pc)[0]
        if word >> 26 != 0x0F:
            continue
        base_reg = (word >> 16) & 31
        upper = (word & 0xFFFF) << 16
        for delta in range(4, 28, 4):
            candidate = struct.unpack_from("<I", memory, pc + delta)[0]
            if candidate >> 26 != 0x31 or ((candidate >> 21) & 31) != base_reg:
                continue
            address = (upper + signed16(candidate & 0xFFFF)) & 0xFFFFFFFF
            if address in wanted:
                refs.append((pc, pc + delta, address))
    return refs


def disassembly_windows(memory, refs, radius=10):
    md = Cs(CS_ARCH_MIPS, CS_MODE_MIPS32 | CS_MODE_LITTLE_ENDIAN)
    rows = {}
    for lui_pc, load_pc, _ in refs:
        key = f"0x{load_pc:08x}"
        start = max(0, lui_pc - radius * 4)
        end = min(len(memory), load_pc + (radius + 1) * 4)
        rows[key] = [
            {
                "address": f"0x{insn.address:08x}",
                "mnemonic": insn.mnemonic,
                "operands": insn.op_str,
            }
            for insn in md.disasm(memory[start:end], start)
        ]
    return rows


def build_report(savestate_path, zstd_dll):
    raw = savestate_path.read_bytes()
    memory = read_zip_entry(savestate_path, "eeMemory.bin", zstd_dll)
    float_addresses = scan_float_addresses(memory)
    wanted = {address for values in float_addresses.values() for address in values}
    refs = scan_lui_lwc1_refs(memory, wanted)
    labels_by_address = defaultdict(list)
    for label, addresses in float_addresses.items():
        for address in addresses:
            labels_by_address[address].append(label)
    return {
        "schema": 1,
        "authority": {
            "game": "Ratchet & Clank",
            "build": "rac1-ntscu-original",
            "serial": "SCUS-97199",
        },
        "literalSource": "exact binary32 constants currently used by Rac1RatchetYawController",
        "savestateSha256": sha256(raw),
        "eeMemorySha256": sha256(memory),
        "eeMemoryBytes": len(memory),
        "floatAddresses": {
            label: [f"0x{address:08x}" for address in float_addresses.get(label, [])]
            for label in FLOATS
        },
        "absoluteFloatLoadSites": [
            {
                "luiPc": f"0x{lui_pc:08x}",
                "loadPc": f"0x{load_pc:08x}",
                "floatAddress": f"0x{address:08x}",
                "labels": labels_by_address[address],
            }
            for lui_pc, load_pc, address in refs
        ],
        "notes": [
            "The savestate and eeMemory payload remain user-local and are never emitted.",
            "eeMemory.bin offsets are EE virtual addresses for ordinary RAM.",
            "Literal matches and absolute load sites are candidates only; gameplay semantics still require dataflow or controlled live traces.",
        ],
    }


def main():
    parser = argparse.ArgumentParser(description="Probe loaded R&C1 EE memory for movement constants")
    parser.add_argument("--savestate", required=True, type=Path)
    parser.add_argument("--zstd-dll", required=True, type=Path)
    parser.add_argument("--out", type=Path)
    args = parser.parse_args()
    report = build_report(args.savestate, args.zstd_dll)
    rendered = json.dumps(report, indent=2) + "\n"
    if args.out:
        args.out.parent.mkdir(parents=True, exist_ok=True)
        args.out.write_text(rendered, encoding="utf-8")
    else:
        print(rendered, end="")


if __name__ == "__main__":
    main()
