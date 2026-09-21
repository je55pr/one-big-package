#!/usr/bin/env python3
"""Verify the loaded R&C1 Veldin restart path and reduce checkpoint-boundary state.

The retail savestate and EE-memory payload stay local. Output contains only
hashes, addresses, scalar state and small loaded-code signatures.
"""
from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import struct
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
MOVEMENT_PROBE_PATH = ROOT / "tools" / "rac1-savestate-movement-probe.py"

PLAYER_STATE = 0x0013F3D0
PLAYER_MOBY = 0x01845E80
NANOTECH = 0x001415F8
RESET_SNAPSHOT = 0x0013E090
RESET_GATE = 0x00160540
RESET_CONTEXT = 0x0013E030
CURRENT_LEVEL = 0x0015ED84
LEVEL13_CLASS_TABLE = 0x00160548
LEVEL13_SELECTOR = 2
LEVEL13_CLASS_ID = 0x215
LEVEL_UID_BITS = 0x0014C190
LOCAL_UID_BITS = 0x001BA4D0
UID_BYTES = 0x100

RESET_SIGNATURES = {
    0x00204C60: 0x27BDFF30,  # reset/player-init routine prologue
    0x00204D2C: 0x2452E090,  # s2 = 0x0013e090 snapshot destination
    0x00204D40: 0x846200A6,  # scan live Mobies by native class id
    0x00204D5C: 0x0C0965A6,  # grounding/query helper 0x00259698
    0x00204D98: 0x24640010,  # a0 = selected class-0 Moby + position
    0x00204DA4: 0x78820000,  # LQ v0, 0(a0): class-0 position vector
    0x00204DA8: 0x7E220000,  # SQ v0, 0(s1): player-state position
    0x00204DBC: 0x3C020016,  # reset-snapshot gate address upper half
    0x00204DC0: 0x8C420540,  # lw v0, 0x540(v0) -> 0x00160540
    0x00204DCC: 0x7A220000,  # LQ first player-state vector
    0x00204DD0: 0x7E420000,  # SQ to 0x0013e090
    0x00204DD4: 0x7A820000,  # LQ second player-state vector
    0x00204DD8: 0x7E620000,  # SQ to 0x0013e0a0
}

GATE_CONTEXT_SIGNATURES = {
    0x00243670: 0x8C63ED84,  # current level from 0x0015ed84
    0x00243674: 0x2402000D,  # special path requires native level 13
    0x00243680: 0x84830026,  # selector from reset/context structure +0x26
    0x00243684: 0x24020002,  # special path requires selector 2
    0x00243690: 0x3C020016,  # gate address upper half
    0x00243694: 0x8C420540,  # same 0x00160540 gate
    0x002436B8: 0x24840548,  # class table base 0x00160548
    0x002436C4: 0x00031880,  # selector * 4
    0x002436CC: 0x8C620000,  # selected native class id
}


def _movement_probe():
    spec = importlib.util.spec_from_file_location("rac1_savestate_probe", MOVEMENT_PROBE_PATH)
    module = importlib.util.module_from_spec(spec)
    assert spec.loader is not None
    spec.loader.exec_module(module)
    return module


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def u16_at(memory: bytes | bytearray, address: int) -> int:
    return struct.unpack_from("<H", memory, address)[0]


def u32_at(memory: bytes | bytearray, address: int) -> int:
    return struct.unpack_from("<I", memory, address)[0]


def f32_at(memory: bytes | bytearray, address: int) -> float:
    return struct.unpack_from("<f", memory, address)[0]


def transform_at(memory: bytes | bytearray, position: int, yaw: int) -> dict[str, object]:
    return {
        "position": [f32_at(memory, position + offset) for offset in (0, 4, 8)],
        "yawRadians": f32_at(memory, yaw),
    }


def bit_count(data: bytes) -> int:
    return sum(value.bit_count() for value in data)


def _verify_signatures(
    memory: bytes | bytearray,
    signatures: dict[int, int],
    label: str,
) -> None:
    mismatches = []
    for address, expected in signatures.items():
        actual = u32_at(memory, address)
        if actual != expected:
            mismatches.append({
                "address": f"0x{address:08x}",
                "expected": f"0x{expected:08x}",
                "actual": f"0x{actual:08x}",
            })
    if mismatches:
        raise RuntimeError(f"loaded R&C1 {label} signatures changed: {mismatches}")


def verify_reset_signatures(memory: bytes | bytearray) -> None:
    _verify_signatures(memory, RESET_SIGNATURES, "reset")


def verify_gate_context_signatures(memory: bytes | bytearray) -> None:
    _verify_signatures(memory, GATE_CONTEXT_SIGNATURES, "reset-gate context")
    selected_class = u32_at(memory, LEVEL13_CLASS_TABLE + LEVEL13_SELECTOR * 4)
    if selected_class != LEVEL13_CLASS_ID:
        raise RuntimeError(
            "loaded R&C1 reset-gate class table changed: "
            f"selector {LEVEL13_SELECTOR} expected 0x{LEVEL13_CLASS_ID:x}, "
            f"actual 0x{selected_class:x}"
        )


