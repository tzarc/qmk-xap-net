#!/usr/bin/env bash
# Copyright 2026 QMK Collaborators
# SPDX-License-Identifier: MIT
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
image="mcr.microsoft.com/dotnet/sdk:10.0-aot"
version="${1:-1.0.0}"

names=(Xap.SourceGenerator Xap.Core Xap.Hid Xap.WebHid)

echo "== Packing ${names[*]} ($version) via $image =="
docker run --rm \
  -u "$(id -u):$(id -g)" \
  -e HOME=/tmp \
  -v "$root:/repo" \
  -w /repo \
  "$image" \
  dotnet pack Xap.slnx \
    -c Release \
    -o nupkgs \
    -p:Version="$version"

for name in "${names[@]}"; do
  out="$root/nupkgs/$name.$version.nupkg"
  if [[ ! -f "$out" ]]; then
    echo "PACK FAIL: expected package not found at $out" >&2
    exit 1
  fi
  echo "Pack OK: $out"
done
