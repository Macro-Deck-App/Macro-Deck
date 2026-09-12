---
title: Serving a view
description: IUiProvider, the session lifecycle every surface shares, the protocol limits, and what a refused update looks like.
---

Macro Deck renders a view inside a **session** that the host owns. Implement
`MacroDeck.Sdk.Ui.IUiProvider` to serve one:

```csharp
public sealed class SetupUiProvider : IUiProvider
{
    public IReadOnlyList<UiSurfaceDeclaration> Surfaces =>
        [new UiSurfaceDeclaration { Kind = "config", SessionMode = "exclusive" }];

    public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
        => Task.FromResult<IUiSession?>(request.Surface.Kind == "config" ? new SetupSession() : null);
}
```

`Surfaces` is what Macro Deck reads to learn which surfaces you serve, before anything is initialized - so
it must be side-effect free and must not depend on a live connection. Declaring a surface does not commit
you to every session for it: returning `null` from `CreateSessionAsync` declines the surface. The
surface-kind vocabulary is open (see [Views and surfaces](/ui/views/)), so declining a kind you do not
recognise is the correct answer, not an error.

`IUiSession` is expressed in `MacroDeck.Ui.Model` terms - `BuildTree`, `DrainPatches`, `Changed`,
`Faulted`, `Dispatch` - so a provider can serve a tree without depending on the DSL. If you authored the
view with `MacroDeck.Ui`, forward each member to your `UiView`.

## The lifecycle every surface shares

1. **Open.** The host asks for a session for one surface, carrying the UI model version it speaks. The
   session id is host-issued.
2. **Snapshot on request.** The host asks for a full tree when a client attaches and whenever it needs to
   resynchronise. `BuildTree()` must describe the same revision your emitted patches have reached.
3. **Patches.** Raise `Changed` when patches are waiting; the host drains them. Coalescing several changes
   into one raise is expected - the host drains rather than counting raises. A patch dropped in
   `DrainPatches` is lost to every attached client.
4. **Events.** `Dispatch` delivers a client event, never concurrently for one session. Reject an event by
   producing no patch, not by throwing: a throw faults the session.
5. **Close.** The host disposes the session. It may do so at any time - a client detaching for good, a
   limit trip, or a fault.

You never see who is attached, how many clients there are, or when one attaches. That is deliberate: the
host owns the session, so a provider writes one code path whether the tree is rendered on one deck or
several.

The host relays your bytes rather than your objects. A tree or patch is bounded and forwarded verbatim, so
unknown members, member order and number formatting reach the client exactly as you produced them.

Out of process, the same contract is the `ui` capability kind on the plugin protocol - see
[the operations below](#over-the-plugin-protocol) and the
[`ui` host api](/reference/websocket/#host-callbacks) for pushing snapshots, patches and faults back.
`MacroDeck.Plugin.Hosting` maps that capability onto `IUiProvider` for you: register an integration that
implements it and the SDK declares the kind, answers `describe` from your `Surfaces`, and drives the
snapshot, patch and fault callbacks from the session you return. There is no capability handler to write.

## Limits

The `maxUi*` limits are listed with every other protocol limit in
[Plugin WebSocket protocol](/reference/websocket/#limits-and-timeouts). The host advertises them in the
protocol descriptor and the session response; read them from there rather than hard-coding them.

What they mean for a provider:

- Tree and patch sizes are measured in UTF-8 bytes of the serialized payload, and node counts include
  `fallback` subtrees.
- The update rate and its burst allowance are per session, not per provider, so one busy view cannot
  starve another.
- `maxUiResourceBytes` bounds both a `UiResource`'s **declared** `byteLength` and the bytes the host's
  resource store accepts for one resource, so a declaration can never promise more than the host will
  serve. A `byteLength` of `null` is accepted.

## What a refused update looks like

Nothing is ever dropped silently - a client left on a revision that will never advance again is the
failure this design exists to prevent.

- **A patch over the byte limit, or too fast:** the patch is refused and the host asks you for a fresh
  tree instead. The client receives that tree, not silence.
- **A patch whose `fromRevision` does not match the session:** refused and resynced the same way. The
  client ends at your current revision through a full tree, never through a patch that did not apply.
- **A patch carrying no operations, or not advancing the revision:** refused with `INVALID_PAYLOAD`.
  Nothing is delivered and nothing is resynced - the session's revision never moved, so no client is
  stale.
- **A tree over the byte, node or declared-resource limit:** the session ends with `PAYLOAD_TOO_LARGE` and
  the attached clients are told. A tree cannot be superseded by anything smaller, so there is nothing to
  resync to.
- **Sustained overload after a resync:** the session ends with `RATE_LIMITED`, which clients are told is
  retryable.

An out-of-process provider observes each of these as an error on that call's own `host.result` - the reply
to the `host.invoke` that carried the payload. So a refusal is always attributable to the update that
caused it.

## Over the plugin protocol

Over the plugin protocol the same contract is the `ui` capability kind, invoked by the host:

| Operation | Purpose |
| --- | --- |
| `describe` | The surfaces this provider can serve, the UI model version it speaks, and the [preview scenarios](/ui/views/developer-preview/) it declares. `previews` is optional: a plugin built against an SDK that predates it omits the key, and the host reads that as none. |
| `session.open` (config) | A `config` surface carries the entry point being configured in its surface attributes - `integration-config` with the config flow session, `action-config` with the action id and the instance's stored parameters, `folder-view-config` with the folder and its view, or `widget-config` with the widget id, type and stored configuration. `MacroDeck.Plugin.Hosting` routes these to `IUiConfigFlow`/`IUiConfigurableActionDefinition` before it consults `IUiProvider`. |
| `session.open` | Open a session for one surface. The session id is host-issued; a provider never mints one. |
| `session.open` (developer preview) | A `developer-preview` surface names one registered preview scenario in its surface attributes. `MacroDeck.Plugin.Hosting` builds that scenario and never consults `IUiProvider`, so a production provider is unreachable from a preview. |
| `session.snapshot` | Produce the session's current full tree. The tree does not return on the result - it arrives as a separate `host.invoke ui/snapshot`, so one delivery path serves a first attach and a resync alike. |
| `session.event` | A client acted on a node. `clientId` says which one, and is meaningful only for a shared session. |
| `session.close` | The host is ending this session. |
| `modal.result` | How a modal this plugin opened ended. Host-to-plugin because the wait is unbounded by a person - see [action modals](/ui/views/modal/). |

Trees, patches and faults travel the other way through the [`ui` host api](/reference/websocket/#host-callbacks).

Like the other provider-shaped capabilities, `ui` declares the single local id `provider`: the capability *is* the plugin's one UI provider, so there is no per-instance identity to name. The host invokes `kind: "ui", localId: "provider"`.
