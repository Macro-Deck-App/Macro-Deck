---
title: Components
description: Every node type the UI framework ships, and which page documents it.
---

A view is a tree of these node types. Each one has its own page below: purpose, properties - and what
leaving one out means - supported children, events and interactions, and how it sizes on the main and
cross axis.

## `ui.*`

| Type | Purpose | Page |
|---|---|---|
| `ui.stack` | Lays children out in one direction | [Stack and layer](/sdk/ui/components/stack-and-layer/) |
| `ui.text` | One run of text | [Text](/sdk/ui/components/text/) |
| `ui.image` | Draws a resource | [Image](/sdk/ui/components/image/) |
| `ui.range-bar` | A gradient-filled span with an optional point marker | [Range bar](/sdk/ui/components/range-bar/) |
| `ui.slider` | A level the user drags | [Slider](/sdk/ui/components/slider/) |
| `ui.button` | A container the user presses | [Button](/sdk/ui/components/button/) |
| `ui.layer` | Stacks children through the depth of the box instead of along an axis | [Stack and layer](/sdk/ui/components/stack-and-layer/) |
| `ui.chart` | A series drawn as a filled line | [Chart](/sdk/ui/components/chart/) |
| `ui.text-field` | A line the user types | [Text field](/sdk/ui/components/text-field/) |
| `ui.list` | A container that scrolls and asks for more | [List](/sdk/ui/components/list/) |
| `ui.transform` | Rotates, scales and shifts its children together about a pivot | [Transform](/sdk/ui/components/transform/) |

## `macrodeck.*`

A component belongs here when a reader cannot draw it from the tree alone, because it must resolve a
Macro Deck-defined reference - a time or a media position
([ADR 0065](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0065-the-component-profile-authoring-contracts.md))
- against its own clock. Everything else is `ui.*`, however Macro Deck-flavoured its styling.

| Type | Purpose | Page |
|---|---|---|
| `macrodeck.dynamic-text` | A run of text derived from a time reference the reader resolves itself | [Time and clock](/sdk/ui/components/time/) |
| `macrodeck.clock-dial` | An analogue clock face drawn from the same kind of reference | [Time and clock](/sdk/ui/components/time/) |
| `macrodeck.progress-bar` | A track whose filled span follows a position that keeps moving | [Progress](/sdk/ui/components/progress/) |
| `macrodeck.progress-text` | A run of text derived from that same moving position | [Progress](/sdk/ui/components/progress/) |
