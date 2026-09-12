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
| `ui.stack` | Lays children out in one direction | [Stack and layer](/ui/components/stack-and-layer/) |
| `ui.text` | One run of text | [Text](/ui/components/text/) |
| `ui.image` | Draws a resource | [Image](/ui/components/image/) |
| `ui.range-bar` | A gradient-filled span with an optional point marker | [Range bar](/ui/components/range-bar/) |
| `ui.slider` | A level the user drags | [Slider](/ui/components/slider/) |
| `ui.button` | A container the user presses | [Button](/ui/components/button/) |
| `ui.layer` | Stacks children through the depth of the box instead of along an axis | [Stack and layer](/ui/components/stack-and-layer/) |
| `ui.chart` | A series drawn as a filled line | [Chart](/ui/components/chart/) |
| `ui.text-field` | A line the user types | [Text field](/ui/components/text-field/) |
| `ui.list` | A container that scrolls and asks for more | [List](/ui/components/list/) |
| `ui.transform` | Rotates, scales and shifts its children together about a pivot | [Transform](/ui/components/transform/) |
| `ui.shape` | A filled and stroked rectangle, rounded rectangle, circle, capsule or path | [Shape](/ui/components/shape/) |
| `ui.icon` | One glyph of Macro Deck's built-in icon set, drawn by name | [Icon](/ui/components/icon/) |
| `ui.grid` | Lays children out in equal columns and rows, with spans | [Grid](/ui/components/grid/) |
| `ui.gauge` | A read-only level drawn along an arc or ring | [Gauge](/ui/components/gauge/) |
| `ui.toggle` | An on/off switch the user flips | [Toggle](/ui/components/toggle/) |
| `ui.segmented` | A row of segments the user chooses one of | [Segmented](/ui/components/segmented/) |
| `ui.dial` | A rotary level the user turns | [Dial](/ui/components/dial/) |
| `ui.modifier` | Pads, fades, clips, masks or frames its one child | [Modifier](/ui/components/modifier/) |

Any node can also carry a `modifiers` object - background, border, radius, accessibility text and
`disabled` - and the gesture events. See [Modifier](/ui/components/modifier/).

## `macrodeck.*`

A component belongs here when a reader cannot draw it from the tree alone, because it must resolve a
Macro Deck-defined reference - a time or a media position
([ADR 0065](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/engineering/decisions/0065-the-component-profile-authoring-contracts.md))
- against its own clock. Everything else is `ui.*`, however Macro Deck-flavoured its styling.

| Type | Purpose | Page |
|---|---|---|
| `macrodeck.dynamic-text` | A run of text derived from a time reference the reader resolves itself | [Time and clock](/ui/components/time/) |
| `macrodeck.clock-dial` | An analogue clock face drawn from the same kind of reference | [Time and clock](/ui/components/time/) |
| `macrodeck.progress-bar` | A track whose filled span follows a position that keeps moving | [Progress](/ui/components/progress/) |
| `macrodeck.progress-text` | A run of text derived from that same moving position | [Progress](/ui/components/progress/) |
