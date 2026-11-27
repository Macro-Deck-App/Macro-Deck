# ADR 0115: The host renders device key images from UI trees

Status: Accepted

## Context

A hardware deck with display keys (a Stream Deck, say) gets a `DeviceSurface` per session: positions,
a label, colours and an icon id per widget (ADR 0068). Every widget with a UI tree - a clock, a gauge, a
plugin tile - is invisible on it, and each provider redraws the appearance fields in its own way, so a key
never looks like the same widget in the web client.

The web client draws those trees with the framework-free runtime in a browser. The component profile's
measured files pin what Chrome lays out from each tree, box by box and text by text, and other renderers
(the Companion App) are already held to them.

## Decision

- The host draws every key itself and sends the provider a finished picture per key, opt-in per layout
  (`LayoutGrid.KeyImages`, issue #1868). The pictures are drawn from the same widget trees, deck geometry,
  theme, accent, UI font and culture the web client draws from.
- The renderer is a .NET port of the runtime's layout and paint rules (`MacroDeckHost.Rendering`, on the
  Skia and HarfBuzz packages the host already ships), held to the shared component-profile fixtures by
  `MacroDeckHost.Tests.Rendering`: every measured scene's boxes and text widths, and every layout table.
- The application layer reaches it through a port; trees come from host-observed UI sessions, one per
  widget on the keys, like a client's.

## Alternatives

- **Plugin-side compositing** - send trees to the provider and let it draw. Every provider would need a
  renderer of its own, plugins out of process included, and none would be held to the fixtures.
- **A headless browser** - draw with the real runtime in Chromium. Exact by construction, but a browser
  process per host, startup and memory cost on small machines, and a large native dependency to ship and
  patch on every platform.

## Consequences

- The runtime and the host renderer must change together: a change to a measured file, or to the
  runtime's layout and paint rules, has to be followed in `MacroDeckHost.Rendering` in the same release.
- A device with key images holds one UI session per visible widget, so a widget provider sees one more
  viewer, and the host spends CPU on drawing; only changed keys are drawn and pushed, capped at the
  layout's `MaxUpdatesPerSecond`.
- Folder views stay unavailable on such a device; only the widget grid is drawn.

## References

- Issue #1868, `docs/src/content/docs/features/devices.md` (Key images),
  `ui-model/fixtures/component-profile/README.md`.
