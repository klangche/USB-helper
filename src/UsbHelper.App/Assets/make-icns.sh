#!/usr/bin/env bash
# Generates usb-helper.icns (the macOS app icon: the white glyph on a rounded blue tile) from usb-helper-macos.svg.
# macOS only (qlmanage, sips, iconutil). Run it after changing the SVG, from any folder:
# src/UsbHelper.App/Assets/make-icns.sh
set -euo pipefail

assets="$(cd "$(dirname "$0")" && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

qlmanage -t -s 1024 -o "$work" "$assets/usb-helper-macos.svg" >/dev/null 2>&1
master="$work/usb-helper-macos.svg.png"
set="$work/usb-helper.iconset"
mkdir "$set"
for size in 16 32 128 256 512; do
  sips -z "$size" "$size" "$master" --out "$set/icon_${size}x${size}.png" >/dev/null
  sips -z "$((size * 2))" "$((size * 2))" "$master" --out "$set/icon_${size}x${size}@2x.png" >/dev/null
done
iconutil -c icns "$set" -o "$assets/usb-helper.icns"
