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
# A freshly written volume can stay busy for a few seconds (Spotlight, fseventsd), and detach then fails (#278). Retry,
# then force; never quiet, so a failure says why.
# hdiutil can unmount the volume and still report "couldn't eject ... Resource busy"; every later attempt then fails
# with "No such file or directory" because the mount point is gone, and the script stopped although the volume was
# detached. A mount point that is no longer mounted counts as detached.
# macOS lists the mount under its real path (/private/var/... for /var/...), so both are matched.
is_mounted() {
  local real
  real=$(cd "$1" 2>/dev/null && pwd -P) || return 1
  mount | grep -qF -e " on $real (" -e " on $1 ("
}
detach() {
  local attempt
  for attempt in 1 2 3 4 5; do
    if hdiutil detach "$1"; then
      return 0
    fi
    if ! is_mounted "$1"; then
      echo "Detaching $1 reported an error, but it is no longer mounted." >&2
      return 0
    fi
    echo "Detaching $1 failed (attempt $attempt of 5); retrying." >&2
    sleep 3
  done
  is_mounted "$1" || return 0
  hdiutil detach -force "$1"
}
# hdiutil on the build runners also answers "Resource busy" for a moment to create, attach or convert, for example right
# after another disk image was detached (#282). Retry such a step, 3 seconds apart; the last failure stops the script.
retry() {
  local attempt
  for attempt in 1 2 3 4; do
    if "$@"; then
      return 0
    fi
    echo "$1 $2 failed (attempt $attempt of 5); retrying." >&2
    sleep 3
  done
  "$@"
}
cleanup() {
  if ((mounted)); then
    detach "$mountpoint" || true
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
# The app icon on the mounted disk (#204): .VolumeIcon.icns in the root, and the custom-icon flag (kHasCustomIcon, 0x0400
# in the Finder flags) on the root folder. The flag can only be set on a mounted, writable disk, so the image is made
# writable first, then compressed.
cp "$script_dir/../src/UsbHelper.App/Assets/usb-helper.icns" "$stage/.VolumeIcon.icns"
writable="$temporary/writable.dmg"
retry hdiutil create -volname USB-helper -srcfolder "$stage" -ov -format UDRW "$writable"
mkdir "$mountpoint"
retry hdiutil attach -nobrowse -readwrite -mountpoint "$mountpoint" "$writable" >/dev/null
mounted=1
xattr -wx com.apple.FinderInfo 0000000000000000040000000000000000000000000000000000000000000000 "$mountpoint"
detach "$mountpoint"
mounted=0
rm -f "$archive"
retry hdiutil convert "$writable" -format UDZO -ov -o "$archive"
hdiutil verify "$archive"
retry hdiutil attach -nobrowse -readonly -mountpoint "$mountpoint" "$archive" >/dev/null
mounted=1
for entry in \
  USB-helper.app/Contents/Info.plist \
  USB-helper.app/Contents/MacOS/USB-helper \
  USB-helper.app/Contents/Resources/usb-helper.icns \
  .VolumeIcon.icns \
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