def checkpoint_report(memory: bytes | bytearray) -> dict[str, object]:
    verify_reset_signatures(memory)
    verify_gate_context_signatures(memory)
    level_bits = bytes(memory[LEVEL_UID_BITS:LEVEL_UID_BITS + UID_BYTES])
    local_bits = bytes(memory[LOCAL_UID_BITS:LOCAL_UID_BITS + UID_BYTES])
    return {
        "loadedResetSignaturesVerified": len(RESET_SIGNATURES),
        "loadedGateContextSignaturesVerified": len(GATE_CONTEXT_SIGNATURES),
        "addresses": {
            "playerState": f"0x{PLAYER_STATE:08x}",
            "class0LiveMoby": f"0x{PLAYER_MOBY:08x}",
            "nanotech": f"0x{NANOTECH:08x}",
            "resetSnapshot": f"0x{RESET_SNAPSHOT:08x}",
            "resetSnapshotGate": f"0x{RESET_GATE:08x}",
            "resetContext": f"0x{RESET_CONTEXT:08x}",
            "currentLevel": f"0x{CURRENT_LEVEL:08x}",
            "level13ClassTable": f"0x{LEVEL13_CLASS_TABLE:08x}",
            "levelUidBits": f"0x{LEVEL_UID_BITS:08x}",
            "localUidBits": f"0x{LOCAL_UID_BITS:08x}",
        },
        "witness": {
            "nanotech": u32_at(memory, NANOTECH),
            "liveClassId": u16_at(memory, PLAYER_MOBY + 0xA6),
            "playerState": transform_at(memory, PLAYER_STATE, PLAYER_STATE + 0x18),
            "class0Moby": transform_at(memory, PLAYER_MOBY + 0x10, PLAYER_MOBY + 0x48),
            "resetSnapshot": transform_at(memory, RESET_SNAPSHOT, RESET_SNAPSHOT + 0x18),
            "resetSnapshotGate": u32_at(memory, RESET_GATE),
            "currentLevel": u32_at(memory, CURRENT_LEVEL),
            "resetContextSelector": u16_at(memory, RESET_CONTEXT + 0x26),
            "level13SelectedClassId": u32_at(
                memory,
                LEVEL13_CLASS_TABLE + LEVEL13_SELECTOR * 4,
            ),
            "uidPersistence": {
                "mapsEqual": level_bits == local_bits,
                "levelSetBits": bit_count(level_bits),
                "localSetBits": bit_count(local_bits),
                "levelSha256": sha256(level_bits),
                "localSha256": sha256(local_bits),
            },
        },
        "resetDataflow": {
            "routine": "0x00204c60",
            "classSelection": "scan live Moby pool until Moby+0xa6 == 0",
            "placement": "selected class-0 Moby +0x10 position is copied into player state at 0x0013f3d0",
            "orientation": "selected class-0 Moby +0x48 yaw seeds the player reset path",
            "snapshot": "when 0x00160540 == 0, the first 32 bytes of rebuilt player state are copied to 0x0013e090",
            "snapshotAuthority": False,
        },
        "resetSnapshotGateContext": {
            "resetConsumer": "0x00204dc0 suppresses the downstream 0x0013e090 snapshot copy when nonzero",
            "verifiedSecondaryConsumer": "0x00243670..0x002436cc",
            "secondaryConsumerConditions": {
                "currentLevel": 13,
                "resetContextSelector": LEVEL13_SELECTOR,
            },
            "secondaryConsumerClassTable": f"0x{LEVEL13_CLASS_TABLE:08x}",
            "secondaryConsumerSelectedClassId": f"0x{LEVEL13_CLASS_ID:x}",
            "genericCheckpointSelectorSupported": False,
            "boundary": "the secondary consumer is a level-13 native-class object path; its broader gameplay meaning remains unrecovered",
        },
        "boundary": [
            "This probe verifies the Veldin reset dataflow; it does not identify a generic checkpoint selector.",
            "The 0x00160540 gate is also consumed by a level-13 selector-2 native-class path resolving to class 0x215, which is evidence against treating it as a generic checkpoint selector.",
            "The reset snapshot is downstream state. A controlled mutation was overwritten by retail before respawn and did not redirect placement.",
            "UID-map equality in one savestate is state evidence only; death preservation requires the separately retained controlled live transition.",
        ],
    }


def build_report(savestate: Path, zstd_dll: Path) -> dict[str, object]:
    raw = savestate.read_bytes()
    probe = _movement_probe()
    memory = probe.read_zip_entry(savestate, "eeMemory.bin", zstd_dll)
    return {
        "schema": 1,
        "authority": {
            "game": "Ratchet & Clank",
            "build": "rac1-ntscu-original",
            "serial": "SCUS-97199",
        },
        "savestateSha256": sha256(raw),
        "eeMemorySha256": sha256(memory),
        "eeMemoryBytes": len(memory),
        "checkpointBoundary": checkpoint_report(memory),
        "notes": [
            "The savestate and EE-memory payload remain user-local and are never emitted.",
            "Small instruction signatures are retained only to fail closed if the loaded retail path changes.",
        ],
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
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
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
