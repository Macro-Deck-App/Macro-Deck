# Weather condition icons

Vendored from [Makin-Things/weather-icons](https://github.com/Makin-Things/weather-icons) (MIT, see
`LICENSE`; artwork © ammap.com). Only the icons `WeatherWidgetIcons.IconName` can resolve to are kept.

Two families. The top level holds the **animated** variants, which drive their motion from CSS keyframes
in a `<style>` block inside each SVG and run wherever the file is loaded as an image document - the
earlier objection to them was that the widget was rasterised host-side to a still frame, and that no
longer happens. `static/` holds the **still** variants for the forecast rows, and only the thirteen day
names a row can resolve to: the current condition is the tile's live element, while a column of moving
thumbnails is a table that is hard to read, and a deck full of weather tiles would otherwise animate
dozens of documents at once.

Most files share `id="blur"` for a drop-shadow filter. That is safe because each icon is loaded as its
own document; it would collide if they were ever inlined into one.

Files are embedded resources (see the project file) and are used verbatim, so refreshing them is a
straight copy from upstream.
