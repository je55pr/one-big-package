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

INPUT_STATE = 0x0013C940
PLAYER_GLOBAL = 0x0013F350
PLAYER_STATE = 0x0013F3D0
CONTROL_HEADING = 0x00166DD8
HIGH_MAGNITUDE_THRESHOLD = 0x0017BDC4

# Small instruction signatures only. They prove the active loaded overlay/dataflow
# without retaining any executable or savestate payload.
ANALOGUE_SIGNATURES = {
    0x00267274: 0x2445FF81,  # raw byte - 127
    0x00267284: 0x28A30030,  # abs delta < 48
    0x00267294: 0x24A4FFD0,  # abs delta - 48
    0x002672A0: 0x2404004C,  # divisor 76
    0x002672CC: 0x2C42007F,  # restore sign from raw < 127
    0x002116AC: 0xC6010108,  # conditioned left X
    0x002116B0: 0xC600010C,  # conditioned left Y
    0x002116C0: 0x3C013E80,  # exact 0.25f activation threshold
    0x00211B5C: 0xC44D6DD8,  # separate control-heading field
    0x00211B60: 0x0C08003A,  # wrapped-angle add helper
    0x0021C1D8: 0xC443229C,  # capped magnitude sample
    0x0021C254: 0xC440BDC4,  # exact 0.82f high-magnitude facing threshold
}

HEADING_WITNESSES = {
    "07677959a3b7215a89b42745dffe55bb4d4709bce01d1aab31e6514c032a436d": -2.073779821,
    "067e5ca260233fcccc958f54e412e3fad26f38406a2a7d2f13d8f6477e92a0aa": 2.842167616,
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


def f32_at(memory, address):
    return struct.unpack_from("<f", memory, address)[0]


def verify_analogue_signatures(memory):
    mismatches = []
    for address, expected in ANALOGUE_SIGNATURES.items():
        actual = struct.unpack_from("<I", memory, address)[0]
        if actual != expected:
            mismatches.append({
                "address": f"0x{address:08x}",
                "expected": f"0x{expected:08x}",
                "actual": f"0x{actual:08x}",
            })
    if mismatches:
        raise RuntimeError(f"loaded analogue dataflow signatures changed: {mismatches}")


def analogue_report(memory, state_sha256):
    verify_analogue_signatures(memory)
    heading = f32_at(memory, CONTROL_HEADING)
    expected_heading = HEADING_WITNESSES.get(state_sha256)
    heading_delta = None if expected_heading is None else heading - expected_heading
    high_magnitude_threshold = f32_at(memory, HIGH_MAGNITUDE_THRESHOLD)
    return {
        "loadedOverlaySignaturesVerified": len(ANALOGUE_SIGNATURES),
        "inputStateBase": f"0x{INPUT_STATE:08x}",
        "conditionedAxes": {
            "rightX": "I+0x100",
            "rightY": "I+0x104",
            "leftX": "I+0x108",
            "leftY": "I+0x10c",
            "formula": "sign(raw-127) * clamp((abs(raw-127)-48)/76, 0, 1)",
            "routine": "0x00267270..0x002672e0",
        },
        "playerGlobalBase": f"0x{PLAYER_GLOBAL:08x}",
        "playerStateBase": f"0x{PLAYER_STATE:08x}",
        "magnitude": {
            "currentConditionedVector": "P+0x1d20/+0x1d24",
            "activationComparison": "length(currentConditionedVector) < 0.25 falls back to digital/zero input",
            "activationThresholdNormalized": 0.25,
            "activationThresholdRemappedCounts": 19.0,
            "cappedMagnitudeSample": "P+0x229c",
            "cappedMagnitudeRoutine": "0x00211830..0x00211868",
            "highMagnitudeFacingComparison": "0.82 < P+0x229c selects the high-magnitude facing-controller branch at 0x0021c254",
            "highMagnitudeThresholdNormalized": high_magnitude_threshold,
            "highMagnitudeThresholdRemappedCounts": high_magnitude_threshold * 76.0,
            "firstIntegerCardinalAboveHighMagnitudeThreshold": 63,
            "pipelineNote": "P+0x229c is computed before the current conditioned pair is copied into P+0x1d20/+0x1d24, so it is a cached prior-vector magnitude within this update path.",
            "boundaryNote": "The 0.82 branch is proven to select facing-controller coefficients. Its numerical agreement with the live 62/63 speed-band bracket does not by itself prove that it is the translational run-speed selector.",
        },
        "heading": {
            "controlHeadingAddress": f"0x{CONTROL_HEADING:08x}",
            "controlHeadingValue": heading,
            "independentLiveHeading": expected_heading,
            "loadedMinusLiveHeading": heading_delta,
            "targetYaw": "G+0x100",
            "construction": "ordinary mode computes stick term atan2(-conditionedX, -conditionedY), then WrapPi(stickTerm + controlHeading)",
            "equivalentLiveConvention": "G+0x100 = WrapPi(controlHeading - stickAngle)",
            "angleRoutine": "0x001ff8b0",
            "wrapAddRoutine": "0x002000e8",
            "targetRoutine": "0x002118c8..0x00211be4",
        },
        "unresolved": [
            "The state machine that produces 0x00166dd8, including camera chase/recenter/right-stick policy, is not established here.",
            "Special target-construction modes inside 0x002118c8 are not promoted as ordinary locomotion semantics.",
            "The exact walk-speed plateau remains a live witness; this report establishes input conditioning and magnitude branch thresholds, not every downstream speed constant.",
        ],
    }


def build_report(savestate_path, zstd_dll):
    raw = savestate_path.read_bytes()
    memory = read_zip_entry(savestate_path, "eeMemory.bin", zstd_dll)
    state_sha256 = sha256(raw)
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
        "savestateSha256": state_sha256,
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
        "analogueDataflow": analogue_report(memory, state_sha256),
        "notes": [
            "The savestate and eeMemory payload remain user-local and are never emitted.",
            "eeMemory.bin offsets are EE virtual addresses for ordinary RAM.",
            "Literal matches and absolute load sites are candidates only; gameplay semantics still require dataflow or controlled live traces.",
        ],
    }


