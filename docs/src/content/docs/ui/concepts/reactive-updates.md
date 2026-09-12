---
title: Reactive updates
description: How the runtime turns a state change into a patch, and the threading rules that make concurrent writes to a view safe.
---

## Reactive updates

The runtime tracks dependencies at property/structural scope level and emits only the patch operations
required by the change. Plugin code should therefore update state rather than creating a second manual
diff layer.

## Threads

Write state from whatever thread your work finished on. A timer, a `ConfigureAwait(false)` continuation
and a client's dispatch may all reach one view concurrently; the runtime serializes a view together with
every state it reads and every other view those states reach, so you do not need a lock of your own
around `UiState.Set`, `Dispatch` or `DrainPatches`. Views that share no state are serialized separately,
so a slow provider in one session does not hold up another.

Three consequences are worth knowing:

- **A value provider, a condition, a template, a synchronous handler and a `Bind.Custom` setter run under
  that serialization.** They must return promptly and must never block on work that has to write the same
  view from another thread - that is a deadlock, exactly as it always was for a build. Asynchronous work
  belongs in a state a synchronous provider reads.
- **`Changed` and `Faulted` are raised on the thread that did the work,** not on a pump of the host's
  choosing, and after the patch is already queued. A subscriber may read the tree, drain, write state and
  dispatch again; it may equally arrive on a background thread, so marshal to your own thread if you need
  one.
- **A write can run another view's flush on your thread before it returns** when the two views became
  connected by a shared state, so a lock you hold across `Set` is a lock a value provider runs under.

State is still only as consistent as what you write: two writes that must land together belong in one
`UiView.Batch()`. A batch belongs to the view rather than to the thread that opened it, so concurrent
batches on one view coalesce into a single patch.
