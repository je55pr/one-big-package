#!/usr/bin/env python3
"""Capture and reduce R&C1 stick-to-control-heading matrices.

Raw movies/samples stay under captures/. The derived report contains only
small numeric witnesses and hashes suitable for Git.
"""
from __future__ import annotations

import argparse
import importlib.util
import json
import math
import struct
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
HARNESS_PATH = ROOT / "tools" / "rac1-analogue-movement-harness.py"
SPEC = importlib.util.spec_from_file_location("rac1_analogue_harness", HARNESS_PATH)
HARNESS = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(HARNESS)

DIRECTIONS = [
    ("forward", (127, 0), 0.0, (1, 0)),
    ("forward-right", (255, 0), math.pi / 4, (1, 1)),
    ("right", (255, 127), math.pi / 2, (0, 1)),
    ("back-right", (255, 255), 3 * math.pi / 4, (-1, 1)),
    ("back", (127, 255), math.pi, (-1, 0)),
    ("back-left", (0, 255), -3 * math.pi / 4, (-1, -1)),
    ("left", (0, 127), -math.pi / 2, (0, -1)),
    ("forward-left", (0, 0), -math.pi / 4, (1, -1)),
]


def wrap_pi(value: float) -> float:
    return math.atan2(math.sin(value), math.cos(value))


def u32_to_f32(value: int) -> float:
    return struct.unpack("<f", struct.pack("<I", int(value)))[0]


def control_vector(sample: dict[str, object]) -> tuple[float, float, float]:
    if "control_dir_x" in sample:
        return (
            float(sample["control_dir_x"]),
            float(sample["control_dir_y"]),
            float(sample["control_dir_z"]),
        )
    words = sample["candidate_words"]
    return tuple(u32_to_f32(words[key]) for key in ("0x0f0", "0x0f4", "0x0f8"))


def target_row(capture: dict[str, object]) -> dict[str, object]:
    rows = [
        row for row in capture["samples"]
        if row["segment"] == "direction" and int(row["local_frame"]) == 1
    ]
    if len(rows) != 1:
        raise ValueError(f"expected one direction local-frame 1 row, got {len(rows)}")
    return rows[0]


def normalize(vector: tuple[float, float, float]) -> tuple[float, float, float]:
    length = math.sqrt(sum(value * value for value in vector))
    if length == 0.0:
        raise ValueError("cannot normalize zero vector")
    return tuple(value / length for value in vector)


def vector_error(
    actual: tuple[float, float, float],
    expected: tuple[float, float, float],
) -> float:
    return math.sqrt(sum((a - b) ** 2 for a, b in zip(actual, expected)))


def capture_matrix(
    pid: int,
    port: int,
    savestate: Path,
    out_dir: Path,
    settle: float,
    reload_wait: float,
) -> None:
    out_dir = HARNESS.capture_path(out_dir)
    out_dir.mkdir(parents=True, exist_ok=True)
    for label, left, _angle, _basis in DIRECTIONS:
        plan = out_dir / f"{label}.plan.json"
        movie = out_dir / f"{label}.p2m2"
        raw = out_dir / f"{label}.raw.json"
        plan.write_text(json.dumps({
            "schema": 1,
            "segments": [
                {"label": "neutral", "frames": 6, "left": [127, 127]},
                {"label": "direction", "frames": 8, "left": list(left)},
            ],
        }, indent=2) + "\n", encoding="utf-8")
        HARNESS.build_movie(plan, savestate, movie)
        capture = HARNESS.capture_trial(
            pid, port, plan, movie, raw, settle, reload_wait,
        )
        row = target_row(capture)
        print(json.dumps({
            "label": label,
            "left": list(left),
            "targetYaw": row["sample"]["target_yaw"],
        }), flush=True)
def load_capture(path: Path) -> dict[str, object]:
    return json.loads(HARNESS.capture_path(path).read_text(encoding="utf-8"))


