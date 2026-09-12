---
title: The UI model
description: The transport-neutral tree, keys and identity, and how a client and a provider agree on a model version.
---

Every Macro Deck view is a tree of plain JSON nodes; the C# DSL is one way to produce it.

## Example

```csharp
var surface = new UiSurface { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared };
var volume = new UiState<double>(0.4);

var root = new UiStack
{
    Key = "volume",
    Gap = 0.04,
    Children =
    [
        new UiTextRun { Key = "readout", Text = UiText.From(() => $"{volume.Value * 100:0}%") },
        new UiSlider
        {
            Key = "level",
            Level = UiValue.From(() => volume.Value),
            Events = [UiEventHandler.On(UiComponentEvents.Change, e =>
            {
                if (e.TryGetDouble(out var level)) volume.Value = level;
            })],
        },
    ],
};

UiTree tree = UiViewBuilder.Build(surface, root);
string json = UiCanonicalJson.Serialize(tree);
```

```json
{
  "revision": 0,
  "surface": { "kind": "widget", "sessionMode": "shared", "attributes": {} },
  "root": {
    "id": "volume",
    "type": "ui.stack",
    "properties": { "gap": { "basis": 0.04 } },
    "children": [
      { "id": "volume.readout", "type": "ui.text", "properties": { "text": "40%" }, "children": [] },
      { "id": "volume.level", "type": "ui.slider", "properties": { "events": ["change"], "level": 0.4 }, "children": [] }
    ]
  }
}
```

Keys became dot-joined ids, every value was read once, and the handler became nothing more than the name
`change` in the node's `events`. `UiViewBuilder.Build` gives a one-off tree at revision 0; use a
[`UiView`](/ui/concepts/reactive-updates/) for a tree that keeps up with its state.

## A node

```csharp
new UiSlider
{
    Key = "level",
    Level = UiValue.From(() => volume.Value),
    RequiredComponentVersion = 2,
    Fallback = new UiTextRun { Key = "level-text", Text = UiText.From(() => $"{volume.Value * 100:0}%") },
    Events = [/* change handler as above */],
}
```

```json
{
  "id": "volume.level",
  "type": "ui.slider",
  "requiredComponentVersion": 2,
  "properties": { "events": ["change"], "level": 0.4 },
  "children": [],
  "fallback": { "id": "volume.level-text", "type": "ui.text", "properties": { "text": "40%" }, "children": [] }
}
```

| Member | Meaning |
|---|---|
| `id` | Stable, unique across the whole tree, including inside `fallback` subtrees. |
| `type` | The component name, an open vocabulary (`ui.stack`, `macrodeck.progress-bar`, ...). The core model declares and validates none. |
| `requiredComponentVersion` | The component version the node needs. Omitted means `1`; the DSL emits it only when you set `RequiredComponentVersion`. |
| `properties` | Arbitrary, unvalidated JSON for the type to interpret. Always written, even when empty. |
| `children` | In render order. Always written, even when empty. |
| `fallback` | What a reader draws instead when it does not support `type`. Omitted when absent. |

- **Unknown types never fail.** A reader that does not recognise a type, or whose declared range for it does
  not cover `requiredComponentVersion` exactly (never clamped), renders `fallback`, negotiating it in turn.
  With no fallback it renders nothing for that node and its children and carries on with the rest of the
  tree. This is what lets the vocabulary grow without breaking older renderers or plugins.
- **Properties are unvalidated.** Check a value's JSON kind before reading it: `"37.5"` can arrive where a
  number belongs. A property explicitly set to `null` is not the same as an absent one; a patch removes a
  key through `removedProperties`.
- **An input's id is the field name it submits as**, so submission stays keyed by name whichever path
  rendered it.

## Node ids from keys

