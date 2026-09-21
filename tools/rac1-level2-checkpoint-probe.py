#!/usr/bin/env python3
"""Reduce the loaded R&C1 level-2 checkpoint record without retaining payloads.

The authorized retail savestate and EE-memory bytes stay local. Output contains
only hashes, addresses, scalar transforms and small loaded-code signatures.
"""
from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import math
import struct
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
MOVEMENT_PROBE_PATH = ROOT / "tools" / "rac1-savestate-movement-probe.py"

CURRENT_LEVEL = 0x0015ED84
PLAYER_STATE = 0x0013F3D0
NANOTECH = 0x001415F8
LIVE_MOBY_POOL = 0x0015FFD8
CHECKPOINT_RECORD = 0x001BB830
CHECKPOINT_TRANSFORM = CHECKPOINT_RECORD + 0x10
CHECKPOINT_YAW = CHECKPOINT_RECORD + 0x28
CLASS_ID_OFFSET = 0xA6
LIVE_POSITION_OFFSET = 0x10
LIVE_YAW_OFFSET = 0x48

CONSUMER_SIGNATURES = {
    0x00286520: 0x27BDFFC0,  # loaded level-2 restart/checkpoint consumer prologue
    0x00286524: 0x3C02001C,  # checkpoint-record upper address
    0x00286534: 0x8C43B830,  # active flag at 0x001bb830
    0x00286538: 0x2450B830,  # s0 = checkpoint record
    0x0028657C: 0x26030010,  # source = checkpoint record + 0x10
    0x00286580: 0x2484F3D0,  # destination = player state 0x0013f3d0
    0x00286584: 0x78620000,  # LQ first checkpoint-state vector
    0x00286588: 0x7C820000,  # SQ first vector into player state
    0x0028658C: 0x24850010,  # second destination = player state + 0x10
    0x00286590: 0x26030020,  # second source = checkpoint record + 0x20
    0x00286594: 0x78620000,  # LQ second checkpoint-state vector
    0x00286598: 0x7CA20000,  # SQ second vector into player state
    0x00286650: 0x8CA5FFD8,  # live-Moby pool pointer at 0x0015ffd8
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


def verify_consumer_signatures(memory: bytes | bytearray) -> None:
    mismatches = []
    for address, expected in CONSUMER_SIGNATURES.items():
        actual = u32_at(memory, address)
        if actual != expected:
            mismatches.append({
                "address": f"0x{address:08x}",
                "expected": f"0x{expected:08x}",
                "actual": f"0x{actual:08x}",
            })
    if mismatches:
        raise RuntimeError(f"loaded R&C1 level-2 checkpoint signatures changed: {mismatches}")


def checkpoint_report(memory: bytes | bytearray) -> dict[str, object]:
    verify_consumer_signatures(memory)
    level = u32_at(memory, CURRENT_LEVEL)
    if level != 2:
        raise RuntimeError(f"expected loaded R&C1 level 2, got {level}")

    pool = u32_at(memory, LIVE_MOBY_POOL)
    class0 = pool
    class_id = u16_at(memory, class0 + CLASS_ID_OFFSET)
    if class_id != 0:
        raise RuntimeError(
            f"loaded R&C1 level-2 live-Moby index 0 is no longer class 0: 0x{class_id:x}"
        )

    class0_transform = transform_at(
        memory,
        class0 + LIVE_POSITION_OFFSET,
        class0 + LIVE_YAW_OFFSET,
    )
    active_flag = u32_at(memory, CHECKPOINT_RECORD)
    if active_flag == 0:
        raise RuntimeError("loaded R&C1 level-2 checkpoint record is not active")

    checkpoint_transform = transform_at(memory, CHECKPOINT_TRANSFORM, CHECKPOINT_YAW)
    class0_position = class0_transform["position"]
    checkpoint_position = checkpoint_transform["position"]
    distance = math.dist(class0_position, checkpoint_position)

    return {
        "loadedConsumerSignaturesVerified": len(CONSUMER_SIGNATURES),
        "addresses": {
            "currentLevel": f"0x{CURRENT_LEVEL:08x}",
            "playerState": f"0x{PLAYER_STATE:08x}",
            "nanotech": f"0x{NANOTECH:08x}",
            "liveMobyPoolPointer": f"0x{LIVE_MOBY_POOL:08x}",
            "checkpointRecord": f"0x{CHECKPOINT_RECORD:08x}",
            "checkpointTransform": f"0x{CHECKPOINT_TRANSFORM:08x}",
        },
        "witness": {
            "currentLevel": level,
            "nanotech": u32_at(memory, NANOTECH),
            "liveMobyPool": f"0x{pool:08x}",
            "liveClass0Index": 0,
            "liveClass0Address": f"0x{class0:08x}",
            "liveClass0Transform": class0_transform,
            "checkpointActiveFlag": active_flag,
            "checkpointTransform": checkpoint_transform,
            "checkpointDistanceFromClass0": distance,
            "checkpointTransformDiffersFromClass0": distance > 1e-4
            or abs(
                checkpoint_transform["yawRadians"] - class0_transform["yawRadians"]
            ) > 1e-4,
        },
        "loadedConsumer": {
            "routine": "0x00286520",
            "activeGate": "0x001bb830 != 0",
            "placementCopy": (
                "0x0028657c..0x00286598 copies checkpoint record +0x10..+0x2f "
                "into player state 0x0013f3d0..+0x1f"
            ),
            "activationWriterRecovered": False,
        },
        "boundary": [
            "This proves a loaded level-2 checkpoint/restart record and its player-placement consumer.",
            "It does not recover the gameplay event or policy that originally activates/populates the record.",
            "Ordinary ship-travel sequencing is separate from this checkpoint evidence.",
        ],
    }


def build_report(savestate: Path, zstd_dll: Path) -> dict[str, object]:
    raw = savestate.read_bytes()
    memory = _movement_probe().read_zip_entry(savestate, "eeMemory.bin", zstd_dll)
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
        "checkpoint": checkpoint_report(memory),
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
    rendered = json.dumps(build_report(args.savestate, args.zstd_dll), indent=2) + "\n"
    if args.out:
        args.out.parent.mkdir(parents=True, exist_ok=True)
        args.out.write_text(rendered, encoding="utf-8")
    else:
        print(rendered, end="")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
