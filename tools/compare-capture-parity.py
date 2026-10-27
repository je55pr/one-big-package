#!/usr/bin/env python3
"""Compare private before/after reconciliation evidence; never print payloads or paths."""

import argparse
import gzip
import json
import re
import zlib
from pathlib import Path


def read_json(path):
    return json.loads(path.read_text(encoding="utf-8-sig"))


def read_archive(root, name):
    with gzip.open(root / (name + ".run.jsonl.gz"), "rt", encoding="utf-8") as stream:
        return [json.loads(line) for line in stream if line.strip()]


def stable_header(rows):
    # Assembly IDs identify different builds. StartedUtc and input timestamps are
    # host wall time. Source/build/digest, schemas, profiles and initial facts stay exact.
    stable = {key: value for key, value in rows[0]["Header"].items()
              if key not in ("AssemblyId", "Assemblies", "StartedUtc")}
    if "Assemblies" in rows[0]["Header"]:
        stable["Assemblies"] = sorted(rows[0]["Header"]["Assemblies"])
    # The SDK informational version appends the Git commit. Keep its actual
    # version and other metadata exact, excluding only that full commit suffix.
    if "ApplicationVersion" in stable:
        stable["ApplicationVersion"] = re.sub(r"\+[0-9a-fA-F]{40}$", "+<commit>", stable["ApplicationVersion"])
    return stable


def stable_step(step):
    stable = {key: value for key, value in step.items() if key != "Milliseconds"}
    stable["Events"] = [{key: value for key, value in event.items() if key != "Milliseconds"}
                        for event in step["Events"]]
    return stable


def protocol(rows, per_world=False):
    result = []
    physics = 0
    for row in rows:
        if "Step" not in row:
            continue
        step = stable_step(row["Step"])
        if step["Kind"] == 0:
            physics += 1
        result.extend({"physics": physics, "phase": step["Kind"], "event": event} for event in step["Events"])
        for command in step["Commands"]:
            # Bookmarks flush asynchronously after the last marker in each world,
            # allowing a variable number of idle physics ticks before the next entry.
            if per_world and command["Kind"] == "destination":
                physics = 0
            result.append({"physics": physics, "phase": step["Kind"], "command": command})
    return result


def route_markers(rows):
    positions = {}
    physics = 0
    for row in rows:
        if "Step" not in row:
            continue
        step = row["Step"]
        if step["Kind"] == 0:
            physics += 1
        if any(command["Kind"] == "destination" for command in step["Commands"]):
            physics = 0
        positions[step["Index"]] = {"physics": physics, "phase": step["Kind"]}
    return [{**row["Marker"], "Step": positions[row["Marker"]["Step"]]} for row in rows if "Marker" in row]


def completed(rows):
    ends = [row["End"] for row in rows if "End" in row]
    steps = sum("Step" in row for row in rows)
    return steps > 0 and len(ends) == 1 and ends[0]["Reason"] == "saved" and ends[0]["Steps"] == steps


def compare(before, after):
    checks = []

    def equal(name, left, right):
        checks.append({"Check": name, "Passed": left == right})

    for name in ("capabilities-observed.json", "profile-write.json", "profile-restart.json"):
        left, right = read_json(before / name), read_json(after / name)
        equal(name, left, right)
        for result, label in ((left, "before"), (right, "after")):
            equal(label + " passed acceptance " + name, result["Result"] == "passed" and
                  len(result["Checks"]) > 0 and all(check["Passed"] for check in result["Checks"]), True)
    for name in ("legacy", "route"):
        left, right = read_archive(before, name), read_archive(after, name)
        equal(name + " completed archives", completed(left) and completed(right), True)
        equal(name + " source/profile/initial-state header", stable_header(left), stable_header(right))
        equal(name + " ordered input and physics-aligned commands", protocol(left, name == "route"), protocol(right, name == "route"))
        # World imports and bookmark flushes can take a variable number of process
        # frames. Do not compare their wall-time waiting frames as deterministic ticks.
        if name == "legacy":
            equal("legacy complete steps", [stable_step(row["Step"]) for row in left if "Step" in row],
                  [stable_step(row["Step"]) for row in right if "Step" in row])
            equal("legacy markers and end", [row for row in left if "Marker" in row or "End" in row],
                  [row for row in right if "Marker" in row or "End" in row])
        else:
            left_markers, right_markers = route_markers(left), route_markers(right)
            equal("route physics-aligned markers", left_markers, right_markers)
            equal("five route markers present", len(left_markers) == len(right_markers) == 5, True)
    pattern = r"native encounter trace ([0-9A-F]{64})"
    digests = [re.findall(pattern, (root / "trilogy-gameplay.log").read_text(encoding="utf-8"))
               for root in (before, after)]
    equal("mixed-source native actor digest", digests[0], digests[1])
    equal("native actor evidence present", len(digests[0]) == len(digests[1]) == 1, True)
    for root, label in ((before, "before"), (after, "after")):
        for name in ("legacy-replay.json", "replay-route.json", "rac2-LEVEL1.replay.json",
                     "rac3-TABLE1.replay.json", "obp-fusion-lab.replay.json"):
            result = read_json(root / name)
            equal(label + " same-build " + name, result["Result"] == "matched" and
                  result["BuildChanged"] is False and result["CheckpointsCompared"] > 0 and
                  result["FirstDifference"] is None, True)
    return {"Version": 1, "Passed": all(check["Passed"] for check in checks), "Checks": checks}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("before", type=Path)
    parser.add_argument("after", type=Path)
    parser.add_argument("--out", type=Path)
    args = parser.parse_args()
    try:
        result = compare(args.before, args.after)
    except (OSError, ValueError, KeyError, TypeError, IndexError, AttributeError, EOFError, zlib.error):
        # Missing/incomplete or malformed evidence fails without exposing source paths.
        result = {"Version": 1, "Passed": False, "Error": "Evidence missing or invalid."}
    encoded = json.dumps(result, indent=2)
    if args.out:
        args.out.write_text(encoded + "\n", encoding="utf-8")
    print(encoded)
    return 0 if result["Passed"] else 1


if __name__ == "__main__":
    raise SystemExit(main())
