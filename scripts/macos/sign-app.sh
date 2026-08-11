#!/bin/zsh
set -euo pipefail

if [[ $# -lt 1 || $# -gt 2 ]]; then
  print -u2 "usage: sign-app.sh APP_PATH [IDENTITY]"
  exit 64
fi

script_dir=${0:A:h}
repo_root=${script_dir:h:h}
app_path=${1:A}
identity=${2:-${DICTACLONE_CODESIGN_IDENTITY:--}}
entitlements="$repo_root/packaging/macos/DictaClone.entitlements"

if [[ ! -d "$app_path/Contents/MacOS" || ! -f "$app_path/Contents/Info.plist" ]]; then
  print -u2 "Not a DictaClone application bundle: $app_path"
  exit 66
fi

timestamp_args=(--timestamp)
runtime_args=(--options runtime)
if [[ "$identity" == "-" || "$identity" == "Apple Development:"* ]]; then
  timestamp_args=(--timestamp=none)
fi
if [[ "$identity" == "-" ]]; then
  runtime_args=()
fi

while IFS= read -r candidate; do
  if /usr/bin/file "$candidate" | /usr/bin/grep -q 'Mach-O'; then
    /usr/bin/codesign \
      --force \
      --sign "$identity" \
      "${runtime_args[@]}" \
      "${timestamp_args[@]}" \
      "$candidate"
  fi
done < <(/usr/bin/find "$app_path/Contents/MacOS" -type f -print | /usr/bin/sort -r)

/usr/bin/codesign \
  --force \
  --sign "$identity" \
  "${runtime_args[@]}" \
  "${timestamp_args[@]}" \
  --entitlements "$entitlements" \
  "$app_path"

/usr/bin/codesign --verify --strict --verbose=2 "$app_path"
