---
title: WebSocket reference
description: The envelope, the full message catalogue with direction and payload, correlation and cancellation rules, error handling and the unknown-message-type rule.
---

This is the message-by-message reference for the plugin WebSocket transport.
[The protocol page](/reference/protocol/) carries the narrative - why the contract is shaped this way,
how negotiation works, what backpressure and resume mean - and is not repeated here. Read that first;
come here for the exact shapes.

The machine-readable contract is [asyncapi.yaml](/specs/asyncapi.yaml) (AsyncAPI 3.0.0). The C# types
live in
[`protocol/src/MacroDeck.Plugin.Protocol/`](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/protocol/src/MacroDeck.Plugin.Protocol/),
and drift tests keep the two in step.

## Connecting

| | |
| --- | --- |
| Path | `/plugins/ws` |
| Subprotocol | `macrodeck.plugin.v1`, offered through `Sec-WebSocket-Protocol` |
| Authentication | `Authorization: Bearer <sessionToken>`, scope `plugin` |
| Encoding | JSON, camelCase, `application/json` |

The host requires the subprotocol and authenticates before accepting the upgrade. See
[authentication](/reference/authentication/) for how the session token is obtained.

## The envelope

Every message, in either direction, is one `ProtocolEnvelope`.

| Field | Type | Required | Notes |
| --- | --- | --- | --- |
| `type` | string | yes | `<domain>.<verb>`, e.g. `capability.invoke`. |
| `id` | string | yes | UUIDv7, minted by the sender. |
| `correlationId` | string | no | The `id` of the message this one answers. |
| `sentAt` | string | no | RFC 3339 UTC. **Informational only** - clocks differ between the two processes. |
| `protocolVersion` | integer | no | |
| `deadlineMs` | integer | no | |
| `idempotencyKey` | string | no | At most 128 characters, scoped to `(sessionId, key)`. |
| `payload` | object | no | Shaped by `type`. |
| `error` | `ProtocolError` | no | |

`payload` and `error` are **mutually exclusive** on the same envelope. Unknown optional fields are
ignored on read and never echoed back: there is no extension-data slot, so a sender cannot rely on
unrecognised fields surviving a round trip.

The deadline, the idempotency key and the correlation live on the envelope and nowhere else. No payload
carries a second copy - that would be a second source of truth the two peers could disagree about.

Serialisation is strict: camelCase, case-sensitive, no comments, no trailing commas, and a number sent
as a string (`"deadlineMs": "30000"`) is a hard failure. Maximum nesting depth is `maxJsonDepth` (32).

### `ProtocolError`

| Field | Type | Required |
| --- | --- | --- |
| `code` | string | yes |
| `message` | string | yes |
| `details` | map of string to string | no |
| `retryable` | boolean | yes |

`message` is a **default English string keyed by `code`, not copy to render**. Localise from the code.

## Message catalogue

Twenty-eight message types. "Direction" is what the transport enforces - a message arriving from the
wrong side is rejected.

### Session

| Type | Direction | Purpose | Payload |
| --- | --- | --- | --- |
| `session.hello` | plugin → host | Asserts the already-negotiated version and session id. Never re-negotiates. | `protocolVersion` (int, required), `sessionId` (required), `resumeSessionId`, `instanceId` |
| `session.welcome` | host → plugin | The reply to `session.hello`. | `sessionId` (required), `resumed` (bool, required) |
| `session.goodbye` | both | Voluntary teardown. Makes the session non-resumable at once. | `reason` |
| `session.ping` | both | Keep-alive probe. | empty object |
| `session.pong` | both | Keep-alive reply. | empty object |

`resumeSessionId` on `session.hello` is what distinguishes a resume from a replacement. A connection
that presents it inside the resume window resumes; one that does not **replaces** the prior session,
closing it with `4000`.

`session.goodbye` is genuinely bidirectional: a plugin sends it to end its own session, and the host
sends it to a managed plugin the supervisor is deliberately stopping, immediately before a `4004`
close.

### Capabilities

