#!/bin/zsh
set -euo pipefail

if [[ $# -ne 1 ]]; then
  print -u2 "usage: notarize-app.sh APP_PATH"
  exit 64
fi

if [[ -z ${DICTACLONE_CODESIGN_IDENTITY:-} || -z ${DICTACLONE_NOTARY_PROFILE:-} ]]; then
  print -u2 "Set DICTACLONE_CODESIGN_IDENTITY and DICTACLONE_NOTARY_PROFILE"
  exit 64
fi

script_dir=${0:A:h}
app_path=${1:A}
archive_path="${app_path:h}/${app_path:t:r}-notarization.zip"

"$script_dir/sign-app.sh" "$app_path" "$DICTACLONE_CODESIGN_IDENTITY"
/usr/bin/ditto -c -k --sequesterRsrc --keepParent "$app_path" "$archive_path"
/usr/bin/xcrun notarytool submit \
  "$archive_path" \
  --keychain-profile "$DICTACLONE_NOTARY_PROFILE" \
  --wait
/usr/bin/xcrun stapler staple "$app_path"
/usr/bin/xcrun stapler validate "$app_path"
/bin/rm -f -- "$archive_path"
