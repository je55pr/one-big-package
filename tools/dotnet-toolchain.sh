#!/usr/bin/env bash
# Shared, side-effect-free SDK/runtime policy for Linux bootstrap and diagnostics.
obp_required_sdk() {
  sed -nE 's/.*"version"[[:space:]]*:[[:space:]]*"([^"]+)".*/\1/p' "$1/global.json" | head -n 1
}

obp_sdk_compatible() {
  local sdk="$1" required="$2"
  [[ "$sdk" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ && "$required" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || return 1
  [[ "${sdk%.*}" == "${required%.*}" ]] &&
    (( 10#${sdk##*.} >= 10#${required##*.} &&
       10#${sdk##*.} / 100 == 10#${required##*.} / 100 ))
}

obp_runtime_present() {
  grep -Eq '^Microsoft\.NETCore\.App 8\.0\.'
}

obp_has_toolchain() {
  local exe="$1" required="$2" sdk
  sdk="$("$exe" --version 2>/dev/null)" || return 1
  obp_sdk_compatible "$sdk" "$required" || return 1
  "$exe" --list-runtimes 2>/dev/null | obp_runtime_present
}
