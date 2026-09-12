---
title: State and bindings
description: UiState, the binding kinds a property accepts, and how conditional and repeated content stay part of the tree.
---

Put mutable values in `UiState<T>`, read them inside the view, and the runtime keeps the tree in step.

## Example

```csharp
var volume = new UiState<double>(0.4);
var muted = new UiState<bool>(false);

var root = new UiStack
{
    Key = "volume",
    Gap = 0.04,
    Children =
    [
        new UiTextRun
        {
            Key = "readout",
            Text = UiText.From(() => muted.Value ? "Muted" : $"{volume.Value * 100:0}%"),
        },
        new UiSlider
        {
            Key = "level",
            Level = UiValue.From(() => volume.Value),
            Events =
            [
                UiEventHandler.On(UiComponentEvents.Adjust, e =>
                {
                    if (e.TryGetDouble(out var level)) volume.Value = level;
                }),
                UiEventHandler.On(UiComponentEvents.Change, e =>
                {
                    if (e.TryGetDouble(out var level)) { volume.Value = level; muted.Value = level == 0; }
                }),
            ],
        },
    ],
};

var view = new UiView(surface, root);
```

The readout reads `volume` and `muted`, so it re-evaluates when either changes; the slider's level reads
only `volume`. Dragging the slider to `0.55` produces exactly this patch:

```json
{ "fromRevision": 0, "toRevision": 1, "operations": [
  { "op": "set-properties", "nodeId": "volume.readout", "properties": { "text": "55%" } },
  { "op": "set-properties", "nodeId": "volume.level", "properties": { "level": 0.55 } } ] }
```

## Reading and writing state

```csharp
var name = new UiState<string>(string.Empty);
var tag = new UiState<string>(string.Empty, StringComparer.OrdinalIgnoreCase);

string shown = name.Value;   // inside the view: records a dependency
string current = name.Peek(); // in a handler: reads without subscribing
name.Value = "Studio";       // same as name.Set("Studio")
```

- **Reading `Value` during a build records a dependency.** Writing invalidates only the parts of the view
  that read it.
- **Use `Peek()` in handlers and tests.** Reading `Value` there would subscribe whichever cell happens to
  be evaluating.
- **An equal write is free**, compared with the state's comparer (the default, or one you pass): nothing
  re-evaluates, no revision advances, no `Changed` fires.
- **Attachment is lazy.** A state no view has read yet just updates in place.
- **Any thread may read or write.** See [Threads](/ui/concepts/reactive-updates/#threads).

## Derived values

```csharp
Level = UiValue.From(() => volume.Value),
Text = UiText.From(() => muted.Value ? "Muted" : $"{volume.Value * 100:0}%"),
Background = UiValue.Optional(() => accent.Value is { } colour ? colour : UiValue.None<string>()),
```

| Factory | Produces |
|---|---|
| a plain value, or `UiValue.Of(value)` | A constant. Use `Of` when `T` is an interface. |
| `UiValue.From(() => ...)` | A value re-evaluated whenever a state it read changes. |
| `UiValue.Optional(() => ...)` | A value whose presence is decided on each evaluation. |
| `UiValue.None<T>()` / `default` | An absent property - omitted from the node. |
| `UiText.From`, `UiText.FromLocalized`, `UiText.Optional` | The same for text, including localized strings. |

An absent property and an explicit JSON `null` are different states. When an optional value turns absent the
runtime removes the key:

```json
{ "op": "set-properties", "nodeId": "card", "removedProperties": ["background"] }
```

## Binding an input

```csharp
new UiConfigStack
{
    Key = "settings",
    Children =
    [
        new UiStringInput { Key = "label", Label = "Label", Binding = Bind.To(label) },
        new UiStringInput
        {
            Key = "host",
            Label = "Host",
            Binding = Bind.Custom(() => settings["host"], value => settings["host"] = value),
        },
        new UiStringInput { Key = "id", Label = "Id", Binding = Bind.ReadOnly(UiValue.Of("abc-123")) },
    ],
}
```

```json
{ "id": "label", "type": "string", "properties": { "events": ["change"], "label": "Label", "value": "Volume" } }
{ "id": "host",  "type": "string", "properties": { "events": ["change"], "label": "Host", "value": "localhost" } }
{ "id": "id",    "type": "string", "properties": { "label": "Id", "value": "abc-123" } }
```

| Binding | Reads | Writes |
|---|---|---|
| `Bind.To(state)` | `state.Value` | `state.Value = v` |
| `Bind.Custom(get, set)` | `get()` | `set(v)` - for a value backed by another source |
| `Bind.ReadOnly(value)` | `value` | Nothing. The node advertises no `change`, and a `change` sent anyway is rejected. |

A writable binding is the input's write path, so a `change` event needs no handler. Bindings belong to
configuration inputs (`UiInput<T>`); a component such as `UiSlider` or `UiTextField` takes a value plus
[events](/ui/concepts/events/) instead.

## Conditional content

```csharp
new UiWhen
{
    Key = "header-when",
    Condition = () => showHeader.Value,
    Content = () => new UiTextRun { Key = "header", Text = "Up next" },
}
```

When the condition turns false the content leaves the tree (`remove-node`); a condition re-evaluated to the
value it already had emits nothing. `UiWhen` adds no id segment of its own.

Use `UiWhen` when content should not exist at all. Use an input's `VisibleWhen` when a field should stay in
the tree and keep, and submit, its value while hidden.

## Repeated content

```csharp
new UiRepeat<(string Id, string Title)>
{
    Key = "tracks",
    Items = UiValue.From(() => tracks.Value),
    KeySelector = track => track.Id,
    Template = (track, key) => new UiTextRun { Key = key, Text = track.Title },
}
```

`KeySelector` must identify the logical item across inserts, removals and reordering - never its position.
`Template` receives the composed item key; use it as the element's `Key`. An absent `Items` renders nothing,
and assigning the same list instance again emits nothing. See
[Reactive updates](/ui/concepts/reactive-updates/#structural-changes) for the patch a reorder produces.

## See also

- [Events](/ui/concepts/events/)
- [Reactive updates](/ui/concepts/reactive-updates/)
- [The UI model](/ui/concepts/ui-model/#node-ids-from-keys)
