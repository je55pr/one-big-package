#!/usr/bin/env python3
"""Reduce a retail R&C1 neutral-release trace to a slope-stop witness."""

from __future__ import annotations

import argparse
import json
from pathlib import Path

ZERO_EPSILON = 1e-9
POSITION_EPSILON = 1e-6
MIN_STATIONARY_TAIL = 10


def _zero(value: object) -> bool:
    return abs(float(value)) <= ZERO_EPSILON


def _counter_halves(value: object) -> tuple[int, int]:
    word = int(value)
    return word & 0xFFFF, (word >> 16) & 0xFFFF


def derive(capture: dict[str, object]) -> dict[str, object]:
    rows = [row for row in capture["samples"] if row["segment"] == "release"]
    if not rows:
        raise RuntimeError("release segment missing")

    first_planar = next(
        (
            row
            for row in rows
            if _zero(row["sample"]["disp_x"]) and _zero(row["sample"]["disp_y"])
        ),
        None,
    )
    first_full = next(
        (
            row
            for row in rows
            if _zero(row["sample"]["disp_x"])
            and _zero(row["sample"]["disp_y"])
            and _zero(row["sample"]["disp_z"])
        ),
        None,
    )
    if first_planar is None or first_full is None:
        raise RuntimeError("release never reaches an exact stationary state")

    first_full_index = rows.index(first_full)
    tail = rows[first_full_index:]
    if len(tail) < MIN_STATIONARY_TAIL:
        raise RuntimeError(
            f"stationary tail too short: {len(tail)} < {MIN_STATIONARY_TAIL}"
        )

    position = first_full["sample"]
    for row in tail:
        sample = row["sample"]
        if not all(_zero(sample[key]) for key in ("disp_x", "disp_y", "disp_z")):
            raise RuntimeError("stationary tail resumes motion")
        if any(
            abs(float(sample[key]) - float(position[key])) > POSITION_EPSILON
            for key in ("pos_x", "pos_y", "pos_z")
        ):
            raise RuntimeError("stationary tail position changed")
        low, high = _counter_halves(sample["contact_counters_30c"])
        if low != 0 or high != 0:
            raise RuntimeError(
                f"stationary tail lost ordinary support at frame {row['frame']}"
            )

    start_counter = _counter_halves(first_full["sample"]["contact_counters_30c"])
    end_counter = _counter_halves(tail[-1]["sample"]["contact_counters_30c"])
    return {
        "schema": 1,
        "authority": capture.get("authority"),
        "movieSha256": capture.get("movieSha256"),
        "stateSha256": capture.get("stateSha256"),
        "releaseFrames": len(rows),
        "firstPlanarZeroFrame": int(first_planar["frame"]),
        "firstPlanarZeroLocalFrame": int(first_planar["local_frame"]),
        "firstFullyStationaryFrame": int(first_full["frame"]),
        "firstFullyStationaryLocalFrame": int(first_full["local_frame"]),
        "stationaryFrames": len(tail),
        "stationaryPosition": {
            "nativeX": float(position["pos_x"]),
            "nativeY": float(position["pos_y"]),
            "nativeZ": float(position["pos_z"]),
        },
        "stationarySequenceStart": int(first_full["sample"]["sequence"]),
        "stationarySequenceEnd": int(tail[-1]["sample"]["sequence"]),
        "contactSlotStart": int(first_full["sample"]["contact_slot_300"]),
        "contactSlotEnd": int(tail[-1]["sample"]["contact_slot_300"]),
        "unsupportedCounter30cStart": start_counter[0],
        "unsupportedCounter30eStart": start_counter[1],
        "unsupportedCounter30cEnd": end_counter[0],
        "unsupportedCounter30eEnd": end_counter[1],
        "boundary": (
            "This witness proves exact neutral rest while ordinary support remains "
            "admitted at this authored Veldin position. It does not recover host "
            "collision margin or collision-iteration policy."
        ),
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
