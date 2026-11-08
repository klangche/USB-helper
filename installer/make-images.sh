#!/usr/bin/env bash
# Generates the .msi setup images (#204) from the app icon: the white icon on the blue of the macOS icon (#1F6FEB), so it
# shows on the white setup pages. Needs ImageMagick 6 or 7 (convert). Run it after changing the icon, from any folder:
# installer/make-images.sh
set -euo pipefail

here="$(cd "$(dirname "$0")" && pwd)"
icon="$here/../src/UsbHelper.App/Assets/usb-helper.ico[7]" # the 256 x 256 image
blue='#1F6FEB'

# WixUIDialogBmp, 493 x 312: the welcome and finish pages. WiX writes their text from x = 180, so the blue panel is the
# left 164 pixels, with the icon in its middle.
convert -size 493x312 xc:white \
  \( -size 164x312 "xc:$blue" \) -geometry +0+0 -composite \
  \( "$icon" -resize 112x112 \) -geometry +26+100 -composite \
  -strip -type TrueColor BMP3:"$here/dialog.bmp"

# WixUIBannerBmp, 493 x 58: the top of the other pages. WiX writes the title on the left, so the icon is a blue square
# on the right.
convert -size 493x58 xc:white \
  \( -size 58x58 "xc:$blue" \) -geometry +435+0 -composite \
  \( "$icon" -resize 40x40 \) -geometry +444+9 -composite \
  -strip -type TrueColor BMP3:"$here/banner.bmp"
