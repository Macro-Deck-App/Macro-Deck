---
title: Text field
description: ui.text-field is a single line of text the user types, the framework's one text entry.
---

`ui.text-field`

## Purpose

`ui.text-field` is one line the user types, and the framework's only text entry. It exists for the
**dialog** surface rather than for a deck tile, because a tile is a fixed box with nowhere to scroll and
nothing to type into.

It reuses `ui.slider`'s event pair verbatim rather than inventing names for typing, because it is the same
shape of interaction - a value the user works continuously and then settles on. Every keystroke arrives as
`adjust`, and the value they settled on - they left the field, or pressed Enter - arrives as `change`.
Filter on `adjust` if the query behind it is cheap; act only on `change` if each one costs a network call,
and the field still shows everything the user typed in between either way.

## Properties

| Property | Meaning | Absent means |
|---|---|---|
| `text` | The value the field holds - a literal string, not localized text | The field shows nothing |
| `placeholder` | What the field shows while it is empty - localized text | The field shows nothing while empty |
| `size` | The font size, a length | Left to the reader |

`text` is a literal string rather than a localization reference, unlike `placeholder`: it is what the user
typed, or what the producer put there for them to edit, and neither is written for a reader to translate. A
`placeholder`, like any prompt, is written for the reader - see [Colours and text](/ui/concepts/theming/).

**The producer is authoritative for `text`, but never while the field has focus.** A filtering producer
naturally echoes the query back on every keystroke, and applying that to a focused field would move the
caret to somewhere the user did not put it - so a reader shows what has been typed for as long as they are
typing, exactly as `ui.slider` paints its own level until the interaction ends.

## Supported children

None. `ui.text-field` is a leaf.

## Events and interactions

**Interaction is offered only where it is declared**, the same rule `ui.slider` and `ui.button` follow. A
field that declares no events is drawn and reads back what the producer put in it, and accepts no typing at
all - which is what makes it usable as a read-only display of a value the user entered elsewhere, without a
second element for the purpose.

| Event | When | Payload |
|---|---|---|
| `adjust` | Every keystroke, while the user is still typing | The field's current text |
| `change` | The value the user settled on - they left the field, or pressed Enter | The field's current text |

A reader draws one line that scrolls horizontally rather than wrapping, and sends `adjust` no more than ten
times a second - the same ceiling `ui.slider` works to, and for the same reason: each accepted event is one
state write on the producer. See [Events](/ui/concepts/events/) for the shared interaction model.

## Layout behaviour

A leaf: `mainSize` or `fill` if declared on its parent stack, otherwise its content extent. `size` is the
font size, not a main-axis extent - see [Sizing](/ui/concepts/sizing/).

## Example

```csharp
new UiTextField
{
    Key = "search",
    Text = UiValue.From(() => state.Value.Query),
    Placeholder = AppStrings.Widgets.Picker.SearchPlaceholder(),
    Size = 0.045,
    Events =
    [
        UiEventHandler.On(UiComponentEvents.Adjust, query => state.Set(s => s with { Query = query })),
        UiEventHandler.On(UiComponentEvents.Change, RunSearch),
    ],
}
```
