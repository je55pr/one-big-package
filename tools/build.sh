#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
exec dotnet build "$root/OneBigPackage.sln" -c "${1:-Debug}" --nologo
