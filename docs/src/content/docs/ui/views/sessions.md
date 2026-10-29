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

    public ValueTask DisposeAsync()
    {
        _view.Dispose();
        return ValueTask.CompletedTask;
    }
}
```

`ViewSession` is the whole adapter between a `MacroDeck.Ui` `UiView` and `IUiSession`; the other pages
in this section reuse it. `IUiSession` itself speaks only `MacroDeck.Ui.Model` terms, so a provider can
serve a tree without the DSL.

Disposing the view is what lets a closed session go. `_count` belongs to the provider and outlives every
session, and a state keeps each view that reads it alive and flushes into it on every write. A session that
skips `_view.Dispose()` stays in memory for as long as the plugin runs, and so does every patch queued for
it. See [Disposing](/ui/concepts/reactive-updates/#disposing).

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

## Answering the client that acted

Macro Deck calls `Dispatch(UiEvent, IUiEventOrigin)` for every client event. The default forwards to
`Dispatch(UiEvent)`, so a session that does not care who acted implements only that. The origin names the
client (`ClientId`) and acts on it:

| Member | What the client sees |
| --- | --- |
| `ReportFailureAsync(message)` | A failure, shown the way a failed action is shown, for example as a toast. |
| `ShowModalAsync(modal)` | One of your provider's `dialog` views, opened as a [modal](/ui/views/modal/); returns once it is open. |
| `ShowModalAsync<T>(modal)` | The same, waiting for the user's answer. |
| `OpenUrlAsync(url)` | An `http` or `https` page opened in the browser of the device that pressed, for example the phone running the web client. Never the computer Macro Deck runs on, unless that is where the press came from. |

An origin stays usable after your handler returned, so work that finishes later - a request to a device or
a server - can still report its failure on the client that pressed. It acts only while that client is still
attached to the session: once it closed the view or disconnected, the calls answer `false` or a cancellation
and never throw for that. An event Macro Deck raised itself has no client, and its origin reaches nobody.

With the DSL, keep the origin in a field before handing the event to the view. `Dispatch` is never called
concurrently for one session, so the handler that runs during `_view.Dispatch` reads the right one:

```csharp
public sealed class ViewSession : IUiSession
{
    private IUiEventOrigin? _origin;

    // Other members as in the example above.

    public void Dispatch(UiEvent uiEvent) => _view.Dispatch(uiEvent);

    public void Dispatch(UiEvent uiEvent, IUiEventOrigin origin)
    {
        _origin = origin;
        _view.Dispatch(uiEvent);
    }

