#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$root" # global.json SDK resolution uses the working directory
exec dotnet build "$root/OneBigPackage.sln" -c "${1:-Debug}" --nologo
