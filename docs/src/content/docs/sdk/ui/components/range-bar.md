---
title: Range bar
description: ui.range-bar draws a horizontal track carrying one gradient-filled span, with an optional point marker.
---

`ui.range-bar`

## Purpose

`ui.range-bar` draws a horizontal track carrying one gradient-filled span, with an optional point marker -
a non-interactive readout of a range and, optionally, a point within it: a forecast's low-to-high span with
today's value marked on it, a level meter, a position on a fixed scale. It is the non-interactive
counterpart of `ui.slider`, which shares its track and thumb geometry but adds dragging.

## Properties

| Property | Meaning | Absent means |
|---|---|---|
| `start` | Where the filled span begins, a fraction of the track in `0..1` | - |
| `end` | Where the filled span ends, a fraction of the track in `0..1` | - |
| `startColor` | The colour at `start`, as `#rrggbb` | - |
| `endColor` | The colour at `end`, as `#rrggbb` | - |
| `marker` | Where the point marker sits, a fraction of the track in `0..1` | No marker is drawn |
| `thickness` | The track's thickness on the cross axis, a length | Left to the reader |

`startColor` and `endColor` are literal colours rather than theme roles because they encode data, not
theme: what a value looks like must not change when the reader switches themes - see
[Colours and text](/sdk/ui/concepts/theming/).

## Supported children

None. `ui.range-bar` is a leaf.

## Events and interactions

`ui.range-bar` declares no events. It is never interactive - drag the same shape with `ui.slider` instead.

## Layout behaviour

The track spans the element's full main-axis extent and is `thickness` tall on the cross axis; the element
itself follows the ordinary leaf rule on its parent stack's main axis - `mainSize` or `fill` if declared,
otherwise its content extent. See [Sizing](/sdk/ui/concepts/sizing/).

**The geometry below is normative**, not a suggestion: `ui.range-bar` is one of the few primitives whose
appearance is not fully determined by its properties, and two readers that disagree on it draw visibly
different widgets. A reader implements it exactly, and the fixtures in
`ui-model/fixtures/component-profile/` resolve it at two sizes so a new renderer can check itself.

- The track is centred on the cross axis and has fully rounded ends. The unfilled track is painted in the
  reader's tertiary surface colour.
- The filled span runs from `start` to `end`, with a linear gradient from `startColor` to `endColor` along
  the main axis. It carries the track's own fully rounded ends, so a span that stops short of either end
  still reads as a pill rather than a cut-off block.
- The marker, when present, is a filled disc of radius `0.75 * thickness` painted in the reader's primary
  text colour - not in `endColor`, which would leave it invisible wherever it lands on its own gradient -
  ringed by a stroke of width `0.28 * thickness` in the widget's own background colour, so it reads as
  sitting above the track.
- The marker's centre is inset from both ends by `radius + strokeWidth / 2` so the ring never clips; when
  the track is narrower than twice that inset, the marker is centred instead.

## Example

A forecast row's low-to-high span, with today's reading marked on it:

```csharp
new UiRangeBar
{
    Key = "range",
    Start = 0.2,
    End = 0.6,
    StartColor = "#2b6cee",
    EndColor = "#ee2b2b",
    Marker = 0.4,
    Thickness = UiSize.FromBasis(0.03, 0.35),
    Fill = true,
}
```