| Type | Direction | Purpose | Payload |
| --- | --- | --- | --- |
| `capability.declare` | plugin → host | Declares the plugin's capability catalogue. **Complete, not a delta** - a re-declaration replaces what the session knows. | `capabilities` (array, required, ≤ 512 items) |
| `capability.declare.ack` | host → plugin | Acknowledges it with the per-capability negotiation result. | `capabilities` (array, required) |
| `capability.invoke` | host → plugin | Invokes one declared capability. | `kind`, `localId`, `operation` (all required), `arguments` |
| `capability.result` | plugin → host | The result of an invoke. Carries the **success value only** - a failure sets the envelope's `error` instead. | `data` |
| `capability.cancel` | host → plugin | Best-effort cancellation. | `reason` |

A `DeclaredCapability` is `{ kind, localId, versionRange: { minimum, maximum } }` plus an optional
`displayName`. A `CapabilityNegotiationResult` is `{ kind, accepted }` plus `negotiatedVersion` and
`rejectionReason`.

The sixteen `kind` values are `actions`, `events`, `variables`, `icons`, `config-flow`, `music-player`,
`weather`, `virtual-profiles`, `issues`, `ui`, `localization`, `device-provider`, `layout-provider`,
`folder-view-provider`, `migration`, `widget-type-provider`.
`operation` is drawn from a fixed vocabulary per kind - see
[capability operations](/reference/protocol/#capability-operations) for the full table.

Capability negotiation fails **non-fatally**: an unsupported or unknown kind lands in the result as
rejected with a reason, and the session proceeds degraded.

`localization` is how a plugin hands its own strings to the host instead of baking resolved text into
the UI it produces. It has two operations, both `capability.invoke`:

| Operation | Arguments | Result | Purpose |
| --- | --- | --- | --- |
| `describe` | none | `scope`, `defaultCulture`, `cultures` | Declares the plugin's own scope (`plugin:<plugin-id>`) and which cultures it ships, so the host can reject a plugin claiming a scope it does not own. |
| `catalog` | `culture` | `culture`, `entries` (key to template) | Hands over one culture's strings. A plugin with nothing for that culture returns an empty map rather than failing - the host's own fallback chain, not the plugin, decides what happens next. |

The host asks for one culture at a time: a catalog is bounded per culture but not across them, and the
host only ever needs the active language and its fallback chain, not every culture a plugin ships.
Entries are bounded by `maxLocalizationCultures`, `maxLocalizationEntries`, `maxLocalizationKeyLength`
and `maxLocalizationValueLength` below. See [Localization](/features/localization/) for how a plugin
produces these resources, and `MacroDeck.Plugin.Protocol.Capabilities.Localization` for the exact DTOs.

`variables` is **item-shaped and provider-shaped at once**, and is the only kind that is. Its eager half
declares one capability per variable, at that variable's own declared local id, exactly as `actions`
does. Its catalog half declares nothing at all: a provider's browsable resource space may run to tens of
thousands of entries, so those ids are carried in each operation's own arguments instead - what keeps
such a provider well under `maxDeclaredCapabilities`. Six operations, all `capability.invoke`:

| Operation | Arguments | Result | Purpose |
| --- | --- | --- | --- |
| `describe` | none | `variables`, `declaredVariables`, `variablesDependOnConfiguration`, `supportsCatalog`, `supportsPush`, `supportsSearch`, `catalogName` | The provider's eager definitions and whether it also serves a catalog. |
| `get` | none | `value`, `min`, `max`, `step` | Reads one variable's current value together with its volatile attributes, or unavailable when it cannot be read right now. |
| `set` | `value` | `status`, `message` | Applies a value. Only ever invoked for a definition that declared `write` - the host refuses the rest itself. A refused write is a `status`, not a failed operation. |
| `discover` | `parentId`, `search`, `continuationToken`, `pageSize` | `items` (array of definitions), `continuationToken` | One page of the catalog resources the user can browse and bind. |
| `resolve` | `id` | `definition` (nullable) | Resolves one catalog resource id, including one that never came out of `discover`. A `null` result means the id is invalid, not merely unavailable. |
| `subscribe` | `ids` (array) | `values` (array) | Replaces the provider's catalog working set wholesale - an empty array is a legitimate "watch nothing" - and returns each requested id's current value. |

`get` and `set` address one variable through the invoke message's own `localId` - which is why neither
repeats it in `arguments` - while `describe` and the three catalog operations ignore it, the catalog ones
carrying their resource ids in `arguments` instead. A definition's `materialization` (`eager` or
`on-demand`) is a declaration the host validates against the operation it arrived on, so a catalog
cannot smuggle a variable into the eager set or the other way round.

