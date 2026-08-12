#!/bin/zsh
set -euo pipefail

if [[ $# -ne 1 ]]; then
  print -u2 "usage: verify-app.sh APP_PATH"
  exit 64
fi

app_path=${1:A}
info="$app_path/Contents/Info.plist"
executable="$app_path/Contents/MacOS/DictaClone.Mac.App"
icon_file=$(/usr/libexec/PlistBuddy \
  -c "Print :CFBundleIconFile" \
  "$info")
icon_path="$app_path/Contents/Resources/$icon_file"

/usr/bin/plutil -lint "$info"
/usr/bin/codesign --verify --strict --verbose=2 "$app_path"
/usr/bin/codesign -d --entitlements - "$app_path" >/dev/null

if [[ ! -x "$executable" ]]; then
  print -u2 "Bundle executable is missing: $executable"
  exit 66
fi

if [[ ! -s "$icon_path" ]] ||
   ! /usr/bin/file "$icon_path" | /usr/bin/grep -q 'Mac OS X icon'; then
  print -u2 "Bundle application icon is missing or invalid: $icon_path"
  exit 66
fi

signature_info=$(/usr/bin/codesign -dv "$app_path" 2>&1)
if [[ "$signature_info" == *'Signature=adhoc'* ]]; then
  print "Skipping Gatekeeper assessment for an ad-hoc development signature."
else
  /usr/sbin/spctl --assess --type execute --verbose=2 "$app_path"
fi

"$executable" --smoke-test
print "macOS bundle verification passed."
