#!/usr/bin/env bash
# Use the repository's existing .NET SDK and the pinned Godot .NET editor.
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
exec dotnet run --project "$root/tools/OBP.GodotUidTool/OBP.GodotUidTool.csproj" -- "$@" --root "$root"
