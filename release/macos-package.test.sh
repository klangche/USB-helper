#!/usr/bin/env bash
# Exercises the macOS bundle layout and archive with synthetic publish files.
set -euo pipefail

root=$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)
temporary=$(mktemp -d)
trap 'rm -rf "$temporary"' EXIT
arm64="$temporary/arm64"
x64="$temporary/x64"
bin="$temporary/bin"
output="$temporary/output"
mkdir -p "$arm64" "$x64" "$bin"
printf '#!/bin/sh\necho arm64 apphost\n' > "$arm64/UsbHelper"
printf '#!/bin/sh\necho x64 apphost\n' > "$x64/UsbHelper"
chmod +x "$arm64/UsbHelper" "$x64/UsbHelper"
printf 'managed assembly' > "$arm64/UsbHelper.dll"
cp "$arm64/UsbHelper.dll" "$x64/UsbHelper.dll"
cat > "$bin/lipo" <<'EOF'
#!/usr/bin/env bash
if [[ $# == 3 && -f $1 && $2 == -verify_arch ]]; then
  case "$3" in
    arm64|x86_64) exit 0 ;;
  *) exit 2 ;;
  esac
fi
exit 2
EOF
cat > "$bin/uname" <<'EOF'
#!/usr/bin/env bash
echo "$TEST_ARCH"
EOF
chmod +x "$bin/lipo" "$bin/uname"
PATH="$bin:$PATH" bash "$root/release/build-macos-package.sh" "$arm64" "$x64" 0.8.0-ci "$output"

archive="$output/usb-helper-0.8.0-ci-macos-universal.dmg"
[[ -f $archive && -f $archive.sha256 ]]
mountpoint="$temporary/mounted"
mkdir "$mountpoint"
hdiutil attach -nobrowse -readonly -mountpoint "$mountpoint" "$archive" >/dev/null
trap 'hdiutil detach "$mountpoint" -quiet; rm -rf "$temporary"' EXIT
[[ -f $mountpoint/USB-helper.app/Contents/Info.plist ]]
[[ -f $mountpoint/USB-helper.app/Contents/MacOS/USB-helper ]]
[[ -s $mountpoint/USB-helper.app/Contents/Resources/usb-helper.icns ]]
[[ $(plutil -extract CFBundleIconFile raw "$mountpoint/USB-helper.app/Contents/Info.plist") == usb-helper ]]
[[ -f $mountpoint/USB-helper.app/Contents/Resources/osx-arm64/UsbHelper.dll ]]
[[ -f $mountpoint/USB-helper.app/Contents/Resources/osx-x64/UsbHelper.dll ]]
[[ -f $mountpoint/LICENSE.txt && -f $mountpoint/READ_ME_FIRST.txt ]]
[[ -L $mountpoint/Applications && $(readlink "$mountpoint/Applications") == /Applications ]]
read_first="$mountpoint/READ_ME_FIRST.txt"
quarantine_command='xattr -dr com.apple.quarantine /Applications/USB-helper.app'
grep -Fq "USB-helper 0.8.0-ci" "$read_first"
[[ $(grep -Fxc "   $quarantine_command" "$read_first") == 1 ]]
[[ $(grep -Fxc "$quarantine_command" "$root/README.md") == 1 ]]
grep -Fq 'only from USB-helper.app' "$read_first"
grep -Fq 'does not change Gatekeeper' "$read_first"
grep -Fq 'only from USB-helper.app and does not change' "$root/README.md"
if grep -Eiq 'sudo|spctl|xattr.*[*?]' "$read_first"; then
  echo "READ_ME_FIRST contains an unsafe quarantine instruction." >&2
  exit 1
fi
(cd "$output" && shasum -a 256 -c "$(basename "$archive").sha256")
TEST_ARCH=arm64 PATH="$bin:$PATH" "$mountpoint/USB-helper.app/Contents/MacOS/USB-helper" | grep -Fxq "arm64 apphost"
TEST_ARCH=x86_64 PATH="$bin:$PATH" "$mountpoint/USB-helper.app/Contents/MacOS/USB-helper" | grep -Fxq "x64 apphost"
hdiutil detach "$mountpoint" -quiet
trap 'rm -rf "$temporary"' EXIT
echo "macos-package.sh: all checks passed"
