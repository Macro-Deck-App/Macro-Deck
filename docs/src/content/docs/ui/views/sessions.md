---
title: Serving a view
description: IUiProvider, the session lifecycle every surface shares, the protocol limits, and what a refused update looks like.
---

Implement `MacroDeck.Sdk.Ui.IUiProvider` to serve a view; Macro Deck owns the session it renders in.

## Example

A deck widget that counts presses, shared by every device showing it:

```csharp
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;

public sealed class CounterUiProvider : IUiProvider
{
    private readonly UiState<int> _count = new(0);

    public IReadOnlyList<UiSurfaceDeclaration> Surfaces { get; } =
        [new() { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared }];

    public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
    {
        if (request.Surface.Kind != UiSurfaceKinds.Widget)
        {
            return Task.FromResult<IUiSession?>(null);
        }

        var root = new UiButton
        {
            Key = "counter",
            Events = [UiEventHandler.On(UiComponentEvents.Press, () => _count.Value++)],
            Children = [new UiTextRun { Key = "value", Text = UiText.From(() => _count.Value.ToString()), Size = 0.4 }],
        };

        return Task.FromResult<IUiSession?>(new ViewSession(new UiView(request.Surface, root)));
    }
}

public sealed class ViewSession : IUiSession
{
    private readonly UiView _view;

    public ViewSession(UiView view)
    {
        _view = view;
        _view.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
        _view.HandlerFaulted += (_, fault)
            => Faulted?.Invoke(this, new UiSessionFaultedEventArgs(fault.Exception.Message, fault.Exception));
    }

    public event EventHandler? Changed;

    public event EventHandler<UiSessionFaultedEventArgs>? Faulted;

    public UiTree BuildTree() => _view.Tree;

    public IReadOnlyList<UiPatch> DrainPatches() => _view.DrainPatches();

    public void Dispatch(UiEvent uiEvent) => _view.Dispatch(uiEvent);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
```

`ViewSession` is the whole adapter between a `MacroDeck.Ui` `UiView` and `IUiSession`; the other pages
in this section reuse it. `IUiSession` itself speaks only `MacroDeck.Ui.Model` terms, so a provider can
serve a tree without the DSL.

## Declaring surfaces

```csharp
public IReadOnlyList<UiSurfaceDeclaration> Surfaces { get; } =
[
    new() { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared },
    new() { Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive },
];
```

Macro Deck reads `Surfaces` before anything is initialized, so it must be side-effect free and must not
depend on a live connection. Declaring a surface does not commit you to every session for it: return
`null` from `CreateSessionAsync` to decline one. The kind vocabulary is open (see
[Views and surfaces](/ui/views/)), so declining a kind you do not recognise is correct, not an error.

## The lifecycle every surface shares

| Step | What happens | Your side |
| --- | --- | --- |
| Open | The host asks for a session for one surface, carrying the UI model version it speaks (`request.UiModelVersion`). | Return a session or `null`. The session id is host-issued. |
| Snapshot | The host asks for a full tree when a client attaches and whenever it must resynchronise. | `BuildTree()` must describe the revision your emitted patches have reached. |
| Patches | You raise `Changed`; the host drains. | Coalescing several changes into one raise is fine - the host drains rather than counts. A patch dropped in `DrainPatches` is lost to every attached client. |
| Events | `Dispatch` delivers a client event, never concurrently for one session. | Reject an event by producing no patch. A throw faults the session. |
| Close | The host disposes the session - at any time: a client leaving for good, a limit trip, or a fault. | Release what the session holds in `DisposeAsync`. |

You never see who is attached, how many clients there are, or when one attaches: one code path serves one
deck or several.

The host relays your bytes, not your objects. A tree or patch is bounded and forwarded verbatim, so unknown
members, member order and number formatting reach the client exactly as you produced them.

## Limits

