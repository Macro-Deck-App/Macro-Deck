---
title: macrodeck-plugin preview
description: Render a plugin's widget previews to PNG files without a running Macro Deck, for store images and release automation.
---

`macrodeck-plugin preview render` loads a plugin against a disposable stub host, opens every
[`[UiPreview]`](/ui/views/developer-preview/) scenario, draws it with the same UI runtime the web client uses, and
writes one PNG per scenario and size. It needs no running Macro Deck and no sign-in, so it also runs in CI.

## Examples

```bash
macrodeck-plugin preview render --project src/DeviceBatteryInfo \
  --size 200x200 --size 420x200 --size 420x420 \
  --scale 2 --theme dark --background transparent \
  --output artifacts/previews
```

```text
Wrote artifacts/previews/battery-tile-200x200.png
Wrote artifacts/previews/battery-tile-420x200.png
Wrote artifacts/previews/battery-tile-420x420.png
Rendered 3 image(s) to artifacts/previews.
```

Every scenario at three sizes, at twice the resolution, with transparent corners.

```bash
macrodeck-plugin preview render --project src/DeviceBatteryInfo --cells 1x1 --cells 2x1 --preview "Low battery"
```

One scenario at deck sizes: a cell is 120 px with a 12 px gap, so `2x1` is 252 by 120 px.

## What it draws

Only widget previews. A widget is laid out in the deck's 120 px reference cell and scaled to the requested
size, exactly as a tile on the deck is, so a size that is not a whole number of cells still looks like a tile.
Configuration views, such as an action editor or a configuration flow, are drawn by the desktop app and are
skipped with a `preview-unsupported` warning. A skipped preview does not fail the run.

The image is the tile: its background and rounded corners are part of the picture. `--background` is what shows
behind the corners.

## Options

Pick exactly one of `--project`, `--executable` or `--artifact`, as for [`run`](/cli/run/) and
[`test`](/cli/test/).

| Option | Meaning |
| --- | --- |
| `--size <W>x<H>` | A size in pixels. Repeatable. Defaults to one deck cell, `120x120`. |
| `--cells <C>x<R>` | A size in deck cells. Repeatable, and combinable with `--size`. |
| `--preview <name>` | Only the scenario with this name or id. Repeatable. Defaults to every scenario. An unknown name is a usage error. |
| `--scale <n>` | Device pixels per pixel, greater than 0 and at most 8. Defaults to `2`, so `200x200` is a 400 by 400 image. |
| `--theme dark\|light` | Defaults to `dark`. |
| `--background <color>` | `transparent` (the default), a color name or a `#hex` color. |
| `--radius <px>` | The tile's corner radius. Defaults to `22`, the deck's default. |
| `--locale <culture>` | The culture dates and numbers are formatted in. Defaults to `en-US`. |
| `--output <dir>` | Where the files go. Defaults to `./previews`. |
| `--browser <path>` | The browser to use, see below. |

A scenario is a static method with no access to the tile, so `--radius` only shapes the tile's corners. A
widget's own safe area comes from its own padding.

Files are named `<scenario>-<width>x<height>.png`, in lower case. When two views declare a scenario with the
same name, both files are prefixed with their view name.

The clock is fixed, so time-based components draw the same on every run.

## The browser

The pixels come from a locally installed Chrome, Chromium or Edge, which the tool starts headless with a
throwaway profile. Nothing is downloaded. It looks, in order, at `--browser`, the `MACRODECK_BROWSER`
environment variable, the usual install locations, and your `PATH`. If none is found the command exits with
`browser-not-found`.

Text is drawn with the fonts installed on the machine, so an image made on a CI runner can differ from one made
on your computer. Render on the same kind of machine every time when the images need to match.

## Exit codes

| Code | Meaning |
| --- | --- |
| 0 | Every selected preview was rendered or skipped, or the plugin declares no previews. |
| 1 | A scenario could not be built or drawn. The other images are still written. |
| 2 | Usage error: a bad size, scale, radius, background or theme, no or several subject selectors, or an unknown `--preview`. |
| 3 | The plugin could not be launched or built, or no browser was found. |
| 4 | Cancelled (Ctrl-C). |

## See also

- [Developer previews](/ui/views/developer-preview/) - writing the scenarios this command renders
- [`run`](/cli/run/) - the same stub host, with the plugin's output streamed
