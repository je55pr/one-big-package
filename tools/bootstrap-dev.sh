#!/usr/bin/env bash
# One-shot dev bootstrap for a fresh Linux checkout: check .NET, fetch the pinned
# Godot, restore/build, generate the import cache.
#   ./tools/bootstrap-dev.sh
# then: ./tools/test.sh ; ./tools/capture.sh smoke
set -euo pipefail
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root="$(dirname "$script_dir")"
manifest="$script_dir/godot-toolchain.json"

min_major="$(node -e "console.log(require('$manifest').dotnet.minimumSdkMajor)")"
sdk="$(dotnet --version)"
if [ "${sdk%%.*}" -lt "$min_major" ]; then
  echo "Need .NET SDK ${min_major}.x or newer; found $sdk" >&2
  echo "Install: https://dotnet.microsoft.com/download or ./tools/bootstrap-dotnet.sh" >&2
  exit 1
fi
echo ".NET SDK $sdk OK"

godot="$("$script_dir/bootstrap-godot.sh" linux_x86_64 | tail -1)"

dotnet restore "$root/OneBigPackage.sln"
dotnet build "$root/OneBigPackage.sln" -c Debug --nologo
"$godot" --headless --path "$root/game" --import

echo
echo "Bootstrap complete. Next: ./tools/test.sh ; ./tools/capture.sh smoke"