```csharp
new UiStack
{
    Key = "queue",
    Children =
    [
        new UiWhen { Key = "header-when", Condition = () => showHeader.Value,
                     Content = () => new UiTextRun { Key = "header", Text = "Up next" } },
        new UiRepeat<(string Id, string Title)>
        {
            Key = "tracks",
            Items = UiValue.From(() => tracks.Value),
            KeySelector = track => track.Id,
            Template = (track, key) => new UiTextRun { Key = key, Text = track.Title },
        },
    ],
}
```

```text
queue            ui.stack
├─ queue.header  ui.text   "Up next"
├─ queue.t1      ui.text   "Intro"
├─ queue.t2      ui.text   "Verse"
└─ queue.t3      ui.text   "Outro"
```

Keys are public behaviour: supply stable ones yourself.

- A structural node's id is the dot-joined path of keys from the root, including the root's own key.
- `UiWhen` and `UiFragment` are transparent: no node, no path segment. Wrapping an element never changes
  its id - `header-when` and `tracks` appear nowhere above.
- `UiRepeat` is transparent too; each item takes its `KeySelector` key as if it stood directly at the
  repeat's position. Use a stable item key, never an index.
- A top-level input's id is its bare key with no prefix (`label`, not `settings.label`), so submission
  matches field-based configuration. Inside an object or array input container the id is
  `containerId.key`, and nesting composes.
- Every composed id is validated as an identifier, and must be unique across the tree. A failure or a
  duplicate throws `UiViewException` naming the id (and, for a duplicate, both declaration paths).

Never derive ids from position. Reordering must keep surviving items' ids so focus, in-flight edits and
`move-node` patches stay meaningful.

## Revisions

```json
{ "fromRevision": 0, "toRevision": 1, "operations": [ { "op": "set-properties", "nodeId": "volume.readout", "properties": { "text": "55%" } } ] }
```

A tree is built at revision 0, and every accepted patch advances it by exactly one. A patch applies only when
the reader's current revision equals `fromRevision`, `toRevision` is greater, and `operations` is non-empty.
It applies atomically: if any operation cannot apply, the reader discards the whole patch and asks for a full
tree - a resync, never an exception or a closed session. See [Patches](/ui/reference/patches/).

## The three packages

| Package | Use it for |
|---|---|
| `MacroDeck.Ui.Model` | The tree, patch, event and resource contracts on this page. Depend on it alone to build or patch a tree by hand. |
| `MacroDeck.Ui` | The C# DSL and reactive runtime that produce and patch a tree from `UiState`. See [State and bindings](/ui/concepts/state-and-bindings/). |
| `MacroDeck.Ui.Testing` | A headless renderer and test host for either. See [Custom views](/ui/views/custom/#testing-it). |

All three are public NuGet contracts; see [Compatibility](/ui/reference/compatibility/).

## Model-version negotiation

```csharp
var result = UiCapabilityNegotiator.NegotiateModelVersion(new UiCapabilities
{
    UiProtocol = new UiVersionRange { Minimum = 3, Maximum = 9 },
    SupportsAllComponents = true,
});
// IsSupported = true, NegotiatedVersion = 4
// with Minimum = 1, Maximum = 2: IsSupported = false, FallbackReason = "No overlapping UI model version."
```

The negotiated version is `min(reader max, UiModelVersions.Current)`, and negotiation fails when that falls
below `max(reader min, UiModelVersions.Minimum)`. Today `Minimum` is 3 and `Current` is 4.

A client negotiates the model version with the host **before** any session is opened, not per tree. Declining
is therefore cheap: no session is opened and no slot is held, and the client falls back to the surface's
non-tree representation (declared fields for configuration, the built-in grid for a folder). The host opens
each session at `Current` and honours whatever version the provider negotiates down to. Failure is never
fatal. See [Serving a view](/ui/views/sessions/) for the session lifecycle, and
[Compatibility](/ui/reference/compatibility/) for the version history.

## See also

- [State and bindings](/ui/concepts/state-and-bindings/)
- [Reactive updates](/ui/concepts/reactive-updates/)
- [Patches](/ui/reference/patches/)
