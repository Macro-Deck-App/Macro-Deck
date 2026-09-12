---
title: Image
description: Draws a resource handle, fitted into a square box and never cropped.
---

Draws a registered resource, fitted into a square box. A tree never carries image bytes, only a handle
that each client fetches and caches by hash.

`ui.image`

## Example

```csharp
new UiImage
{
    Key = "icon",
    Source = UiValue.From(() => state.Value.ConditionIcon),
    Size = 0.16,
    Transition = UiComponentImageTransitions.Crossfade,
}
```

A weather-condition icon that fades to the new one when the condition changes, adapted from the built-in
Weather widget. `ConditionIcon` is a `UiResource` handle.

Registering artwork is a host-side API today; plugins get one when the upload path lands. See
[Resources](/ui/reference/resources/).

## Changing artwork smoothly

```csharp
Transition = UiComponentImageTransitions.Crossfade,
```

Without `Transition`, a new `Source` simply replaces the old image.

## Dimming artwork

```csharp
Brightness = 0.6,
Saturation = 0.55,
```

A paused cover, as the built-in Music player draws it. `Brightness` changes the artwork itself and looks the
same on any background - use it, not a lower `Opacity`, for "the same picture, darker". `Opacity` lets what
is behind show through. `Saturation = 0` draws in greys.

## A source that may be missing

```csharp
Source = UiValue.Optional(() => state.Value.Icon is { } icon
    ? UiValue.Of(icon)
    : UiValue.None<UiResource>()),
```

An absent or unresolvable source draws nothing. Wrap the image in a `UiWhen` instead if it
should not take up space either.

## Properties

| Property | Values | Default | Meaning |
|---|---|---|---|
| `Source` (`source`) | `UiResource` handle | Nothing is drawn | The resource to draw. |
| `Size` (`size`) | `UiSize` length | Left to the reader | The edge of the square box the image is fitted into, preserving aspect ratio and never cropped. |
| `Transition` (`transition`) | `UiComponentImageTransitions`: `crossfade` | The new image replaces the old at once | How a change of `Source` is drawn. |
| `Opacity` (`opacity`) | `0..1` | Fully opaque | How opaque the image is drawn. |
| `Brightness` (`brightness`) | `0..2` | `1` | A multiplier of the image's own luminance. |
| `Saturation` (`saturation`) | `0..2` | `1` | A multiplier of the image's own saturation; `0` is greyscale. |
| `MainSize` (`mainSize`), `Fill` (`fill`) | See [Sizing](/ui/concepts/sizing/) | `Size` | The image's extent along the parent stack's main axis. |

## Events

None. `ui.image` is never interactive. For a tappable image, use a [Button](/ui/components/button/) with
`Source` set; it adds `Fit`, `Zoom`, `OffsetX` and `OffsetY` framing on top of `Opacity`, `Brightness` and
`Saturation`.

## Children

None. `ui.image` is a leaf.

## Layout

An image is `size` on both axes, fitted into a square box. Along its parent stack's main axis it takes
`mainSize` or `fill` if declared, otherwise `size` is what a reader measures it at without a font. See
[Sizing](/ui/concepts/sizing/).

## Reader behaviour

- A `source` that cannot be resolved draws nothing; it never fails the tree.
- Fit the image into the square box preserving aspect ratio; never crop.
- `crossfade` is normative: hold the outgoing artwork until the incoming one has decoded, then reveal it over
  220 ms on an ease-out curve, from fully transparent at scale 1.02 about its centre to fully opaque at
  scale 1.
- Artwork that fails to decode replaces the outgoing artwork at once, with no fade.
- `brightness` is normative: multiply each channel by the value, before `saturation` and before
  compositing at `opacity`, and clamp each channel rather than wrapping it.
- `saturation` is normative: `out = luma + saturation * (channel - luma)`, with
  `luma = 0.213 R + 0.715 G + 0.072 B`, applied after `brightness`.

## See also

- [Resources](/ui/reference/resources/)
- [Button](/ui/components/button/)
- [Sizing](/ui/concepts/sizing/)
