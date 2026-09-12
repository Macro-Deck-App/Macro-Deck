---
title: The UI model
description: The transport-neutral tree, keys and identity, and how a client and a provider agree on a model version.
---

`MacroDeck.Ui.Model` is the wire contract underneath every Macro Deck UI view: a tree of nodes, the
patches that mutate it, the events a client raises against it, and the resource handles it references.
`MacroDeck.Ui` is one way to produce that tree - the declarative DSL described throughout this section -
but the model itself does not assume a DSL produced it. A provider can build and patch a tree by hand
against `MacroDeck.Ui.Model` directly when that is a better fit than the reactive runtime.

## Trees, nodes and revisions

A view is a tree of nodes, each carrying a type (`ui.stack`, `macrodeck.progress-bar`, and so on),
properties, and children. The tree has a **revision**: every accepted patch advances it by exactly one,
and a client and its provider agree on the current revision so a patch can be validated against the state
it was computed from rather than blindly applied. See [Patches](/ui/reference/patches/) for the
operation vocabulary and what makes a patch valid.

## Node types are open

The set of node types a tree may contain is not closed, and a renderer is never required to recognise
every one it receives. A node carries an optional `fallback` - a subtree in node types the reader is
expected to understand - and a reader that does not recognise a type renders its `fallback` instead of
failing or dropping the node. This is what lets the vocabulary grow (a new `ui.*` or `macrodeck.*` type
shipping in a later Macro Deck release) without breaking an older renderer or an older plugin: the newest
node degrades gracefully, and the oldest renderer never needs to know it exists.

## Keys and identity

Keys are part of the public behavior of a view. Supply stable keys yourself.

- Structural nodes derive ids from their key path.
- Top-level input ids are their field keys so submission remains compatible with existing field-based
  configuration.
- Inputs inside object/array containers compose their id from the container and child key.
- Conditions/fragments do not add identity merely by wrapping an existing element.
- Repeated items use a stable item key, never an array index.

Do not generate ids from position. Reordering a list must preserve the identity of surviving items so
focus, edits, and `move-node` patches remain meaningful.

## The three packages

- **`MacroDeck.Ui.Model`** - the transport-neutral tree, patch, event and resource contracts described on
  this page. Depend on this alone when you want to build or patch a tree without the DSL.
- **`MacroDeck.Ui`** - the declarative C# DSL and fine-grained reactive runtime that produces and updates
  a `MacroDeck.Ui.Model` tree from `UiState` and bindings. Covered by
  [State and bindings](/ui/concepts/state-and-bindings/) and
  [Reactive updates](/ui/concepts/reactive-updates/).
- **`MacroDeck.Ui.Testing`** - a headless renderer/test host for views written against either of the
  above. See [Custom views](/ui/views/custom/) for a worked test.

All three are public NuGet contracts; see [Compatibility](/ui/reference/compatibility/).

## Model-version negotiation

A client negotiates the UI model version it speaks with the host **before** any session is opened, not
per-tree. That is what makes declining cheap: a client too old for the model version a provider would
render costs nothing - no session is opened, no slot is held - and falls back to whatever
non-tree representation the surface offers (declared fields for configuration, the built-in grid for a
folder). See [Serving a view](/ui/views/sessions/) for where this fits in a session's lifecycle, and
[Compatibility](/ui/reference/compatibility/) for why the model's major version is 3.
