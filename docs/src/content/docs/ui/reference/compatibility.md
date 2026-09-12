---
title: Compatibility
description: The three packages as versioned contracts, why the model major moved to 3, and why 4 did not move the floor with it.
---

`MacroDeck.Ui`, `MacroDeck.Ui.Model`, and `MacroDeck.Ui.Testing` are public NuGet contracts. Existing
keys, component meanings, patch semantics, and public API members must follow the normal
[compatibility policy](/policies/compatibility/).

## Why the floor is 3 and the ceiling is 4

The UI model's `Minimum` is 3 and its `Current` is 4, and the gap between them is the difference between
a rename and a widening.

The floor moved to 3 because the component vocabulary was renamed outright rather than extended: every
`widget.*` node type became either `ui.*` or `macrodeck.*`, with no alias kept for the old spelling. A
tree built against the old vocabulary is not representable under the new one, so a package that no longer
understands a single `widget.*` type must not advertise the majors in which those were the only spelling.

The ceiling moved to 4 because an `icon` property may now carry a typed `{"type":…,"reference":…}`
provider reference where it previously always carried a bare icon-pack reference string - the same shape
as the move to 2, where a text property gained the option of carrying a localization reference. A producer
built against 3 emits only the string form and one built against 4 may emit either, so the two are not
interchangeable and the version has to say which a session speaks. The floor stayed where it was because
nothing was renamed: a reader at 4 accepts both readings, so it still reads every tree a producer at 3
emits, and `UiIconInput` is unchanged.

Either way a client and a provider negotiate the model version as they always have - before a session is
opened - and a mismatch produces the same graceful decline, never a tree the reader cannot parse.

See [ADR 0064](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0064-components-are-a-registry-over-two-namespaces.md)
for why the vocabulary is organized as a registry over the `ui.*`/`macrodeck.*` namespaces rather than one
flat list, and [The UI model](/ui/concepts/ui-model/#model-version-negotiation) for where negotiation
sits in a session's lifecycle.
