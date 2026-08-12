#!/bin/zsh
set -euo pipefail
export AVALONIA_TELEMETRY_OPTOUT=1

script_dir=${0:A:h}
repo_root=${script_dir:h:h}
rid=${1:-}
configuration=${CONFIGURATION:-Release}

if [[ -z "$rid" ]]; then
  case $(/usr/bin/uname -m) in
    arm64) rid=osx-arm64 ;;
    x86_64) rid=osx-x64 ;;
    *) print -u2 "Unsupported host architecture"; exit 65 ;;
  esac
fi

if [[ "$rid" != "osx-arm64" && "$rid" != "osx-x64" ]]; then
  print -u2 "RID must be osx-arm64 or osx-x64"
  exit 64
fi

version=$(/usr/bin/sed -n 's:.*<VersionPrefix>\([^<]*\)</VersionPrefix>.*:\1:p' "$repo_root/Directory.Build.props")
if [[ -z "$version" ]]; then
  print -u2 "Could not read VersionPrefix"
  exit 65
fi

artifact_root="$repo_root/artifacts/macos/$version/$rid"
publish_dir="$artifact_root/publish"
app_path="$artifact_root/DictaClone.app"
archive_path="$artifact_root/DictaClone-$version-$rid.zip"

case "$artifact_root" in
  "$repo_root"/artifacts/macos/*) ;;
  *) print -u2 "Unsafe artifact path: $artifact_root"; exit 70 ;;
esac

/bin/rm -rf -- "$artifact_root"
/bin/mkdir -p "$artifact_root"

if [[ ! -f "$repo_root/src/DictaClone.Mac.App/Assets/dictaclone.png" ]]; then
  CLANG_MODULE_CACHE_PATH="${TMPDIR:-/tmp}/dictaclone-clang-cache" \
    /usr/bin/swift \
    "$repo_root/scripts/macos/generate-icon.swift" \
    "$repo_root/src/DictaClone.Mac.App/Assets/dictaclone.png"
fi

dotnet restore "$repo_root/src/DictaClone.Mac.App/DictaClone.Mac.App.csproj" \
  --locked-mode \
  --disable-parallel \
  -m:1
dotnet publish "$repo_root/src/DictaClone.Mac.App/DictaClone.Mac.App.csproj" \
  --configuration "$configuration" \
  --runtime "$rid" \
  --self-contained true \
  --no-restore \
  --disable-build-servers \
  -p:UseSharedCompilation=false \
  -p:DebugSymbols=false \
  -p:DebugType=None \
  -m:1 \
  --output "$publish_dir"

/bin/zsh "$script_dir/build-permission-shim.sh" \
  "$rid" \
  "$publish_dir/libDictaClonePermissions.dylib"

/bin/mkdir -p "$app_path/Contents/MacOS" "$app_path/Contents/Resources"
/bin/cp -R "$publish_dir/." "$app_path/Contents/MacOS/"
if [[ "$rid" == "osx-arm64" ]]; then
  unused_coreml_rid=macos-x64
else
  unused_coreml_rid=macos-arm64
fi
unused_coreml_path="$app_path/Contents/MacOS/runtimes/coreml/$unused_coreml_rid"
if [[ -d "$unused_coreml_path" ]]; then
  /bin/rm -rf -- "$unused_coreml_path"
fi
/bin/mkdir -p "$app_path/Contents/Resources/app"
for candidate in "$app_path/Contents/MacOS"/*(.N); do
  if ! /usr/bin/file "$candidate" | /usr/bin/grep -q 'Mach-O'; then
    name=${candidate:t}
    /bin/mv "$candidate" "$app_path/Contents/Resources/app/$name"
    /bin/ln -s "../Resources/app/$name" "$candidate"
  fi
done
for candidate in "$app_path/Contents/MacOS"/*(.N); do
  name=${candidate:t}
  /bin/ln -s "../../MacOS/$name" \
    "$app_path/Contents/Resources/app/$name"
done
if [[ -d "$app_path/Contents/MacOS/runtimes" ]]; then
  /bin/ln -s "../../MacOS/runtimes" \
    "$app_path/Contents/Resources/app/runtimes"
fi
/bin/cp "$repo_root/packaging/macos/Info.plist" "$app_path/Contents/Info.plist"
/bin/zsh "$script_dir/build-app-icon.sh" \
  "$repo_root/src/DictaClone.Mac.App/Assets/dictaclone.png" \
  "$app_path/Contents/Resources/dictaclone.icns"
/usr/libexec/PlistBuddy -c "Set :CFBundleShortVersionString $version" "$app_path/Contents/Info.plist"
/usr/libexec/PlistBuddy -c "Set :CFBundleVersion $version" "$app_path/Contents/Info.plist"
/bin/chmod 755 "$app_path/Contents/MacOS/DictaClone.Mac.App"

"$script_dir/sign-app.sh" "$app_path" "${DICTACLONE_CODESIGN_IDENTITY:--}"
/usr/bin/ditto -c -k --sequesterRsrc --keepParent "$app_path" "$archive_path"
/usr/bin/shasum -a 256 "$archive_path" > "$archive_path.sha256"

print "$app_path"
print "$archive_path"
