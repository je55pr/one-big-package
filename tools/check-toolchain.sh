#!/usr/bin/env bash
# Diagnose the selected SDK and C# language version from the repo root.
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"
source "$root/tools/dotnet-toolchain.sh"
required="$(obp_required_sdk "$root")"
if ! command -v dotnet >/dev/null 2>&1; then
  echo "No dotnet on PATH. Run ./tools/bootstrap-dotnet.sh and add .tools/dotnet to this shell's PATH." >&2
  exit 1
fi
if ! sdk="$(dotnet --version 2>&1)"; then
  echo "Cannot select .NET SDK $required (global.json: latestPatch): $sdk" >&2
  echo "Run ./tools/bootstrap-dotnet.sh, then add .tools/dotnet to this shell's PATH." >&2
  exit 1
fi
if ! obp_sdk_compatible "$sdk" "$required"; then
  echo "Unexpected SDK $sdk; global.json requires $required or later in the same feature band." >&2
  exit 1
fi
language="$(dotnet msbuild src/OBP.Core/OBP.Core.csproj -getProperty:LangVersion -nologo | tail -n 1)"
if [[ "$language" != "12.0" ]]; then
  echo "Expected C# 12.0, but MSBuild reports '$language'. Check Directory.Build.props." >&2
  exit 1
fi
if ! dotnet --list-runtimes | obp_runtime_present; then
  echo 'The .NET 8 runtime needed to execute net8.0 tools/tests is missing. Run ./tools/bootstrap-dotnet.sh.' >&2
  exit 1
fi
echo "Toolchain OK: .NET SDK $sdk (global.json: >= $required, latestPatch); C# $language; .NET 8 runtime present"
