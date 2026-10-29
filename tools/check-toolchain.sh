#!/usr/bin/env bash
# Diagnose the selected SDK and C# language version from the repo root.
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root"
required="$(sed -nE 's/.*"version"[[:space:]]*:[[:space:]]*"([^"]+)".*/\1/p' global.json | head -1)"
if ! command -v dotnet >/dev/null 2>&1; then
  echo "No dotnet on PATH. Run ./tools/bootstrap-dotnet.sh and add .tools/dotnet to this shell's PATH." >&2
  exit 1
fi
if ! sdk="$(dotnet --version 2>&1)"; then
  echo "Cannot select .NET SDK $required (global.json: latestPatch): $sdk" >&2
  echo "Run ./tools/bootstrap-dotnet.sh, then add .tools/dotnet to this shell's PATH." >&2
  exit 1
fi
if [[ ! "$sdk" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] ||
   [[ "${sdk%.*}" != "${required%.*}" ]] ||
   (( ${sdk##*.} < ${required##*.} || ${sdk##*.} / 100 != ${required##*.} / 100 )); then
  echo "Unexpected SDK $sdk; global.json requires $required or later in the same feature band." >&2
  exit 1
fi
language="$(dotnet msbuild src/OBP.Core/OBP.Core.csproj -getProperty:LangVersion -nologo | tail -n 1)"
if [[ "$language" != "12.0" ]]; then
  echo "Expected C# 12.0, but MSBuild reports '$language'. Check Directory.Build.props." >&2
  exit 1
fi
if ! dotnet --list-runtimes | grep -Eq '^Microsoft\.NETCore\.App 8\.0\.'; then
  echo 'The .NET 8 runtime needed to execute net8.0 tools/tests is missing. Run ./tools/bootstrap-dotnet.sh.' >&2
  exit 1
fi
echo "Toolchain OK: .NET SDK $sdk (global.json: >= $required, latestPatch); C# $language; .NET 8 runtime present"
