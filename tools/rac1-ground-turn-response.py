#!/usr/bin/env python3
"""Recover grounded R&C1 displacement response to steering direction changes.

Raw movies and PINE captures stay under captures/. The committed report retains
only derived displacement/yaw/target/sequence witnesses.
"""
from __future__ import annotations

import argparse
import importlib.util
import json
import math
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
HARNESS_PATH = ROOT / "tools" / "rac1-analogue-movement-harness.py"
SPEC = importlib.util.spec_from_file_location("rac1_analogue_harness", HARNESS_PATH)
HARNESS = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(HARNESS)

RUN = {
    "forward": [127, 0], "right45": [255, 0], "right": [255, 127],
    "back_right": [255, 255], "back": [127, 255], "back_left": [0, 255],
    "left": [0, 127], "left45": [0, 0],
}
WALK = {
    "forward": [127, 17], "right45": [219, 35], "right": [237, 127],
    "back_right": [219, 219], "back": [127, 237], "back_left": [35, 219],
    "left": [17, 127], "left45": [35, 35],
}


def wrap_pi(value: float) -> float:
    return (value + math.pi) % (2.0 * math.pi) - math.pi


def trial_specs() -> list[dict[str, object]]:
    result: list[dict[str, object]] = []
    turns = [("45", "right45"), ("90", "right"), ("135", "back_right"), ("180", "back")]
    for band, inputs, prelude, release in (
        ("run", RUN, 56, 4),
        ("walk", WALK, 40, 2),
    ):
        for angle, target in turns:
            result.append({
                "id": f"{band}-forward-turn-{angle}",
                "band": band, "kind": "turn", "prelude": "forward",
                "target": target, "preludeFrames": prelude, "releaseFrames": 0,
                "inputs": inputs,
            })
        for angle, target in (("45", "left45"), ("90", "left"), ("135", "back_left")):
            result.append({
                "id": f"{band}-forward-turn-left-{angle}",
                "band": band, "kind": "turn", "prelude": "forward",
                "target": target, "preludeFrames": prelude, "releaseFrames": 0,
                "inputs": inputs,
            })
        for name, start, target in (
            ("left-right-reversal", "right", "left"),
            ("left-to-right-reversal", "left", "right"),
            ("forward-back-reversal", "forward", "back"),
            ("back-to-forward-reversal", "back", "forward"),
        ):
            result.append({
                "id": f"{band}-{name}",
                "band": band, "kind": "reversal", "prelude": start,
                "target": target, "preludeFrames": prelude, "releaseFrames": 0,
                "inputs": inputs,
            })
        for angle, target in (("90", "right"), ("180", "back")):
            result.append({
                "id": f"{band}-release-turn-{angle}",
                "band": band, "kind": "release-turn", "prelude": "forward",
                "target": target, "preludeFrames": prelude, "releaseFrames": release,
                "inputs": inputs,
            })
    return result


def plan_for(spec: dict[str, object]) -> dict[str, object]:
    inputs = spec["inputs"]
    segments = [{
        "label": "prelude",
        "frames": spec["preludeFrames"],
        "left": inputs[spec["prelude"]],
    }]
    if int(spec["releaseFrames"]):
        segments.append({
            "label": "release",
            "frames": spec["releaseFrames"],
            "left": [127, 127],
        })
    segments.append({"label": "turn", "frames": 22, "left": inputs[spec["target"]]})
    return {"schema": 1, "segments": segments}


def sequence_path(rows: list[dict[str, object]]) -> list[int]:
    result: list[int] = []
    for row in rows:
        value = int(row["sample"]["sequence"])
        if not result or result[-1] != value:
            result.append(value)
    return result


