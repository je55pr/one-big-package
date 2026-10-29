#!/usr/bin/env bash
# Install the SDK selected by global.json and the net8.0 runtime locally.
# Does not modify machine-wide .NET installations or configuration.
set -euo pipefail
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root="$(dirname "$script_dir")"
cd "$root"
source "$script_dir/dotnet-toolchain.sh"
version="$(obp_required_sdk "$root")"
[ -n "$version" ] || { echo "Cannot read sdk.version from global.json" >&2; exit 1; }

if command -v dotnet >/dev/null 2>&1 && obp_has_toolchain dotnet "$version"; then
  echo "Compatible SDK $(dotnet --version) and .NET 8 runtime already available."
  exit 0
fi

dest="$root/.tools/dotnet"
if [ -x "$dest/dotnet" ] && obp_has_toolchain "$dest/dotnet" "$version"; then
  echo "Project-local SDK $("$dest/dotnet" --version) and runtime already available."
else
  mkdir -p "$dest"
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o "$dest/dotnet-install.sh"
  chmod +x "$dest/dotnet-install.sh"
  "$dest/dotnet-install.sh" --version "$version" --install-dir "$dest" --no-path
  "$dest/dotnet-install.sh" --runtime dotnet --channel 8.0 --install-dir "$dest" --no-path
  obp_has_toolchain "$dest/dotnet" "$version" || { echo "Installed toolchain failed validation." >&2; exit 1; }
  echo "Installed .NET SDK $version and .NET 8 runtime in $dest."
fi
echo "For this shell, run:"
echo "  export DOTNET_ROOT=\"$dest\""
echo "  export PATH=\"\$DOTNET_ROOT:\$PATH\""