    private async Task ToggleAsync()
    {
        var origin = _origin;
        if (!await _client.TryToggleAsync())
        {
            await origin!.ReportFailureAsync(Strings.Widget.ToggleFailed());
        }
    }
}
```

Capture the origin into a local before the first `await`, as `ToggleAsync` does: the field holds the
origin of the latest event, which may be a different client's by the time the work resumes.

### Opening a link on the client that pressed

`OpenUrlAsync` is narrower than the other members, because it puts a page in front of someone:

- It accepts only an absolute `http` or `https` URL and throws `ArgumentException` for anything else, such
  as `javascript:`, `file:` or a relative path.
- It reaches only the connection the event came from, not every tab or device that shares its client id.
- That connection must have told Macro Deck it can open links. The web client and the desktop UI do;
  hardware decks, older clients and clients that cannot open a browser do not.
- It works only for a short time after the event, about 10 seconds, so answer the press with it rather
  than keep it for later work.
- One event opens one link. A second call for the same press answers `false`. Calls from a plugin also
  count against its callback throttle.

It answers `true` when the link was handed to the client. That does not prove a page opened: a browser may
still block the new tab, in which case the web client offers the link to tap. It answers `false` when any
of the conditions above does not hold, and a Macro Deck or client that predates it always answers `false`.
Show the link some other way then, for example in one of your dialogs:

```csharp
private async Task OpenArticleAsync(IUiEventOrigin origin, Uri link)
{
    if (!await origin.OpenUrlAsync(link))
    {
        await origin.ShowModalAsync(new ModalDefinition
        {
            ViewId = "article",
            Data = new Dictionary<string, JsonElement>
            {
                ["url"] = JsonSerializer.SerializeToElement(link.AbsoluteUri)
            }
        });
    }
}
```

An `IUiEventOrigin` implemented before `OpenUrlAsync` existed keeps compiling: the default member checks
the URL the same way and answers `false`.

A plugin talking to a Macro Deck that predates origins still receives every event through this overload;
its origins answer `false` or a cancellation. In tests, pass a `FakeUiEventOrigin` from
`MacroDeck.Plugin.Testing` to `Dispatch` and read what the session showed from `ReportedFailures`,
`OpenedModals` and `OpenedUrls`.

## While Macro Deck is locked

While the computer running Macro Deck is locked, a client's events to a `widget` or `dialog` session of a
plugin, or of an integration served the same way, never reach the provider - the same rule that keeps
hardware presses and actions from running then. The client's request is refused with `HOST_LOCKED`, and a
press, long press, double press, tap, change, adjust, swipe or end of a drag shows the client the same
"locked" failure a refused action shows. `reveal` is still delivered, so a widget can keep its content up to
date. You do not need a lock check of your own. On Linux the host cannot detect the lock and is never in the
locked state, so these events are always delivered there.

## The lifecycle every surface shares

| Step | What happens | Your side |
| --- | --- | --- |
| Open | The host asks for a session for one surface, carrying the UI model version it speaks (`request.UiModelVersion`). | Return a session or `null`. The session id is host-issued. |
| Snapshot | The host asks for a full tree when a client attaches and whenever it must resynchronise. | `BuildTree()` must describe the revision your emitted patches have reached. |
| Patches | You raise `Changed`; the host drains. | Coalescing several changes into one raise is fine - the host drains rather than counts. A patch dropped in `DrainPatches` is lost to every attached client. |
| Events | `Dispatch` delivers a client event, never concurrently for one session. | Reject an event by producing no patch. A throw faults the session. |
| Close | The host disposes the session - at any time: a client leaving for good, a limit trip, a fault, or .NET Hot Reload updating your plugin, after which clients open a new session. | Release what the session holds in `DisposeAsync`, including disposing its `UiView`. |

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
| `maxUiResourceBytes` | Both a `UiResource`'s declared `byteLength` in a plugin's tree and the bytes the host's resource store accepts for one resource a plugin registers. A `byteLength` of `null` is accepted. |
| `maxUiResourceBytesPerPlugin` / `maxUiResourcesPerPlugin` | The bytes and the number of [resources](/ui/reference/resources/#registering-your-own-images) one plugin may have registered at once. |
| `maxUiWidgetSessionsPerProvider` | Open `widget` and `preview` sessions of one provider for one viewer (the device that owns them). The same deck on three devices can give your provider up to three times as many sessions. |
| `maxUiSessionsPerProvider` | Open `folder` and `screensaver` sessions of one provider for one viewer, and, as a separate count, every other surface (`config`, `dialog`, `page`) of one provider across all viewers. |

A session over one of these caps is refused with `TOO_MANY_SESSIONS`. Clients retry a refused `widget`, `folder` or `screensaver` session with backoff, because those slots free up as other sessions of the same viewer close.

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
| `describe` | The surfaces this provider serves, the UI model version it speaks, and the [preview scenarios](/ui/views/developer-preview/) it declares. `previews` is optional: a plugin built against an SDK that predates it omits the key, and the host reads that as none. `pages` lists the [integration pages](/ui/views/page/) it declares, optional the same way. |
| `session.open` | Open a session for one surface. The session id is host-issued; a provider never mints one. |
| `session.open` (config) | A `config` surface names its entry point in its surface attributes - `integration-config` with the config flow session, `action-config` with the action id and the instance's stored parameters, `folder-view-config` with the folder and its view, or `widget-config` with the widget id, type and stored configuration. `MacroDeck.Plugin.Hosting` routes `integration-config` and `action-config` to `IUiConfigFlow`/`IUiConfigurableActionDefinition` before it consults `IUiProvider`. |
| `session.open` (developer preview) | A `developer-preview` surface names one registered scenario in its surface attributes. `MacroDeck.Plugin.Hosting` builds that scenario and never consults `IUiProvider`, so a production provider is unreachable from a preview. |
| `session.snapshot` | Produce the current full tree. The tree does not return on the result - it arrives as a separate `host.invoke ui/snapshot`, so a first attach and a resync share one delivery path. A provider may also send `ui/snapshot` unasked to replace its whole tree, at any revision; later patches continue from that tree's revision. |
| `session.event` | A client acted on a node. `clientId` says which one, and is meaningful only for a shared session. `MacroDeck.Plugin.Hosting` hands it to your session as the event's origin. A `pointer-move` still waiting to be sent is replaced by a newer one for the same node from the same client, and one the plugin could not take because it was busy or slow is dropped; see [Pointer streams and taps](/ui/components/modifier/#pointer-streams-and-taps). |
| `session.flush` | Answer once every `session.event` sent for the session before it has been handled, and every `ui/report-parameter-values` those events raised has been answered. The host sends it when a client is about to save an action. A plugin built before it answers `CAPABILITY_UNSUPPORTED`, which the host treats as flushed: such a plugin never reports values. |
| `session.close` | The host is ending this session. |
| `modal.result` | How a modal this plugin opened ended. Host-to-plugin because a person, not a timeout, bounds the wait - see [Modal views](/ui/views/modal/). |

Trees, patches and faults travel the other way through the [`ui` host api](/reference/websocket/#host-callbacks),
and so do an origin's calls: `origin.report-failure`, `origin.show-modal` and `origin.open-url` name the
session and the `clientId` the event carried. The host acts on them only for a session the plugin serves and
a client still attached to it, and answers `delivered: false` or `opened: false` otherwise; `origin.open-url`
also needs the conditions in [Opening a link on the client that pressed](#opening-a-link-on-the-client-that-pressed)
and answers `INVALID_PAYLOAD` for a URL that is not `http` or `https`. A host that predates them answers
`CAPABILITY_UNSUPPORTED`, which the SDK turns into `false` or a cancellation.

`origin.read-upload` reads text a [code input](/ui/views/configuration/#code-input) uploaded in one of the
plugin's sessions. It answers `found: false` for any session the plugin does not serve, so one plugin never
reads another's uploads, and sends the bytes after the reply as a `ui-upload` asset; see
[the protocol reference](/reference/protocol/#ui-uploads). A host that predates it answers
`CAPABILITY_UNSUPPORTED`, which the SDK turns into `NotSupportedException`.

An action configuration session that reports parameter values (see
[From an action](/ui/views/configuration/#reporting-values-the-tree-edits)) sends them as
`ui/report-parameter-values`. `MacroDeck.Plugin.Hosting` sends each report from the session's own queue,
in order, and waits for its answer before it handles the next queued item, which is what lets
`session.flush` promise that the host already has them. A host that predates the operation answers
`CAPABILITY_UNSUPPORTED`; the SDK then drops reports for the rest of the connection and logs it once.

On the client side, a session's reported values reach the attached client as
`UiSessionParameterValuesReportedEvent` (`sessionId`, `values`). A client asks with
`FlushUiSession` (`sessionId`, `flushId`), which is answered at once, and receives
`UiSessionFlushedEvent` (`sessionId`, `flushId`) on that connection alone once the session's provider has
handled every event the client sent before, after every value those events reported. A host that predates
flushing refuses the request as an unknown type, so a client treats that as flushed.

## See also

- [Views and surfaces](/ui/views/)
- [Integration pages](/ui/views/page/)
- [Patches](/ui/reference/patches/)
- [Resources](/ui/reference/resources/)
- [Plugin hosting](/reference/plugin-hosting/)