def comparison_report(paths, zstd_dll):
    rows = []
    for path in paths:
        raw = path.read_bytes()
        state_sha256 = sha256(raw)
        memory = read_zip_entry(path, "eeMemory.bin", zstd_dll)
        dataflow = analogue_report(memory, state_sha256)
        rows.append({
            "savestateSha256": state_sha256,
            "controlHeadingValue": dataflow["heading"]["controlHeadingValue"],
            "independentLiveHeading": dataflow["heading"]["independentLiveHeading"],
            "loadedMinusLiveHeading": dataflow["heading"]["loadedMinusLiveHeading"],
            "highMagnitudeThresholdNormalized": dataflow["magnitude"]["highMagnitudeThresholdNormalized"],
        })
    return rows


def main():
    parser = argparse.ArgumentParser(description="Probe loaded R&C1 EE memory for movement constants")
    parser.add_argument("--savestate", required=True, type=Path)
    parser.add_argument("--compare-savestate", action="append", default=[], type=Path)
    parser.add_argument("--zstd-dll", required=True, type=Path)
    parser.add_argument("--out", type=Path)
    args = parser.parse_args()
    report = build_report(args.savestate, args.zstd_dll)
    if args.compare_savestate:
        report["analogueDataflowComparison"] = comparison_report(
            [args.savestate, *args.compare_savestate], args.zstd_dll,
        )
    rendered = json.dumps(report, indent=2) + "\n"
    if args.out:
        args.out.parent.mkdir(parents=True, exist_ok=True)
        args.out.write_text(rendered, encoding="utf-8")
    else:
        print(rendered, end="")


if __name__ == "__main__":
    main()
