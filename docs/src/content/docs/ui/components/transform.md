---
title: Transform
description: ui.transform rotates, scales and shifts its children together about a pivot, so a needle or a compass turns by patching one number.
---

`ui.transform`

## Purpose

`ui.transform` draws its children the way [`ui.layer`](/ui/components/stack-and-layer/) does - every
child across the whole content box, first one furthest back - and then scales, rotates and shifts all of
them together as one picture.

It is the generic way to build gauge needles, compass roses, dials and meters: declare the artwork once and
move it by changing a property. Because every property is a plain number, turning a needle is a
`set-properties` patch carrying `rotation` alone, and the reader redraws it locally - no new image per
update.

## Properties

| Property | Meaning | Absent means |
|---|---|---|
| `rotation` | The turn in degrees, clockwise, about the pivot | `0` |
| `originX` | Where the pivot sits across the box, a fraction of the element's own width. `0.5` is the centre; a value outside `0..1` puts the pivot outside the box | `0.5` |
| `originY` | The same down the element's own height | `0.5` |
| `zoom` | A multiplier scaling the content about the pivot. A value not greater than `0` means `1` | `1` |
| `offsetX` | The content shifted across, a fraction of the element's own width | `0` |
| `offsetY` | The same down the element's own height | `0` |

The order is fixed: `zoom`, then `rotation`, both about the pivot, then the offsets, in the parent's
unrotated axes. `zoom`, `offsetX` and `offsetY` are the same keys a [button](/ui/components/button/)
frames its artwork with; on a button the artwork scales about its own centre and the ranges listed there
apply, on `ui.transform` it scales about the pivot and any finite value is allowed.

`ui.transform` also carries the properties every container does - `mainSize`, `fill` and `answer`. See
[Sizing](/ui/concepts/sizing/).

## Supported children

Any element, any number of children. Transforms nest, and nested transforms compose.

## Layout, clipping and interaction

The transform is visual only. The node's own box, its size on the parent's main axis and its siblings are
laid out as if it were absent, and text inside it is fitted to its untransformed box. `ui.transform` clips
nothing itself; an ancestor that clips, such as a tile or a button, still does.

Presses hit the drawn shape. A `ui.slider` under a non-zero `rotation` has no defined pointer mapping.
`ui.transform` declares no events of its own.

## Older readers

A reader that does not know `ui.transform` draws the node's `fallback`, so a gauge should carry one that
still shows its reading:

```json
{
  "type": "ui.transform",
  "properties": { "rotation": 42, "originX": 0.5, "originY": 0.9 },
  "children": [{ "type": "ui.image", "properties": { "source": { "resourceId": "needle" } } }],
  "fallback": { "type": "ui.text", "properties": { "text": "42 km/h" } }
}
```
