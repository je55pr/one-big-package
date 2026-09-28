#!/usr/bin/env python3
"""Verify the retail R&C1 ordinary contact-orientation path."""

from __future__ import annotations

import argparse
import hashlib
import json
import runpy
import struct
from pathlib import Path

CONTACT_VECTOR_OFFSET = 0x270
CONTACT_METRIC_OFFSET = 0x2DC
ORIENTATION_LIMIT_RADIANS = 0.7853981633974483
CONTACT_METRIC_GATE = 0.25

SIGNATURES = {
    0x00212800: 0x3C013F80,  # 1.0f
    0x00212804: 0x44810000,  # mtc1 -> f0
    0x00212814: 0xE6200278,  # P+0x278 = 1
    0x00212824: 0xAE200270,  # P+0x270 = 0
    0x00212828: 0xAE200274,  # P+0x274 = 0
    0x00212D50: 0xC60102DC,  # P+0x2dc
    0x00212D54: 0x3C013E80,  # 0.25f
    0x00212D6C: 0x26040270,  # a0 = P+0x270
    0x00212D70: 0x0C08581E,  # vector -> orientation
    0x00212D7C: 0x3C01BF49,  # -pi/4 upper word
    0x00212D80: 0x34210FDB,
    0x00212D9C: 0x3C013F49,  # +pi/4 upper word
    0x00212DA0: 0x34210FDB,
    0x00213014: 0x260400E0,  # a0 = ordinary request P+0xe0
    0x00213018: 0x0C07FCE6,  # request dot contact vector
    0x0021301C: 0x26050270,  # a1 = P+0x270
    0x002160D0: 0xC7A00080,  # transformed x
    0x002160D4: 0xC7AC0088,  # transformed z
    0x002160E0: 0x0C07FC78,  # sqrt(x*x + z*z)
    0x002160E8: 0xC7AD0084,  # transformed y
    0x002160EC: 0x0C07FE2C,  # atan2(y, horizontal)
    0x002160F4: 0xC7AC0088,  # transformed z
    0x002160F8: 0xC7AD0080,  # transformed x
    0x002160FC: 0x0C07FE2C,  # atan2(z, x)
    0x00216100: 0xE6400000,  # orientation[0]
    0x00216114: 0xE6400004,  # orientation[1]
}


def _u32(memory: bytes, address: int) -> int:
    return struct.unpack_from("<I", memory, address)[0]


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
        raise RuntimeError(f"contact-orientation signatures changed: {mismatches}")

    return {
        "schema": 1,
        "contactOrientationVector": {
            "playerOffset": f"0x{CONTACT_VECTOR_OFFSET:x}",
            "initialValue": [0.0, 0.0, 1.0],
            "initializer": "0x00212800..0x00212828",
        },
        "contactMetricGate": {
            "playerOffset": f"0x{CONTACT_METRIC_OFFSET:x}",
            "comparisonValue": CONTACT_METRIC_GATE,
            "routine": "0x00212d50..0x00212d70",
        },
        "orientationConversion": {
            "routine": "0x00216078",
            "firstAngle": "atan2(transformedY, sqrt(transformedX^2 + transformedZ^2))",
            "secondAngle": "-atan2(transformedZ, transformedX)",
        },
        "orientationAdmissionBoundRadians": ORIENTATION_LIMIT_RADIANS,
        "orientationAdmissionBoundDegrees": 45.0,
        "ordinaryRequestDotContactVector": "0x00213014..0x0021301c",
        "interpretation": (
            "P+0x270 is an ordinary contact orientation/normal-like vector. "
            "The loaded path initializes it to world up, converts it to two "
            "orientation angles, and uses it in a request dot-product."
        ),
        "boundaryNote": (
            "The +/-pi/4 comparisons are proven contact-orientation gates in "
            "this path; this report does not claim they are a global walkable-slope limit."
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
    report["authorityStateSha256"] = hashlib.sha256(
        args.savestate.read_bytes()
    ).hexdigest()
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(args.out)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
