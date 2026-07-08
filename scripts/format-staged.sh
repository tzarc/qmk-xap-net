#!/usr/bin/env bash
# Copyright 2026 QMK Collaborators
# SPDX-License-Identifier: MIT
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
image="mcr.microsoft.com/dotnet/sdk:10.0-aot"

if [[ $# -eq 0 ]]; then
  exit 0
fi

docker run --rm \
  -u "$(id -u):$(id -g)" \
  -e HOME=/tmp \
  -v "$root:/repo" \
  -w /repo \
  "$image" \
  dotnet format Xap.slnx --include "$@"
