#!/usr/bin/env python3
"""Reproduce payload-free R&C1 crate/pickup evidence from the authorized NTSC-U ISO.

This is static executable evidence. No retail payload or machine path is emitted.
"""
import argparse
import hashlib
import importlib.util
import json
import struct
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
SPEC = importlib.util.spec_from_file_location("overlay_probe", ROOT / "tools/rac1-level-overlay-probe.py")
PROBE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(PROBE)

BLOCKS = {
    "crateUpdate": (0x2CF218, 0x2CFCB8),
    "crateBreakAndDebris": (0x2D09B8, 0x2D1428),
    "crateFall": (0x2D1428, 0x2D197C),
    "rewardSelection": (0x257470, 0x2576D0),
    "boltEmitter": (0x260A88, 0x261350),
    "boltUpdateAndConstructor": (0x2A5DD8, 0x2A76D0),
    "placedBoltInitialization": (0x2A5E74, 0x2A60B8),
    "placedBoltCollectionTracking": (0x2A6A94, 0x2A6B34),
    "boltRestPoseAndSupport": (0x2A6DE8, 0x2A7210),
    "fragmentConstructorAndUpdate": (0x2AF8B0, 0x2AFBF0),
    "ammoSelection": (0x257218, 0x257470),
    "ammoConstructorAndUpdate": (0x2C52A8, 0x2C5BD0),
    "nanotechUpdate": (0x2D89F0, 0x2D97D8),
}


def build_report(path):
    with Path(path).open("rb") as stream:
        digest = hashlib.file_digest(stream, "sha256").hexdigest()
        if digest != PROBE.ISO_SHA256:
            raise ValueError("ISO differs from the original NTSC-U authority")
        data = PROBE.read_veldin_overlay(stream, stream.seek(0, 2))
    overlay_hash = hashlib.sha256(data).hexdigest()
    if overlay_hash != PROBE.OVERLAY_SHA256:
        raise ValueError("Veldin overlay differs from the retained authority")
    overlay = PROBE.Overlay(data)

    def float_immediate(high, low=None):
        upper = overlay.word(high)
        if upper >> 16 != 0x3C01:
            raise ValueError("Expected native lui at floating-point constant")
        bits = (upper & 0xFFFF) << 16
        if low is not None:
            lower = overlay.word(low)
            if lower >> 16 != 0x3421:
                raise ValueError("Expected native ori at floating-point constant")
            bits |= lower & 0xFFFF
        return struct.unpack("<f", struct.pack("<I", bits))[0]

    return {
        "schema": 2,
        "authority": {"build": "rac1-ntscu-original", "isoSha256": digest,
                      "levelId": 0, "overlaySha256": overlay_hash},
        "blocks": {name: {"start": f"0x{start:08x}", "end": f"0x{end:08x}",
                          "sha256": hashlib.sha256(overlay.read(start, end - start)).hexdigest()}
                   for name, (start, end) in BLOCKS.items()},
        "boltStates": {str(state): f"0x{overlay.word(0x1E9790 + state * 4):08x}" for state in range(5)},
        "placedBoltProfiles": [
            {"class": native_class,
             "placedScale": float_immediate(*placed_scale),
             "emittedScale": float_immediate(*emitted_scale),
             "restAngleXStep": float_immediate(*angle_step),
             "restTiltY": float_immediate(*tilt),
             "restNormalOffset": float_immediate(*normal),
             "restBasisXOffset": float_immediate(*radial)}
            for native_class, placed_scale, emitted_scale, angle_step, tilt, normal, radial in (
                (13, (0x2A6060, 0x2A6064), (0x2A74B4, 0x2A74B8), (0x2A6EB8, 0x2A6EBC),
                 (0x2A6EC4,), (0x2A6ECC, 0x2A6ED0), (0x2A6ED8, 0x2A6EDC)),
                (14, (0x2A6074, 0x2A6078), (0x2A74D0, 0x2A74D4), (0x2A6EF8, 0x2A6EFC),
                 (0x2A6F04,), (0x2A6F0C, 0x2A6F10), (0x2A6F18, 0x2A6F1C)),
                (15, (0x2A6088,), (0x2A74EC,), (0x2A6F38, 0x2A6F3C),
                 (0x2A6F44, 0x2A6F48), (0x2A6F50, 0x2A6F54), (0x2A6F5C,)),
                (16, (0x2A609C, 0x2A60A0), (0x2A7504, 0x2A7508), (0x2A6F78, 0x2A6F7C),
                 (0x2A6F84, 0x2A6F88), (0x2A6F90, 0x2A6F94), (0x2A6F9C, 0x2A6FA0)),
            )],
        "crateDebrisBranches500Through511": [f"0x{overlay.word(0x1E9EB0 + i * 4):08x}" for i in range(12)],
        "ammoDefinitions": [{"item": item,
                             "modelClass": int.from_bytes(overlay.read(0x179AC0 + item * 0x4C + 0x3A, 2), "little"),
                             "amount": int.from_bytes(overlay.read(0x1C40B0 + item * 0x18 + 0xC, 2), "little")}
                            for item in (10, 11, 13, 15, 16, 17, 19, 20, 23, 24, 25)],
        "limits": ["Static evidence, not a live emulator replay.",
                   "Host collision queries, pool budget, random seed/call order and particle presentation are separate adapters."]
    }


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--iso", type=Path, required=True)
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args()
    if args.iso.resolve() == args.out.resolve():
        parser.error("report must not overwrite the retail source")
    report = build_report(args.iso)
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(f"Verified {len(BLOCKS)} crate/pickup executable blocks; payload-free report written.")


if __name__ == "__main__":
    main()
