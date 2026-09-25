#!/usr/bin/env python3
"""Reduce controlled R&C1 ground/slope contact captures to payload-free evidence."""

from __future__ import annotations

import argparse
import json
import math
import statistics
import struct
from pathlib import Path

RUN_CAP = 0.09500919
CAP_TOLERANCE = 5e-5
VERTICAL_CORRECTION = 0.015
CORRECTION_TOLERANCE = 5e-5


def _f32(word: int) -> float:
    return struct.unpack("<f", struct.pack("<I", int(word)))[0]


def _length3(vector: tuple[float, float, float]) -> float:
    return math.sqrt(sum(value * value for value in vector))


def _sample_vectors(row: dict[str, object]) -> dict[str, object]:
    sample = row["sample"]
    words = sample["candidate_words"]
    pre = tuple(_f32(words[f"0x{offset:03x}"]) for offset in (0x60, 0x64, 0x68))
    final = tuple(float(sample[name]) for name in ("disp_x", "disp_y", "disp_z"))
    correction = tuple(final[i] - pre[i] for i in range(3))
    return {
        "frame": int(row["frame"]),
        "segment": row["segment"],
        "sequence": int(sample["sequence"]),
        "preContact": list(pre),
        "finalDisplacement": list(final),
        "contactCorrection": list(correction),
        "finalMagnitude": _length3(final),
    }


def _is_stable_ground_contact(row: dict[str, object]) -> bool:
    correction = row["contactCorrection"]
    return (
        row["segment"] == "forward"
        and row["sequence"] == 4
        and abs(row["finalMagnitude"] - RUN_CAP) <= CAP_TOLERANCE
        and math.hypot(correction[0], correction[1]) <= CORRECTION_TOLERANCE
        and abs(correction[2] - VERTICAL_CORRECTION) <= CORRECTION_TOLERANCE
    )


def _representatives(rows: list[dict[str, object]]) -> list[dict[str, object]]:
    if len(rows) <= 8:
        return rows
    indexes = sorted({
        0,
        1,
        len(rows) // 4,
        len(rows) // 2,
        (3 * len(rows)) // 4,
        len(rows) - 2,
        len(rows) - 1,
    })
    return [rows[index] for index in indexes]


def derive(capture: dict[str, object]) -> dict[str, object]:
    reduced = [_sample_vectors(row) for row in capture["samples"]]
    stable = [row for row in reduced if _is_stable_ground_contact(row)]
    if not stable:
        raise ValueError("capture contains no stable run-cap ground-contact witnesses")

    correction_z = [row["contactCorrection"][2] for row in stable]
    magnitudes = [row["finalMagnitude"] for row in stable]
    uphill = [row for row in stable if row["finalDisplacement"][2] > 0.01]
    flat = [row for row in stable if abs(row["finalDisplacement"][2]) < 1e-4]
    return {
        "schema": 1,
        "authority": capture.get("authority"),
        "movieSha256": capture.get("movieSha256"),
        "stateSha256": capture.get("stateSha256"),
        "lawStatus": (
            "runtime-observed terrain response; ordinary -0.015 downward request "
            "is statically pinned in rac1-ground-contact-static.json"
        ),
        "runCapNativeUnitsPerTick": RUN_CAP,
        "stableWitnessCount": len(stable),
        "stableFrameRange": [stable[0]["frame"], stable[-1]["frame"]],
        "flatWitnessCount": len(flat),
        "uphillWitnessCount": len(uphill),
        "finalMagnitudeError": {
            "maxAbs": max(abs(value - RUN_CAP) for value in magnitudes),
            "medianAbs": statistics.median(abs(value - RUN_CAP) for value in magnitudes),
        },
        "verticalContactCorrection": {
            "expectedObservedValue": VERTICAL_CORRECTION,
            "min": min(correction_z),
            "max": max(correction_z),
            "median": statistics.median(correction_z),
        },
        "representativeWitnesses": _representatives(stable),
    }


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--capture", type=Path, required=True)
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args()

    capture = json.loads(args.capture.read_text(encoding="utf-8"))
    report = derive(capture)
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(args.out)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
