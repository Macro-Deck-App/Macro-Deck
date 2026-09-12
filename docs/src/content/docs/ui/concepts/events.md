---
title: Events
description: Handling events, validating input, and the rule that a component offers only the interaction its node declares.
---

A node lists the events it handles; a reader sends only those, and each lands in one handler.

## Example

```csharp
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
}
```

```json
{ "id": "volume.level", "type": "ui.slider", "properties": { "events": ["adjust", "change"], "level": 0.4 } }
```

The reader follows the drag with `adjust` and ends it with `change`. Each dispatch writes state; the runtime
turns the writes into one patch. Handlers update state or call your own logic - never rebuild the view.

## Reading the payload

```csharp
UiEventHandler.On(UiComponentEvents.Reveal, e =>
{
    if (e.TryGetDouble(out var index) && index >= songs.Peek().Count - 5)
    {
        LoadMore();
    }
}),
```

`UiEventData` offers `TryGetString`, `TryGetBoolean`, `TryGetDouble` and the verbatim `Raw` JSON. A payload is
unvalidated client data, so a getter for the wrong JSON kind returns `false` rather than throwing. Read state
with `Peek()` inside a handler so the handler does not subscribe to it.

## Declining an event

```csharp
UiEventHandler.On(UiComponentEvents.Press, _ => name.Peek().Length == 0
    ? UiEventOutcome.Rejected("Enter a name first.")
    : UiEventOutcome.Accepted),
```

A declined event is a rejected dispatch carrying your reason, which a renderer can show the user. It is not
a fault. Whatever the handler wrote before declining is still flushed, so a validation message arrives with
the refusal. A handler that throws also rejects the dispatch, and is reported on `UiView.HandlerFaulted`.

Keep validation next to the field that can fix the problem, using the input constraints and
[validation rules](/ui/views/configuration/) the DSL exposes.

## Async work

```csharp
UiEventHandler.OnAsync(UiComponentEvents.Press, async ct =>
{
    busy.Value = true;
    try { await SkipAsync(ct); }
    finally { busy.Value = false; }
}),

view.HandlerFaulted += (_, e) => Console.Error.WriteLine($"{e.NodeId}/{e.EventName}: {e.Exception}");
await view.WhenIdleAsync(); // in a test: wait for every async handler and load
```

`Dispatch` stays synchronous: an async handler is started and tracked as pending work, and the dispatch is
accepted before it finishes. Its outcome cannot reach that dispatch, so an async rejection is visible only
through the state it writes - use the synchronous overload when the refusal must be the answer. Honour the
cancellation token, and keep network or provider policy (for option loading, say) in plugin code rather than
in the UI runtime.

## What a dispatch answers

```csharp
var result = view.Dispatch(new UiEvent { NodeId = "volume.level", Name = "press" });
// Ignored: The node 'volume.level' does not accept the event 'press'.
```

| Outcome | When | Example reason |
|---|---|---|
| `Accepted` | A handler (or a writable binding) took it. Everything it changed is in one patch; a no-op advances nothing. | - |
| `Ignored` | Unknown node id, or a name the node does not accept. Never fatal - it is what a newer reader looks like to an older plugin. | `No node with id 'nope' is in the tree at revision 2.` |
| `Rejected` | Meant for this node but refused: wrong payload kind, a read-only binding, a handler that declined or threw. | `A JSON Number payload cannot be written to a 'String' value.` |

A dispatch is one batch and runs under the view's serialization, so a concurrent state write lands wholly
before or after it. A synchronous handler must therefore not block on work that writes the same view from
another thread. `Change` on a bound input needs no handler: the binding is the write path.

## Interaction only where declared

```csharp
new UiButton { Key = "idle" } // drawn, but accepts nothing
```

```json
{ "id": "idle", "type": "ui.button", "properties": {}, "children": [] }
```

The names in `Events` are exactly what the node advertises under `events`, and a reader sends an event only
where that list names it. There is no separate disabled property: declaring nothing *is* disabled, and
declaring some names says the rest are not yours to receive.

A component's own list of names is documentation, not a gate - the wire format does not stop a slider from
declaring any name. It is the reader's contract to send only what a node declared. A reader never infers
`press` from a `press-start`/`press-end` pair and never sends an undeclared name because the component type
happens to support it.

Structurally absent and hidden fields differ too: an absent field's value no longer exists or submits; a
hidden one keeps and submits its value. See [Conditional content](/ui/concepts/state-and-bindings/#conditional-content).

## Every event name

| Name | Constant | Declared by | Fires | Payload |
|---|---|---|---|---|
| `change` | `UiComponentEvents.Change` | `ui.slider`, `ui.text-field` | The value the user settled on - released the drag, left the field, pressed Enter. Always sent when an interaction ends, even if equal to the last `adjust`. | The value: a number (slider), a string (text field) |
| `adjust` | `UiComponentEvents.Adjust` | `ui.slider`, `ui.text-field` | Continuously while the user works the control - every drag step or keystroke. At most ten a second, never after the `change` that ended it. | Same as `change` |
| `press` | `UiComponentEvents.Press` | `ui.button` | A press completed without being held past the long-press threshold. The primary name a reader implements first. | None |
| `long-press` | `UiComponentEvents.LongPress` | `ui.button` | The press was still held after 600 ms. At most once per interaction, never together with `press`. | None |
| `press-start` | `UiComponentEvents.PressStart` | `ui.button` | The press began. | None |
| `press-end` | `UiComponentEvents.PressEnd` | `ui.button` | The press ended, however it ended. Exactly one follows each `press-start`, including a cancelled gesture or the pointer leaving the element. | None |
| `double-press` | `UiComponentEvents.DoublePress` | `ui.slider` | Two taps completed in quick succession, each without a drag. Sent after the second tap's `change`, never instead of it. | None |
| `reveal` | `UiComponentEvents.Reveal` | `ui.list` | The user scrolled further down the list. At most twice a second, and only for an index beyond the furthest already sent for that list. | Index of the furthest child in view, a number |

Configuration inputs use `change` from `UiConfigEvents`. See the [component reference](/ui/components/) for
each component's geometry and semantics, and [Modal views](/ui/views/modal/) for `modal.complete`, the one
event that is not a component interaction but the answer that ends a dialog.

## See also

- [State and bindings](/ui/concepts/state-and-bindings/)
- [Reactive updates](/ui/concepts/reactive-updates/)
- [Button](/ui/components/button/)
- [Slider](/ui/components/slider/)
