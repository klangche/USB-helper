#!/usr/bin/env bash
# Generates usb-helper.ico (the exe and window icon) from usb-helper.svg.
# Needs rsvg-convert (librsvg) and ImageMagick 6 or 7 (convert). Run it after changing the SVG,
# from any folder: src/UsbHelper.App/Assets/make-icon.sh
set -euo pipefail

assets="$(cd "$(dirname "$0")" && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT

# Each size is drawn from the SVG, so small sizes stay sharp.
pngs=()
for size in 16 20 24 32 40 48 64 256; do
  rsvg-convert --width "$size" --height "$size" "$assets/usb-helper.svg" --output "$work/$size.png"
  pngs+=("$work/$size.png")
done

# 32-bit colour with alpha in every size; -strip leaves out dates, so the same SVG always gives the same file.
convert "${pngs[@]}" -type TrueColorAlpha -define png:color-type=6 -strip "$assets/usb-helper.ico"
