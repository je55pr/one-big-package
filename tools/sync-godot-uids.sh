#!/usr/bin/env bash
# Generate missing sidecars using Godot; pass --check for read-only validation.
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
exec node "$root/tools/sync-godot-uids.mjs" "$@"
