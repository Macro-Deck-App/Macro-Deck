---
title: Resources
description: The resource handle a tree references instead of carrying bytes, and the limit that bounds what it can promise.
---

A tree never carries bytes. Register your artwork and reference the handle:

```csharp
new UiImage { Key = "icon", Source = UiValue.Of(handle), Size = 0.2 }
```

The handle carries a `resourceId`, and optionally a `contentHash`, `mediaType` and `byteLength`. Macro
Deck serves the bytes and each client caches them by hash, so an icon shown by a hundred deck widgets is
transferred once per client rather than embedded a hundred times. Registering artwork is a host-side API
today; a plugin gets one when the upload path referenced on [Deck widget views](/sdk/ui/views/widget/)
lands.

`UiButton.Source` takes the same handle for its backdrop, framed by `Fit`, `Zoom`, `OffsetX`, `OffsetY`
and `Opacity`. Those are fractions and multipliers, not pixels or percentages: the scale is applied inside
the translation, so an offset covers the same distance at any zoom.

`Transition` says how a *change* of `Source` is drawn. `UiImageTransitions.Crossfade` holds the outgoing
artwork until the incoming one has decoded and then reveals it over 220 ms, which is normative rather than
a suggestion - two readers that chose their own timing would animate visibly differently. Leave it out and
the new artwork simply replaces the old one, which is also what a reader that does not implement the key
does.

`Opacity`, `Brightness` and `Saturation` adjust the artwork itself. Reach for `Opacity` to let what is
behind the artwork show through, and for the other two to change the artwork regardless of its ground -
"the same picture, darker" is `Brightness`, not a lower opacity, because a half-transparent cover ends up
looking like whatever sits behind it. Both are multipliers where absent means `1`, applied brightness
first and then saturation; the saturation result is normative down to its luma coefficients, since two
readers using different ones desaturate the same image to different greys.

## The `maxUiResourceBytes` limit

`maxUiResourceBytes` bounds both a `UiResource`'s **declared** `byteLength` and the bytes the host's
resource store accepts for one resource, so a declaration can never promise more than the host will
serve. A `byteLength` of `null` is accepted. It is one of the `maxUi*` limits listed with every other
protocol limit in [Plugin WebSocket protocol](/reference/websocket/#limits-and-timeouts); read it from the
protocol descriptor or the session response rather than hard-coding it - see
[Serving a view](/sdk/ui/views/sessions/#limits) for the rest of the `maxUi*` family.
