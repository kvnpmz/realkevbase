#!/usr/bin/env bash
# Rebuild the site icons from the header's brand tile.
#
# The tab icon used to be the green "S" from the SaveSimple wordmark, so the tab and the
# header disagreed: the header has always painted the RK tile. These icons are that tile —
# same gradient, same radius ratio, same weight — so the two match.
#
# The colours and initials are hardcoded because this runs without Node; they mirror BRAND
# in src/realkevbase-web/src/data/site.ts. Change one, change the other.
#
# Usage: scripts/build-favicon.sh   (needs ImageMagick 7, writes into public/)
set -euo pipefail

OUT="$(dirname "$0")/../src/realkevbase-web/public"
FROM='#1B44C8'      # ACCENT, the tile's first stop
TO='#14339A'        # the tile's second stop
MID='#173BB1'       # their midpoint, needed to define a 135° ramp
FONT='Inter-ExtraBold'
INITIALS='RK'

# The tile is 34px with a 10px radius and 0.382em initials; those ratios are carried up to
# 1024 rather than eyeballed. Three points define a plane, so sparse-color gives the exact
# corner-to-corner gradient — rotating a vertical one leaves blank corners to crop around.
magick -size 1024x1024 xc: \
  -sparse-color barycentric "0,0 $FROM 1023,1023 $TO 1023,0 $MID" \
  -font "$FONT" -pointsize 460 -kerning -8 -fill white -gravity center \
  -annotate +0+0 "$INITIALS" -depth 8 /tmp/rk-tile.png

# Rounded corners stay transparent, so the icon sits on a dark toolbar as well as a light one.
magick /tmp/rk-tile.png \
  \( -size 1024x1024 xc:none -draw "roundrectangle 0,0,1023,1023,295,295" \) \
  -alpha set -compose DstIn -composite /tmp/rk-rounded.png

magick /tmp/rk-rounded.png -resize 192x192 -depth 8 "$OUT/favicon-192.png"
magick /tmp/rk-rounded.png -resize 32x32 -depth 8 "$OUT/favicon-32.png"
magick /tmp/rk-rounded.png -define icon:auto-resize=48,32,16 "$OUT/favicon.ico"

# iOS masks the touch icon itself and paints transparency black, so that one is full-bleed
# and square, with the letters pulled in for the safe area.
magick -size 1024x1024 xc: \
  -sparse-color barycentric "0,0 $FROM 1023,1023 $TO 1023,0 $MID" \
  -font "$FONT" -pointsize 410 -kerning -8 -fill white -gravity center \
  -annotate +0+0 "$INITIALS" -resize 180x180 -depth 8 "$OUT/apple-touch-icon.png"

rm -f /tmp/rk-tile.png /tmp/rk-rounded.png
echo "wrote favicon-32.png, favicon-192.png, favicon.ico, apple-touch-icon.png to $OUT"
