#!/usr/bin/env bash
# Install a project-local .NET SDK into .tools/dotnet/ when the system has none
# (or too old). Uses Microsoft's official dotnet-install.sh. On a machine that
# already has a recent SDK this is unnecessary — bootstrap-dev.sh will say so.
set -euo pipefail
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root="$(dirname "$script_dir")"
channel="${1:-8.0}"   # 8.0 is the minimum Godot 4.7 needs; newer is fine too
dest="$root/.tools/dotnet"

if command -v dotnet >/dev/null 2>&1; then
  have="$(dotnet --version)"
  if [ "${have%%.*}" -ge "${channel%%.*}" ]; then
    echo "System .NET SDK $have already satisfies >= $channel; nothing to do."
    exit 0
  fi
fi

mkdir -p "$dest"
curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$dest/dotnet-install.sh"
chmod +x "$dest/dotnet-install.sh"
"$dest/dotnet-install.sh" --channel "$channel" --install-dir "$dest"
echo
echo "Installed to $dest. Add to PATH for this shell:"
echo "  export PATH=\"$dest:\$PATH\""
