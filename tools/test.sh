#!/usr/bin/env bash
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
exec dotnet test "$root/OneBigPackage.sln" -c "${1:-Debug}" --nologo
