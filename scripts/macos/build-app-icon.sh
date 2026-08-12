#!/bin/zsh
set -euo pipefail

if [[ $# -ne 2 ]]; then
  print -u2 "usage: build-app-icon.sh SOURCE_PNG OUTPUT_ICNS"
  exit 64
fi

source_png=${1:A}
output_icns=${2:A}
script_dir=${0:A:h}

if [[ ! -f "$source_png" ]]; then
  print -u2 "Icon source does not exist: $source_png"
  exit 66
fi

CLANG_MODULE_CACHE_PATH="${TMPDIR:-/tmp}/dictaclone-clang-cache" \
  /usr/bin/swift \
  "$script_dir/build-icns.swift" \
  "$source_png" \
  "$output_icns"

if [[ ! -s "$output_icns" ]]; then
  print -u2 "Failed to create application icon: $output_icns"
  exit 70
fi
