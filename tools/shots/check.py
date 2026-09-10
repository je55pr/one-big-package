#!/usr/bin/env python3
"""Diff shot-capture sidecars against committed goldens.

Usage: check.py <shots-dir> <golden-dir>

Compares only the deterministic keys (geometry counts, camera pose, overlay
state, shot spec). A missing golden is written for review and reported, not
failed. A changed golden fails (exit 1).
"""
import json
import sys
from pathlib import Path

VOLATILE = {"renderedFrames", "engine", "renderer", "width", "height",
            "savePngResult", "worldSwitches", "animClockSeconds"}


def stable(d: dict) -> dict:
    return {k: v for k, v in d.items() if k not in VOLATILE}


def main() -> int:
    shots_dir, golden_dir = Path(sys.argv[1]), Path(sys.argv[2])
    golden_dir.mkdir(parents=True, exist_ok=True)

    failed = wrote = 0
    for sidecar in sorted(shots_dir.glob("*.json")):
        cur = stable(json.loads(sidecar.read_text()))
        gold_path = golden_dir / sidecar.name

        if not gold_path.exists():
            gold_path.write_text(json.dumps(cur, indent=2, sort_keys=True) + "\n", newline="\n")
            print(f"  NEW  {sidecar.name}  (golden written -- review + commit)")
            wrote += 1
            continue

        gold = stable(json.loads(gold_path.read_text()))
        if cur == gold:
            print(f"  ok   {sidecar.name}")
            continue

        failed += 1
        print(f"  FAIL {sidecar.name}")
        for key in sorted(set(cur) | set(gold)):
            if cur.get(key) != gold.get(key):
                print(f"         {key}: golden={gold.get(key)!r}  now={cur.get(key)!r}")

    if wrote:
        print(f"\n{wrote} new golden(s) written.")
    if failed:
        print(f"\n{failed} shot(s) differ from golden.")
        return 1
    return 0


if __name__ == "__main__":
    sys.exit(main())
