# ADR 0084: Built-in icon names are a versioned public vocabulary

Status: Accepted

## Context

`ui.icon` (issue #777) lets a producer draw one of Macro Deck's own glyphs by name instead of uploading
artwork. A plugin may stay compiled against an old SDK indefinitely, and a reader draws nothing for a name
it does not carry, so a name a plugin uses must keep drawing on every reader that claims to support it. The
component registry and its version negotiation ([ADR 0064](0064-components-are-a-registry-over-two-namespaces.md),
[ADR 0065](0065-the-component-profile-authoring-contracts.md)) are the only way a producer learns what a
reader can draw.

## Decision

- `ui.icon` component version 1 publishes all 76 app glyphs: the glyph classes of
  `ui/runtime/styles/icons.css` minus the size modifiers, with the `wifi` alias in place of
  `wifi-solid-full`. They are `UiIcons.Version1` in C# and the first group of `UI_ICON_VERSIONS` in the
  runtime.
- The vocabulary is frozen and additive-only: a published name is never removed, renamed or redrawn as a
  different symbol.
- New names go into a new version group, and each group raises `ui.icon`'s maximum component version. A
  producer using a later name sets `RequiredComponentVersion` to `UiIcons.VersionOf(name)` and carries a
  fallback.
- `ci/scripts/verify-icon-names.mjs` guards it: every published name is a glyph class with an SVG behind it,
  no size modifier is published, the C# `Version1` equals the runtime's group 1, and the runtime's maximum
  `ui.icon` version equals the group count. Published names are a subset of the glyph classes, so a new app
  icon does not become public by accident.

## Consequences

- An app icon can be added freely; publishing it is a separate, deliberate step into a new group.
- A published icon can never be retired or renamed, and its SVG must keep meaning the same thing, even if
  the app stops using it.
- Readers must carry every group up to the version they advertise.

## References

- Issue #777
- [`UiIcons`](../../ui-model/src/MacroDeck.Ui/Components/UiComponentValues.cs)
- [`verify-icon-names.mjs`](../../ci/scripts/verify-icon-names.mjs)