def row_metrics(row: dict[str, object], pre_heading: float, pre_speed: float) -> dict[str, object]:
    sample = row["sample"]
    dx = float(sample["disp_x"])
    dy = float(sample["disp_y"])
    speed = math.hypot(dx, dy)
    yaw = float(sample["yaw"])
    target = float(sample["target_yaw"])
    heading = math.atan2(dy, dx) if speed > 1e-7 else None
    return {
        "segment": row["segment"],
        "localFrame": int(row["local_frame"]),
        "planarSpeed": speed,
        "travelHeading": heading,
        "yaw": yaw,
        "targetYaw": target,
        "sequence": int(sample["sequence"]),
        "travelMinusYaw": None if heading is None else wrap_pi(heading - yaw),
        "travelMinusTarget": None if heading is None else wrap_pi(heading - target),
        "targetMinusYaw": wrap_pi(target - yaw),
        "travelChange": None if heading is None else wrap_pi(heading - pre_heading),
        "yawChange": wrap_pi(yaw - pre_heading),
        "speedRatio": None if pre_speed <= 1e-7 else speed / pre_speed,
    }


def derive_trial(spec: dict[str, object], capture: dict[str, object]) -> dict[str, object]:
    rows = list(capture["samples"])
    prelude = [row for row in rows if row["segment"] == "prelude"]
    turn = [row for row in rows if row["segment"] == "turn"]
    release = [row for row in rows if row["segment"] == "release"]
    pre_tail = prelude[-6:]
    pre_speeds = [
        math.hypot(float(row["sample"]["disp_x"]), float(row["sample"]["disp_y"]))
        for row in pre_tail
    ]
    pre_speed = sum(pre_speeds) / len(pre_speeds)
    last_pre = prelude[-1]["sample"]
    pre_heading = math.atan2(float(last_pre["disp_y"]), float(last_pre["disp_x"]))
    before_turn_row = release[-1] if release else prelude[-1]
    before_turn = row_metrics(before_turn_row, pre_heading, pre_speed)

    metrics = [row_metrics(row, pre_heading, pre_speed) for row in [*release, *turn]]
    previous_target = float(before_turn_row["sample"]["target_yaw"])
    effective_local = None
    for row in turn:
        current = float(row["sample"]["target_yaw"])
        if abs(wrap_pi(current - previous_target)) > 0.05:
            effective_local = int(row["local_frame"])
            break
        previous_target = current

    moving_turn = [m for m in metrics if m["segment"] == "turn" and m["planarSpeed"] > 1e-4]
    effective = [
        m for m in moving_turn
        if effective_local is not None and int(m["localFrame"]) >= effective_local
    ]
    raw_transition = [*release, *turn]
    transition_z = [float(row["sample"]["pos_z"]) for row in raw_transition]
    max_abs_vertical_step = max(
        (abs(float(row["sample"]["disp_z"])) for row in raw_transition),
        default=0.0,
    )
    return {
        "id": spec["id"],
        "band": spec["band"],
        "kind": spec["kind"],
        "preludeDirection": spec["prelude"],
        "targetDirection": spec["target"],
        "preludeFrames": spec["preludeFrames"],
        "releaseFrames": spec["releaseFrames"],
        "preTurn": {
            "meanTailSpeed": pre_speed,
            "travelHeading": pre_heading,
            "yaw": float(last_pre["yaw"]),
            "targetYaw": float(last_pre["target_yaw"]),
            "sequence": int(last_pre["sequence"]),
        },
        "beforeTurn": before_turn,
        "effectiveTurnLocalFrame": effective_local,
        "sequencePath": sequence_path([*release, *turn]),
        "transition": metrics,
        "summary": {
            "minEffectiveSpeed": min((m["planarSpeed"] for m in effective), default=None),
            "maxEffectiveSpeed": max((m["planarSpeed"] for m in effective), default=None),
            "maxAbsTravelMinusYaw": max(
                (abs(m["travelMinusYaw"]) for m in effective if m["travelMinusYaw"] is not None),
                default=None,
            ),
            "maxAbsTravelMinusTarget": max(
                (abs(m["travelMinusTarget"]) for m in effective if m["travelMinusTarget"] is not None),
                default=None,
            ),
            "maxAbsTargetMinusYaw": max((abs(m["targetMinusYaw"]) for m in effective), default=None),
            "minEffectiveSpeedRatio": min((m["speedRatio"] for m in effective), default=None),
            "maxEffectiveSpeedRatio": max((m["speedRatio"] for m in effective), default=None),
            "maxAbsVerticalStep": max_abs_vertical_step,
            "positionZRange": [
                min(transition_z, default=None),
                max(transition_z, default=None),
            ],
        },
    }