See [Variables](/features/variables/) for the SDK-side contract these operations mirror.

### Host callbacks

| Type | Direction | Purpose | Payload |
| --- | --- | --- | --- |
| `host.invoke` | plugin → host | A plugin calling a host API - the reverse of `capability.invoke`. | `api`, `operation` (required), `arguments` |
| `host.result` | host → plugin | The result. Success value only. | `data` |
| `host.cancel` | plugin → host | Best-effort cancellation of a `host.invoke`. | `reason` |
| `host.state` | host → plugin | The host pushing the list a plugin's synchronous members serve from. | `api` (required), `data` |

The host APIs a plugin may call are `variables`, `user-variables`, `config`, `deck`, `scripts`,
`widgets`, `notifications`, `action-interactions`, `ui`, `devices`, `variable-values`, `layouts`,
`folder-views`, `widget-types`. `events` is deliberately absent - `event.publish` already exists.

`action-interactions`/`show-modal` is the one host API whose answer does not come back on its own result.
It answers with a modal id as soon as the modal is open, because `host.invoke` carries a fixed request
deadline and a person is not bound by it; the user's answer arrives later as a `ui`/`modal.result`
`capability.invoke` naming the same modal. Exactly one result is ever sent per modal, so a plugin waiting
on one always settles.

`host.state` for `config` carries no `data`: it is a bare invalidation meaning "your config changed,
re-read it".

The `widgets` api is the one place a payload differs by negotiated protocol major, because a widget
may now have any number of states rather than exactly two:

| | Major `1` | Major `2` |
| --- | --- | --- |
| `widgets`/`apply` arguments | `state`, an integer selector (`0` current, `1` on, `2` off, `3` both) | `stateIds`, an array of stable state ids or the `$current` / `$all` sentinels |
| `host.state` for `widgets` | each entry carries `hasOnOffStates` | each entry carries `states` (`{id, label}`) and `currentStateId` |

The host translates for a major `1` session rather than refusing it — `off`/`on` map to the states
with those literal ids, falling back to the first and second state, and `both` reaches every state
including a third and beyond. See the [migration guide](/policies/migrations/) for the full mapping.

The `ui` api is how a UI provider pushes into a session the host opened on it. It has three operations,
all `host.invoke`:

| Operation | Arguments | Purpose |
| --- | --- | --- |
| `snapshot` | `sessionId`, `tree` | The session's current full tree. Send one for every `session.snapshot` the host invokes. |
| `patch` | `sessionId`, `patch` | One patch advancing the session's revision. `fromRevision` and `toRevision` are read out of the patch itself - they are not repeated on the arguments. |
| `fault` | `sessionId`, `code`, `message` | This session can no longer be served. The host ends it and tells the attached clients; your text is not relayed to them. |

`tree` and `patch` are opaque JSON: the host bounds them and forwards the exact bytes you sent, so
unknown members, member order and number formatting all survive to the client unchanged.

The reply matters here. A refused snapshot or patch comes back as an error on that call's own
`host.result` - `PAYLOAD_TOO_LARGE`, `RATE_LIMITED`, `INVALID_PAYLOAD`, `SESSION_NOT_FOUND` - so a
provider always learns that an update did not reach anyone. Unlike every other host api, `ui` is not
charged to the per-plugin callback throttle; it is bounded per session by `maxUiUpdatesPerSecond` and
`maxUiUpdateBurst` instead. See [Serving a view](/ui/views/sessions/).

The `devices` api is how a device provider registers devices and, once a session is open, reports
input and fetches icons. All seven operations are `host.invoke`:

| Operation | Arguments | Purpose |
| --- | --- | --- |
| `register` | a `DeviceDescriptor` | Registers a device, or re-registers a known provider-local id as the same device. |
| `update` | a `DeviceDescriptor` | Refreshes a registered device's metadata. |
| `presence` | `deviceId`, `presence` | Reports whether a registered device is currently reachable. |
| `unregister` | `deviceId` | Withdraws a device from this session; the device itself is retained. |
| `interaction` | `sessionId`, `kind`, `widgetId`, `controlIndex`, `value`, `surfaceRevision`, `data` | Reports a hardware interaction from an open device session. Returns the host's `DeviceInteractionResult`. |
| `icon` | `sessionId`, `iconId`, `size`, `knownETag` | Fetches icon bytes referenced by the device's current surface. The bytes travel over `host.asset.*`, not in this call's own result. |
| `close` | `sessionId` | Closes an open device session at the provider's own request. |

