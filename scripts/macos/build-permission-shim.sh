#!/bin/zsh
set -euo pipefail

script_dir=${0:A:h}
repo_root=${script_dir:h:h}
rid=${1:-}
output_path=${2:-}

if [[ -z "$rid" || -z "$output_path" ]]; then
  print -u2 "Usage: build-permission-shim.sh <osx-arm64|osx-x64> <output-path>"
  exit 64
fi

case "$rid" in
  osx-arm64) architecture=arm64 ;;
  osx-x64) architecture=x86_64 ;;
  *) print -u2 "RID must be osx-arm64 or osx-x64"; exit 64 ;;
esac

case "$output_path" in
  "$repo_root"/artifacts/macos/*) ;;
  *) print -u2 "Unsafe native output path: $output_path"; exit 70 ;;
esac

sdk_path=$(xcrun --sdk macosx --show-sdk-path)
/bin/mkdir -p "${output_path:h}"
xcrun clang \
  -arch "$architecture" \
  -isysroot "$sdk_path" \
  -mmacosx-version-min=14.0 \
  -fobjc-arc \
  -fblocks \
  -Wall \
  -Wextra \
  -Werror \
  -dynamiclib \
  -framework AppKit \
  -framework ApplicationServices \
  -framework AVFoundation \
  -framework Foundation \
  -install_name @rpath/libDictaClonePermissions.dylib \
  "$repo_root/native/macos/DictaClonePermissions.m" \
  -o "$output_path"
