# ADR 0038: The UI model is a surface-agnostic versioned package, authored through a reactive DSL

Status: Accepted

## Context

Macro Deck UI needs one transport-neutral contract that plugins can produce and different clients can
render. The core must not assume configuration forms, Angular, or a particular future widget vocabulary,
and once published through NuGet its public surface becomes a compatibility commitment.

Plugin authors still need a practical way to produce trees and incremental patches. Rebuilding and
diffing the entire tree for every state change would make update cost depend on tree size and bake that
limitation into the public authoring API.

## Decision

### `MacroDeck.Ui.Model` — the transport model

An independent package containing only the shared UI transport model: a tree of nodes identified by
stable string ids, **arbitrary node type strings and JSON properties** rather than a closed hierarchy,
ordered id-addressed patch operations, UI events, surface metadata and capability negotiation, resource
handles that reference assets rather than embedding bytes, and deterministic canonical JSON
serialization. It contains no renderer, no DSL, no configuration vocabulary and no dependency on another
Macro Deck package.

**UI model versioning is independent of the plugin protocol version.** Breaking either contract must not
force a major in the other.

Node ids are session-local stable identities, not qualified capability ids; producers must keep them
stable and unique. Property values stay opaque to the core — profile-specific meaning belongs to the
producer and the renderer.

Patch application is ordered and atomic: a renderer that cannot apply a patch requests a full tree rather
than keeping a partially applied state. Capability negotiation is non-fatal — an unsupported component
uses its fallback where one exists, and is otherwise omitted rather than failing the session.

### `MacroDeck.Ui` — the DSL and reactive runtime

A bound value is a reactive cell rather than a one-time snapshot: state reads establish dependencies, and
changing state re-evaluates only the affected property cells or structural scopes and emits the required
patch operations. Update cost is therefore proportional to changed cells rather than to total tree size.

**Element keys are author-supplied and stable.** Conditions, fragments and repeats do not invent
positional identity, and repeated items use stable item keys, never array indexes — a positional key for
a mutable list is a defect.

Configuration controls are a **profile** expressed through ordinary node types and properties, not
additions to the transport-neutral model, so a new profile adds vocabulary without touching the core.

**Absent and explicit `null` are distinct**: removing a property uses the patch model's removal
mechanism rather than encoding removal as JSON null.

Async option loading stays plugin-side work. The runtime handles cancellation and state updates but does
not own provider-specific polling, caching, retry or timing policy.

The runtime is thread-safe, and **the unit of serialization is one connected component of views and the
states they read** — not one view, and not the process. Any thread may read or write state, dispatch and
drain; components sharing no state proceed in parallel, so one plugin's slow value provider cannot stall
another session. Evaluation stays synchronous and pure and runs under that component's lock, which is
what makes per-thread dependency tracking sound; change and fault notifications are raised outside it.

`MacroDeck.Ui.Testing` renders views headlessly, simulates events and verifies that emitted patches
reproduce the resulting tree, without imposing a unit-test framework on consumers.

## Consequences

- New UI profiles can add node types and properties without changing the core model, and plugin protocol
  and UI model compatibility evolve independently.
- Stable node identity is required for meaningful patches, focus preservation and reordering, and
  renderers must tolerate unknown vocabulary.
- Large binary resources travel through the resource path instead of being repeated in tree JSON.
- Public changes to either package require the same compatibility discipline as every other published
  SDK contract, and both must evolve additively.
- Plugin code needs no lock of its own around a view, but a provider or synchronous handler must not
  block: it runs under its component's lock, and a write may run a connected view's flush on the writing
  thread before the write returns.
- Existing field-list configuration remains a fallback for renderers that do not support the tree model.
  A client renders one representation, never a merge of both.

## References

- [Issue #539](https://github.com/Macro-Deck-App/Macro-Deck/issues/539),
  [Issue #540](https://github.com/Macro-Deck-App/Macro-Deck/issues/540),
  [Issue #830](https://github.com/Macro-Deck-App/Macro-Deck/issues/830)
- [Macro Deck UI guide](https://docs.macro-deck.app/ui/)
