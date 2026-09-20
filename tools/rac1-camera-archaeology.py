#!/usr/bin/env python3
"""Deterministic R&C1 camera archaeology through PCSX2 input replay + PINE.

Raw movies, savestate companions and frame samples stay under ignored captures/.
This tool intentionally treats camera addresses as evidence inputs: known
player/control fields have fixed addresses, while semantic camera fields can be
supplied by a payload-free field-map JSON as archaeology identifies them.
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

INPUT_STATE = 0x0013C940
PLAYER_STATE = 0x0013F3D0
PLAYER_MOBY = 0x01845E80
CONTROL_HEADING = 0x00166DD8
DEFAULT_CANDIDATE_START = 0x00166C00
DEFAULT_CANDIDATE_BYTES = 0x400
KNOWN_FIELDS = {
    "player_yaw": (PLAYER_STATE + 0x18, "f32"),
    "player_pos_x": (PLAYER_MOBY + 0x10, "f32"),
    "player_pos_y": (PLAYER_MOBY + 0x14, "f32"),
    "player_pos_z": (PLAYER_MOBY + 0x18, "f32"),
    "player_moby_yaw": (PLAYER_MOBY + 0x48, "f32"),
    "control_heading": (CONTROL_HEADING, "f32"),
    "right_conditioned_x": (INPUT_STATE + 0x100, "f32"),
    "right_conditioned_y": (INPUT_STATE + 0x104, "f32"),
}


def wrap_pi(value: float) -> float:
    return (value + math.pi) % (2.0 * math.pi) - math.pi


def scenario_specs() -> list[dict[str, object]]:
    neutral = [127, 127]
    forward = [127, 0]
    right = [255, 127]
    return [
        {
            "id": "fixed-heading",
            "purpose": "stationary no-input baseline for heading/camera stability",
            "segments": [{"label": "baseline", "frames": 120, "left": neutral}],
        },
        {
            "id": "moving",
            "purpose": "steady forward locomotion with neutral right stick",
            "segments": [
                {"label": "settle", "frames": 20, "left": neutral},
                {"label": "move", "frames": 180, "left": forward},
            ],
        },
        {
            "id": "turning",
            "purpose": "player-facing turn without deliberate right-stick input",
            "segments": [
                {"label": "prelude", "frames": 50, "left": forward},
                {"label": "turn", "frames": 120, "left": right},
            ],
        },
        {
            "id": "idle",
            "purpose": "long neutral chase/follow drift witness",
            "segments": [{"label": "idle", "frames": 240, "left": neutral}],
        },
        {
            "id": "recenter",
            "purpose": "forward movement, horizontal right-stick pulse, then recenter",
            "segments": [
                {"label": "prelude", "frames": 40, "left": forward},
                {"label": "right-stick-left", "frames": 36, "left": forward, "right": [0, 127]},
                {"label": "recenter", "frames": 150, "left": forward},
            ],
        },
        {
            "id": "obstruction",
            "purpose": "forward approach from a dedicated obstruction anchor",
            "requiresObstructionState": True,
            "segments": [
                {"label": "settle", "frames": 20, "left": neutral},
                {"label": "approach", "frames": 180, "left": forward},
                {"label": "idle-after-contact", "frames": 80, "left": neutral},
            ],
        },
    ]


def plan_for(spec: dict[str, object]) -> dict[str, object]:
    return {"schema": 1, "segments": spec["segments"]}


def parse_address(value: object) -> int:
    if isinstance(value, int):
        result = value
    elif isinstance(value, str):
        result = int(value, 0)
    else:
        raise ValueError(f"address must be int or string, got {value!r}")
    if result < 0 or result > 0x01FFFFFF:
        raise ValueError(f"EE address outside ordinary RAM: 0x{result:08x}")
    if result & 3:
        raise ValueError(f"field address must be 4-byte aligned: 0x{result:08x}")
    return result


def load_field_map(path: Path | None) -> tuple[dict[str, tuple[int, str]], list[tuple[int, int]]]:
    fields: dict[str, tuple[int, str]] = {}
    ranges = [(DEFAULT_CANDIDATE_START, DEFAULT_CANDIDATE_BYTES)]
    if path is None:
        return fields, ranges

    raw = json.loads(path.read_text(encoding="utf-8"))
    if raw.get("schema") != 1:
        raise ValueError("camera field map must use schema 1")
    for name, spec in raw.get("fields", {}).items():
        kind = str(spec.get("kind", "f32"))
        if kind not in ("f32", "u32"):
            raise ValueError(f"unsupported field kind for {name}: {kind}")
        if name in KNOWN_FIELDS:
            raise ValueError(f"field map cannot override known field {name}")
        fields[str(name)] = (parse_address(spec["address"]), kind)

    if "candidateRanges" in raw:
        ranges = []
        for item in raw["candidateRanges"]:
            start = parse_address(item["start"])
            size = int(item["bytes"])
            if size <= 0 or size % 4:
                raise ValueError("candidate range bytes must be positive and 4-byte aligned")
            ranges.append((start, size))
    return fields, ranges


def candidate_addresses(ranges: list[tuple[int, int]]) -> list[int]:
    result: list[int] = []
    seen: set[int] = set()
    for start, size in ranges:
        for address in range(start, start + size, 4):
            if address not in seen:
                seen.add(address)
                result.append(address)
    return result


def decode(raw: int, kind: str) -> float | int:
    if kind == "u32":
        return raw
    return struct.unpack("<f", struct.pack("<I", raw))[0]


def sample_frame(
    pine: object,
    extra_fields: dict[str, tuple[int, str]],
    ranges: list[tuple[int, int]],
) -> dict[str, object]:
    fields = {**KNOWN_FIELDS, **extra_fields}
    candidates = candidate_addresses(ranges)
    addresses = [address for address, _ in fields.values()] + candidates
    values = pine.read32(addresses)
    sample: dict[str, object] = {}
    for (name, (_, kind)), raw in zip(fields.items(), values[: len(fields)]):
        sample[name] = decode(raw, kind)
    sample["candidate_words"] = {
        f"0x{address:08x}": raw
        for address, raw in zip(candidates, values[len(fields):])
    }
    return sample


def capture_scenario(
    pid: int,
    port: int,
    plan_path: Path,
    movie: Path,
    raw_out: Path,
    extra_fields: dict[str, tuple[int, str]],
    ranges: list[tuple[int, int]],
    settle: float,
    reload_wait: float,
) -> dict[str, object]:
    raw_out = HARNESS.capture_path(raw_out)
    movie = HARNESS.capture_path(movie)
    manifest_path = movie.with_suffix(movie.suffix + ".json")
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    plan = HARNESS.load_plan(plan_path)
    frames = HARNESS.expand_plan(plan)
    main = HARNESS.start_movie_replay(pid, movie)
    HARNESS.time.sleep(reload_wait)
    pine = HARNESS.Pine("127.0.0.1", port)
    try:
        initial = sample_frame(pine, extra_fields, ranges)
        samples: list[dict[str, object]] = []
        for frame_index, command in enumerate(frames):
            HARNESS.frame_advance(main, settle)
            observed = sample_frame(pine, extra_fields, ranges)
            samples.append({
                "frame": frame_index,
                "segment": command["label"],
                "segment_index": command["segment_index"],
                "local_frame": command["local_frame"],
                "left": list(command["left"]),
                "right": list(command["right"]),
                "buttons": list(command["buttons"]),
                "sample": observed,
            })
    finally:
        pine.close()

    capture = {
        "schema": 1,
        "authority": "R&C1 NTSC-U SCUS-97199 / fixed savestate + input recording + PINE",
        "movieSha256": manifest["movieSha256"],
        "stateSha256": manifest.get("stateSha256"),
        "fieldMap": {
            name: {"address": f"0x{address:08x}", "kind": kind}
            for name, (address, kind) in extra_fields.items()
        },
        "candidateRanges": [
            {"start": f"0x{start:08x}", "bytes": size}
            for start, size in ranges
        ],
        "initial": initial,
        "samples": samples,
    }
    raw_out.parent.mkdir(parents=True, exist_ok=True)
    raw_out.write_text(json.dumps(capture, indent=2) + "\n", encoding="utf-8")
    return capture


def normalized_segments(plan: list[dict[str, object]]) -> list[dict[str, object]]:
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
    raw_path: Path,
    movie: Path,
    savestate: Path,
    plan: list[dict[str, object]],
    extra_fields: dict[str, tuple[int, str]],
    ranges: list[tuple[int, int]],
) -> bool:
    manifest_path = movie.with_suffix(movie.suffix + ".json")
    if not raw_path.exists() or not movie.exists() or not manifest_path.exists():
        return False
    try:
        manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
        raw = json.loads(raw_path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        return False
    expected_field_map = {
        name: {"address": f"0x{address:08x}", "kind": kind}
        for name, (address, kind) in extra_fields.items()
    }
    expected_ranges = [
        {"start": f"0x{start:08x}", "bytes": size}
        for start, size in ranges
    ]
    return (
        manifest.get("stateSha256") == HARNESS.sha256(savestate)
        and raw.get("stateSha256") == manifest.get("stateSha256")
        and raw.get("movieSha256") == manifest.get("movieSha256")
        and manifest.get("segments") == normalized_segments(plan)
        and raw.get("fieldMap") == expected_field_map
        and raw.get("candidateRanges") == expected_ranges
    )


def capture_suite(
    pid: int,
    port: int,
    savestate: Path,
    obstruction_savestate: Path | None,
    out_dir: Path,
    field_map_path: Path | None,
    settle: float,
    reload_wait: float,
) -> None:
    out_dir = HARNESS.capture_path(out_dir)
    out_dir.mkdir(parents=True, exist_ok=True)
    extra_fields, ranges = load_field_map(field_map_path)
    specs = scenario_specs()

    for index, spec in enumerate(specs, start=1):
        state = obstruction_savestate if spec.get("requiresObstructionState") else savestate
        if state is None:
            print(json.dumps({
                "scenario": spec["id"],
                "index": index,
                "total": len(specs),
                "status": "skipped-no-obstruction-savestate",
            }), flush=True)
            continue
        trial = out_dir / str(spec["id"])
        trial.mkdir(exist_ok=True)
        plan_path = trial / "plan.json"
        plan_path.write_text(json.dumps(plan_for(spec), indent=2) + "\n", encoding="utf-8")
        movie = trial / "trial.p2m2"
        raw = trial / "raw.json"
        plan = HARNESS.load_plan(plan_path)
        if existing_capture_is_current(raw, movie, state, plan, extra_fields, ranges):
            print(json.dumps({
                "scenario": spec["id"],
                "index": index,
                "total": len(specs),
                "status": "existing-capture",
            }), flush=True)
            continue
        HARNESS.build_movie(plan_path, state, movie)
        capture = capture_scenario(
            pid,
            port,
            plan_path,
            movie,
            raw,
            extra_fields,
            ranges,
            settle,
            reload_wait,
        )
        print(json.dumps({
            "scenario": spec["id"],
            "index": index,
            "total": len(specs),
            "samples": len(capture["samples"]),
            "stateSha256": capture["stateSha256"],
        }), flush=True)


def numeric_summary(values: list[float | int]) -> dict[str, object]:
    numeric = [float(value) for value in values]
    changes = sum(a != b for a, b in zip(values, values[1:]))
    return {
        "start": numeric[0],
        "end": numeric[-1],
        "min": min(numeric),
        "max": max(numeric),
        "range": max(numeric) - min(numeric),
        "changes": changes,
    }


def candidate_summary(rows: list[dict[str, object]]) -> list[dict[str, object]]:
    if not rows:
        return []
    keys = list(rows[0]["sample"]["candidate_words"].keys())
    report: list[dict[str, object]] = []
    for key in keys:
        words = [int(row["sample"]["candidate_words"][key]) for row in rows]
        changes = sum(a != b for a, b in zip(words, words[1:]))
        distinct = len(set(words))
        floats = [float(decode(word, "f32")) for word in words]
        finite = [value for value in floats if math.isfinite(value) and abs(value) < 1e8]
        item: dict[str, object] = {
            "address": key,
            "distinctWords": distinct,
            "changes": changes,
            "finitePlausibleF32Samples": len(finite),
        }
        if finite:
            item["f32Min"] = min(finite)
            item["f32Max"] = max(finite)
            item["f32Range"] = max(finite) - min(finite)
        report.append(item)
    return report


def derive_scenario(scenario_id: str, capture: dict[str, object]) -> dict[str, object]:
    rows = list(capture["samples"])
    if not rows:
        raise ValueError(f"{scenario_id} capture contains no samples")
    field_names = [*KNOWN_FIELDS.keys(), *capture.get("fieldMap", {}).keys()]
    fields = {
        name: numeric_summary([row["sample"][name] for row in rows])
        for name in field_names
    }
    right_commands: list[list[int]] = []
    for row in rows:
        right = list(row["right"])
        if not right_commands or right_commands[-1] != right:
            right_commands.append(right)

    player_start = [fields[f"player_pos_{axis}"]["start"] for axis in "xyz"]
    player_end = [fields[f"player_pos_{axis}"]["end"] for axis in "xyz"]
    planar_delta = math.hypot(
        player_end[0] - player_start[0],
        player_end[1] - player_start[1],
    )
    return {
        "id": scenario_id,
        "samples": len(rows),
        "stateSha256": capture.get("stateSha256"),
        "movieSha256": capture.get("movieSha256"),
        "rightStickCommandPath": right_commands,
        "conditionedRightStick": {
            "x": fields["right_conditioned_x"],
            "y": fields["right_conditioned_y"],
        },
        "controlHeading": fields["control_heading"],
        "playerYaw": fields["player_yaw"],
        "playerPosition": {
            "start": player_start,
            "end": player_end,
            "netPlanarDelta": planar_delta,
            "zRange": fields["player_pos_z"]["range"],
        },
        "cameraFields": {
            name: fields[name]
            for name in capture.get("fieldMap", {})
            if name.startswith("camera_")
        },
        "candidateFields": candidate_summary(rows),
    }


def derive_suite(capture_dir: Path) -> dict[str, object]:
    scenarios: list[dict[str, object]] = []
    missing: list[str] = []
    for spec in scenario_specs():
        raw_path = capture_dir / str(spec["id"]) / "raw.json"
        if not raw_path.exists():
            missing.append(str(spec["id"]))
            continue
        raw = json.loads(raw_path.read_text(encoding="utf-8"))
        scenarios.append(derive_scenario(str(spec["id"]), raw))

    by_address: dict[str, dict[str, int]] = {}
    for scenario in scenarios:
        for field in scenario["candidateFields"]:
            by_address.setdefault(field["address"], {})[scenario["id"]] = int(field["changes"])
    interesting = [
        {"address": address, "changesByScenario": changes}
        for address, changes in by_address.items()
        if changes.get("recenter", 0) > changes.get("idle", 0)
        or changes.get("turning", 0) > changes.get("fixed-heading", 0)
        or changes.get("obstruction", 0) > changes.get("idle", 0)
    ]
    interesting.sort(
        key=lambda item: (-sum(item["changesByScenario"].values()), item["address"])
    )
    return {
        "schema": 1,
        "authority": "R&C1 NTSC-U SCUS-97199 / fixed savestate + input recording + PINE",
        "sampleCadence": "one batched PINE sample after each PCSX2 FrameAdvance(1)",
        "knownFields": {
            name: {"address": f"0x{address:08x}", "kind": kind}
            for name, (address, kind) in KNOWN_FIELDS.items()
        },
        "scenarios": scenarios,
        "missingScenarios": missing,
        "candidateShortlist": interesting,
        "evidenceBoundary": [
            "rightStickCommandPath is literal DS2 movie input; conditionedRightStick is native I+0x100/+0x104 state",
            "controlHeading is the proven ordinary target-construction source at 0x00166dd8",
            "cameraFields appear only when a field-map supplies addresses; the default candidate span is not semantically labeled",
            "raw words and per-frame captures remain under ignored captures/; this report retains only derived summaries",
        ],
    }


def write_derived(capture_dir: Path, out: Path) -> dict[str, object]:
    report = derive_suite(capture_dir)
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    return report


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    sub = parser.add_subparsers(dest="command", required=True)

    scenarios = sub.add_parser("scenarios")
    scenarios.add_argument("--out", type=Path)

    capture = sub.add_parser("capture")
    capture.add_argument("--pid", type=int, required=True)
    capture.add_argument("--pine-port", type=int, default=28099)
    capture.add_argument("--savestate", type=Path, required=True)
    capture.add_argument("--obstruction-savestate", type=Path)
    capture.add_argument("--out-dir", type=Path, required=True)
    capture.add_argument("--field-map", type=Path)
    capture.add_argument("--settle", type=float, default=0.05)
    capture.add_argument("--reload-wait", type=float, default=1.0)

    derive = sub.add_parser("derive")
    derive.add_argument("--capture-dir", type=Path, required=True)
    derive.add_argument("--out", type=Path, required=True)
    return parser


def main() -> int:
    args = build_parser().parse_args()
    if args.command == "scenarios":
        result = {"schema": 1, "scenarios": scenario_specs()}
        rendered = json.dumps(result, indent=2) + "\n"
        if args.out:
            args.out.parent.mkdir(parents=True, exist_ok=True)
            args.out.write_text(rendered, encoding="utf-8")
        else:
            print(rendered, end="")
        return 0

    if args.command == "capture":
        capture_suite(
            args.pid,
            args.pine_port,
            args.savestate,
            args.obstruction_savestate,
            args.out_dir,
            args.field_map,
            args.settle,
            args.reload_wait,
        )
        return 0

    report = write_derived(args.capture_dir, args.out)
    print(json.dumps({
        "out": str(args.out),
        "scenarios": len(report["scenarios"]),
        "missingScenarios": report["missingScenarios"],
        "candidateShortlist": len(report["candidateShortlist"]),
    }, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
