#!/usr/bin/env bash
# Regenerates every shipped icon and installer graphic from the Macro Deck logo
# and icon asset kit. Run by hand after a brand change, never from CI - the
# outputs are committed so that building the app needs neither the kit nor the
# tooling below.
#
#   ci/scripts/generate-brand-assets.sh [path-to-asset-kit]
#
# Which source feeds which target:
#   Full Icon/MD_Logo.png             -> Windows and Linux app icons, favicons,
#                                        UI logo. The full artwork with glow and
#                                        gradients; it is the brand icon.
#   Full Icon/Favicons/*              -> the 16/32/48 px entries of the .ico and
#                                        favicon.ico. Hand-tuned by the designer
#                                        for those exact sizes, so they beat a
#                                        downscale of the 1024 px master.
#   01_Foreground/Foreground.png      -> the Windows tray icon. The brand icon
#                                        without its background plate, but with
#                                        the glow and gradients the simplified
#                                        artwork drops - they are what keeps the
#                                        keys solid rather than washed out at
#                                        the size the notification area draws.
#   macOS Icons/Macro Deck.icon       -> Assets.car, the adaptive icon macOS 26
#                                        renders itself (light, dark, tinted and
#                                        clear appearances from one source).
#   macOS Icons/.../iOS-Dark-1024     -> icon.icns, the fallback for macOS < 26,
#                                        inset into the standard macOS icon grid.
#   Simplified Lv 4/MD_Simpl_4.svg    -> the macOS and Linux tray icons. No
#                                        glow, no gradients and no strokes,
#                                        which is what survives at the 16-22 px
#                                        a tray actually draws.
#
# macOS-only, because the .icns and the Assets.car both need Apple's tooling:
# iconutil packs the .icns and actool compiles the .icon. actool ships inside
# Xcode, not the command line tools; without it the Assets.car step is skipped
# and the committed one stays as it is. Also needs ImageMagick and Google
# Chrome (headless, as the SVG renderer).
set -euo pipefail

KIT="${1:-$HOME/Downloads/MacroDeck_Logo_and_Icon_Assets}"
REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
ASSETS="$KIT/Assets"
ICONS="$REPO/ui/bootstrapper/icons"
INSTALLER="$REPO/ui/bootstrapper/installer"
MACOS_PKG="$REPO/ui/bootstrapper/packaging/macos"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

[ -d "$ASSETS" ] || { echo "asset kit not found: $ASSETS" >&2; exit 1; }
for tool in magick qlmanage iconutil python3; do
  command -v "$tool" >/dev/null || { echo "missing required tool: $tool" >&2; exit 1; }
done
CHROME='/Applications/Google Chrome.app/Contents/MacOS/Google Chrome'
[ -x "$CHROME" ] || { echo "missing required tool: $CHROME" >&2; exit 1; }

LOGO="$ASSETS/Full Icon/MD_Logo.png"
FAVICONS="$ASSETS/Full Icon/Favicons"
# Named after the kit's current layout, as is SIMPL4 below; the remaining
# sources still carry the folder names of the kit revision this script was
# written against and have to be re-pointed before it runs end to end again.
FOREGROUND="$ASSETS/01_Foreground/Foreground.png"
MAC_ICON="$ASSETS/macOS Icons/Macro Deck.icon"
MAC_FALLBACK="$ASSETS/macOS Icons/Macro Deck Exports/Macro Deck-iOS-Dark-1024x1024@1x.png"
SIMPL4="$ASSETS/04_Simplified/Level-4_No-Glow_Graph_Gradients_Strokes_SmallButtons/Icon.svg"

say() { printf '\n== %s\n' "$1"; }

# Headless Chrome keeps the exact aspect ratio it is given, which is what the
# three composed graphics need - they are laid out to fixed pixel canvases. The
# SVG is wrapped in a page that stretches it over the viewport, so the window
# size alone decides the output size.
#
# It is rendered several times larger than the target and scaled back down:
# partly for antialiasing, but mostly because Chrome refuses to open a window as
# small as the two NSIS bitmaps and silently screenshots a blank page instead.
render_svg_exact() { # <svg> <width> <height> <out.png>
  local dir="$WORK/chrome.$RANDOM" longest scale
  mkdir -p "$dir"
  longest=$(( $2 > $3 ? $2 : $3 ))
  scale=$(( (1400 + longest - 1) / longest ))
  [ "$scale" -lt 1 ] && scale=1
  {
    printf '<!doctype html><meta charset="utf-8"><style>html,body{margin:0;padding:0}svg{display:block;width:100vw;height:100vh}</style>'
    cat "$1"
  } >"$dir/page.html"
  "$CHROME" --headless --disable-gpu --hide-scrollbars \
    --default-background-color=00000000 \
    --window-size="$(($2 * scale)),$(($3 * scale))" \
    --screenshot="$dir/shot.png" "file://$dir/page.html" >/dev/null 2>&1
  magick "$dir/shot.png" -filter Lanczos -resize "$2x$3!" "PNG32:$4"
}

