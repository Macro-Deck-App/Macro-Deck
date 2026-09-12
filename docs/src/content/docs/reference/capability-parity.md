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
| Variables | Yes | Differs | Eager half snapshot-backed, catalog half live, scalar wire types only - see [Snapshot-backed state](#snapshot-backed-state). |
| Events | Yes | Same | Catalog, options and publication work; the catalog is snapshot-backed. |
| Integration icon | Yes | Differs | Uploaded and cached as an asset instead of read synchronously, so a replacement shows after a new asset commit. |
| Config flows | Yes | Differs | Flow sessions work; OAuth and session context cross as invocation state, not as a live host object. |
| Music players | Yes | Same | Instances, state, controls, artwork, catalog, devices and transfer work; state reads can degrade to unavailable, but catalog and device failures stay real failures. |
| Weather | Yes | Same | Instances and snapshots work; unavailable remote state becomes an unavailable snapshot. |
| Virtual profiles | Yes | Differs | Catalogs and widget interactions work; the catalog is snapshot-backed and interaction delivery is fire-and-forget. |
| Device providers | Yes | Same | Full parity from capability version 2, but sessions are always re-opened, never resumed - see [Device provider sessions](#device-provider-sessions). |
| Folder view providers | Yes | Same | Registered through the `folder-views` host API; see [Provider catalogues across a disconnect](#provider-catalogues-across-a-disconnect). |
| Widget type providers | Yes | Same | Widgets of the type are drawn, previewed and configured through the provider's own `ui` sessions, one per widget per viewer, with no separate rendering path. |
| Layout providers | Yes | Same | Registered through the `layouts` host API; see [Provider catalogues across a disconnect](#provider-catalogues-across-a-disconnect). |
| Migrations | Yes | Same | `describe`, `migrate-action` and `migrate-configuration` work; the declared list is snapshot-backed - see [Migrations](#migrations). |
| Integration issues | Yes | Same | Listing and resolving are live calls. |
| Host callbacks (`IIntegrationContext`) | Yes | Same | Host APIs cross the protocol or use pushed snapshots instead of object references - see [Host callbacks](#host-callbacks). |
| Synchronous catalogs | Yes | Differs | Serve the last `describe` snapshot until `state.update` refreshes it. |
| Multiple integrations per process | Yes | No | One plugin session is one integration; ship separate plugins for separate integration identities. |
| Integration version and name | Yes | Same | Taken from installed or declared plugin metadata, with host-controlled precedence for managed plugins. |
| Platform declaration | Yes | Differs | Follows packaged entrypoints and runtime support, not the `[MacroDeckIntegration]` attribute ([MDP2006](/reference/analyzers/#mdp2006)). |
| Logging | Yes | Same | `MacroDeck.Plugin.Serilog` forwards events to the host log; identity is stamped from the authenticated session. |
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

An icon provider is polled like a state provider: `IIconProviderActionDefinition.IconPollInterval` (default 5 seconds) is a request, clamped the same way, and polled less often while nothing displays the widget. It also has a push, which does not replace polling:

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

Folder view, widget type and layout providers register and withdraw through host callbacks, and the host reads each catalogue back when the plugin connects, so a reconnect restores it without waiting for discovery.

| Provider | Losing the session | Withdrawing |
| --- | --- | --- |
| Folder view | Folders keep their stored view id and configuration and show Macro Deck's placeholder until the view returns. | Same as losing the session. |
| Widget type | The catalogue entry stays, so widgets keep their name and default data while the plugin restarts. | Only uninstalling or stopping the integration withdraws a type, and never deletes a placed widget. |
| Layout | Devices using its layouts keep their last-resolved geometry instead of becoming unconstrained. | - |

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
