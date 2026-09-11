---
title: List
description: ui.list is a container that scrolls, and asks its producer for more as the user reaches the end of what it holds.
---

`ui.list`

## Purpose

`ui.list` is a container that scrolls, and asks you for more as the user reaches the end of what it holds.
Like `ui.text-field`, it exists for the **dialog** surface: a deck tile is a fixed box, and a list inside
one would hide content behind a gesture the deck itself uses.

It is deliberately not a stack with a scroll flag. A stack divides a **bounded** main axis between its
children, and every length in the framework is a fraction of that bound; a list's main axis has no bound at
all, so a fraction of it would mean nothing. Two different layouts under one type is how a reader ends up
guessing which one a tree meant.

## Properties

| Property | Meaning | Absent means |
|---|---|---|
| `gap` | The gap between children, a length | No gap |
| `padding` | Inner padding on every edge, a length | No padding |
| `background` | The list's own fill, as `#rrggbb` | The list paints nothing behind its children |
| `direction` | The scroll axis, `vertical` or `horizontal` | `vertical` |

`background` follows the same literal-colour rule every other container fill does - see
[Colours and text](/sdk/ui/concepts/theming/).

**`horizontal` needs `requiredComponentVersion: 2` and a fallback.** Negotiation catches an unknown node
*type*, never an unknown property *value*: a reader that predates version 2 ignores `direction` and
scrolls vertically, stacking a row of columns into one tall column. Ask for version 2 and carry a
`ui.stack` as the node's `fallback`, and such a reader draws that instead.

## Supported children

Any element, any number, laid out one after another along the list's unbounded scroll axis.

## Events and interactions

`ui.list` declares interaction the same way every other element does: only where `events` names it.

| Event | When | Payload |
|---|---|---|
| `reveal` | The user has reached the end of what the list currently holds | The index of the furthest child brought into view, as a bare number |

Loading more is a conversation rather than a protocol. Declare `reveal` and the reader tells you the index
of the furthest child the user has brought into view; append children by patch, or do not. Nothing says how
many, how often, or whether there are more - a producer that has run out simply appends nothing, and the
reader asks again only when the user goes further than they have been before. There is deliberately no
"loading" or "has more" property: both are the producer's own state, and both are already expressible as
children. A reader sends `reveal` no more than twice a second, and only for an index beyond the furthest
one it has already sent for the same list. See [Events](/sdk/ui/concepts/events/) for the shared
interaction model.

An index rather than a page, because paging is arithmetic only the producer can do: a reader would have to
agree with it on a page size it cannot know. How far down its own children the user has come is the one
thing a reader can state without agreeing with the producer on anything.

## Layout behaviour

A list's main axis - its `direction`, so `y` for a vertical list and `x` for a horizontal one - is unbounded
by definition: children take their natural extent along it, and `mainSize`/`fill` are ignored on that
axis. Across it, each child takes the list's inner extent. On its own parent's main axis, a list follows the ordinary
container rule - `mainSize` or `fill` if declared, otherwise its content extent. See
[Sizing](/sdk/ui/concepts/sizing/).

## Example

```csharp
new UiList
{
    Key = "tracks",
    Gap = UiSize.FromBasis(0.02),
    Events = [UiEventHandler.On(UiComponentEvents.Reveal, index => LoadMore(index))],
    Children = state.Value.Loaded.Select(Row).ToList(),
}
```
