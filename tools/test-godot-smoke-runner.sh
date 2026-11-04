#!/usr/bin/env bash
set -euo pipefail
runner=$(cd -- "$(dirname -- "$0")" && pwd)/run-godot-smoke.sh
marker='[probe] PASS:'
cases=0
check() {
  local expected=$1 program=$2 actual=0
  bash "$runner" "$marker" bash -c "$program" >/dev/null 2>&1 || actual=$?
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
echo "[godot-runner-tests] PASS: $cases outcome controls"