def derive_matrix(matrix_dir: Path) -> dict[str, object]:
    matrix_dir = HARNESS.capture_path(matrix_dir)
    captures: dict[str, dict[str, object]] = {}
    rows: dict[str, dict[str, object]] = {}
    state_hashes: set[str] = set()
    for label, _left, _angle, _basis in DIRECTIONS:
        capture = load_capture(matrix_dir / f"{label}.raw.json")
        captures[label] = capture
        rows[label] = target_row(capture)
        state_hashes.add(str(capture.get("stateSha256") or ""))
    if len(state_hashes) != 1:
        raise ValueError(f"matrix mixes savestates: {sorted(state_hashes)}")

    heading = float(rows["forward"]["sample"]["target_yaw"])
    forward = control_vector(rows["forward"]["sample"])
    right = control_vector(rows["right"]["sample"])
    direction_rows: list[dict[str, object]] = []
    max_angle_error = 0.0
    max_vector_error = 0.0
    max_projection_delta = 0.0

    for label, left, stick_angle, (forward_sign, right_sign) in DIRECTIONS:
        sample = rows[label]["sample"]
        target = float(sample["target_yaw"])
        delta = wrap_pi(target - heading)
        expected_delta = wrap_pi(-stick_angle)
        angle_error = abs(wrap_pi(delta - expected_delta))
        vector = control_vector(sample)
        expected_vector = normalize(tuple(
            forward_sign * forward[index] + right_sign * right[index]
            for index in range(3)
        ))
        basis_error = vector_error(vector, expected_vector)
        projected_yaw = math.atan2(vector[1], vector[0])
        projection_delta = abs(wrap_pi(target - projected_yaw))
        max_angle_error = max(max_angle_error, angle_error)
        max_vector_error = max(max_vector_error, basis_error)
        max_projection_delta = max(max_projection_delta, projection_delta)
        direction_rows.append({
            "label": label,
            "left": list(left),
            "stickAngleRad": stick_angle,
            "targetYaw": target,
            "targetDeltaFromForwardRad": delta,
            "expectedTargetDeltaRad": expected_delta,
            "targetAngleErrorRad": angle_error,
            "controlVector": list(vector),
            "controlVectorNorm": math.sqrt(sum(value * value for value in vector)),
            "basisCombinationError": basis_error,
            "targetMinusProjectedVectorYawRad": wrap_pi(target - projected_yaw),
            "movieSha256": captures[label]["movieSha256"],
        })
    return {
        "stateSha256": next(iter(state_hashes)),
        "controlHeadingRad": heading,
        "basis": {
            "forward3d": list(forward),
            "right3d": list(right),
            "forwardNorm": math.sqrt(sum(value * value for value in forward)),
            "rightNorm": math.sqrt(sum(value * value for value in right)),
            "dot": sum(a * b for a, b in zip(forward, right)),
        },
        "directions": direction_rows,
        "maxTargetAngleErrorRad": max_angle_error,
        "maxControlVectorCombinationError": max_vector_error,
        "maxTargetMinusProjectedVectorYawAbsRad": max_projection_delta,
    }


def derive_fixed_stick(capture_path: Path) -> dict[str, object]:
    capture = load_capture(capture_path)
    rows = [
        row for row in capture["samples"]
        if row["left"] == [127, 0] and int(row["local_frame"]) >= 1
    ]
    if not rows:
        raise ValueError("fixed-stick capture has no forward-stick rows")

    first = rows[0]["sample"]
    last = rows[-1]["sample"]
    first_vector = control_vector(first)
    last_vector = control_vector(last)
    projection_errors = []
    segment_rows: list[dict[str, object]] = []

    labels: list[str] = []
    for row in rows:
        label = str(row["segment"])
        if label not in labels:
            labels.append(label)
        sample = row["sample"]
        vector = control_vector(sample)
        projection_errors.append(abs(wrap_pi(
            float(sample["target_yaw"]) - math.atan2(vector[1], vector[0])
        )))

    for label in labels:
        group = [row for row in rows if row["segment"] == label]
        start_target = float(group[0]["sample"]["target_yaw"])
        end_target = float(group[-1]["sample"]["target_yaw"])
        segment_rows.append({
            "label": label,
            "right": list(group[0]["right"]),
            "samples": len(group),
            "startTargetYaw": start_target,
            "endTargetYaw": end_target,
            "targetDeltaRad": wrap_pi(end_target - start_target),
        })

    start_target = float(first["target_yaw"])
    end_target = float(last["target_yaw"])
    return {
        "stateSha256": capture.get("stateSha256"),
        "movieSha256": capture.get("movieSha256"),
        "left": [127, 0],
        "samples": len(rows),
        "startTargetYaw": start_target,
        "endTargetYaw": end_target,
        "targetDeltaRad": wrap_pi(end_target - start_target),
        "startControlVector": list(first_vector),
        "endControlVector": list(last_vector),
        "maxTargetMinusProjectedControlVectorYawAbsRad": max(projection_errors),
        "segments": segment_rows,
    }


