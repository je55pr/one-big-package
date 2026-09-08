#!/usr/bin/env bash
# Download and verify the pinned Godot .NET editor into .tools/godot/ (git-ignored).
# Reads tools/godot-toolchain.json. Idempotent.
set -euo pipefail

script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
repo_root="$(dirname "$script_dir")"
godot_dir="$repo_root/.tools/godot"
manifest="$script_dir/godot-toolchain.json"

platform="${1:-linux_x86_64}"   # linux_x86_64 | win64

read_json() { node -e "const m=require('$manifest');console.log(m.godot$1)"; }
version="$(read_json '.version')"
base_url="$(read_json '.baseUrl')"
file="$(read_json ".artifacts.${platform}.file")"
sha512="$(read_json ".artifacts.${platform}.sha512")"
exe_rel="$(read_json ".artifacts.${platform}.exe")"

mkdir -p "$godot_dir"
archive="$godot_dir/$file"
exe="$godot_dir/$exe_rel"

verify() { [ -f "$1" ] && echo "$2  $1" | sha512sum -c --status; }

if [ -x "$exe" ] && [ "${FORCE:-}" != "1" ]; then
  echo "Godot $version already present: $exe"
  echo "$exe"
  exit 0
fi

if [ "${FORCE:-}" = "1" ] || ! verify "$archive" "$sha512"; then
  echo "Downloading $base_url/$file"
  curl -fL --retry 3 -o "$archive" "$base_url/$file"
fi

verify "$archive" "$sha512" || { echo "SHA-512 mismatch for $archive" >&2; exit 1; }
echo "SHA-512 OK: $file"

unzip -oq "$archive" -d "$godot_dir"
chmod +x "$exe" 2>/dev/null || true
[ -x "$exe" ] || { echo "Extraction did not produce $exe" >&2; exit 1; }
echo "Godot $version ready: $exe"
echo "$exe"
