# ADR 0064: Components are a registry over two namespaces

Status: Accepted

## Context

[ADR 0038](0038-ui-model-and-declarative-dsl.md) gave Macro Deck a UI model that is already generic, and
the only profile shipped on top of it was configuration. Built-in widgets were implemented once per
client: the Weather widget was an Angular component displaying a WebP the host rasterised. Neither half
survives a second client, and the bitmap forced two further compromises — the client resolved CSS theme
colours to hex and sent them to the host as query parameters, and every visible string was hardcoded
English inside the renderer ([#744](https://github.com/Macro-Deck-App/Macro-Deck/issues/744)).

Migrating each built-in widget in turn then surfaced what the profile could not express — a display that
changes every second, an analogue clock face, a dragged level, a moving media position, a pressable tile
— and the vocabulary that grew out of that revealed a second problem
([#842](https://github.com/Macro-Deck-App/Macro-Deck/issues/842)). Every type was spelled `widget.*`,
including a container, a run of text and a picture: the generic half of the vocabulary was named after
the one surface that happened to need it first. The renderer carried seven hardcoded type lists, not
one. And the advertised render capabilities were a closed union of every type anyone had thought of —
including the 41 configuration types, which that renderer has never had a paint arm for.

## Decision

### A component is registered, not enumerated

`UiComponentDefinition` carries everything the renderer needs about one type: how to create its element,
bind it, paint it, release it, its natural main-axis extent, and whether it repaints on the reader's own
clock. All seven lists collapse into that declaration, and advertised capabilities are **derived from
what a registry actually holds** rather than from a constant — so a renderer that never composed the
Macro Deck profile cannot claim to draw Macro Deck components, and no renderer claims the configuration
vocabulary any more. #843's plugin-provided widgets need no new rendering architecture: a plugin
component is a definition like any other.

### Two namespaces, and the line is a property of the value

A component is `macrodeck.*` when a reader **cannot draw it from the tree alone**, because it must
resolve a Macro Deck-defined reference against its own clock. Everything else is `ui.*`, however
Macro Deck-flavoured its styling: a button on a deck tile is still `ui.button`, and a ring animation is a
colour and a style name, not a domain concept. The alternative line — "shipped by Macro Deck" — coincides
with this one today, diverges later, and would make the namespace a statement about authorship a renderer
cannot act on.

The **configuration vocabulary stays unprefixed, and that is load-bearing**: its types are drawn only by
Angular, and their lack of a namespace is what makes "is this a component-profile type" a sound namespace
test. That predicate has to be a namespace test rather than a registry lookup, because the caller is
deciding how to draw a type it could *not* resolve.

The rename was a hard cut with no shim, and both halves moved at once, so the UI model major and its
`Minimum` moved together: a package understanding no old spelling must not advertise the majors in which
those were the only spelling, or a session would negotiate down successfully and then draw every node as
a placeholder. This was affordable exactly once — no plugin serves a component surface yet, and no tree
is ever persisted.

## Consequences

- The renderer can be reasoned about without knowing what a Macro Deck widget is, and a generic tree
  renders without being structured around one. Plugin-provided components need no new rendering
  architecture: a plugin component is a definition like any other.
- Registering over an existing type replaces it and returns what it displaced, which is the shape plugin
  components need - and it also lets a plugin shadow a core component. Whether it may is a trust
  question that belongs with that feature.
- Two C# names had to move permanently to make room for the generic spellings, and the internal CSS
  class names keep their old spelling: they are internal identifiers with no contract movement.
- The profile's own authoring contracts - geometry, colour, reader-resolved values, interaction - are
  [ADR 0065](0065-the-component-profile-authoring-contracts.md).

## References

- [Issue #842](https://github.com/Macro-Deck-App/Macro-Deck/issues/842),
  [Issue #843](https://github.com/Macro-Deck-App/Macro-Deck/issues/843)
- [ADR 0038](0038-ui-model-and-declarative-dsl.md),
  [ADR 0065](0065-the-component-profile-authoring-contracts.md)
