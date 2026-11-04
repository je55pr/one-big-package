#!/usr/bin/env bash
# Positive Godot probes must finish their assertions and retire cleanly.
set -euo pipefail
if [[ $# -lt 2 || -z "$1" ]]; then
  echo 'Usage: run-godot-smoke.sh COMPLETION_MARKER COMMAND [ARGUMENT ...]' >&2
  exit 64
fi
marker=$1
shift
log=$(mktemp "${TMPDIR:-/tmp}/obp-godot-smoke.XXXXXX")
trap 'rm -f -- "$log"' EXIT
set +e
"$@" 2>&1 | tee "$log"
statuses=("${PIPESTATUS[@]}")
set -e
if [[ ${statuses[0]} -ne 0 ]]; then exit "${statuses[0]}"; fi
if [[ ${statuses[1]} -ne 0 ]]; then exit 2; fi
failure='ERROR[[:space:]]*:|SCRIPT ERROR|SHADER ERROR|Unhandled[[:space:]]+exception|FATAL([[:space:]]|:)|E[[:space:]]+[0-9]+:[0-9]{2}:[0-9]{2}|\[[^]]+\][[:space:]]+FAIL|ObjectDB instances (were )?leaked|resources still in use at exit|Leaked instance:|quit preparation failed|(^|[[:space:]])([[:alnum:]_]+\.)*[[:alnum:]_]*Exception([[:space:]]*:|[[:space:]]*$)'
if ! grep -Fq -- "$marker" "$log" || grep -Eiq -- "$failure" "$log"; then
  echo 'Godot probe did not complete cleanly.' >&2
  exit 2
fi
