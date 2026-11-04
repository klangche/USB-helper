#!/usr/bin/env bash
# Bundles both self-contained macOS runtimes into a universal app and disk image.
set -euo pipefail

if (($# != 4)); then
  echo "Usage: $0 <osx-arm64-publish> <osx-x64-publish> <version> <output-dir>" >&2
  exit 2
fi

arm64=$(cd "$1" && pwd)
x64=$(cd "$2" && pwd)
version=$3
core_version=${version%%-*}
output=$(mkdir -p "$4" && cd "$4" && pwd)
script_dir=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
if [[ ! $version =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.-]+)?$ ]]; then
  echo "Invalid release version: $version" >&2
  exit 2
fi

temporary=$(mktemp -d)
mounted=0
mountpoint="$temporary/mounted"
cleanup() {
  if ((mounted)); then
    hdiutil detach "$mountpoint" -quiet
  fi
  rm -rf "$temporary"
}
trap cleanup EXIT
app="$temporary/USB-helper.app"
resources="$app/Contents/Resources"
mkdir -p "$app/Contents/MacOS" "$resources"
for runtime in osx-arm64 osx-x64; do
  source="$arm64"
  architecture=arm64
  if [[ $runtime == osx-x64 ]]; then
    source="$x64"
    architecture=x86_64
  fi
  apphost="$source/UsbHelper"
  if [[ ! -x $apphost ]]; then
    echo "The $runtime publish output has no executable UsbHelper apphost." >&2
    exit 1
  fi
  lipo "$apphost" -verify_arch "$architecture"
  cp -R "$source" "$resources/$runtime"
done

sed "s/__CORE_VERSION__/$core_version/g" "$script_dir/macos/Info.plist" > "$app/Contents/Info.plist"
plutil -lint "$app/Contents/Info.plist"
cp "$script_dir/../src/UsbHelper.App/Assets/usb-helper.icns" "$resources/usb-helper.icns"
cp "$script_dir/macos/USB-helper" "$app/Contents/MacOS/USB-helper"
chmod +x "$app/Contents/MacOS/USB-helper"
stage="$temporary/package"
mkdir -p "$stage"
cp -R "$app" "$stage/"
ln -s /Applications "$stage/Applications"
cp "$script_dir/../LICENSE" "$stage/LICENSE.txt"
sed "s/{version}/$version/g" "$script_dir/macos/READ_ME_FIRST.txt" > "$stage/READ_ME_FIRST.txt"

name="usb-helper-$version-macos-universal.dmg"
archive="$output/$name"
hdiutil create -volname USB-helper -srcfolder "$stage" -ov -format UDZO "$archive"
hdiutil verify "$archive"
mkdir "$mountpoint"
hdiutil attach -nobrowse -readonly -mountpoint "$mountpoint" "$archive" >/dev/null
mounted=1
for entry in \
  USB-helper.app/Contents/Info.plist \
  USB-helper.app/Contents/MacOS/USB-helper \
  USB-helper.app/Contents/Resources/usb-helper.icns \
  USB-helper.app/Contents/Resources/osx-arm64/UsbHelper \
  USB-helper.app/Contents/Resources/osx-x64/UsbHelper \
  LICENSE.txt \
  READ_ME_FIRST.txt; do
  if [[ ! -e $mountpoint/$entry ]]; then
    echo "The macOS disk image has no $entry." >&2
    exit 1
  fi
done
if [[ ! -L $mountpoint/Applications || $(readlink "$mountpoint/Applications") != /Applications ]]; then
  echo "The macOS disk image has no Applications-folder link." >&2
  exit 1
fi
(cd "$output" && shasum -a 256 "$name" > "$name.sha256")
(cd "$output" && shasum -a 256 -c "$name.sha256")