say 'Windows and Linux app icons'
for size in 32 64 128 256 512; do
  magick "$LOGO" -filter Lanczos -resize "${size}x${size}" -strip "PNG32:$WORK/logo-$size.png"
done
cp "$WORK/logo-32.png"  "$ICONS/32x32.png"
cp "$WORK/logo-64.png"  "$ICONS/64x64.png"
cp "$WORK/logo-128.png" "$ICONS/128x128.png"
cp "$WORK/logo-256.png" "$ICONS/128x128@2x.png"
cp "$WORK/logo-512.png" "$ICONS/icon.png"
magick "$LOGO" -strip "PNG32:$ICONS/app-icon.png"

# Windows reads whichever entry matches the current DPI, so the .ico carries the
# designer's per-size renderings where they exist and downscales for the rest.
# Every entry is a PNG payload, as in the icon this replaces.
say 'icon.ico'
pack_ico() { # <out.ico> <png>...
  python3 - "$@" <<'PY'
import struct, sys
out, pngs = sys.argv[1], sys.argv[2:]
entries = []
for path in pngs:
    data = open(path, 'rb').read()
    width, height = struct.unpack('>II', data[16:24])
    entries.append((width, height, data))
entries.sort(key=lambda e: e[0])
offset = 6 + 16 * len(entries)
directory, payload = b'', b''
for width, height, data in entries:
    directory += struct.pack('<BBBBHHII', width % 256, height % 256, 0, 0, 1, 32, len(data), offset)
    payload += data
    offset += len(data)
open(out, 'wb').write(struct.pack('<HHH', 0, 1, len(entries)) + directory + payload)
PY
}
for size in 16 32 48; do
  magick "$FAVICONS/Favicon_MD_Logo_${size}px.png" -strip "PNG32:$WORK/ico-$size.png"
done
for size in 24 64 256; do
  magick "$LOGO" -filter Lanczos -resize "${size}x${size}" -strip "PNG32:$WORK/ico-$size.png"
done
pack_ico "$ICONS/icon.ico" "$WORK"/ico-*.png

say 'icon.icns'
# macOS icons do not fill their canvas: the artwork sits in an 824 px box inside
# the 1024 px tile and casts a soft shadow, which is what keeps a Dock row of
# icons optically the same size. The kit's export is edge to edge because it is
# an iOS/macOS 26 tile, so it has to be inset here.
magick "$MAC_FALLBACK" -filter Lanczos -resize 824x824 "PNG32:$WORK/mac-art.png"
magick -size 1024x1024 xc:none \
  \( "$WORK/mac-art.png" -alpha extract -morphology Dilate Octagon:2 -blur 0x10 \
     -background black -alpha shape -channel A -evaluate multiply 0.45 +channel \) \
  -geometry +100+108 -composite \
  "$WORK/mac-art.png" -geometry +100+100 -composite \
  "PNG32:$WORK/mac-1024.png"
rm -rf "$WORK/icon.iconset"
mkdir -p "$WORK/icon.iconset"
for pair in 16:icon_16x16 32:icon_16x16@2x 32:icon_32x32 64:icon_32x32@2x \
            128:icon_128x128 256:icon_128x128@2x 256:icon_256x256 512:icon_256x256@2x \
            512:icon_512x512 1024:icon_512x512@2x; do
  magick "$WORK/mac-1024.png" -filter Lanczos -resize "${pair%%:*}x${pair%%:*}" \
    -strip "PNG32:$WORK/icon.iconset/${pair#*:}.png"
done
iconutil -c icns "$WORK/icon.iconset" -o "$ICONS/icon.icns"

say 'macOS adaptive icon (Assets.car)'
rm -rf "$MACOS_PKG/Macro Deck.icon"
cp -R "$MAC_ICON" "$MACOS_PKG/"
find "$MACOS_PKG/Macro Deck.icon" -name .DS_Store -delete
if command -v actool >/dev/null; then
  mkdir -p "$WORK/car"
  actool "$MACOS_PKG/Macro Deck.icon" \
    --compile "$WORK/car" --app-icon 'Macro Deck' \
    --output-partial-info-plist "$WORK/car-partial.plist" \
    --platform macosx --minimum-deployment-target 11.0 \
    --enable-on-demand-resources NO >/dev/null
  cp "$WORK/car/Assets.car" "$MACOS_PKG/Assets.car"
else
  echo 'actool not found (needs Xcode, not just the command line tools) - keeping the committed Assets.car'
fi