def derive_report(
    matrix_dirs: list[Path],
    fixed_stick_capture: Path | None = None,
) -> dict[str, object]:
    matrices = [derive_matrix(path) for path in matrix_dirs]
    report = {
        "schema": 1,
        "authority": "R&C1 NTSC-U SCUS-97199 / fixed savestate + PCSX2 input recording + PINE",
        "sample": "direction local-frame 1, the first frame with G+0x100 updated",
        "playerState": {
            "base": "0x0013f3d0",
            "yaw": "G+0x018",
            "controlVector": ["G+0x0f0", "G+0x0f4", "G+0x0f8"],
            "targetYaw": "G+0x100",
        },
        "coordinateConvention": {
            "worldYaw": "atan2(+Y,+X), positive from +X toward +Y",
            "stickBytes": "X high=right, X low=left, Y low=forward, Y high=back",
            "targetRule": "WrapPi(controlHeading - stickAngle), where forward stickAngle=0 and right=+pi/2",
            "vectorRule": "normalize(forward3d*forwardSign + right3d*rightSign)",
        },
        "matrices": matrices,
        "proven": [
            "G+0x100 is the planar facing target consumed by the already-recovered yaw recurrence.",
            "Cardinals and diagonals apply exact signed 45-degree increments around the sampled control heading.",
            "G+0x0f0/+0x0f4/+0x0f8 form an orthogonal 3D control basis combination for the same stick direction.",
            "Diagonal G+0x100 is planar angle composition, not merely atan2 of the pitched 3D control vector.",
        ],
        "notProven": [
            "The retail chase-camera follow/recenter/obstruction law.",
            "The exact right-stick-to-control-heading response law or turn rate.",
            "This heading-matrix probe does not itself establish partial-stick magnitude shaping; see research/generated/rac1-analogue-input-law.json.",
        ],
    }
    if fixed_stick_capture is not None:
        report["fixedForwardStickControlBasisChange"] = derive_fixed_stick(
            fixed_stick_capture
        )
        report["proven"].append(
            "With forward stick held fixed, G+0x100 follows the changing projected control vector while the native control basis rotates."
        )
    return report


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)
    capture = sub.add_parser("capture")
    capture.add_argument("--pid", required=True, type=int)
    capture.add_argument("--pine-port", type=int, default=28099)
    capture.add_argument("--savestate", required=True, type=Path)
    capture.add_argument("--out-dir", required=True, type=Path)
    capture.add_argument("--settle", type=float, default=0.05)
    capture.add_argument("--reload-wait", type=float, default=0.8)

    derive = sub.add_parser("derive")
    derive.add_argument("--matrix-dir", action="append", required=True, type=Path)
    derive.add_argument("--fixed-stick-capture", type=Path)
    derive.add_argument("--out", required=True, type=Path)
    return parser


def main() -> int:
    args = build_parser().parse_args()
    if args.command == "capture":
        capture_matrix(
            args.pid, args.pine_port, args.savestate, args.out_dir,
            args.settle, args.reload_wait,
        )
        return 0

    report = derive_report(args.matrix_dir, args.fixed_stick_capture)
    args.out.parent.mkdir(parents=True, exist_ok=True)
    args.out.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
