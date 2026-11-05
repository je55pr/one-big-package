#!/usr/bin/env bash
# Godot probes must finish their assertions and retire cleanly.
set -euo pipefail
if [[ $# -lt 2 || -z "$1" ]]; then
  echo 'Usage: run-godot-smoke.sh MARKERS_OR_--missing-ambient-source COMMAND [ARGUMENT ...]' >&2
  exit 64
fi
markers=$1
expected=0
if [[ $markers == '--missing-ambient-source' ]]; then expected=1; fi
shift
log=$(mktemp "${TMPDIR:-/tmp}/obp-godot-smoke.XXXXXX")
checked=$(mktemp "${TMPDIR:-/tmp}/obp-godot-checked.XXXXXX")
trap 'rm -f -- "$log" "$checked"' EXIT
scan=$log
set +e
"$@" 2>&1 | tee "$log"
statuses=("${PIPESTATUS[@]}")
set -e
if [[ ${statuses[0]} -ne $expected ]]; then
  if [[ ${statuses[0]} -ne 0 ]]; then exit "${statuses[0]}"; fi
  exit 2
fi
if [[ ${statuses[1]} -ne 0 ]]; then exit 2; fi
failure='ERROR[[:space:]]*:|SCRIPT ERROR|SHADER ERROR|Unhandled[[:space:]]+exception|FATAL([[:space:]]|:)|E[[:space:]]+[0-9]+:[0-9]{2}:[0-9]{2}|\[[^]]+\][[:space:]]+FAIL|ObjectDB instances (were )?leaked|resources still in use at exit|Leaked instance:|RIDs? of type .* (was|were) leaked|quit preparation failed|(^|[[:space:]])([[:alnum:]_]+\.)*[[:alnum:]_]*Exception([[:space:]]*:|[[:space:]]*$)'
if [[ $expected -eq 1 ]]; then
  allowed='^ERROR: \[ambient-uv-scroll-smoke\] FAIL: System\.IO\.FileNotFoundException:'
  if [[ $(grep -Ec -- "$allowed" "$log" || true) -ne 1 ]]; then exit 2; fi
  # Only this exact expected error header is allowed; inspect all other output.
  sed -E "/$allowed/d" "$log" > "$checked"
  scan=$checked
else
  while IFS= read -r marker; do
    if [[ -z $marker ]] || ! grep -Fq -- "$marker" "$log"; then
      echo 'Godot probe did not complete its assertions.' >&2
      exit 2
    fi
  done <<< "$markers"
fi
if grep -Eiq -- "$failure" "$scan"; then
  echo 'Godot probe did not complete cleanly.' >&2
  exit 2
fi
