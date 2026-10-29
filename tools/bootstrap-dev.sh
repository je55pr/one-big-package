#!/usr/bin/env bash
# Bootstrap a fresh Linux checkout with the SDK from global.json and pinned Godot.
# If the SDK is missing, run ./tools/bootstrap-dotnet.sh first.
set -euo pipefail
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root="$(dirname "$script_dir")"
cd "$root"
source "$script_dir/dotnet-toolchain.sh"
required="$(obp_required_sdk "$root")"

# Prefer only a complete, compatible local SDK/runtime over a working system SDK.
if [ -x "$root/.tools/dotnet/dotnet" ] && obp_has_toolchain "$root/.tools/dotnet/dotnet" "$required"; then
  export DOTNET_ROOT="$root/.tools/dotnet"
  export PATH="$DOTNET_ROOT:$PATH"
fi

bash "$script_dir/check-toolchain.sh"
godot="$(bash "$script_dir/bootstrap-godot.sh" linux_x86_64 | tail -1)"

dotnet restore "$root/OneBigPackage.sln"
dotnet build "$root/OneBigPackage.sln" -c Debug --nologo
"$godot" --headless --path "$root/game" --import

echo
echo "Bootstrap complete. Next: ./tools/test.sh ; ./tools/capture.sh smoke"
