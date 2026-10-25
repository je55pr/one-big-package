#!/usr/bin/env bash
# Launch OBP in interactive player mode (Linux): load a Going Commando ISO,
# import LEVEL1 (Oozla), spawn the debug capsule. WASD / mouse / Space / Esc.
#   OBP_GC_ISO=/path/to/gc.iso ./tools/play.sh
#   ./tools/play.sh /path/to/gc.iso 1
set -euo pipefail
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root="$(dirname "$script_dir")"

rac2_iso="${1:-${OBP_GC_ISO:-}}"
gc_level="${2:-1}"

if [ -n "$rac2_iso" ] && [ -f "$rac2_iso" ]; then
  scene_args=(--test-scene player --rac2-iso "$rac2_iso" --gc-level "$gc_level")
  echo "OBP: player mode - LEVEL$gc_level from $rac2_iso"
else
  scene_args=(--test-scene picker)
  echo "No Going Commando ISO found (set OBP_GC_ISO); launching Game Sources." >&2
fi

manifest="$script_dir/godot-toolchain.json"
exe_rel="$(node -e "console.log(require('$manifest').godot.artifacts.linux_x86_64.exe)")"
godot="$root/.tools/godot/$exe_rel"
[ -x "$godot" ] || "$script_dir/bootstrap-godot.sh" linux_x86_64 >/dev/null

[ "${SKIP_BUILD:-}" = "1" ] || dotnet build "$root/game/OBP.Game.csproj" -c Debug --nologo
[ -d "$root/game/.godot" ] || "$godot" --headless --path "$root/game" --import

echo "Controls: WASD move - mouse look - Space jump - F fly/noclip - Tab cursor - Esc quit"
exec "$godot" --path "$root/game" --rendering-method "${RENDERING_METHOD:-gl_compatibility}" -- "${scene_args[@]}"
