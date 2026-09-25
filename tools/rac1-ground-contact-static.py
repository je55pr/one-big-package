#!/usr/bin/env python3
"""Verify the retail R&C1 ordinary-ground downward contact request."""

from __future__ import annotations

import argparse
import hashlib
import json
import runpy
import struct
from pathlib import Path

TICK_SCALAR_ADDRESS = 0x0015ED70
GROUND_BIAS_MULTIPLIER = 54.0
SIGNATURES = {
    0x0021C41C: 0x3C010016,
    0x0021C420: 0xC42CED70,
    0x0021C424: 0x260400E0,
    0x0021C428: 0x3C014258,
    0x0021C42C: 0x44810000,
    0x0021C430: 0x1000138C,
    0x0021C434: 0x0080282D,
    0x00221264: 0x0C08CD34,
    0x00221268: 0x460C0302,
    0x002334F0: 0x908320B3,
    0x00233508: 0x10600007,
    0x00233528: 0xC6000008,
    0x0023352C: 0x460C0001,
    0x00233530: 0x1000001F,
    0x00233534: 0xE6200008,
}


def _u32(memory: bytes, address: int) -> int:
    return struct.unpack_from("<I", memory, address)[0]


def _f32(memory: bytes, address: int) -> float:
    return struct.unpack_from("<f", memory, address)[0]


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
        raise RuntimeError(f"ground-contact signatures changed: {mismatches}")

    tick_scalar = _f32(memory, TICK_SCALAR_ADDRESS)
    bias = tick_scalar * GROUND_BIAS_MULTIPLIER
    return {
        "schema": 1,
        "tickScalarAddress": f"0x{TICK_SCALAR_ADDRESS:08x}",
        "tickScalar": tick_scalar,
        "groundBiasMultiplier": GROUND_BIAS_MULTIPLIER,
        "groundDownwardRequestPerTick": bias,
        "state2Path": "0x0021c41c..0x0021c434 -> 0x00221264",
        "vectorHelper": "0x002334d0",
        "ordinaryHelperBranch": "0x00233528..0x00233534",
        "equation": "request.z = request.z - (54 * (1/3600))",
        "signaturesVerified": len(SIGNATURES),
        "scope": (
            "ordinary action-state-2 path; later collision clipping/projection "
            "and transition branches remain separate recovery work"
        ),
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
