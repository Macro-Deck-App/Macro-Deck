---
title: Sizing
description: Why every length in a Macro Deck UI tree is a fraction rather than a pixel, and how a view stays correct at any box size.
---

*"Layout" here means arranging nodes inside a view; for device layout descriptors see
[Layout providers](/features/layouts/).* This page is `sizing`, not `layout`, precisely because of that
collision.

## Lengths are fractions, not pixels

A view is drawn at whatever size its box happens to be - a deck cell, a folder's whole surface, a dialog
Macro Deck sized for the client, or a browser window. A `UiLength` is therefore a fraction of the view's
**basis** - the smaller side of its content box - optionally clamped against a fraction of the containing
stack's cross extent:

```csharp
new UiTextRun
{
    Key = "temperature",
    Text = AppStrings.Weather.TemperatureValue(23),
    Size = 0.17,                            // 0.17 x basis
    MainSize = UiSize.FromBasis(0.144, 1.2) // min(0.144 x basis, 1.2 x row height)
}
```

One tree is correct at every size, so a resize never costs a round-trip. Note `Size` (the font) and
`MainSize` (the extent along the parent's main axis) are different properties.

A stack measures a child that declares neither `MainSize` nor `Fill` from what a renderer can work out
without a font: an image is its `Size`, and a text is its line height across the stack, but **nothing at
all along it**. So give every text in a row its own `MainSize` whenever a sibling fills - the filling
sibling is otherwise handed the width those texts need on top of its own, and pushes them out of the box.

## `UiLength.Cell` is Macro Deck-specific

A length can also stop growing altogether once the view passes a size, which is what a caption that
should read the same on a one-cell deck widget and a nine-cell one needs. That cap cannot be a fraction of
the basis - the basis is the thing it has to stop tracking - so it is a fraction of a **cell**, the extent
of one deck cell in the reference space a deck widget view is laid out in:

```csharp
Size = UiSize.Capped(0.11, 13)   // min(0.11 x basis, 13 reference units)
```

`UiLength.Cell` is that unit's value. It is a definition rather than a measurement: a renderer lays a
deck widget out in this space and scales the result to the real cell size, so the number only has to
agree across the wire, never with any device's pixels.

:::note
`UiLength.Cell` only means something where a deck grid exists. A renderer that lays views out on
something other than a cell grid - a folder view, a browser window, a dialog - ignores the clamp rather
than guessing, exactly as it does for `MaxOfCross`.
:::

## Charts and layers

A series is a `ui.chart`: `Points` are fractions of the chart's plot band in `0..1`, drawn as a line with
the area beneath it filled. The normalisation is yours, because you are the side that knows whether the
scale is fixed or follows the data - a chart carrying raw values would drag a scale, an axis and a
rounding rule onto the wire with it. `PlotTop` confines the plot to a band at the foot of the element, and
an empty series draws nothing at all rather than a flat line along that foot, which would read as a real
value of zero.

`ui.layer` is the one container that stacks its children through the depth of the box instead of along an
axis of it: every child gets the whole content box, in declaration order, the first furthest back. That
is how a chart sits behind its labels, and how content is centred on the whole card rather than on the
space the elements above it left over. A layer has no padding, gap or alignment of its own - wrap a child
in a `ui.stack` for those, which is also what lets two layers inset their content differently.

```csharp
new UiLayer
{
    Key = "card",
    Children =
    [
        new UiChart { Key = "chart", Points = points, PlotTop = 0.66 },
        new UiStack { Key = "labels", Padding = 0.07, Children = [title] },
    ],
}
```

A live numeric readout should also reserve its width with `UiTextRun.Digits`, or whatever sits beside it
moves every time the value gains or loses a digit. It is a count of digit widths rather than a length,
because how wide a digit is belongs to the reader's face and size, not to you; it is fractional because a
decimal separator takes materially less room than a digit. A reserved run is drawn with equal digit
advances, so it does not shift as it counts.
