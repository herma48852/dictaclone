#!/bin/zsh
set -euo pipefail

script_dir=${0:A:h}
repo_root=${script_dir:h:h}
version=$(/usr/bin/sed -n 's:.*<VersionPrefix>\([^<]*\)</VersionPrefix>.*:\1:p' "$repo_root/Directory.Build.props")
release_root="$repo_root/artifacts/macos/$version"
notarize_release=false

if [[ -n ${DICTACLONE_NOTARY_PROFILE:-} ]]; then
  if [[ -z ${DICTACLONE_CODESIGN_IDENTITY:-} ]]; then
    print -u2 "DICTACLONE_NOTARY_PROFILE requires DICTACLONE_CODESIGN_IDENTITY"
    exit 64
  fi

  notarize_release=true
fi

"$script_dir/test.sh"
"$script_dir/build-app.sh" osx-arm64
"$script_dir/build-app.sh" osx-x64
"$script_dir/verify-app.sh" "$release_root/osx-arm64/DictaClone.app"

if $notarize_release; then
  for rid in osx-arm64 osx-x64; do
    app_path="$release_root/$rid/DictaClone.app"
    archive_path="$release_root/$rid/DictaClone-$version-$rid.zip"
    "$script_dir/notarize-app.sh" "$app_path"
    /usr/bin/ditto \
      -c -k --sequesterRsrc --keepParent \
      "$app_path" \
      "$archive_path"
    (
      cd "${archive_path:h}"
      /usr/bin/shasum -a 256 "${archive_path:t}"
    ) > "$archive_path.sha256"
  done

  "$script_dir/verify-app.sh" "$release_root/osx-arm64/DictaClone.app"
fi

checksum_file="$release_root/SHA256SUMS.txt"
(
  cd "$release_root"
  /usr/bin/shasum -a 256 \
    osx-arm64/DictaClone-$version-osx-arm64.zip \
    osx-x64/DictaClone-$version-osx-x64.zip
) > "$checksum_file"

/bin/cp \
  "$repo_root/docs/MACOS_CLEAN_ROOM_INSTALLATION.md" \
  "$release_root/MACOS_CLEAN_ROOM_INSTALLATION.md"
print "$release_root"
