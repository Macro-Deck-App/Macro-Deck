---
title: Image
description: ui.image draws a resource handle, fitted into a square box and never cropped.
---

`ui.image`

## Purpose

`ui.image` draws a resource. A tree never carries bytes: you register artwork and reference the handle, so
the same view costs the same whether one client renders it or a hundred - each caches the bytes by hash
rather than receiving them embedded in every widget that shows them. Registering artwork is a host-side API
today; a plugin gets one when the upload path lands - see
[Resources](/sdk/ui/reference/resources/).

## Properties

| Property | Meaning | Absent means |
|---|---|---|
| `source` | The resource to draw | A reader that cannot resolve it draws nothing rather than failing the tree |
| `size` | The edge of the square box the image is fitted into, preserving aspect ratio and never cropping | Left to the reader |
| `transition` | How a *change* of `source` is drawn - `crossfade` | The new image simply replaces the old one |
| `opacity` | How opaque the image is drawn, `0..1` | Fully opaque |
| `brightness` | A multiplier of the image's own luminance, `0..2`, applied before `saturation` | `1` |
| `saturation` | A multiplier of the image's own saturation, `0..2` - `0` draws it in greys | `1` |

`transition: "crossfade"` is normative, not a suggestion: the outgoing artwork is held until the incoming
one has decoded, then revealed over `220 ms` on an ease-out curve, from fully transparent and scaled to
`1.02` about its own centre, to fully opaque at its natural scale. Two readers choosing their own timing
would animate visibly differently. Artwork that fails to decode replaces the outgoing artwork at once, with
no fade - holding the previous artwork would attribute it to whatever the element now stands for.

`opacity` lets what is behind the artwork show through, so what a half-transparent image ends up looking
like depends on the colour behind it. `brightness` changes the artwork itself and looks the same on any
ground - reach for it, not a lower `opacity`, for "the same picture, darker". The `saturation` result is
normative down to its luma coefficients (`luma = 0.213 R + 0.715 G + 0.072 B`, applied after `brightness`):
two readers using different coefficients desaturate the same image to visibly different greys.

## Supported children

None. `ui.image` is a leaf.

## Events and interactions

`ui.image` declares no events. It is never interactive on its own; a tappable image is a `ui.button` with
`source` set, which frames the same artwork with `fit`, `zoom`, `offsetX` and `offsetY` in addition to
`opacity`, `brightness` and `saturation` - see [Button](/sdk/ui/components/button/).

## Layout behaviour

An image sizes to `size` on both axes - it is fitted into a square box. Along its parent stack's main axis
it still follows the ordinary rule: `mainSize` or `fill` if declared, otherwise its `size` is what a
renderer measures it at without a font. See [Sizing](/sdk/ui/concepts/sizing/).

## Example

```csharp
new UiImage
{
    Key = "icon",
    Source = UiValue.Of(handle),
    Size = 0.2,
    Transition = UiComponentImageTransitions.Crossfade,
}
```
