---
title: Text field
description: A single line of text the user types, for dialogs.
---

One line of text the user types - the framework's only text entry. It is meant for
[dialogs](/ui/views/modal/): a deck tile has nowhere to type.

`ui.text-field`

## Example

```csharp
var query = new UiState<string>(string.Empty);

new UiTextField
{
    Key = "search",
    Text = UiValue.From(() => query.Value),
    Placeholder = Strings.SearchPlaceholder(),
    Size = 0.045,
    Events =
    [
        UiEventHandler.On(UiComponentEvents.Adjust, e =>
        {
            if (e.TryGetString(out var text))
            {
                query.Value = text;
            }
        }),
    ],
}
```

A search box that filters as the user types, adapted from the built-in Music player picker.

## Acting only on the final value

```csharp
Events = [UiEventHandler.On(UiComponentEvents.Change, RunSearch)],
```

Every keystroke arrives as `adjust`; the value the user settled on - they left the field or pressed
Enter - arrives as `change`. Filter on `adjust` when the query is cheap; act only on `change` when each
one costs a network call. The field shows everything typed in between either way.

## Read-only display

```csharp
new UiTextField { Key = "apiKey", Text = UiValue.From(() => state.Value.ApiKey) }
```

A field that declares no events is drawn with the producer's text and accepts no typing.

## Properties

| Property | Values | Default | Meaning |
|---|---|---|---|
| `Text` (`text`) | `UiValue<string>`, literal only | Shows nothing | The value the field holds - what the user typed or the producer put there to edit. |
| `Placeholder` (`placeholder`) | `UiText`: literal or localized | Shows nothing while empty | What the field shows while it is empty. |
| `Size` (`size`) | `UiSize` length | Left to the reader | The font size. |
| `MainSize` (`mainSize`), `Fill` (`fill`) | See [Sizing](/ui/concepts/sizing/) | Content extent | The field's extent along the parent stack's main axis. |

`Text` is never localized; `Placeholder` is, like any prompt.

## Events

| Event | When | Payload |
|---|---|---|
| `adjust` (`UiComponentEvents.Adjust`) | Every keystroke, while the user is still typing | The field's current text |
| `change` (`UiComponentEvents.Change`) | The user left the field or pressed Enter | The field's current text |

Typing is offered only when events are declared. The pair is the same one [Slider](/ui/components/slider/)
uses. See [Events](/ui/concepts/events/).

## Children

None. `ui.text-field` is a leaf.

## Layout

A leaf: `mainSize` or `fill` if declared, otherwise its content extent. `size` is the font size, not a
main-axis extent. See [Sizing](/ui/concepts/sizing/).

## Reader behaviour

- Draw one line that scrolls horizontally; never wrap.
- Accept typing only when the field declares events; otherwise display `text` read-only.
- Send `adjust` no more than ten times a second - each accepted event is one state write on the producer.
- The producer is authoritative for `text`, except while the field has focus: keep showing what the user
  typed and do not apply an echoed value, so the caret never moves under them.

## See also

- [Events](/ui/concepts/events/)
- [Slider](/ui/components/slider/)
- [List](/ui/components/list/) for the results under a search field
- [Modal views](/ui/views/modal/)