The `maxUi*` values are listed with every other protocol limit in
[Plugin WebSocket protocol](/reference/websocket/#limits-and-timeouts). The host advertises them in the
protocol descriptor and the session response - read them from there, never hard-code them.

| Limit | Measured as |
| --- | --- |
| Tree and patch size | UTF-8 bytes of the serialized payload. |
| Node count | Every node, including `fallback` subtrees. |
| Update rate and burst | Per session, not per provider - one busy view cannot starve another. |
| `maxUiResourceBytes` | Both a `UiResource`'s declared `byteLength` and the bytes the host's resource store accepts for one resource. A `byteLength` of `null` is accepted. |

## What a refused update looks like

Nothing is dropped silently - no client is ever left on a revision that will never advance.

| You send | The host | The client sees |
| --- | --- | --- |
| A patch over the byte limit, or too fast | Refuses it and asks you for a fresh tree. | That tree. |
| A patch whose `fromRevision` does not match the session | Refuses it and resyncs the same way. | Your current revision, through a full tree. |
| A patch with no operations, or one that does not advance the revision | Refuses it with `INVALID_PAYLOAD`. No resync - the revision never moved. | Nothing; it is not stale. |
| A tree over the byte, node or declared-resource limit | Ends the session with `PAYLOAD_TOO_LARGE`. Nothing smaller can supersede a tree. | The session ended. |
| Sustained overload after a resync | Ends the session with `RATE_LIMITED`. | The session ended; retryable. |

Out of process, each refusal is an error on that call's own `host.result` - the reply to the `host.invoke`
that carried the payload - so it is always attributable to the update that caused it.

## Over the plugin protocol

Out of process, the same contract is the `ui` capability kind. `MacroDeck.Plugin.Hosting` maps it onto
`IUiProvider`: register an integration that implements it and the SDK declares the kind, answers
`describe` from your `Surfaces`, and drives the snapshot, patch and fault callbacks from your session.
There is no capability handler to write.

Like the other provider-shaped capabilities, `ui` declares the single local id `provider` - the capability
*is* the plugin's one UI provider. The host invokes `kind: "ui", localId: "provider"`.

| Operation | Purpose |
| --- | --- |
| `describe` | The surfaces this provider serves, the UI model version it speaks, and the [preview scenarios](/ui/views/developer-preview/) it declares. `previews` is optional: a plugin built against an SDK that predates it omits the key, and the host reads that as none. |
| `session.open` | Open a session for one surface. The session id is host-issued; a provider never mints one. |
| `session.open` (config) | A `config` surface names its entry point in its surface attributes - `integration-config` with the config flow session, `action-config` with the action id and the instance's stored parameters, `folder-view-config` with the folder and its view, or `widget-config` with the widget id, type and stored configuration. `MacroDeck.Plugin.Hosting` routes `integration-config` and `action-config` to `IUiConfigFlow`/`IUiConfigurableActionDefinition` before it consults `IUiProvider`. |
| `session.open` (developer preview) | A `developer-preview` surface names one registered scenario in its surface attributes. `MacroDeck.Plugin.Hosting` builds that scenario and never consults `IUiProvider`, so a production provider is unreachable from a preview. |
| `session.snapshot` | Produce the current full tree. The tree does not return on the result - it arrives as a separate `host.invoke ui/snapshot`, so a first attach and a resync share one delivery path. |
| `session.event` | A client acted on a node. `clientId` says which one, and is meaningful only for a shared session. |
| `session.close` | The host is ending this session. |
| `modal.result` | How a modal this plugin opened ended. Host-to-plugin because a person, not a timeout, bounds the wait - see [Modal views](/ui/views/modal/). |

Trees, patches and faults travel the other way through the [`ui` host api](/reference/websocket/#host-callbacks).

## See also

- [Views and surfaces](/ui/views/)
- [Patches](/ui/reference/patches/)
- [Resources](/ui/reference/resources/)
- [Plugin hosting](/reference/plugin-hosting/)
