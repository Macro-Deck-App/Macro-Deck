---
title: Capability parity
description: Where out-of-process plugins behave differently from in-process integrations, and what that means for your code.
---

A plugin implements the same SDK contracts as an in-process integration, but the process and protocol boundary changes timing, caching, failure handling and a few identity rules. Once a difference is documented and shipped it is part of the plugin contract, and changes follow the [compatibility policy](/policies/compatibility/).

## Capabilities

**Same** means the same contract from a plugin author's point of view; **Differs** means supported with a difference worth knowing; **No** means unavailable to plugins.

| Capability | In-process | Plugin | Notes |
| --- | --- | --- | --- |
| Actions | Yes | Same | Execution, dynamic options and state providing work; remote failures and timeouts become action results, never transport exceptions. |
| Action state providers | Yes | Differs | Polled, not pushed - see [The state-poll window](#the-state-poll-window). |
| Action icon providers | Yes | Differs | Polled plus a coarse invalidate push, and unavailability falls back to the configured icon - see [The icon-poll window](#the-icon-poll-window). |
| Explicit widget state writes | Yes | No | `IWidgetApi.SetStateAsync`/`AdvanceStateAsync` have no wire operation, so they return `NotFound` - see [Explicit state writes](#explicit-state-writes). |
| Widget appearance | Yes | Differs | From protocol major `2` a change addresses its state by stable id; a major `1` session keeps the fixed selector, translated by the host, and cannot reach states past the first two. |
| `ActionResult.Accepted` | Yes | Differs | Same meaning, carried inside the action-result payload rather than as a separate transport status. |
| Action ids | Yes | Differs | A plugin owns one integration namespace, so action local ids must be unique across the whole plugin process. |
| Action interactions | Yes | Differs | Interaction callbacks such as pickers work only while their action execution is live. |
| Action modals | Yes | Differs | Opening answers at once with a modal id, and the answer arrives later - see [Action modals](#action-modals). |
| Notifications | Yes | Differs | `ShowAsync` answers at once with the id and the outcome arrives later as a `ui`/`notification.result` invoke; a result is held while the plugin is detached, and a lost session ends pending ones as `HostUnavailable` - see [Notifications](/features/notifications/). |
| Variables | Yes | Differs | Eager half snapshot-backed, catalog half live, scalar wire types only - see [Snapshot-backed state](#snapshot-backed-state). |
| Events | Yes | Differs | Catalog, options and publication work; the catalog is snapshot-backed, and `GetBindings()` serves the last `event-bindings` push, so it is empty until the host has pushed once. |
| Integration icon | Yes | Differs | Uploaded and cached as an asset instead of read synchronously, so a replacement shows after a new asset commit. |
| Config flows | Yes | Differs | Flow sessions work; OAuth and session context cross as invocation state, not as a live host object. |
| Music players | Yes | Same | Instances, state, controls, artwork, catalog, devices and transfer work; state reads can degrade to unavailable, but catalog and device failures stay real failures. |
| Weather | Yes | Same | Instances, snapshots and the details action work; unavailable remote state becomes an unavailable snapshot. `WeatherProvider.NotifySnapshotsChangedAsync` crosses as `weather`/`snapshots-changed`, merged while one is on its way, needs the negotiated `weather` capability and is a no-op on a Macro Deck that predates it. |
| Calendars | Yes | Differs | Accounts, calendars, events and single-event details work. The account list is snapshot-backed, an `events` reply carries no description or participants and is cut to a size limit, and read failures stay real failures - see [Calendars over the plugin protocol](/features/calendars/#over-the-plugin-protocol). |
| Email inboxes | Yes | Differs | Inboxes, snapshots and `Changed` work. The inbox list is snapshot-backed, `Changed` reaches the host as a `state.update` held back 250 ms per inbox, a snapshot is cut to its field and size limits, and read failures stay real failures - see [Email inboxes over the plugin protocol](/features/email-inboxes/#over-the-plugin-protocol). |
| Virtual profiles | Yes | Differs | Catalogs and widget interactions work; the catalog is snapshot-backed and interaction delivery is fire-and-forget. |
| Device providers | Yes | Same | Full parity from capability version 2, but sessions are always re-opened, never resumed - see [Device provider sessions](#device-provider-sessions). |
| Folder view providers | Yes | Same | Registered through the `folder-views` host API; see [Provider catalogues across a disconnect](#provider-catalogues-across-a-disconnect). |
| Screensaver providers | Yes | Same | Registered through the `screensavers` host API; see [Provider catalogues across a disconnect](#provider-catalogues-across-a-disconnect). |
| Widget type providers | Yes | Same | Widgets of the type are drawn, previewed and configured through the provider's own `ui` sessions, one per widget per viewer, with no separate rendering path. |
| Layout providers | Yes | Same | Registered through the `layouts` host API; see [Provider catalogues across a disconnect](#provider-catalogues-across-a-disconnect). |
| Migrations | Yes | Same | `describe`, `migrate-action` and `migrate-configuration` work; the declared list is snapshot-backed - see [Migrations](#migrations). |
| Integration issues | Yes | Same | Listing and resolving are live calls. |
| Android devices (`IAndroidDeviceManager`) | Yes | Differs | The same operations, typed ones included, and the same locked-host rule; a built-in integration reads it from `IIntegrationContext.AndroidDevices` and needs only ADB enabled, a plugin also needs `host:adb` and the user's consent. A plugin's screenshot is limited to 8 MiB - see [Android devices](/features/android-devices/). |
| Video stream providers | Yes | Differs | Same contract and exactly-one-close rule; a plugin's sessions end with its connection and are never carried across a reconnect - see [Video stream sessions](#video-stream-sessions). |
| Battery providers | Yes | Differs | Same contract and limits. A plugin's devices are withdrawn when its session ends, a resumed reconnect resends them in one `sync` call, and on a host without battery support a publish is a logged no-op - see [Batteries in a plugin](/features/batteries/#in-a-plugin). |
| Battery devices (`IIntegrationContext.Batteries`) | Yes | Differs | A plugin reads the last `batteries` push, so `Devices` is empty until the host has pushed once and `RemovedIds` can miss a removal made while it was disconnected; a built-in integration reads the live catalog - see [Reading devices](/features/batteries/#reading-devices). |
| Companion devices (`ICompanionDeviceApi`) | Yes | Differs | The same devices, commands, screenshots and results. A built-in integration reads it from `IIntegrationContext.CompanionDevices` and always has access; a plugin also needs `host:companion-devices` and the user's consent, reads `Access` and `Devices` from the last `companion-devices` push, so `Access` is `Unsupported` and `Devices` empty until the host has pushed once, and is limited to 8 MiB per screenshot and four calls in flight - see [Companion devices](/features/companion-devices/). |
| Companion device entries (`ICompanionDeviceConfigEntries`) | Yes | Differs | Declared as `companion-device-entries`. Entries are created only for one owner, and a built-in integration that asks for them, such as the built-in Companion integration, always comes before a plugin; a plugin owner gets them only while the plugin may use Companion devices; the entry is prepared through `config-entries` like any stored entry - see [A configuration entry per device](/features/companion-devices/#a-configuration-entry-per-device). |
| Desktop notifications (`IIntegrationContext.DesktopNotifications`) | Yes | Differs | The same shell-first path to the operating system. A built-in integration always may show them; a plugin also needs `host:desktop-notifications` and reads `IsSupported` from the `desktop-notifications` push sent on registration, so it is `false`, and `ShowAsync` shows nothing, until that push arrives - see [Desktop notifications](/features/notifications/#desktop-notifications). |
| Messaging (`IIntegrationContext.Messages`) | Yes | Same | Plugins and built-in integrations share one broker and one set of topics; a plugin's handlers are unreachable while it reconnects - see [Messaging between plugins](/features/messaging/). |
| Host callbacks (`IIntegrationContext`) | Yes | Same | Host APIs cross the protocol or use pushed snapshots instead of object references - see [Host callbacks](#host-callbacks). |
| Synchronous catalogs | Yes | Differs | Serve the last `describe` snapshot until `state.update` refreshes it. |
| Multiple integrations per process | Yes | No | One plugin session is one integration; ship separate plugins for separate integration identities. |
| Integration version and name | Yes | Same | Taken from installed or declared plugin metadata, with host-controlled precedence for managed plugins. |
| Platform declaration | Yes | Differs | Follows packaged entrypoints and runtime support, not the `[MacroDeckIntegration]` attribute ([MDP2006](/reference/analyzers/#mdp2006)). |
| Logging | Yes | Same | `MacroDeck.Plugin.Serilog` forwards events to the host log; identity is stamped from the authenticated session. |
| Client positions | Yes | Differs | A plugin reads them from pushed deck state, so quick moves can arrive as one change and `GetClients()` is empty until the first push after a host restart. In process, every move is seen. See [Deck and clients](/features/deck/). |
| UI resource registration | Yes | Same | Same names, media types and quota. In process the resources are kept across re-initialisation and released when the integration is turned off or Macro Deck stops; a plugin's live as long as its session. See [Resources](/ui/reference/resources/#registering-your-own-images). |
| Reported action parameter values (`ActionConfigurationSession`) | Yes | Same | The same filter and the same flush guarantee: a value reported synchronously inside `Dispatch` reaches the client before a save waits it out. A plugin sends each report from its session queue and waits for the host's answer, which is what keeps that order across the socket - see [Reporting values the tree edits](/ui/views/configuration/#reporting-values-the-tree-edits). |
| Bundled icons (`GetPluginIconAsync`) | Yes | Differs | A plugin names a pack from its manifest's `bundledIconPacks`, served by the icon store and listed to the user. In process the key is a folder under the integration's own `Icons` directory, embedded in its assembly, and the icon (SVG, PNG or WebP) is served as a UI resource the user never sees as a pack. Either way it uses no quota and keeps its handle across restarts. |
| UI event origins (`IUiEventOrigin`) | Yes | Differs | A plugin's calls cross as `ui`/`origin.*`, and a dialog's answer arrives later as `modal.result`; a Macro Deck that predates them answers `false` or a cancellation. See [Answering the client that acted](/ui/views/sessions/#answering-the-client-that-acted). |
| Opening a link on the pressing client (`IUiEventOrigin.OpenUrlAsync`) | Yes | Same | Crosses as `ui`/`origin.open-url` under the same limits in and out of process: `http` or `https` only, the pressing connection only, a client that declared it can open links, about 10 seconds after the event. A Macro Deck that predates it answers `false`. See [Opening a link on the client that pressed](/ui/views/sessions/#opening-a-link-on-the-client-that-pressed). |
| Client events while locked | Yes | Same | Refused with `HOST_LOCKED` for the `widget` and `dialog` sessions of plugins and of integrations served through the SDK alike, except `reveal`. |
| Configuration entry lifecycle (`IConfigEntryLifecycle`) | Yes | Differs | Every member is a `config-entries` round trip; a plugin that cannot be reached gets the defaults, so the entry is stored unchanged and the integration initialized again. Retrying on a taken variable name works the same way. The variable api handed to `SynchronizeEntriesAsync` is the plugin's own remote one. See [Taking part in storing an entry](/features/setup-flows/#taking-part-in-storing-an-entry). |
| Completing a flow without an entry (`ConfigFlowResult.CompleteWithoutEntry`) | Yes | Same | Carried as `withoutEntry` on the `flow.submit` result. See [Completing without an entry](/features/setup-flows/#completing-without-an-entry). |
| Widget data (`IWidgetApi.GetWidgetDataAsync`) | Yes | Differs | Only widgets of the caller's own types; a plugin's read crosses as `widgets`/`get-data` and answers `null` on a Macro Deck that predates it. |
| Variable history (`IVariableApi.OpenHistoryAsync`) | Yes | Differs | Only the caller's own global variables. In process a window is raised on every sample; a plugin's window crosses as `variables`/`history.*` and reads the samples once per second, and ends with the connection. A Macro Deck that predates it reports the history as unsupported. |
| Variable refresh requests | Yes | Differs | `RequestRefreshAsync` and `RequestDefinitionRefreshAsync` cross as `variables`/`refresh`, the latter with `definitionIds`, which can name declared variables and bound catalog resources alike; requests are merged while one is on its way (a request for every declared variable covers requests for declared ones, and named catalog resource ids are sent next to it), and a Macro Deck that predates them makes them a no-op, or reads every variable for a definition refresh. `NotifyDeclarationsChangedAsync` is a `state.update` for `variables`. |
| Recording variable bindings (`IVariableApi.BindAsync`) | Yes | Differs | A plugin's bindings cross as `variables`/`bind`, at most 512 per call; an invalid request is an `ArgumentException` either way. A Macro Deck that predates it reports `NotSupportedException`. |
| Integration storage (`IIntegrationStorage`) | Yes | Differs | Same keys, copies and read outcomes. A built-in integration's documents are under the host's data directory and travel with the Integrations part of a backup; a plugin's are under `integration-data/<plugin id>/` in its own data directory, are never crossed over the protocol, and a plugin without a data directory reads every key as missing and cannot write. See [Integration storage](/features/integration-storage/). |
| Music players of every provider (`IIntegrationContext.MusicPlayers`) | Yes | Differs | A plugin reads `Instances` from the last `music-players` push, so it is empty until the host has pushed once; `GetAlbumColorAsync` crosses as `music-players`/`album-color`, shares the limit of four artwork lookups in flight, and answers `null` when the host refuses or predates it. A built-in integration reads the live list. See [Reading every provider's players](/features/music-players/#reading-every-providers-players). |
| Time format (`IIntegrationContext.Locale`) | Yes | Differs | A plugin's call crosses as `locale`/`time-of-day-format`; a Macro Deck that predates it answers the plugin's current culture with a 24-hour clock. See [Formatting a time of day](/features/localization/#formatting-a-time-of-day). |
| User-Agent (`IIntegrationContext.Http`, `IConfigFlowContext.Http`) | Yes | Differs | A plugin reads the last `http` push, so it reports the SDK default until the host has pushed once, or always on a Macro Deck that predates the push. See [Outgoing HTTP requests](/reference/plugin-hosting/#outgoing-http-requests). |
| Macro Deck UI | Yes | Same | One provider contract: sessions, patch relay, limits and invalidation behave identically, `maxUiResourceBytes` bounds a real transfer, and payloads are relayed byte for byte. |

## Snapshot-backed state

A synchronous SDK property cannot wait on a WebSocket request, so a plugin's catalogs (variables, events, profiles, provider instances, migrations) are served from the most recent snapshot the plugin sent. After a change, publish a state update; until the host has it, it still serves the previous snapshot. Read anything that must be live through an async operation.

Variables split one capability in two:

| Member | Served from |
| --- | --- |
| `IVariableProvider.Variables`, `DeclaredVariables` | The last `variables/describe` snapshot. |
| `DiscoverAsync`, `ResolveAsync`, `ReadAsync`, `SetValueAsync` | A live `capability.invoke` every time. |

When the plugin is disconnected, a read answers **unavailable**, never a stale value. A write answers `Unavailable` or `Failed`, never a silent success. Values and writes use the protocol's scalar types, and an unsupported CLR value degrades to unavailable. Static attributes ride the `describe` snapshot; volatile `min`, `max` and `step` ride each reading.

### Which side enforces a variable limit

| Limit | Enforced by | Effect |
| --- | --- | --- |
| `MaxVariableCatalogPageSize` | Plugin SDK (`MacroDeck.Plugin.Hosting`) | Clamped. Not binding on a raw-protocol plugin or an in-process provider. |
| `MaxVariableSubscriptions` | Plugin SDK | Clamped. Not binding on a raw-protocol plugin or an in-process provider. |
| `MaxVariableValuesPerBatch` | Both, on the `value` host-API push (not `subscribe`) | The SDK splits an oversized publish into batches; the host rejects an oversized batch with `InvalidPayload` rather than truncating it. |
| `MaxEagerVariablesPerProvider` | Plugin SDK **and** host, at registration | Binds every provider however it is written; conformance check [MDC0315](/reference/conformance/#mdc0315) reports a subject over it. |

## The state-poll window

A button following a state provider lags by up to its poll interval. There is no push: `state.update` is keyed by declared capability id (the action *type*), and a configured instance has no wire identity, so a plugin cannot say which instance changed.

Two moments do not wait for the next poll. When the user presses a button and its own provider action succeeds, the host reads the state again right away, with or without an expected state id. When a button starts being displayed after it was hidden, its polling returns to the requested interval on the next scheduling tick instead of keeping the slower idle cadence it used while nothing showed it.

```csharp
// A request, not a guarantee: the host clamps it to 1 s - 2 min.
public TimeSpan StatePollInterval => TimeSpan.FromMilliseconds(200); // polled every 1 s
```

- The default is 2 seconds. While nothing displays the button, the host polls about every 30 seconds instead.
- A `state.update` for the `actions` kind brings the next read forward.
- An action can bridge the lag by returning the state it expects. The host shows it until a matching read confirms it (disagreeing stale reads do not replace it) or 5 seconds pass:

```csharp
return ActionResult.Success(expectedStateId: "playing");
```

Answer a state read from what the provider already holds; do not connect or authenticate to answer it.

## The icon-poll window

An icon provider is polled like a state provider: `IIconProviderActionDefinition.IconPollInterval` (default 5 seconds) is a request, clamped the same way, and polled less often while nothing displays the widget. When the widget starts being displayed again, polling returns to the requested interval on the next scheduling tick. It also has a push, which does not replace polling:

```csharp
await context.Widgets.InvalidateIconAsync("now-playing", cancellationToken);
```

- It is keyed by the action's *declared local id*: the host re-reads every widget following that action and refetches bytes only for those whose identity changed.
- It is its own operation because `state.update` re-describes a whole capability's catalog, which is too heavy for every track change.
- The SDK default is a no-op, so against a host that predates the operation the call does nothing.

On `null`, a timeout, an exception, or a missing or disabled block or integration, the host shows the widget's **configured icon**, not the last one it showed. This is the opposite of a state provider, which keeps its last state, because an icon has a meaningful default and a state does not.

## Explicit state writes

```csharp
var result = await context.Widgets.SetStateAsync(widgetId, "on", cancellationToken);
// In a plugin: result.Success == false, result.Error == WidgetStateWriteError.NotFound
```

Influence a button's state by providing it as a state provider instead.

## Action modals

A modal opens while its `actions/execute` is live, like a picker. Opening returns a modal id immediately, because `host.invoke` has a fixed deadline and a person does not. The user's answer arrives later as a `ui`/`modal.result` invoke. If the plugin disconnects while the modal is open, the modal is cancelled, not left hanging.

## Device provider sessions

Registration, updates, presence and unregistration cross as host callbacks. The host reads the provider's catalogue back on connect, so a reconnect restores its devices; a dropped session takes them offline and keeps them.

| Capability version | Behaviour |
| --- | --- |
| 1 | Registration only; the host never opens a session. |
| 2 and later | Deck surfaces, interactions and icon transfer at full parity; icon bytes use the chunked asset pipeline in both directions. |

**Sessions are always re-opened, never resumed.** A reconnect gets a fresh session, a fresh revision sequence and a complete snapshot, never a diff.

## Provider catalogues across a disconnect

Folder view, screensaver, widget type and layout providers register and withdraw through host callbacks, and the host reads each catalogue back when the plugin connects, so a reconnect restores it without waiting for discovery.

| Provider | Losing the session | Withdrawing |
| --- | --- | --- |
| Folder view | Folders keep their stored view id and configuration and show Macro Deck's placeholder until the view returns. | Same as losing the session. |
| Screensaver | Devices keep their stored screensaver id and configuration and show Macro Deck's clock until the screensaver returns. | Same as losing the session. |
| Widget type | The catalogue entry stays, so widgets keep their name and default data while the plugin restarts. | Only uninstalling or stopping the integration withdraws a type, and never deletes a placed widget. |
| Layout | Devices using its layouts keep their last-resolved geometry instead of becoming unconstrained. | - |

## Video stream sessions

A plugin's [video stream providers](/features/video-streams/) behave as a built-in integration's do, except
where a plugin crosses a connection:

| Event | Built-in integration | Plugin |
| --- | --- | --- |
| The connection to Macro Deck drops, resumed or not | - | The plugin's side closes every open session with `HostDisconnected`; consumers see `ProviderRemoved`, and the providers are listed again once the host has read them back. |
| Stream catalogue | Read from the provider object | Read over `describe` and `streams`, coalesced, after `providers-changed` and `streams-changed` |
| Media | The description's URL is fetched by the host's [relay](/features/video-streams/#the-relay) | The same: the URL is fetched by the host, so a plugin's source can listen on loopback only |
| Call bounds | Every call bounded by ten seconds; an open that returns later is closed at once | At most 8 calls at a time and 4 closes, up to 256 more queued, each bounded by the capability invoke timeout |

Sessions are never resumed across a reconnect: consumers open new ones.

## Migrations

A source name the host does not know is dropped from the declaration instead of rejecting the capability. Any failure - unreachable plugin, timeout, malformed result - reads as "no equivalent" and costs one placeholder action that carries the original configuration, never a failed migration.

<a id="failure-behavior"></a>

## Failure behaviour

Remote adapters keep the meaning of the SDK contract and hide the transport:

| Operation | On transport failure |
| --- | --- |
| State-like reads | Unavailable or empty, but only where that does not falsely claim a real provider state. |
| Reads where empty is a real answer (catalog browsing) | A real failure, never an empty list. |
| Action execution | A truthful `ActionResult`. |
| Config flows | An error, since silently continuing a setup wizard would corrupt it. |

Do not depend on which internal transport exception produced these results.

## Identity

- The plugin id is also the integration owner id the host sees.
- Macro Deck qualifies local capability ids; never pass an already qualified id where the SDK expects a local one.
- Runtime resource ids may use the broader resource-id grammar; authored capability ids use the stable declared-id grammar.

## Host callbacks

`IIntegrationContext` APIs stay available, but a call can be a protocol round trip, so prefer events or caching to tight loops against the host. Some synchronous context properties are filled from host-pushed snapshots, for the same reason as plugin catalogs.

## See also

- [Plugin protocol](/reference/protocol/) - exact operations and message fields.
- [Capabilities](/features/) - capability semantics.
- [Compatibility policy](/policies/compatibility/)
