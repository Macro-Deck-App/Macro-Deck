---
title: Reactive updates
description: How the runtime turns a state change into a patch, and the threading rules that make concurrent writes to a view safe.
---

Write state; the view compiles each change into the smallest patch that describes it.

## Example

```csharp
var view = new UiView(surface, root); // the volume view from State and bindings, at revision 0
view.Changed += (_, _) => Send(view.DrainPatches());

volume.Value = 0.55;
```

```json
[{ "fromRevision": 0, "toRevision": 1, "operations": [
  { "op": "set-properties", "nodeId": "volume.readout", "properties": { "text": "55%" } },
  { "op": "set-properties", "nodeId": "volume.level", "properties": { "level": 0.55 } } ] }]
```

Only the two cells that read `volume` re-evaluated. Each emits one `set-properties` listing only the keys
whose value changed. Update state rather than keeping a diff layer of your own.

## Property changes

```csharp
muted.Value = true;
```

```json
{ "op": "set-properties", "nodeId": "volume.readout", "properties": { "text": "Muted" } }
```

- One `set-properties` per affected node, listing only changed keys; a value turned absent is listed in
  `removedProperties`.
- Only the spine from the root to the changed nodes is rebuilt; every untouched subtree stays the same
  `UiNode` instance, never re-allocated or re-serialized.
- A write that changes nothing - an equal value, or a re-evaluation producing the same output - enqueues
  nothing and advances no revision. An empty patch would be inapplicable.

## Structural changes

```csharp
// before: [t1 Intro, t2 Verse, t3 Outro]
tracks.Value = [("t3", "Outro"), ("t1", "Intro"), ("t4", "Bridge")];
showHeader.Value = false;
```

```json
[{ "fromRevision": 0, "toRevision": 1, "operations": [
   { "op": "remove-node", "nodeId": "queue.t2" },
   { "op": "move-node", "nodeId": "queue.t3", "parentId": "queue", "index": 1 },
   { "op": "insert-node", "nodeId": "queue.t4", "parentId": "queue",
     "node": { "id": "queue.t4", "type": "ui.text", "properties": { "text": "Bridge" }, "children": [] } } ] },
 { "fromRevision": 1, "toRevision": 2, "operations": [
   { "op": "remove-node", "nodeId": "queue.header" } ] }]
```

A `UiWhen` that flipped or a `UiRepeat` whose list was replaced re-materializes only the run of its parent's
children it owns, and diffs that run by id:

| Operation | Emitted for |
|---|---|
| `remove-node` | The top-most node of each removed subtree. |
| `move-node` | A surviving node that changed position. |
| `insert-node` | New content, at its rendered index (omitted index = append). |
| `replace-node` | A node whose type changed. |
| `set-properties` | Each surviving node whose properties differ. |

A surviving node keeps its id, and with it focus and in-flight edits. A condition that re-evaluates to the
same boolean, or an item list that is still the same instance, emits nothing. See
[Patches](/ui/reference/patches/) for how a reader applies each operation.

## Batching

```csharp
using (view.Batch())
{
    volume.Value = 0.8;
    muted.Value = false;
}
```

```json
[{ "fromRevision": 2, "toRevision": 3, "operations": [
  { "op": "set-properties", "nodeId": "volume.level", "properties": { "level": 0.8 } },
  { "op": "set-properties", "nodeId": "volume.readout", "properties": { "text": "80%" } } ] }]
```

Without the batch, the same two writes produce two patches and two revisions. A batch coalesces everything
written inside it into one patch, one revision and one `Changed`. Batches nest, and only the outermost one
flushes. Two writes that must land together belong in one batch.

A batch belongs to the view, not to the thread that opened it: concurrent batches on one view coalesce into
a single patch, one thread's open batch defers the other's flush, and the scope may be disposed on another
thread. Every `Dispatch` is already one batch.

## Draining

```csharp
view.Changed += (_, _) =>
{
    foreach (var patch in view.DrainPatches()) Send(patch);
};
```

Each flush that changed something advances `Revision` by one and queues one patch. `DrainPatches` returns
everything queued since the last call and clears the queue; it is safe from any thread and from inside
`Changed`, and two concurrent calls neither lose nor duplicate a patch. `view.Tree` is the current tree.
When serving a session, the host drains for you - see [Serving a view](/ui/views/sessions/).

## Threads

Write state from whatever thread your work finished on. A timer, a `ConfigureAwait(false)` continuation and a
client's dispatch may all reach one view at once; the runtime serializes a view together with every state it
reads and every other view those states reach, so you need no lock of your own around `UiState.Set`,
`Dispatch` or `DrainPatches`. Views that share no state run in parallel, so a slow provider in one session
does not hold up another.

- **Value providers, conditions, templates, synchronous handlers and `Bind.Custom` setters run under that
  serialization.** They must return promptly and must never block on work that has to write the same view
  from another thread - that deadlocks. Put asynchronous work in a state a synchronous provider reads.
- **`Changed` and `HandlerFaulted` are raised on the thread that did the work**, not on a pump, after the
  patch is queued. A subscriber may read the tree, drain, write state and dispatch again; marshal to your own
  thread if you need one.
- **A write can run another view's flush on your thread before it returns** when a shared state connects the
  two views, so any lock you hold across `Set` is a lock a value provider runs under.

## See also

- [State and bindings](/ui/concepts/state-and-bindings/)
- [Events](/ui/concepts/events/)
- [Patches](/ui/reference/patches/)
- [Serving a view](/ui/views/sessions/)
