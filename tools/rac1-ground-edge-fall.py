#!/usr/bin/env python3
"""Reduce a controlled R&C1 ordinary edge departure to retained evidence."""

from __future__ import annotations

import argparse
import json
import struct
from pathlib import Path

GROUND_REQUEST = 54.0 / 3600.0
EDGE_INCREMENT = 25.0 / 3600.0
TOLERANCE = 1e-5


def _f32(word: int) -> float:
    return struct.unpack("<f", struct.pack("<I", int(word)))[0]


def _request_z(row: dict[str, object]) -> float:
    return _f32(row["sample"]["candidate_words"]["0x068"])


def derive(capture: dict[str, object]) -> dict[str, object]:
    rows = capture["samples"]
    loss_index = None
    for index in range(1, len(rows)):
        prior = rows[index - 1]["sample"]
        sample = rows[index]["sample"]
        if (
            int(prior["contact_slot_300"]) != 0
            and int(sample["contact_slot_300"]) == 0
            and int(sample["action_state"]) == 2
        ):
            loss_index = index
            break
    if loss_index is None:
        raise ValueError("capture contains no ordinary state-2 support-loss transition")

    unsupported = []
    for row in rows[loss_index:]:
        sample = row["sample"]
        if int(sample["contact_slot_300"]) != 0:
            break
        if int(sample["action_state"]) != 2:
            break
        unsupported.append(row)
    if len(unsupported) < 3:
        raise ValueError("support-loss transition is too short to establish recurrence")


    first_request = _request_z(unsupported[0])
    if abs(first_request + GROUND_REQUEST) > TOLERANCE:
        raise ValueError("first unsupported request does not carry the ground request")

    witnesses = []
    prior_request = None
    for row in unsupported:
        sample = row["sample"]
        request_z = _request_z(row)
        delta = None if prior_request is None else request_z - prior_request
        witnesses.append({
            "frame": int(row["frame"]),
            "requestZ": request_z,
            "actualDisplacementZ": float(sample["disp_z"]),
            "contactSlot300": int(sample["contact_slot_300"]),
            "packedCounters30c": int(sample["contact_counters_30c"]),
            "requestDelta": delta,
        })
        prior_request = request_z

    stable_deltas = [
        row["requestDelta"] for row in witnesses[1:7]
        if row["requestDelta"] is not None
    ]
    if not stable_deltas or any(
        abs(delta + EDGE_INCREMENT) > TOLERANCE for delta in stable_deltas
    ):
        raise ValueError("unsupported request does not follow the 25/3600 recurrence")


    return {
        "schema": 1,
        "authority": capture.get("authority"),
        "movieSha256": capture.get("movieSha256"),
        "stateSha256": capture.get("stateSha256"),
        "supportLossFrame": int(unsupported[0]["frame"]),
        "groundRequestPerTick": GROUND_REQUEST,
        "edgeFallAccelerationPerTick": EDGE_INCREMENT,
        "equation": "first=-54/3600; subsequent[n]=subsequent[n-1]-25/3600",
        "unsupportedFramesBeforeRecontact": len(unsupported),
        "witnesses": witnesses[:7],
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
