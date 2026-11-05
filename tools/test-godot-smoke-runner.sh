#!/usr/bin/env bash
set -euo pipefail
runner=$(cd -- "$(dirname -- "$0")" && pwd)/run-godot-smoke.sh
marker='[probe] PASS:'
cases=0
check() {
  local expected=$1 program=$2 markers=${3:-$marker} actual=0
  bash "$runner" "$markers" bash -c "$program" >/dev/null 2>&1 || actual=$?
  if [[ $actual -ne $expected ]]; then
    echo "Smoke runner outcome mismatch: expected $expected, got $actual" >&2
    exit 1
  fi
  cases=$((cases + 1))
}
check 0 'printf "[probe] PASS: complete\n"'
check 17 'printf "[probe] PASS: complete\n"; exit 17'
check 2 'printf "[probe] started\n"'
check 2 'printf "[other] PASS: complete\n"'
for failure in 'ERROR: callback' 'SCRIPT ERROR: callback' 'SHADER ERROR: callback' \
  'Unhandled exception. System.InvalidOperationException' 'System.InvalidDataException: invalid' \
  'System.InvalidDataException' 'E 0:00:04:123: callback' '[probe] FAIL: late callback' \
  'WARNING: 2 ObjectDB instances were leaked at exit.' 'WARNING: resources still in use at exit.' \
  '[lifecycle] quit preparation failed: callback'; do
  # These fixed strings contain no quotes or shell metacharacters.
  check 2 "printf '[probe] PASS: complete\\n'; printf '%s\\n' '$failure' >&2"
done
check 2 'printf "[probe] PASS:\nWARNING: 1 RID of type CanvasItem was leaked.\n"'
check 2 'printf "[probe] PASS:\nWARNING: 2 RIDs of type CanvasItem were leaked.\n"'
both=$'[probe] PASS:\n[final] PASS:'
check 0 'printf "[probe] PASS:\n[final] PASS:\n"' "$both"
check 2 'printf "[probe] PASS:\n"' "$both"
check 2 'printf "[final] PASS:\n"' "$both"
missing='ERROR: [ambient-uv-scroll-smoke] FAIL: System.IO.FileNotFoundException: synthetic source'
check 0 "printf '%s\\n' '$missing'; exit 1" '--missing-ambient-source'
check 2 "printf '%s\\n' '$missing'; exit 0" '--missing-ambient-source'
check 2 'printf "ERROR: unrelated failure\n"; exit 1' '--missing-ambient-source'
check 2 "printf '%s\\n' '$missing'; printf 'ERROR: unrelated teardown failure\\n' >&2; exit 1" '--missing-ambient-source'
check 2 "printf '%s\\n' '$missing'; printf 'WARNING: 1 RID of type CanvasItem was leaked.\\n' >&2; exit 1" '--missing-ambient-source'
echo "[godot-runner-tests] PASS: $cases outcome controls"