say 'tray icons'
# The macOS and Linux tray artwork drops the icon's background plate: a menu bar
# or panel icon has to sit on whatever colour is behind it. The macOS one is
# additionally flattened to a single black silhouette, which is what a template
# image is - macOS recolours it for the menu bar's appearance and for selection.
#
# Windows drops the plate as well, but takes the foreground artwork rather than
# the simplified one: the notification area sits next to icons that are all
# plateless, and the glow and gradients are what stop the keys from washing out
# there. The export is trimmed because it carries a margin the tray cannot
# afford at 16 px.
python3 - "$SIMPL4" "$WORK/tray-color.svg" "$WORK/tray-mono.svg" <<'PY'
import re, sys
src, colour_out, mono_out = sys.argv[1:4]
head, body = open(src).read().split('</defs>')
body = body.replace('</svg>', '')
body = re.sub(r'<rect class="cls-1"[^>]*/>', '', body, count=1)  # the background plate
open(colour_out, 'w').write(head + '</defs>' + body.strip() + '</svg>')
flat = re.sub(r'\sclass="cls-\d+"', '', body).strip()
open(mono_out, 'w').write(
    '<svg viewBox="0 0 2000 2000" xmlns="http://www.w3.org/2000/svg">'
    f'<g fill="#000000">{flat}</g></svg>')
PY
for variant in color mono; do
  # Chrome rather than a QuickLook thumbnail: QuickLook flattens onto opaque
  # white, and dropping the background plate above is pointless if the renderer
  # hands one straight back. An opaque plate is invisible in the colour variant
  # (white on white) but decides the whole macOS icon, because a template image
  # is drawn from its alpha alone - every opaque pixel becomes menu-bar
  # foreground, so a plateless glyph and a full white square are the difference
  # between the logo and a solid block.
  render_svg_exact "$WORK/tray-$variant.svg" 1024 1024 "$WORK/tray-$variant-raw.png"
  # The keys only cover ~79% of the source canvas; a tray icon wants the glyph
  # closer to the edges than an app icon does.
  magick "$WORK/tray-$variant-raw.png" -trim +repage -filter Lanczos -resize 922x922 \
    -background none -gravity center -extent 1024x1024 "PNG32:$WORK/tray-$variant.png"
done
magick "$WORK/tray-color.png" -filter Lanczos -resize 44x44 -strip "PNG32:$ICONS/tray-icon.png"
magick "$WORK/tray-mono.png"  -filter Lanczos -resize 44x44 -strip "PNG32:$ICONS/tray-icon-macos.png"
magick "$FOREGROUND" -trim +repage -filter Lanczos -resize 32x32 \
  -background none -gravity center -extent 32x32 -strip "PNG32:$ICONS/tray-icon-win.png"

say 'Angular favicons and UI logo'
for app in desktop-ui web-client; do
  public="$REPO/ui/angular/projects/$app/public"
  cp "$ICONS/icon.ico" "$public/favicon.ico"
  cp "$WORK/logo-512.png" "$public/images/logo.png"
done

say 'installer and DMG graphics'
# Each of the three graphics is drawn by an SVG next to its output, with the app
# icon embedded as a data URI so that the SVG alone reproduces the bitmap.
python3 - "$WORK/logo-256.png" "$WORK/icon-data-uri.txt" <<'PY'
import base64, sys
data = base64.b64encode(open(sys.argv[1], 'rb').read()).decode()
open(sys.argv[2], 'w').write('data:image/png;base64,' + data)
PY
for target in "$INSTALLER/header:150:57" "$INSTALLER/sidebar:164:314" "$MACOS_PKG/dmg-background:1320:1000"; do
  path="${target%%:*}"; rest="${target#*:}"; width="${rest%%:*}"; height="${rest#*:}"
  name="$(basename "$path")"
  python3 - "$path.svg" "$WORK/icon-data-uri.txt" "$WORK/$name.svg" <<'PY'
import sys
template, uri_path, out = sys.argv[1:4]
open(out, 'w').write(open(template).read().replace('APP_ICON_DATA_URI', open(uri_path).read()))
PY
  render_svg_exact "$WORK/$name.svg" "$width" "$height" "$WORK/$name-raw.png"
  case "$path" in
    *dmg-background)
      # 2x the 660x500 pt DMG window; the DPI stamp is what makes Finder draw it
      # at 660x500 pt instead of 1320x1000.
      magick "$WORK/dmg-background-raw.png" -strip "PNG32:$path.png"
      sips -s dpiWidth 144 -s dpiHeight 144 "$path.png" >/dev/null
      ;;
    *)
      # NSIS wants a bottom-up 24-bit BMP at exactly the configured size.
      magick "$WORK/$name-raw.png" \
        -background white -alpha remove -alpha off -type TrueColor -strip "BMP3:$path.bmp"
      ;;
  esac
done

say 'done'
git -C "$REPO" status --short -- ui ci
