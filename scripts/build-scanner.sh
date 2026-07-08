#!/usr/bin/env bash
# Copyright 2026 QMK Collaborators
# SPDX-License-Identifier: MIT
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
image="mcr.microsoft.com/dotnet/sdk:10.0-aot"
rid="${1:-linux-x64}"

"$root/scripts/pack-packages.sh" 1.0.0

echo "== Cleaning previous Xap.Scanner build state =="
rm -rf "$root/samples/Xap.Scanner/obj" "$root/samples/Xap.Scanner/bin"

echo "== Publishing Xap.Scanner (Native AOT, $rid) via $image =="
docker run --rm \
  -u "$(id -u):$(id -g)" \
  -e HOME=/tmp \
  -v "$root:/repo" \
  -w /repo \
  "$image" \
  dotnet publish samples/Xap.Scanner/Xap.Scanner.csproj \
    -c Release \
    -r "$rid" \
    --self-contained

out="$root/samples/Xap.Scanner/bin/Release/net10.0/$rid/publish/Xap.Scanner"
if [[ ! -x "$out" ]]; then
  echo "PUBLISH FAIL: expected native binary not found at $out" >&2
  exit 1
fi

echo "AOT publish OK: $out"
file "$out" 2>/dev/null || true
