---
title: Capability parity
description: Behavioral differences between in-process integrations and out-of-process plugins.
---

Out-of-process plugins use the same SDK capability contracts as in-process integrations, but a process and protocol boundary changes timing, caching, failure handling, and a few identity rules. This page records those differences.

Status meanings:

- **Implemented**: same meaningful contract from a plugin author's perspective.
- **Partial**: supported with a behavior difference worth knowing.
- **Missing**: unavailable to out-of-process plugins.

## Capability matrix

| Area | Status | Important difference |
| --- | --- | --- |
| Actions | Implemented | Execution, dynamic options, and button-state providing are supported. Remote failures/timeouts are converted to action results rather than leaking transport exceptions. |
| Action state providers | Partial | Polled, not pushed - see the state-poll window below. A successful action may provide an expected state id, which the host uses for up to five seconds until a matching provider read confirms it. |
| Action icon providers | Partial | Polled like a state provider, plus a coarse invalidate push - see the icon-poll window below. Unavailability falls back to the widget's configured icon rather than holding the last one shown, the opposite of a state provider's own failure behavior. |
| Explicit widget state writes | Missing | `IWidgetApi.SetStateAsync`/`AdvanceStateAsync` have no wire operation yet, so a plugin call returns `NotFound`. A plugin influences a button's state by *providing* it instead. |
| Widget appearance | Partial | The state a change applies to is addressed by stable id from protocol major `2`. A major `1` session keeps the older fixed selector and the host translates it, so states past the first two are unreachable to it. |
| `ActionResult.Accepted` | Partial | Preserved semantically, although the wire represents it within the action-result payload rather than as a separate transport status. |
| Action ids | Partial | A plugin owns one integration namespace, so action local ids must be unique across the plugin process. |
| Action interactions | Partial | Interaction callbacks such as pickers are available only while the corresponding action execution is live. |
| Variables | Partial | The eager half is snapshot-backed; the catalog half is live - see [Snapshot-backed state](#snapshot-backed-state). Values and writes crossing the wire use the protocol-supported scalar types, and an unsupported CLR value degrades to unavailable. Static attributes ride the `describe` snapshot; volatile `min`/`max`/`step` ride each reading. See [Which side enforces a variable limit](#which-side-enforces-a-variable-limit). |
| Events | Implemented | Catalog/options and event publication are supported. Catalog state is snapshot-backed. |
| Integration icon | Partial | Plugin icons are uploaded/cached as assets rather than synchronously read by the host. A late replacement becomes visible after a new asset commit. |
| Config flows | Partial | Flow sessions are supported. OAuth/session context crosses the wire as invocation state rather than a continuously live host object. |
| Music players | Implemented | Instances, state, controls, artwork, catalog, devices, and transfer operations are supported. State reads can degrade to unavailable; catalog/device failures remain real failures so an unreachable provider is not presented as an empty library. |
| Weather | Implemented | Instances and snapshots are supported; unavailable remote state degrades to an unavailable snapshot. |
| Virtual profiles | Partial | Profile catalogs and widget interactions are supported; catalog state is snapshot-backed and interaction delivery remains fire-and-forget. |
| Device providers | Implemented | Registration, updates, presence and unregistration cross the wire as host callbacks, and the host reads the provider's catalogue back on connect, so a reconnect restores its devices. A dropped session takes them offline and retains them. From capability version 2, deck surfaces, interactions and icon transfer cross with full parity; icon bytes travel the chunked asset pipeline in either direction. **Sessions are always re-opened, never resumed** - a reconnect gets a fresh session, a fresh revision sequence and a complete snapshot rather than a diff. A version 1 plugin gets registration only and the host never opens a session for it. |
| Folder view providers | Implemented | Registration and withdrawal cross the wire as host callbacks (the `folder-views` host API); the host reads the provider's catalog back when the plugin connects, so a reconnect restores it. Withdrawing a view, or losing the session, never resets the folders that selected it: they keep the stored view id and configuration and show Macro Deck's placeholder until the view returns. |
| Widget type providers | Implemented | Registration and withdrawal cross the wire as host callbacks, and the host reads the catalog back on connect. A widget of a provider's type is drawn, previewed and configured through that provider's own `ui` sessions - one per widget per viewer - with no separate rendering path. Losing the session leaves the catalog entry in place, so widgets keep their name and default data across a restarting plugin; only uninstalling or stopping the integration withdraws it. Withdrawing a type never deletes a placed widget. |
| Action modals | Partial | A modal is opened while its `actions/execute` is live, exactly as a picker is. Opening answers immediately with a modal id rather than with the user's answer - `host.invoke` carries a fixed request deadline that a person is not bound by - and the answer arrives later as a `ui`/`modal.result` invoke. A plugin that disconnects while its modal is open gets a cancellation rather than a hang. |
| Layout providers | Implemented | Registration and withdrawal cross the wire as host callbacks (the `layouts` host API); the host reads the provider's layout catalogue back when the plugin connects, so a reconnect restores it without waiting for discovery. A dropped session leaves devices referencing its layouts with their last-resolved geometry rather than unconstraining them. |
| Migrations | Implemented | `describe`, `migrate-action` and `migrate-configuration` are supported, and the declared list is snapshot-backed. A source name the host does not know is dropped from the declaration rather than rejecting the capability. Every failure - an unreachable plugin, a timeout, a malformed result - is read by the host as "no equivalent", so it costs one placeholder action carrying the original configuration rather than failing the migration. |
| Integration issues | Implemented | Listing and resolving issues are supported through live calls. |
| Host callbacks (`IIntegrationContext`) | Implemented | Host APIs cross the protocol or use pushed snapshots rather than direct object references. |
| Synchronous catalogs | Partial | A synchronous SDK enumeration cannot perform a live network round trip. Plugins expose the last host-cached `describe` snapshot until `state.update` refreshes it. |
| Multiple integrations per process | Missing | One connected plugin session is represented as one Macro Deck integration. Ship separate plugins when separate integration identities are required. |
| Integration version/name | Implemented | Derived from installed/declared plugin metadata with host-controlled precedence for managed plugins. |
| Platform declaration | Partial | Availability follows packaged entrypoints/runtime support rather than the in-process integration attribute model. |
| Logging | Implemented | `MacroDeck.Plugin.Serilog` forwards events into the host log pipeline; identity is stamped from the authenticated session. |
| Macro Deck UI | Implemented | Sessions, patch relay, limits and invalidation behave identically for an in-process provider and a plugin: one provider contract, one set of client-visible outcomes. `maxUiResourceBytes` bounds a real transfer, and a payload is relayed byte-for-byte rather than deserialized and re-serialized, so a producer's exact JSON is what a renderer applies. |

## Snapshot-backed state

The most important difference is synchronous enumeration. In-process integrations can expose an in-memory list directly. A plugin cannot block a synchronous SDK property on a WebSocket request, so catalogs such as variables, events, profiles, and provider instances are represented by the most recent snapshot received from the plugin.

The variable catalog is the one exception, and it splits a single capability in two: the eager half is
snapshot-backed, the catalog half is live. `IVariableProvider.Variables` and `DeclaredVariables` are
synchronous properties and are served from the last `variables/describe` snapshot like any other catalog.
`DiscoverAsync`, `ResolveAsync`, `ReadAsync` and `SetValueAsync` are `async` end to end, so there is no
synchronous member to fall back to a snapshot for: they cross the wire as live `capability.invoke` calls
against the connected plugin every time, and a disconnected plugin answers a read as unavailable rather
than serving a stale value. A write is deliberately harsher than a read: it does not degrade to a silent
no-op, but comes back as `Unavailable` or `Failed`, because a control that reports success while changing
nothing is worse than one that reports the truth.

When plugin-side catalog state changes, publish the appropriate state update so the host refreshes it. There is necessarily a short window in which the host still exposes the previous snapshot.

Use explicit async operations for data that must be read live.

### Which side enforces a variable limit

Three of the variable limits are enforced by the plugin-side SDK rather than by the host, so a plugin
talking the raw wire protocol and an in-process provider are bound by neither: `MaxVariableCatalogPageSize`
and `MaxVariableSubscriptions` are clamped in `MacroDeck.Plugin.Hosting`, and `MaxVariableValuesPerBatch`
bounds the `value` host-API push rather than `subscribe` - the SDK splits an oversized publish into several
batches on the way out, and the host rejects an oversized batch on the way in with `InvalidPayload` rather
than truncating it.

`MaxEagerVariablesPerProvider` is the exception. It is clamped plugin-side *and* by the host at
registration, so it binds every provider however it is written, and conformance check MDC0315 reports a
subject that exceeds it.

### The state-poll window

An action that provides a button's states is **polled**, and the lag is part of the contract rather than an implementation detail. A push cannot replace it: `state.update` is keyed by declared capability id - the action *type* - and a provider is a *configured instance*, which has no wire identity, so a plugin cannot name which instance's state changed.

`IStateProviderActionDefinition.StatePollInterval` is a request. The host clamps it to between one second and two minutes, and drops to roughly every thirty seconds while nothing is displaying the button - enough to keep `vars.state` and a later subscriber's first push reasonably fresh without reading for nothing. A `state.update` for the `actions` kind brings the next read forward.

So a button follows a provider within about its poll interval, not instantly. A successful provider action can report an advertised expected state id to bridge that delay: stale reads that disagree do not replace it, a matching read confirms it and returns authority to the provider, and it expires after five seconds. Report state the provider already holds; do not connect or authenticate to answer a read.

### The icon-poll window

An action that provides a widget's icon is polled the same way a state provider is: `IIconProviderActionDefinition.IconPollInterval` is a request, clamped the same way `StatePollInterval` is, and drops while nothing displays the widget.

Unlike state, the icon capability also has a push - `IWidgetApi.InvalidateIconAsync(actionId, cancellationToken)` - but it does not replace polling any more than `state.update` does. It is coarse by design, keyed by the action's *declared local id*, never a configured instance: a configured instance has no wire identity, so the host resolves the call to every widget currently following that action and re-reads each one's own identity, refetching bytes only for the ones that actually changed. It is a dedicated operation rather than a reuse of `state.update`, which re-describes an entire capability's catalog snapshot - far too heavy to fire on every track change. The SDK default implementation is a no-op, so a plugin calling it against a host that predates this capability sees silence, exactly as `SetStateAsync` documents for its own default.

Unavailability behaves the opposite of a state provider: rather than holding the last icon shown, the host falls back to the widget's own configured icon on `null`, a timeout, a thrown exception, or a missing/disabled block or integration. An icon has a meaningful configured default to fall back to; a state does not.

## Failure behavior

Remote adapters should preserve the meaning of the SDK contract rather than expose transport mechanics:

- State-like reads generally degrade to unavailable/empty only when that shape does not falsely claim a meaningful provider state.
- Operations where empty is a valid and materially different answer, such as catalog browsing, surface a real failure instead of pretending the provider returned no entries.
- Action execution returns a truthful `ActionResult`.
- Configuration-flow transport loss is an error because silently continuing a setup wizard would corrupt its state.

Do not write plugin logic that depends on the specific internal transport exception used to produce these SDK-level results.

## Identity

A plugin id is also the integration owner id presented to the host. Local capability ids are qualified by Macro Deck. Do not send already-qualified ids where the SDK expects a local id.

Runtime resource ids may use the broader resource-id grammar defined by the SDK, while authored capability ids use the stable declared-id grammar.

## Host callbacks

`IIntegrationContext` APIs remain available to plugins, but calls can become protocol round trips. Avoid high-frequency loops that repeatedly call the host when an event-driven or cached approach is possible.

Some synchronous context properties are populated from host-pushed snapshots for the same reason as plugin capability catalogs.

## Compatibility

A parity difference is part of the public plugin contract once documented and shipped. Changes must follow the [compatibility policy](/policies/compatibility/).

For exact operation/message fields use the [plugin protocol](/reference/protocol/) and machine-readable specifications. For capability semantics use [Capabilities](/features/).
