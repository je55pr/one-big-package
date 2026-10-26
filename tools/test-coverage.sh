#!/usr/bin/env bash
set -euo pipefail
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
if ! command -v pwsh >/dev/null 2>&1; then
  echo 'Portable coverage uses PowerShell 7 (pwsh) on Linux; install it and rerun this command.' >&2
  exit 2
fi
exec pwsh -NoProfile -File "$script_dir/test-coverage.ps1" "$@"
