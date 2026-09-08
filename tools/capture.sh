#!/usr/bin/env bash
# Deterministic Godot capture on Linux: build -> launch under Xvfb -> screenshot -> exit.
#   ./tools/capture.sh smoke
#   SCENE=smoke FRAME=30 OUT=captures/smoke.png ./tools/capture.sh
set -euo pipefail
script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
root="$(dirname "$script_dir")"

scene="${1:-${SCENE:-smoke}}"
frame="${FRAME:-30}"
gc_iso="${GC_ISO:-${OBP_GC_ISO:-}}"
gc_level="${GC_LEVEL:-1}"
[ "${PICKER:-}" = "1" ] && scene=picker
if [ "${PLAYER:-}" = "1" ]; then scene=player; frame="${FRAME:-260}"; fi
gc_args=()
if [ -n "$gc_iso" ]; then
  [ -f "$gc_iso" ] || { echo "GC ISO not found: $gc_iso" >&2; exit 2; }
  gc_args=(--gc-iso "$gc_iso" --gc-level "$gc_level")
  [ "${VERIFY_HASH:-}" = "1" ] && gc_args+=(--verify-hash)
  [ "${COLLISION_DEBUG:-}" = "1" ] && gc_args+=(--collision-debug)
  if [ "${PICKER:-}" = "1" ]; then : "${OUT:=$root/captures/gc-picker.png}"
  elif [ "${PLAYER:-}" = "1" ]; then : "${OUT:=$root/captures/gc-player-l$gc_level.png}"
  elif [ "${COLLISION_DEBUG:-}" = "1" ]; then : "${OUT:=$root/captures/gc-collision.png}"
  else : "${OUT:=$root/captures/gc-level$gc_level.png}"; fi
fi
out="${OUT:-$root/captures/$scene.png}"
out="$(cd "$(dirname "$out")" 2>/dev/null && pwd || mkdir -p "$(dirname "$out")" && cd "$(dirname "$out")" && pwd)/$(basename "$out")"
rendering_method="${RENDERING_METHOD:-gl_compatibility}"
resolution="${RESOLUTION:-1280x720}"

manifest="$script_dir/godot-toolchain.json"
exe_rel="$(node -e "console.log(require('$manifest').godot.artifacts.linux_x86_64.exe)")"
godot="$root/.tools/godot/$exe_rel"
[ -x "$godot" ] || "$script_dir/bootstrap-godot.sh" linux_x86_64 >/dev/null

[ "${SKIP_BUILD:-}" = "1" ] || dotnet build "$root/game/OneBigPackage.csproj" -c Debug --nologo
[ -d "$root/game/.godot" ] || "$godot" --headless --path "$root/game" --import

mkdir -p "$(dirname "$out")"
xvfb-run -a "$godot" --path "$root/game" --rendering-method "$rendering_method" --resolution "$resolution" \
  -- --test-scene "$scene" --capture-frame "$frame" --capture-out "$out" "${gc_args[@]}"
code=$?
if [ $code -eq 0 ] && [ -f "$out" ]; then echo "capture: $out"; else echo "capture failed ($code)" >&2; fi
exit $code
