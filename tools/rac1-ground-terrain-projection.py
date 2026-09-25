#!/usr/bin/env python3
"""Verify the retail R&C1 ordinary terrain contact-vector clip."""

from __future__ import annotations

import argparse
import hashlib
import json
import runpy
import struct
from pathlib import Path

SIGNATURES = {
    0x00212620: 0x2672F350,
    0x00212624: 0x26103E40,
    0x0021262C: 0x26110040,
    0x00212644: 0xC64100E0,
    0x00212648: 0xC6040044,
    0x0021264C: 0x46000847,
    0x00212650: 0xC6020040,
    0x00212654: 0xC64000E4,
    0x00212658: 0x44801800,
    0x0021265C: 0x46020842,
    0x00212660: 0x46040002,
    0x00212664: 0x46000B01,
    0x00212668: 0x460C1834,
    0x00212670: 0x45000007,
    0x00212674: 0x0220202D,
    0x00212678: 0x0C07FCC0,
    0x0021267C: 0x0220282D,
    0x00212680: 0x264400E0,
    0x00212684: 0x0220302D,
    0x00212688: 0x0C07FC9E,
    0x0021268C: 0x0080282D,
}


def _u32(memory: bytes, address: int) -> int:
    return struct.unpack_from("<I", memory, address)[0]


def derive_static(memory: bytes) -> dict[str, object]:
    mismatches = []
    for address, expected in SIGNATURES.items():
        actual = _u32(memory, address)
        if actual != expected:
            mismatches.append(
                {
                    "address": f"0x{address:08x}",
                    "expected": f"0x{expected:08x}",
                    "actual": f"0x{actual:08x}",
                }
            )
    if mismatches:
        raise RuntimeError(f"terrain-projection signatures changed: {mismatches}")

    return {
        "schema": 1,
        "requestVector": "P+0xe0",
        "acceptedContactVector": "0x00173e80",
        "clipRange": "0x00212644..0x0021268c",
        "ordinaryClip": (
            "s = -(request.x * contact.x + request.y * contact.y); "
            "if s > 0: request += s * contact"
        ),
        "directionalSlopeLaw": (
            "for authored face normal n and horizontal heading h: "
            "rise = -(n.x*h.x + n.y*h.y)/n.z; "
            "normalize (h.x,h.y,rise) to the controller planar-step magnitude"
        ),
        "importantBoundary": (
            "the accepted contact vector is not assumed to be the raw authored "
            "triangle normal; the directional-slope law is independently checked "
            "against retail LEVEL0 displacement and authored collision triangles"
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
