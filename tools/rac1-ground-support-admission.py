#!/usr/bin/env python3
"""Verify retail R&C1 ordinary ground-support admission gates."""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import runpy
import struct
from pathlib import Path

SIGNATURES = {
    0x00212838: 0x3C014200,  # 42.0f default metric
    0x0021283C: 0x44810000,
    0x00212848: 0xE62002DC,  # P+0x2dc = 42.0f before collision selection
    0x00212AE0: 0x2615FD60,  # s5 = P
    0x00212AE4: 0x2613FDE0,  # s3 = G / PLAYER_STATE
    0x00212AE8: 0x0200282D,  # a1 = P+0x2a0
    0x00212AEC: 0x0260202D,  # a0 = G
    0x00212AF0: 0x0C07FD14,  # 0x001ff450(G, P+0x2a0)
    0x00212B08: 0xE6A002DC,  # producer result -> P+0x2dc
    0x00212B54: 0x44800800,
    0x00212B5C: 0x46000834,
    0x00212B64: 0x45020005,
    0x00212B6C: 0xC6A002DC,
    0x00212B70: 0x46000007,  # negate negative metric
    0x00212B74: 0xE6A002DC,
    0x00212B78: 0xC6A102DC,
    0x00212B68: 0xC6A102DC,
    0x00212B7C: 0x3C013CA3,
    0x00212B80: 0x3421D70A,
    0x00212B8C: 0x46000834,
    0x00212B94: 0x45000018,
    0x00212B9C: 0xC6A102E0,
    0x00212BA0: 0x3C013F5F,
    0x00212BA4: 0x342166F3,
    0x00212BB0: 0x46000836,
    0x00212BB8: 0x45010008,
    0x00212BBC: 0xA6A0030C,
    0x00212BC0: 0x92A320B3,
    0x00212BC4: 0x24020001,
    0x00212BC8: 0x10620004,
    0x00212BCC: 0x24020016,
    0x00212BD0: 0x8EA3208C,
    0x00212BD4: 0x14620008,
    0x00212BEC: 0xA460030E,
    0x00233D44: 0x90C220B3,
    0x00233D50: 0x0C07FD02,
    0x00233D5C: 0xC60C0008,
    0x00233D94: 0x0C07FE2C,
}

CONTACT_METRIC_LIMIT_BITS = 0x3CA3D70A
SUPPORT_ANGLE_LIMIT_BITS = 0x3F5F66F3
PLAYER_BASE = 0x0013F350
PLAYER_STATE = 0x0013F3D0


def _f32_bits(word: int) -> float:
    return struct.unpack("<f", struct.pack("<I", word))[0]


def _u32(memory: bytes, address: int) -> int:
    return struct.unpack_from("<I", memory, address)[0]


def _f32(memory: bytes, address: int) -> float:
    return struct.unpack_from("<f", memory, address)[0]


def derive_authority_witness(memory: bytes) -> dict[str, object]:
    metric = _f32(memory, PLAYER_BASE + 0x2DC)
    player = [_f32(memory, PLAYER_STATE + offset) for offset in (0, 4, 8)]
    accepted = [_f32(memory, PLAYER_BASE + 0x2A0 + offset) for offset in (0, 4, 8)]
    return {
        "contactMetric": metric,
        "absoluteContactMetric": abs(metric),
        "playerStatePosition": player,
        "acceptedContactPosition": accepted,
        "positionDeltaAtFrameBoundary": [
            player[i] - accepted[i] for i in range(3)
        ],
        "groundDownwardRequestDifference": abs(abs(metric) - (54.0 / 3600.0)),
        "interpretationBoundary": (
            "The fixed-state metric is retained after the player/contact positions "
            "have converged at the frame boundary; it is not a standing clearance."
        ),
    }


def derive_static(memory: bytes) -> dict[str, object]:
    mismatches = []
    for address, expected in SIGNATURES.items():
        actual = _u32(memory, address)
        if actual != expected:
            mismatches.append({
                "address": f"0x{address:08x}",
                "expected": f"0x{expected:08x}",
                "actual": f"0x{actual:08x}",
            })
    if mismatches:
        raise RuntimeError(f"ground-support signatures changed: {mismatches}")

    contact_limit = _f32_bits(CONTACT_METRIC_LIMIT_BITS)
    angle_limit = _f32_bits(SUPPORT_ANGLE_LIMIT_BITS)
    return {
        "schema": 1,
        "contactMetricLimit": contact_limit,
        "ordinarySupportMaxAngleRadians": angle_limit,
        "ordinarySupportMaxAngleDegrees": math.degrees(angle_limit),
        "angleProducer": "0x00233d30",
        "contactMetricField": "P+0x2dc",
        "contactMetricInitialization": 42.0,
        "contactMetricProducer": "0x001ff450(G, P+0x2a0) -> P+0x2dc",
        "contactMetricNormalization": "negative values are negated before the 0.02 comparison",
        "contactAngleField": "P+0x2e0",
        "shortContactCounterField": "P+0x30c",
        "unsupportedCounterField": "P+0x30e",
        "ordinaryAdmission": (
            "after contact-metric sign normalization, P+0x2dc < 0.02; "
            "then P+0x2e0 <= 0.87266463 rad clears P+0x30e"
        ),
        "specialOverrides": [
            "P+0x20b3 == 1",
            "P+0x208c == 0x16",
        ],
        "note": (
            "The +/-pi/4 gates at 0x212d84..0x212e68 cache orientation "
            "components and are not the ordinary support-admission angle."
        ),
        "signaturesVerified": len(SIGNATURES),
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--savestate", type=Path, required=True)
    parser.add_argument("--zstd-dll", type=Path, required=True)
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args()

    movement_probe = runpy.run_path(
        str(Path(__file__).with_name("rac1-savestate-movement-probe.py"))
    )
    memory = movement_probe["read_zip_entry"](
        args.savestate, "eeMemory.bin", args.zstd_dll
    )
    report = derive_static(memory)
    report["authorityStateWitness"] = derive_authority_witness(memory)
    report["authorityStateSha256"] = hashlib.sha256(
        args.savestate.read_bytes()
    ).hexdigest()
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(args.out)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
