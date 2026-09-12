---
title: Chart
description: ui.chart draws a series as a line across the element with the area beneath it filled.
---

`ui.chart`

## Purpose

`ui.chart` draws a series as a line across the element, with the area beneath the line filled. The series
arrives already normalised, as fractions in `0..1` of the chart's plot band - a chart carrying raw values
would drag a scale, an axis, a unit and a rounding rule onto the wire with it, and two readers would
disagree wherever any of those did. The producer that has the values also has the range they are
meaningful in, and is the only side that knows whether that range is fixed or follows the data.

## Properties

| Property | Meaning | Absent means |
|---|---|---|
| `points` | The series, oldest first, as fractions of the plot band in `0..1` | Nothing is drawn - and equally for an empty series |
| `color` | The line and fill colour, as `#rrggbb` | **The reader's own accent colour** - how this profile spells "the chart has no colour of its own" |
| `plotTop` | Where the plot band begins, a fraction of the element's own height in `0..1` | `0` - the band is the whole element |
| `thickness` | The line's width, a length | Left to the reader |

`color` is a literal colour rather than a theme role because it encodes data, not theme - the same split
`ui.range-bar`'s `startColor` makes. See [Colours and text](/ui/concepts/theming/). Values outside
`0..1` in `points` are clamped rather than rejected, so a series that outgrows a fixed scale flattens
against the top of the band instead of failing the tree.

## Supported children

None. `ui.chart` is a leaf.

## Events and interactions

`ui.chart` declares no events. It is never interactive.

## Layout behaviour

A chart follows the ordinary leaf rule on its parent stack's main axis - `mainSize` or `fill` if declared,
otherwise its content extent. See [Sizing](/ui/concepts/sizing/). Reserve a live numeric readout's
width beside a chart with `ui.text`'s `digits` property, or the readout moves the chart every time its
value gains or loses a digit - see [Text](/ui/components/text/).

**The geometry below is normative**, for the reason `ui.range-bar`'s is: none of it follows from the
properties, and two readers that disagree on it draw visibly different charts. The fixtures in
`ui-model/fixtures/component-profile/` pin it, including a dense series and every clamp branch.

- The plot band spans the element's full width and runs from `plotTop` of its height to its bottom edge.
  `0` in the series is the bottom of that band and `1` is the top.
- Points sit at equal horizontal spacing with the first on the leading edge and the last on the trailing
  edge, joined by straight segments with round joins and caps. A series of exactly one point is drawn as a
  flat line across the whole width at that point's height - a single sample is a value that has held, not a
  dot.
- The line is drawn in `color` at `0.9` opacity and `thickness` wide.
- The area between the line and the element's bottom edge is filled in `color` at `0.16` opacity, with no
  stroke of its own.
- An absent or empty series draws nothing at all - neither line nor fill, and in particular not a flat line
  along the foot of the band, which would read as a real value of zero.

## Example

```csharp
new UiChart
{
    Key = "history",
    Points = UiValue.From(() => state.Value.Samples),
    PlotTop = 0.34,
    Fill = true,
}
```
