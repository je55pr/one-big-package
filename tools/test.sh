#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
node "$root/tools/sync-godot-uids.mjs" --check
node --test "$root/tools/sync-godot-uids.test.mjs"
exec dotnet test "$root/OneBigPackage.sln" -c "${1:-Debug}" --nologo
