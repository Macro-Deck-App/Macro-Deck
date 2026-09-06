---
title: Plugin protocol
description: Human overview of the versioned HTTP and WebSocket contract for out-of-process plugins.
---

The public plugin protocol is a versioned JSON contract over HTTP and WebSocket. Most .NET plugins should use `MacroDeck.Plugin.Hosting`; implement this protocol directly only when building another runtime or language binding.

Machine-readable contracts are authoritative for exact request/message fields:

- [OpenAPI specification](/specs/openapi.yaml) for HTTP bootstrap, registration, sessions, and the WebSocket endpoint.
- [AsyncAPI specification](/specs/asyncapi.yaml) for WebSocket envelopes and message payloads.

The public DTOs and constants live in `MacroDeck.Plugin.Protocol` and are independent from host implementation assemblies.

## Connection lifecycle

A self-registering plugin typically:

1. Reads the protocol descriptor.
2. Registers with an enrollment credential when it does not already have a plugin registration.
3. Exchanges its plugin credential for a short-lived session.
4. Connects to `/plugins/ws` with the session token.
5. Sends `session.hello` and waits for `session.welcome`.
6. Exchanges capability and host-callback messages until the session ends.

Installed managed plugins skip self-registration and receive launch credentials from the host. See [Authentication](/sdk/authentication/) and [Plugin hosting](/sdk/hosting/).

## Envelope

Every WebSocket message uses the protocol envelope. Exact fields are defined by AsyncAPI and `MacroDeck.Plugin.Protocol`.

Compatibility rules:

- Unknown optional fields are ignored.
- Unknown message types return a protocol error and do not by themselves terminate the session.
- Malformed messages are rejected as protocol errors rather than becoming unhandled parser exceptions.
- Correlation ids are preserved where possible so callers can match replies/errors to requests.

## Version negotiation

The protocol uses an integer major version. Additive changes remain inside the current major; breaking wire changes require a new major.

Three majors are served today: `1` through `3`. Major `2` changes only the `widgets` host-api payload, where a widget appearance change now names the states it applies to by stable id instead of the old fixed selector. Major `3` lets a plugin's descriptor text be a localized reference instead of a plain string. A plugin that speaks an earlier major keeps working; the host translates for it. See the [migration guide](/policies/migrations/).

The WebSocket sub-protocol string names the protocol *family*, not the major, and does not change between majors — the session handshake is the only place the version is negotiated.

Session creation negotiates the highest mutually supported version. `session.hello` confirms the already negotiated session/version; it does not perform a second negotiation.

Capability versions negotiate independently. An unsupported capability is rejected/degraded without necessarily rejecting the whole session.

See the [compatibility policy](/policies/compatibility/).

## Capability operations

A plugin declares the capabilities it can serve. The host invokes them through `capability.invoke`; the plugin replies through `capability.result` or the corresponding error/cancellation path.

Supported capability families include actions, variables, events, icons, config flows, music players, weather, virtual profiles, issues, device providers (`device-provider`), layout providers (`layout-provider`), folder view providers (`folder-view-provider`), migrations (`migration`), and Macro Deck UI (`ui`). Exact operation names and payloads are defined by the protocol package and AsyncAPI.

Do not invent custom operation names inside an existing capability kind. Additions to the public operation vocabulary are compatibility-sensitive protocol changes.

`state.update` is an invalidation signal. It tells the peer to refresh the relevant capability state rather than defining a second per-capability diff protocol.

The `actions` kind gained a `state` operation, additively and inside major `1`, for actions that supply an Action Button's states. It is keyed by the action's *configured parameters*, so the host polls it rather than expecting a push: `state.update` is keyed by declared capability id — the action type — and so cannot name which configured instance changed. See [capabilities](/sdk/capabilities/).

