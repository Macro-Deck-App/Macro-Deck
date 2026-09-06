---
title: State and bindings
description: UiState, the binding kinds a property accepts, and how conditional and repeated content stay part of the tree.
---

## State and bindings

`UiState<T>` is reactive state. Reading it while a property or structural expression is evaluated records
a dependency; writing it invalidates only the dependent part of the view.

Common bindings:

- `Bind.To(state)` for read/write state.
- `Bind.ReadOnly(value)` for display-only values.
- `Bind.Custom(get, set)` when the value is backed by another source.

Use `UiValue` for constants, reactive providers, and optional properties. An absent property and an
explicit JSON `null` are different states.

### A worked example

A text field bound to state, with a derived label that stays in sync without any manual patching:

```csharp
var name = new UiState<string>(string.Empty);

var view = new UiStack
{
    Key = "greeting",
    Children =
    [
        new UiTextField
        {
            Key = "name",
            Label = UiText.Of("Your name"),
            Binding = Bind.To(name),
        },
        new UiTextRun
        {
            Key = "preview",
            Text = UiValue.From(() => name.Value.Length == 0
                ? MyStrings.EnterAName()
                : MyStrings.Greeting(name.Value)),
        },
    ],
};
```

Typing in the field writes `name`; the runtime notices `preview`'s text depends on `name.Value` and
re-evaluates only that node, emitting a `set-properties` patch for it alone.

## Conditional and repeated UI

Use structural conditions when content should not exist in the tree at all. Use visibility properties
when a field should remain present and retain/submit its value while hidden.

For repeated content, choose a key that identifies the logical item across inserts, removals, and
reordering.
