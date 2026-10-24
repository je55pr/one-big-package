#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dotnet run --project "$root/tools/OBP.GodotUidTool/OBP.GodotUidTool.csproj" -- --check --root "$root"
exec dotnet test "$root/OneBigPackage.sln" -c "${1:-Debug}" --nologo
