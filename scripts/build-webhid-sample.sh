#!/usr/bin/env bash
# Copyright 2026 QMK Collaborators
# SPDX-License-Identifier: MIT
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
image="xap-wasm-build:10.0"

if ! docker image inspect "$image" >/dev/null 2>&1; then
  echo "== Building $image (wasm-tools + wasm-experimental workloads + python3 for emcc) =="
  docker build -t "$image" -f "$root/scripts/Dockerfile.wasm-build" "$root/scripts"
fi

"$root/scripts/pack-packages.sh" 1.0.0

echo "== Cleaning previous Xap.WebHid.Sample build state =="
rm -rf "$root/samples/Xap.WebHid.Sample/obj" "$root/samples/Xap.WebHid.Sample/bin"

echo "== Publishing Xap.WebHid.Sample (browser-wasm) via $image =="
docker run --rm \
  -u "$(id -u):$(id -g)" \
  -e HOME=/tmp \
  -v "$root:/repo" \
  -w /repo \
  "$image" \
  dotnet publish samples/Xap.WebHid.Sample/Xap.WebHid.Sample.csproj -c Release

out="$root/samples/Xap.WebHid.Sample/bin/Release/net10.0/publish/wwwroot"
if [[ ! -f "$out/index.html" ]]; then
  echo "PUBLISH FAIL: expected $out/index.html not found" >&2
  exit 1
fi

echo "Publish OK: $out"
echo "Serve it over HTTP(S) (WebHID requires a secure context) and open in a Chromium browser, e.g.:"
echo "  cd $out && python3 -m http.server 8080"
