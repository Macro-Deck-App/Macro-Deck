# Icon assets

`app-icon.png` (1024x1024) is the brand icon produced by `ci/scripts/generate-brand-assets.sh` -
the full artwork with glow and gradients, transparent outside its rounded shape. It feeds the
desktop app icons in this directory and, in turn, the opaque PWA icons shipped by the web client.

## Web client PWA icons

`ui/angular/projects/web-client/public/icons/apple-touch-icon-{180,167,152}.png` and
`icon-{192,512}.png` must be full-bleed **opaque** squares with no alpha channel: iOS applies its
own superellipse mask to `apple-touch-icon-*`, and a source that still carries transparent corners
lets whatever is behind the mask show through on the home screen. `icon-maskable-512.png` is
exempt - its whole point is the safe-zone padding a maskable manifest icon needs - and is
maintained separately.

Regenerate them by hand after a brand change (needs ImageMagick 7, `magick`):

```bash
for s in 180 167 152; do
  magick -size 1024x1024 xc:'#121212' \
    ui/bootstrapper/icons/app-icon.png -gravity center -composite \
    -resize ${s}x${s} -alpha remove -alpha off -strip \
    PNG24:ui/angular/projects/web-client/public/icons/apple-touch-icon-${s}.png
done
for s in 192 512; do
  magick -size 1024x1024 xc:'#121212' \
    ui/bootstrapper/icons/app-icon.png -gravity center -composite \
    -resize ${s}x${s} -alpha remove -alpha off -strip \
    PNG24:ui/angular/projects/web-client/public/icons/icon-${s}.png
done
```

`#121212` is the `background_color`/`theme_color` in `manifest.webmanifest` and `index.html`'s
`<meta name="theme-color">` - keep it in sync with those if the brand palette changes. The
`-alpha remove -alpha off` pair plus the `PNG24:` output prefix are all required together: without
`PNG24:`, ImageMagick may still emit a PNG colour type that carries an (now fully opaque) alpha
channel instead of dropping it, which iOS masks incorrectly the same way. Verify the result with
`node ci/scripts/verify-app-icons.mjs`, which reads each file's IHDR chunk directly and fails on
anything but colour type 2.