The `actions` kind also gained `icon` and `icon.content` operations, the same way and inside the same major, for an action whose configured instance supplies a widget's rendered icon; `ActionDescriptorDto.ProvidesIcon` marks it. `icon` is polled like `state`, answering an identity rather than bytes; `icon.content` fetches the bytes behind that identity only when it changes, uploaded over the same `asset.*` pipeline a plugin uses to send the host any other asset it originates, never inside the capability reply itself. A plugin can also ask the host to re-read a specific action sooner than its next poll through the `widgets` host API's `invalidate-icon` operation. See [capabilities](/sdk/capabilities/) and [Capability parity](/sdk/capability-parity/).

## Host callbacks

Plugins can call host-owned APIs through the host invocation/result messages. `MacroDeck.Plugin.Hosting` maps these to `IIntegrationContext` APIs.

Some synchronous-looking SDK state is backed by the last snapshot pushed over the protocol. See [Capability parity](/sdk/capability-parity/) before assuming an out-of-process call has the same timing as an in-process integration.

A host callback can also hand bytes back to the plugin - today, an icon fetched through the `devices`
api's `icon` operation, or a widget's currently rendered action-icon-provider icon fetched through the
same api's `widget-icon` operation. Those travel over `host.asset.*`, a chunked pipeline the host drives
in the opposite direction from the plugin-driven `asset.*` pipeline a plugin uses to upload its own assets.
The two are separate message-type sets on purpose: `asset.*` keeps its existing plugin-to-host
direction exactly as any other protocol major-1 type must, and the new host-to-plugin direction gets
its own types instead of a meaning change grafted onto old ones. See
[the WebSocket reference](/reference/websocket/#assets).

## Errors

Protocol errors use stable error codes and correlated replies where a correlation id is available. Malformed input, unsupported operations/capabilities, authentication failures, timeouts, cancellation, and backpressure are protocol outcomes, not unhandled transport exceptions.

Use the AsyncAPI/OpenAPI specifications and `MacroDeck.Plugin.Protocol` for the exact error payload and currently defined codes.

## Delivery and retries

The protocol is at-most-once and does not maintain a replay log for messages lost during a disconnect.

For retryable operations that must not execute twice, reuse the protocol idempotency key as required by the operation. Do not assume the host will replay an unacknowledged event after reconnection.

## Limits and backpressure

Message size, queue, concurrency, timeout, and asset limits are published in the protocol descriptor and constants. Treat these values as negotiated/runtime limits rather than copying literal numbers into plugin logic or documentation.

A peer that produces work faster than the bounded protocol queues can accept may be disconnected or receive the defined protocol error. Plugin code should use bounded concurrency and honor cancellation.

## Session resume and replacement

A dropped connection may resume the same session while it remains resumable. A fresh session after the old session expires is different from a resume and can require capability state/lifecycle reinitialization.

`MacroDeck.Plugin.Hosting` handles the normal reconnect/resume behavior for .NET plugins.

## Security

Plugin endpoints require plugin-specific credentials and are restricted to the local machine. Browser cookies are not a plugin WebSocket authentication mechanism.

Never log plugin secrets, session tokens, enrollment credentials, OAuth credentials, or authorization headers.

See [Security](/policies/security/) and [Authentication](/sdk/authentication/).

## SDK compatibility metadata

A plugin can report the SDK version it was built against and build-time evidence about deprecated API usage. The host can return a compatibility report without making older plugins unable to deserialize a session response.

See [Deprecations](/policies/deprecations/) for the evidence model.

## Implementing the protocol yourself

Use the OpenAPI/AsyncAPI specifications and the `MacroDeck.Plugin.Protocol` package as the source of truth. The human documentation intentionally does not duplicate every field, error code, timeout, operation, or message type.

A custom implementation should test at least negotiation, authentication, unknown-message tolerance, cancellation, idempotency/retry behavior, reconnect/resume, backpressure, and capability payload compatibility.

## Related documentation

- [Plugin hosting](/sdk/hosting/)
- [Capability parity](/sdk/capability-parity/)
- [Manifest](/reference/manifest/)
- [Conformance suite](/sdk/conformance/)
- [Compatibility policy](/policies/compatibility/)