`register`, `update`, `presence` and `unregister` exist independently of a session and are keyed by the
provider-local `deviceId`; they are available to every plugin declaring `device-provider`.
`interaction`, `icon` and `close` are keyed by the `sessionId` the host handed out with `session.open`
and only apply to a device whose session the host has opened - capability version 2 - and are the reverse direction of the
capability's own `session.open`/`session.surface`/`session.close` invokes described under
[capabilities](#capabilities). See [Device providers](/features/devices/).

The `variable-values` api is how a push-capable variable provider delivers values the host did not ask
for, and how it tells the host its resource catalog changed. Both operations are `host.invoke`:

| Operation | Arguments | Purpose |
| --- | --- | --- |
| `value` | `values` (array of `{id, reading}`) | Publishes values for catalog resources in the provider's most recently subscribed set. An id outside that set is dropped rather than faulting the call. Bounded per batch by `maxVariableValuesPerBatch`. |
| `invalidate` | none | Tells the host the enumerable resource set changed. Currently accepted and recorded but not acted on: nothing re-queries `discover` in response, so a browser must still re-query on its own schedule. |

This api is data-carrying rather than an invalidate-then-reread signal, for the same reason `ui` is:
pushed values are plugin-initiated and asynchronous, and no dedicated message type carries them. It
covers the catalog half only - a provider's eager variables are polled through `variables`/`get` whatever
it reports for push. See [Push instead of poll](/features/variables/#push-instead-of-poll).

### Events, logs and state

| Type | Direction | Purpose | Payload |
| --- | --- | --- | --- |
| `event.publish` | plugin → host | Publishes a domain event. **Fire-and-forget** - no reply message. | `eventId` (required, unqualified; the host qualifies it with the authenticated plugin id), `parameters` (an object; a value that is itself an object or array is delivered to triggers and templates as its JSON text) |
| `log.publish` | plugin → host | Forwards a batch of structured log events. **Fire-and-forget.** | `events` (array, required), `dropped` (int) |
| `state.update` | plugin → host | "This kind's snapshot is stale, re-describe it." Deliberately **not** data-carrying. | `kind` (required), `localId`, `reason` |

A `LogEventDto` is `{ timestamp, level, messageTemplate, renderedMessage }` plus optional
`sourceContext`, a flat string-to-string `properties` map, and a structured `exception`
(`{ type, message, stackTrace?, inner? }`). It carries nothing identifying - no plugin id, integration
id, version or process id - because the host already has all four from the authenticated session. See
[logging](/features/logging/).

### Assets

| Type | Direction | Purpose | Payload |
| --- | --- | --- | --- |
| `asset.begin` | plugin → host | Starts a chunked upload. `totalBytes` is checked against `maxAssetBytes` before a byte is buffered. | `assetId`, `kind`, `mimeType`, `totalBytes`, `contentHash` (all required) |
| `asset.chunk` | plugin → host | One chunk. `data` is base64; the pre-encoding size is bounded by `maxAssetChunkBytes`. | `assetId`, `index`, `data` (all required) |
| `asset.commit` | plugin → host | Finalises the upload. | `assetId` (required) |
| `asset.ack` | host → plugin | Acknowledges any of the three. | `assetId`, `accepted` (required), `index` |

One unacknowledged step in flight at a time, never a burst. `index` must equal the next expected index
- no reordering, no gaps, no duplicates - and `asset.commit` verifies both the final byte count and a
recomputed content hash against what `asset.begin` declared. A resumed session never resumes an
in-flight upload; it restarts from `asset.begin`.

`host.asset.*` is the same chunked mechanism run in the opposite direction, for the host to hand a
plugin bytes it asked for - today, an icon requested through the `devices` api's `icon` operation. It
is a **separate message-type set**, not a reuse of `asset.begin`/`chunk`/`commit`/`ack` with the
direction flipped: those four are fixed at plugin → host (`asset.ack` the lone host → plugin reply),
and changing what an existing v1 message type means would be a breaking wire change rather than an
additive one. `host.asset.*` exists precisely so it doesn't have to be one.

| Type | Direction | Purpose | Payload |
| --- | --- | --- | --- |
| `host.asset.begin` | host → plugin | Starts a chunked download the plugin did not ask for by a separate message - it is the host's answer to a `devices`/`icon` call. | `assetId`, `kind`, `mimeType`, `totalBytes`, `contentHash` (all required) |
| `host.asset.chunk` | host → plugin | One chunk, same shape and bounds as `asset.chunk`. | `assetId`, `index`, `data` (all required) |
| `host.asset.commit` | host → plugin | Finalises the transfer. | `assetId` (required) |
| `host.asset.ack` | plugin → host | Acknowledges any of the three. | `assetId`, `accepted` (required), `index` |

The same ordering, bounding and resume rules apply, mirrored: the host sends at most one
unacknowledged step at a time, and a resumed session restarts the transfer from `host.asset.begin`
rather than continuing an in-flight one.

### Flow control and errors

| Type | Direction | Purpose | Payload |
| --- | --- | --- | --- |
| `flow.pause` | both | Requests the peer stop sending non-exempt types. | `reason` (required), `resumeAfterMs` |
| `flow.resume` | both | Lifts a prior pause. | `reason` (required), `resumeAfterMs` |
| `protocol.error` | both | Carries a `ProtocolError`. | see below |

## Correlation and response pairing

A reply carries the `id` of the message it answers in its own `correlationId`.

Five types **require** a correlation id; a reply of one of these without it is malformed:

`capability.result`, `capability.declare.ack`, `asset.ack`, `host.result`, `host.asset.ack`.

`protocol.error` is deliberately excluded from that list: whether it correlates depends on whether it
answers a specific message, which a flat per-type predicate cannot express.

The resolution rules, in order:

| Situation | Outcome |
| --- | --- |
| A reply type with no `correlationId` | `MALFORMED_ENVELOPE` |
| A non-reply type with no `correlationId` | Accepted |
| A correlation that already timed out | **Dropped silently** - never reported |
| A correlation the receiver does not recognise | Dropped and logged as `CORRELATION_UNKNOWN` |
| A known, live correlation | Accepted |

The late-result case is silent on purpose: a result arriving after its own timeout is expected on a
loaded system, not a fault worth reporting.

Cancellation is best-effort in both directions. `capability.cancel` and `host.cancel` against an
unknown correlation are a no-op, never an error, because a cancel and a result can legitimately cross
on the wire. The receiver of a cancel still emits exactly one `capability.result` with a cancelled
outcome, unless it had already replied.

Retry safety comes entirely from `idempotencyKey` - delivery is at-most-once, with no sequence numbers
and no replay buffer. A repeat while the original is in flight fails with `DUPLICATE_IDEMPOTENCY_KEY`;
a repeat after completion returns the cached result. That cache is host-side and survives a resume, but
a restarted plugin process re-executes.

## Backpressure

`flow.pause` asks the peer to stop sending; `flow.resume` lifts it. Fourteen message types stay exempt
while a pause is in effect, because they drain the peer's queue rather than growing it:

`capability.result`, `capability.declare.ack`, `asset.ack`, `host.asset.ack`, `host.result`,
`session.ping`, `session.pong`, `session.goodbye`, `flow.pause`, `flow.resume`, `capability.cancel`,
`protocol.error`, `host.invoke`, `host.cancel`.

`host.invoke` and `host.cancel` are exempt for a reason specific to the callback direction: a plugin's
capability handler is often synchronously blocked waiting on `host.result`, so parking its `host.invoke`
behind a pause would live-lock the handler rather than merely delay it.

Ignoring a pause past `maxInboundQueueDepth` earns one `QUEUE_OVERFLOW` and a `1013` close.

## Error handling

A protocol-level failure sets the envelope's `error` instead of its `payload`. The full code table with
default messages is in [the protocol page](/reference/protocol/#errors); the twenty-one codes are:

`PROTOCOL_VERSION_UNSUPPORTED`, `UNKNOWN_MESSAGE_TYPE`, `MALFORMED_ENVELOPE`, `INVALID_PAYLOAD`,
`UNAUTHENTICATED`, `PLUGIN_ALREADY_REGISTERED`, `SESSION_EXPIRED`, `SESSION_NOT_RESUMABLE`,
`SESSION_REPLACED`, `SESSION_NOT_FOUND`, `CAPABILITY_UNSUPPORTED`, `CAPABILITY_UNAVAILABLE`,
`PAYLOAD_TOO_LARGE`,
`ASSET_TOO_LARGE`, `QUEUE_OVERFLOW`, `RATE_LIMITED`, `TIMEOUT`, `CANCELLED`, `CORRELATION_UNKNOWN`,
`DUPLICATE_IDEMPOTENCY_KEY`, `INTERNAL_ERROR`.

The list is append-only within a protocol major: removing or renaming a code requires the current
version to advance.

### Close codes

Closing the socket is reserved for exactly seven conditions:

| Code | Condition |
| --- | --- |
| `1013` | `QUEUE_OVERFLOW` - RFC 6455 "Try Again Later", not a Macro Deck code |
| `4000` | `SESSION_REPLACED` |
| `4001` | `PROTOCOL_VERSION_UNSUPPORTED` |
| `4002` | `SESSION_EXPIRED` |
| `4003` | Authentication failed |
| `4004` | `SupervisorShutdown` - the supervisor is stopping a managed plugin. Not an error code: nothing failed |
| `4005` | `RegistrationRejected` - invalid or duplicated declared ids, or a colliding integration id. Terminal, not a resumable drop |

### The unknown-message-type rule

An envelope whose `type` is not recognised produces `UNKNOWN_MESSAGE_TYPE`, with the parsed `id`
preserved so the reply can set `correlationId`. **It never closes the connection.** Neither does a
malformed envelope, which produces `MALFORMED_ENVELOPE` - covering oversize input, depth violations and
a missing `type`.

This is what makes every additive protocol change backward-compatible by construction: a new message
type sent to a peer that has never heard of it is reported and ignored, not fatal. Together with
"unknown fields are ignored", it is why a protocol version bump only ever means *breaking*.

## Limits and timeouts

Advertised at runtime in the protocol descriptor and again in the session response, so read them rather
than hard-coding them. The values today:

| Limit | Value |
| --- | --- |
| `maxMessageBytes` | 256 KiB |
| `maxAssetBytes` | 8 MiB |
| `maxAssetChunkBytes` | 64 KiB |
| `maxInboundQueueDepth` / `maxOutboundQueueDepth` | 256 |
| Queue high / low watermark | 192 / 64 |
| `maxConcurrentInvocations` | 32 |
| `maxDeclaredCapabilities` | 512 |
| `maxIdempotencyKeyLength` | 128 |
| `maxJsonDepth` | 32 |
| `maxSessionsPerPlugin` | 1 |
| `maxLocalizationCultures` | 64 |
| `maxLocalizationEntries` | 2000 |
| `maxLocalizationKeyLength` | 128 |
| `maxLocalizationValueLength` | 4096 |
| `maxUiTreeBytes` | 192 KiB |
| `maxUiPatchBytes` | 64 KiB |
| `maxUiNodesPerTree` | 2000 |
| `maxUiUpdatesPerSecond` / `maxUiUpdateBurst` | 30 / 90 |
| `maxUiResourceBytes` | 2 MiB |
| `maxUiAttachmentsPerSession` | 16 |
| `maxUiSessionsPerProvider` | 8 |

The `maxUi*` limits are optional in the descriptor: a host that predates the `ui` capability omits
them.

| Timeout | Value |
| --- | --- |
| Handshake | 10s |
| Default request | 30s |
| Capability invoke | 30s |
| Asset upload | 60s |
| Keep-alive interval | 20s |
| Keep-alive timeout | 60s |
| Session resume window | 60s |
| Graceful close | 5s |

## See also

- [Plugin protocol](/reference/protocol/) - the narrative: negotiation, host callbacks, the asset
  pipeline, reconnection and resume.
- [asyncapi.yaml](/specs/asyncapi.yaml) - this page, machine-readable.
- [openapi.yaml](/specs/openapi.yaml) - the REST surface that precedes the upgrade.
- [Authentication](/reference/authentication/) - how the session token on the upgrade is obtained.
- [Plugin hosting](/reference/plugin-hosting/) - the .NET client that implements all of this for you.
