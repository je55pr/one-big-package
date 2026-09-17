#!/usr/bin/env python3
import argparse
import hashlib
import json
import struct
from collections import defaultdict
from pathlib import Path

from capstone import Cs, CS_ARCH_MIPS, CS_MODE_LITTLE_ENDIAN, CS_MODE_MIPS32

ISO_SHA256 = "ab849fe7cc9cc81c487d61b0d3ea15b5849943481b6a6ebf4d9aa9cf7bc40d9d"
EXE_SHA256 = "e050581032e4bb3f20341307da5b69b76f1574910519155380ea771e55c3c0c9"
ISO_SECTOR_SIZE = 2048
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


def read_root_iso_file(path, wanted_name):
    with path.open("rb") as f:
        f.seek(16 * ISO_SECTOR_SIZE)
        pvd = f.read(ISO_SECTOR_SIZE)
        if pvd[0] != 1 or pvd[1:6] != b"CD001":
            raise RuntimeError("ISO primary volume descriptor not found")
        root_len = pvd[156]
        root = pvd[156:156 + root_len]
        extent = struct.unpack_from("<I", root, 2)[0]
        length = struct.unpack_from("<I", root, 10)[0]
        f.seek(extent * ISO_SECTOR_SIZE)
        directory = f.read(length)
        offset = 0
        while offset < len(directory):
            record_len = directory[offset]
            if record_len == 0:
                offset = ((offset + ISO_SECTOR_SIZE) // ISO_SECTOR_SIZE) * ISO_SECTOR_SIZE
                continue
            name_len = directory[offset + 32]
            name = directory[offset + 33:offset + 33 + name_len].decode("ascii")
            normalized = name.split(";", 1)[0]
            if normalized.upper() == wanted_name.upper():
                file_extent = struct.unpack_from("<I", directory, offset + 2)[0]
                file_len = struct.unpack_from("<I", directory, offset + 10)[0]
                f.seek(file_extent * ISO_SECTOR_SIZE)
                return f.read(file_len)
            offset += record_len
    raise RuntimeError(f"root ISO file not found: {wanted_name}")


def load_segments(exe):
    if exe[:4] != b"\x7fELF":
        raise RuntimeError("SCUS_971.99 is not ELF")
    phoff = struct.unpack_from("<I", exe, 28)[0]
    phentsize = struct.unpack_from("<H", exe, 42)[0]
    phnum = struct.unpack_from("<H", exe, 44)[0]
    segments = []
    for i in range(phnum):
        at = phoff + i * phentsize
        if struct.unpack_from("<I", exe, at)[0] != 1:
            continue
        offset, vaddr, _paddr, filesz, _memsz, flags, _align = struct.unpack_from("<7I", exe, at + 4)
        segments.append({"offset": offset, "vaddr": vaddr, "size": filesz, "flags": flags})
    return segments


def locate_float_addresses(exe, segments):
    found = defaultdict(list)
    for label, value in FLOATS.items():
        needle = struct.pack("<f", value)
        for seg in segments:
            blob = exe[seg["offset"]:seg["offset"] + seg["size"]]
            start = 0
            while True:
                index = blob.find(needle, start)
                if index < 0:
                    break
                found[label].append(seg["vaddr"] + index)
                start = index + 1
    return found


def signed16(value):
    return value - 0x10000 if value & 0x8000 else value


def code_words(exe, code):
    blob = exe[code["offset"]:code["offset"] + code["size"]]
    for offset in range(0, len(blob) - 3, 4):
        yield code["vaddr"] + offset, struct.unpack_from("<I", blob, offset)[0]


def read_reginfo_gp(exe):
    shoff = struct.unpack_from("<I", exe, 32)[0]
    shentsize = struct.unpack_from("<H", exe, 46)[0]
    shnum = struct.unpack_from("<H", exe, 48)[0]
    for i in range(shnum):
        at = shoff + i * shentsize
        section_type = struct.unpack_from("<I", exe, at + 4)[0]
        if section_type != 0x70000006:  # SHT_MIPS_REGINFO
            continue
        offset = struct.unpack_from("<I", exe, at + 16)[0]
        return struct.unpack_from("<I", exe, offset + 20)[0]
    raise RuntimeError("MIPS .reginfo section not found")


def gp_float_refs(exe, code, float_addresses, gp):
    wanted = {addr for addresses in float_addresses.values() for addr in addresses}
    refs = []
    for pc, word in code_words(exe, code):
        if word >> 26 != 0x31 or ((word >> 21) & 31) != 28:
            continue
        ft = (word >> 16) & 31
        imm = signed16(word & 0xFFFF)
        address = (gp + imm) & 0xFFFFFFFF
        if address in wanted:
            refs.append((pc, ft, imm, address))
    return refs


def disassembly_windows(exe, code, pcs, radius=8):
    blob = exe[code["offset"]:code["offset"] + code["size"]]
    md = Cs(CS_ARCH_MIPS, CS_MODE_MIPS32 | CS_MODE_LITTLE_ENDIAN)
    rows = {}
    for pc in sorted(set(pcs)):
        start_pc = max(code["vaddr"], pc - radius * 4)
        end_pc = min(code["vaddr"] + code["size"], pc + (radius + 1) * 4)
        start = start_pc - code["vaddr"]
        chunk = blob[start:end_pc - code["vaddr"]]
        rows[f"0x{pc:08x}"] = [
            {
                "address": f"0x{insn.address:08x}",
                "mnemonic": insn.mnemonic,
                "operands": insn.op_str,
            }
            for insn in md.disasm(chunk, start_pc)
        ]
    return rows


def build_report(iso_path):
    iso = iso_path.read_bytes()
    if sha256(iso) != ISO_SHA256:
        raise RuntimeError(f"unexpected ISO sha256 {sha256(iso)}")
    exe = read_root_iso_file(iso_path, "SCUS_971.99")
    if sha256(exe) != EXE_SHA256:
        raise RuntimeError(f"unexpected executable sha256 {sha256(exe)}")
    segments = load_segments(exe)
    code = next((seg for seg in segments if seg["flags"] & 1), None)
    if code is None:
        raise RuntimeError("no executable PT_LOAD code segment")
    float_addresses = locate_float_addresses(exe, segments)
    gp = read_reginfo_gp(exe)
    refs = gp_float_refs(exe, code, float_addresses, gp)
    by_addr = {addr: label for label, addresses in float_addresses.items() for addr in addresses}
    xrefs = [
        {
            "pc": f"0x{pc:08x}",
            "label": by_addr[address],
            "floatAddress": f"0x{address:08x}",
            "fpuRegister": ft,
            "gpOffset": imm,
        }
        for pc, ft, imm, address in refs
    ]
    return {
        "schema": 1,
        "authority": {
            "game": "Ratchet & Clank",
            "build": "rac1-ntscu-original",
            "serial": "SCUS-97199",
            "isoSha256": ISO_SHA256,
            "executableSha256": EXE_SHA256,
        },
        "literalSource": "exact binary32 constants currently used by Rac1RatchetYawController",
        "floatAddresses": {
            label: [f"0x{address:08x}" for address in float_addresses.get(label, [])]
            for label in FLOATS
        },
        "reginfoGp": f"0x{gp:08x}",
        "floatLoadSites": xrefs,
        "notes": [
            "This probe is static executable archaeology only; load sites establish literal use, not gameplay semantics by themselves.",
            "A recurrence is admitted only when surrounding dataflow and/or controlled live witnesses identify the player movement path.",
        ],
    }


def main():
    parser = argparse.ArgumentParser(description="Probe R&C1 NTSC-U movement/yaw constants in the retail ELF")
    parser.add_argument("--iso", required=True, type=Path)
    parser.add_argument("--out", type=Path)
    args = parser.parse_args()
    report = build_report(args.iso)
    rendered = json.dumps(report, indent=2) + "\n"
    if args.out:
        args.out.parent.mkdir(parents=True, exist_ok=True)
        args.out.write_text(rendered, encoding="utf-8")
    else:
        print(rendered, end="")


if __name__ == "__main__":
    main()
