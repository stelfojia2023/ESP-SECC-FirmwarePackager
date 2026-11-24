#!/usr/bin/env bash
# Run the Windows desktop test suite from WSL against a fresh copy of this tree.
# Usage: bash tests/run-tests-from-wsl.sh [comma-separated cases]
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
only="${1:-}"

win_temp_raw="$(powershell.exe -NoProfile -Command '$env:TEMP' | tr -d '\r\n')"
if [[ -z "$win_temp_raw" ]]; then
    echo "无法读取 Windows TEMP 目录" >&2
    exit 1
fi
win_temp="$(wslpath -u "$win_temp_raw")"
staging="$(mktemp -d "$win_temp/FirmwarePackager-tests-XXXXXX")"
trap 'rm -rf "$staging"' EXIT

# Copy the current tree so manual edits or stale test binaries cannot leak in.
tar -C "$root" --warning=no-timestamp --exclude=.git --exclude=evidence --exclude=.archify -cf - . | tar -C "$staging" -xf -

win_staging="$(wslpath -w "$staging")"
win_log="$win_staging\\run-tests.log"
args=""
if [[ -n "$only" ]]; then
    ps_only="$(printf '%s' "$only" | awk -F, '{for (i = 1; i <= NF; i++) { printf "%s\"%s\"", (i > 1 ? "," : ""), $i }}')"
    args="-Only @($ps_only)"
fi

set +e
powershell.exe -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass \
    -Command "& '$win_staging\\tests\\run-tests.ps1' $args *> '$win_log'; exit \$LASTEXITCODE"
status=$?
set -e

if [[ -f "$staging/run-tests.log" ]]; then
    # PowerShell 5.1 writes redirected output as UTF-16LE.
    iconv -f UTF-16LE -t UTF-8 "$staging/run-tests.log" 2>/dev/null || cat "$staging/run-tests.log"
fi
exit "$status"
