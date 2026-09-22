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
transferred once per client rather than embedded a hundred times. A plugin gets a handle for its own bytes
from [`UiResources`](#registering-your-own-images).

Macro Deck's own icons need no resource at all: name one with [`ui.icon`](/ui/components/icon/) and every
reader draws it from its own set.

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

## Registering your own images

```csharp
public async Task InitializeAsync(IIntegrationContext context)
{
    _photo = await context.UiResources.RegisterAsync("photo", await File.ReadAllBytesAsync(path), "image/jpeg");
}

new UiImage { Key = "photo", Source = UiValue.Of(_photo) }
```

`IIntegrationContext.UiResources` turns bytes into a handle. Put the handle it returns into the tree, never
one you build yourself: it carries the `contentHash` that makes clients fetch new bytes.

- **Names** are yours: a letter or digit, then up to 63 letters, digits, hyphens or underscores. All
  integrations in one plugin share them. Two plugins using the same name never collide.
- **Registering a name again replaces its bytes.** The `resourceId` stays, the `contentHash` changes, so a
  photo frame can show every picture under one name. Update the tree with the new handle and clients draw
  the new picture; a client still holding the old hash is sent the current bytes without caching them.
  Registering the same bytes under the same name again costs no upload.
- **Media types** are `image/png`, `image/jpeg`, `image/webp` and `image/gif`. SVG is not accepted from a
  plugin, the same rule as for icons a plugin supplies. PNG and JPEG are the safe choice for photos.
- **Size.** One resource is at most `maxUiResourceBytes` (2 MiB). That limit is deliberate and applies per
  resource: downscale camera photos before registering them. All of a plugin's resources together are at
  most `maxUiResourceBytesPerPlugin` (16 MiB) and `maxUiResourcesPerPlugin` (256). Over the quota,
  registration throws `UiResourceException` with `QuotaExceeded` and the name keeps what it had. Free room
  with `RemoveAsync`; removing a name that holds nothing is not an error.
- **Lifetime.** Macro Deck keeps resources in memory, never on disk, for as long as the plugin session
  lasts: through a reconnect that resumes it and through re-initialisation after a configuration change.
  They are released when the session ends and are gone after Macro Deck restarts, when your integration is
  initialised again on the new session. Register in `InitializeAsync`, or before you build the tree that
  shows the image.
- **Errors.** Argument problems (name, media type, empty or oversized content) are `ArgumentException`
  before anything is sent. `UiResourceException.ErrorCode` is `Unsupported` on a Macro Deck that predates
  resource registration, `QuotaExceeded`, `RateLimited`, or `Failed`, for example when the connection
  dropped. Registrations run one at a time, so starting many at once is safe.

In tests, `FakeIntegrationContext.UiResources` is a `FakeUiResourceRegistry` that applies the same rules and
exposes what was registered, and `MacroDeckTestHost` answers registrations over the wire.

## Limits

`maxUiResourceBytes` bounds both a `UiResource`'s **declared** `byteLength` and the bytes the host's
resource store accepts for one resource, so a declaration can never promise more than the host will
serve. A `byteLength` of `null` is accepted. `maxUiResourceBytesPerPlugin` and `maxUiResourcesPerPlugin`
bound what one plugin may have registered at once. They are `maxUi*` limits, listed with every other
protocol limit in [Plugin WebSocket protocol](/reference/websocket/#limits-and-timeouts); read them from the
protocol descriptor or the session response rather than hard-coding them - see
[Serving a view](/ui/views/sessions/#limits) for the rest of the `maxUi*` family.