def capture_matches_manifest(raw: Path, manifest: dict[str, object]) -> bool:
    if not raw.exists():
        return False
    try:
        existing = json.loads(raw.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        return False
    return (
        existing.get("movieSha256") == manifest.get("movieSha256")
        and existing.get("stateSha256") == manifest.get("stateSha256")
    )


def normalized_manifest_segments(plan: list[dict[str, object]]) -> list[dict[str, object]]:
    return [
        {
            "label": segment["label"],
            "frames": segment["frames"],
            "left": list(segment["left"]),
            "right": list(segment["right"]),
            "buttons": list(segment["buttons"]),
        }
        for segment in plan
    ]


def existing_capture_is_current(
    raw: Path,
    movie: Path,
    savestate: Path,
    plan: list[dict[str, object]],
) -> bool:
    manifest_path = movie.with_suffix(movie.suffix + ".json")
    if not raw.exists() or not movie.exists() or not manifest_path.exists():
        return False
    try:
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        return (
            capture_matches_manifest(raw, manifest)
            and manifest.get("stateSha256") == HARNESS.sha256(savestate)
            and manifest.get("segments") == normalized_manifest_segments(plan)
        )
    except (OSError, json.JSONDecodeError):
        return False


def capture_suite(
    pid: int,
    port: int,
    savestate: Path,
    out_dir: Path,
    settle: float,
    reload_wait: float,
) -> None:
    out_dir = HARNESS.capture_path(out_dir)
    out_dir.mkdir(parents=True, exist_ok=True)
    specs = trial_specs()
    for index, spec in enumerate(specs, start=1):
        trial = out_dir / str(spec["id"])
        trial.mkdir(exist_ok=True)
        plan_path = trial / "plan.json"
        plan_path.write_text(json.dumps(plan_for(spec), indent=2) + "\n", encoding="utf-8")
        movie = trial / "trial.p2m2"
        raw = trial / "raw.json"
        normalized_plan = HARNESS.load_plan(plan_path)
        if existing_capture_is_current(raw, movie, savestate, normalized_plan):
            print(json.dumps({
                "trial": spec["id"], "index": index, "total": len(specs),
                "status": "existing-capture",
            }), flush=True)
            continue
        if raw.exists():
            print(json.dumps({
                "trial": spec["id"], "index": index, "total": len(specs),
                "status": "stale-capture-recapture",
            }), flush=True)
        HARNESS.build_movie(plan_path, savestate, movie)
        capture = HARNESS.capture_trial(pid, port, plan_path, movie, raw, settle, reload_wait)
        print(json.dumps({
            "trial": spec["id"], "index": index, "total": len(specs),
            "samples": len(capture["samples"]), "stateSha256": capture.get("stateSha256"),
        }), flush=True)


def derive_suite(capture_dir: Path) -> dict[str, object]:
    reports: list[dict[str, object]] = []
    state_hashes: set[str] = set()
    movie_hashes: dict[str, str] = {}
    for spec in trial_specs():
        trial = capture_dir / str(spec["id"])
        raw = json.loads((trial / "raw.json").read_text(encoding="utf-8"))
        state_hashes.add(str(raw.get("stateSha256")))
        movie_hashes[str(spec["id"])] = str(raw["movieSha256"])
        reports.append(derive_trial(spec, raw))
    if len(state_hashes) != 1:
        raise ValueError(f"turn suite mixes savestates: {sorted(state_hashes)}")

    direct = [r for r in reports if r["kind"] in ("turn", "reversal")]
    forward_turns = [r for r in reports if r["kind"] == "turn"]
    flat_forward_turns = [
        r for r in forward_turns
        if r["summary"]["maxAbsVerticalStep"] <= 1e-6
    ]
    moving = [
        m for r in reports for m in r["transition"]
        if m["segment"] == "turn" and m["planarSpeed"] > 1e-4
    ]
    return {
        "schema": 1,
        "authority": "R&C1 NTSC-U SCUS-97199 / fixed savestate + input recording + PINE",
        "stateSha256": next(iter(state_hashes)),
        "sampleCadence": "one PINE sample after each PCSX2 FrameAdvance(1)",
        "trials": reports,
        "aggregate": {
            "trialCount": len(reports),
            "maxAbsTravelMinusYawWhileMoving": max(
                abs(m["travelMinusYaw"]) for m in moving if m["travelMinusYaw"] is not None
            ),
            "flatForwardTurnRunMinEffectiveSpeedRatio": min(
                r["summary"]["minEffectiveSpeedRatio"]
                for r in flat_forward_turns if r["band"] == "run"
            ),
            "flatForwardTurnRunMaxEffectiveSpeedRatio": max(
                r["summary"]["maxEffectiveSpeedRatio"]
                for r in flat_forward_turns if r["band"] == "run"
            ),
            "flatForwardTurnWalkMinEffectiveSpeedRatio": min(
                r["summary"]["minEffectiveSpeedRatio"]
                for r in flat_forward_turns if r["band"] == "walk"
            ),
            "flatForwardTurnWalkMaxEffectiveSpeedRatio": max(
                r["summary"]["maxEffectiveSpeedRatio"]
                for r in flat_forward_turns if r["band"] == "walk"
            ),
            "flatForwardTurnRunMaxAbsTravelMinusYaw": max(
                r["summary"]["maxAbsTravelMinusYaw"]
                for r in flat_forward_turns if r["band"] == "run"
            ),
            "flatForwardTurnWalkMaxAbsTravelMinusYaw": max(
                r["summary"]["maxAbsTravelMinusYaw"]
                for r in flat_forward_turns if r["band"] == "walk"
            ),
            "nonFlatForwardTurnIds": [
                r["id"] for r in forward_turns if r not in flat_forward_turns
            ],
            "directRunMinEffectiveSpeedRatioIncludingReversals": min(
                r["summary"]["minEffectiveSpeedRatio"]
                for r in direct if r["band"] == "run"
            ),
        },
        "movieSha256": movie_hashes,
        "provenance": [
            "literal DualShock 2 byte movies generated by rac1-analogue-movement-harness.py",
            "same fixed retail savestate reloaded for every trial",
            "raw movies, savestate companions and PINE captures remain under ignored captures/",
            "committed rows retain only derived displacement/yaw/target-yaw/sequence values",
        ],
    }


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)
    capture = sub.add_parser("capture")
    capture.add_argument("--pid", type=int, required=True)
    capture.add_argument("--pine-port", type=int, required=True)
    capture.add_argument("--savestate", type=Path, required=True)
    capture.add_argument("--out-dir", type=Path, required=True)
    capture.add_argument("--settle", type=float, default=0.05)
    capture.add_argument("--reload-wait", type=float, default=1.0)
    derive = sub.add_parser("derive")
    derive.add_argument("--capture-dir", type=Path, required=True)
    derive.add_argument("--out", type=Path, required=True)
    return parser


def main() -> int:
    args = build_parser().parse_args()
    if args.command == "capture":
        capture_suite(
            args.pid, args.pine_port, args.savestate, args.out_dir,
            args.settle, args.reload_wait,
        )
    else:
        report = derive_suite(args.capture_dir)
        args.out.parent.mkdir(parents=True, exist_ok=True)
        args.out.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
        print(json.dumps({
            "out": str(args.out),
            "stateSha256": report["stateSha256"],
            **report["aggregate"],
        }, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
