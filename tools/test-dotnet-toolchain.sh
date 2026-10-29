#!/usr/bin/env bash
# Offline regression checks. Never downloads an SDK or mutates system configuration.
set -euo pipefail
root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
source "$root/tools/dotnet-toolchain.sh"
required="$(obp_required_sdk "$root")"
[[ "$required" == "10.0.403" ]] || { echo "Update test cases for SDK $required" >&2; exit 1; }

for good in 10.0.403 10.0.404 10.0.499; do
  obp_sdk_compatible "$good" "$required" || { echo "Rejected valid SDK $good" >&2; exit 1; }
done
for bad in 8.0.427 10.0.402 10.0.500 10.0.400 11.0.100 10.0.403-preview abc; do
  if obp_sdk_compatible "$bad" "$required"; then echo "Accepted invalid SDK $bad" >&2; exit 1; fi
done

tmp="$(mktemp -d)"
trap 'rm -rf "$tmp"' EXIT
mkdir -p "$tmp/repo/tools" "$tmp/bin"
cp "$root/global.json" "$tmp/repo/global.json"
cp "$root/tools/dotnet-toolchain.sh" "$root/tools/bootstrap-dotnet.sh" "$root/tools/check-toolchain.sh" "$root/tools/build.sh" "$root/tools/test.sh" "$tmp/repo/tools/"
cat > "$tmp/bin/dotnet" <<'EOF'
#!/usr/bin/env bash
case "$1" in
  --version) echo "$OBP_FAKE_SDK" ;;
  --list-runtimes) [ "${OBP_FAKE_RUNTIME:-0}" = "1" ] && echo 'Microsoft.NETCore.App 8.0.33 [/mock/shared/Microsoft.NETCore.App]' ;;
  msbuild) echo "${OBP_FAKE_LANGUAGE:-12.0}" ;;
  build|test|run) pwd >> "$OBP_FAKE_CWD_LOG" ;;
  *) exit 1 ;;
esac
EOF
cat > "$tmp/bin/curl" <<'EOF'
#!/usr/bin/env bash
echo 'installer requested' > "$OBP_INSTALL_MARKER"
exit 79
EOF
chmod +x "$tmp/bin/dotnet" "$tmp/bin/curl"
export PATH="$tmp/bin:$PATH" OBP_FAKE_SDK=10.0.403 OBP_FAKE_RUNTIME=1
export OBP_INSTALL_MARKER="$tmp/install-requested" OBP_FAKE_CWD_LOG="$tmp/wrapper-cwds"
obp_has_toolchain "$tmp/bin/dotnet" "$required"
(cd "$tmp" && bash "$tmp/repo/tools/build.sh" Release && bash "$tmp/repo/tools/test.sh" Release)
[ "$(wc -l < "$OBP_FAKE_CWD_LOG")" -eq 3 ] || { echo 'Build/test wrappers missed mock commands' >&2; exit 1; }
if grep -Fvx "$tmp/repo" "$OBP_FAKE_CWD_LOG"; then echo 'Wrapper did not select repository root' >&2; exit 1; fi
# The diagnostics must find global.json even when called from another directory.
(cd "$tmp" && bash "$tmp/repo/tools/check-toolchain.sh" >/dev/null)
(cd "$tmp" && bash "$tmp/repo/tools/bootstrap-dotnet.sh" >/dev/null)
[ ! -e "$OBP_INSTALL_MARKER" ] || { echo 'Compatible SDK unexpectedly requested install' >&2; exit 1; }

export OBP_FAKE_SDK=10.0.402
if obp_has_toolchain "$tmp/bin/dotnet" "$required"; then echo 'Incompatible SDK accepted' >&2; exit 1; fi
if (cd "$tmp" && bash "$tmp/repo/tools/check-toolchain.sh" >/dev/null 2>&1); then echo 'Diagnostic accepted old SDK' >&2; exit 1; fi
if (cd "$tmp" && bash "$tmp/repo/tools/bootstrap-dotnet.sh" >/dev/null 2>&1); then echo 'Bootstrap accepted old SDK' >&2; exit 1; fi
[ -e "$OBP_INSTALL_MARKER" ] || { echo 'Bootstrap failed to request installation for old SDK' >&2; exit 1; }

export OBP_FAKE_SDK=10.0.403 OBP_FAKE_RUNTIME=0
if obp_has_toolchain "$tmp/bin/dotnet" "$required"; then echo 'Missing .NET 8 runtime accepted' >&2; exit 1; fi
if (cd "$tmp" && bash "$tmp/repo/tools/check-toolchain.sh" >/dev/null 2>&1); then echo 'Diagnostic accepted missing runtime' >&2; exit 1; fi
export OBP_FAKE_RUNTIME=1 OBP_FAKE_LANGUAGE=13.0
if (cd "$tmp" && bash "$tmp/repo/tools/check-toolchain.sh" >/dev/null 2>&1); then echo 'Diagnostic accepted wrong C# language' >&2; exit 1; fi
echo 'Shell SDK/runtime/bootstrap policy checks passed.'
