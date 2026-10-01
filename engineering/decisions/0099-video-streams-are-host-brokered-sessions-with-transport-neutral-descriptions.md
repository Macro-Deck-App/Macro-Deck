# ADR 0099: Video streams are host-brokered sessions with transport-neutral descriptions

Status: Accepted. Partially superseded by [ADR 0101](0101-the-host-relays-video-stream-media-and-clients-play-hls-and-mjpeg.md):
the host now relays the media, the description is `{transport, url}` for `hls` and `mjpeg` only, and
signaling and the consumer context are removed.

## Context

Plugins and built-in integrations know about live video: OBS scenes, cameras behind a video recorder,
capture devices. Macro Deck's clients should be able to show it, but no client can talk to a plugin
directly: plugins are separate processes that only talk to the host
([ADR 0026](0026-plugin-protocol-and-sdk-boundary.md)), and built-in integrations run inside it. One
contract has to serve both, the same way for every client.

The media itself must not flow through the host. Every source already serves video in its own way:
WebRTC and WHEP, HLS, MJPEG over HTTP. WebRTC is one transport among several, and the host cannot know
them all. What a source needs is a place to be asked for a stream, to say how to play it, to exchange a few
negotiation messages, and to learn when to release it again.

Whatever carries this becomes public SDK and protocol surface, frozen once shipped, and a plugin built
before video streams existed must keep working on a host that has them and the reverse.

## Decision

**An integration opts in through a marker interface and registers provider objects.**
`IVideoStreamIntegration.InitializeAsync` receives an `IVideoStreamProviderContext`, called after the
integration itself initialized. The integration registers any number of `IVideoStreamProvider` objects,
one per source it talks to, and withdraws them as its configuration changes. The capability kind is
declared only by a plugin with such an integration, so no other plugin's declared capabilities change.

**The host is the single writer of the catalog.** It reads providers and their streams through the
`video-stream-provider` capability kind (`describe`, `streams`). The plugin side only says when to read
again (`providers-changed`, `streams-changed` on the `video-streams` host api). Reads are coalesced, one in
flight per plugin, and applied only while the plugin connection they came from is still current. A
registration carries a fresh id, so a provider withdrawn and registered again between two reads still
closes the sessions of the earlier registration.

**Sessions are brokered by the host and owned by a UI connection.** A client opens a session over the UI
WebSocket ([ADR 0062](0062-ui-realtime-transport.md)); the host mints the session id, asks the provider,
and owns the session on behalf of that one connection, which has to renew a lease to keep it. The host
exposes this as a client session API on the UI socket, not as public plugin surface. Provider calls never
run on the UI connection's dispatch loop.

**The description is transport-neutral, and signaling is opaque.** A provider answers an open with a
transport token and a URL, parameters, payload and expiry that only the consumer interprets. Signals are a
type and a payload the provider and consumer agree on, relayed unchanged and in order per session.
Supporting a new transport needs no change to Macro Deck.

**Exactly one close.** Every open that returned a description is followed by exactly one close for that
session, unless the provider closed it itself, including when the consumer went away while the open was
still running. The first close wins: the consumer is told once, with one reason. This is what lets a
provider release per-session credentials and resources in one place.

**Video has its own budgets.** Host-to-plugin video calls use their own concurrency slots per plugin, with
closes in separate slots that are never dropped, and plugin-to-host `video-streams` calls have their own
rate limit, so video traffic cannot starve button presses and other host calls. Sizes are bounded by
`VideoStreamLimits`, checked in the SDK before sending and again by the host.

**Sessions do not survive a plugin reconnect.** Whenever a plugin session ends or detaches, the host closes
every video session opened under it and removes the plugin's providers until it reads them again; the SDK
closes its side of those sessions with `HostDisconnected`. Registrations survive a resumed connection;
consumers open new sessions either way.

**Older hosts degrade quietly.** A host without the kind rejects it non-fatally, the plugin is reported
partially incompatible, and registering returns an empty registration instead of throwing.

**`host:video-streams` is declared, not enforced**, like every permission except `host:adb`
([ADR 0092](0092-plugins-reach-adb-through-a-permission-gated-host-api.md)).

## Consequences

- Trust model: any authenticated Macro Deck client, the desktop app or any paired device, can list every
  provider's streams and open a session on them. Whatever a description carries reaches that client, so
  providers are told to hand out short-lived, session-scoped credentials with an expiry rather than
  long-lived secrets. The consumer context (device id, address, connection kind) is a hint for building a
  reachable URL and never a basis for authorization. The host never logs descriptions or signal payloads.
- Reachability is the provider's problem. The host tells it how the consumer is connected, including a
  USB-tunnelled device whose loopback address is its own, but carries no media and opens no ports for it.
- A dropped plugin connection interrupts every stream it served, even one that resumes within seconds.
  In exchange no provider has to reconcile sessions across a connection it did not see end, and the host
  has no half-alive state to time out.
- Adding a transport is a provider and client change only. The vocabulary of transport tokens and signal
  types is open and not validated beyond its shape.
- Consumers get a stable wire and reasons to act on: `ProviderRemoved` is retryable once the provider is
  listed again, and `video_stream_` reasons refine `CAPABILITY_UNAVAILABLE` without a new error code or
  protocol major.

## Alternatives rejected

- **A member on `IIntegrationContext`, like messaging.** It would have made every plugin declare the kind
  on every host that offers it, whether it has video or not, and put a provider API on a context most
  integrations never need. A marker interface keeps the declaration to the plugins that serve video.
- **Keeping sessions across a plugin resume and reconciling them afterwards.** It needed a reconciliation
  operation, a reconnect deadline and pending closes that span connections, all to save a reopen that
  clients do anyway after any interruption.

## References

- [Video streams](../../docs/src/content/docs/features/video-streams.md)
- [`MacroDeck.Sdk.VideoStreams`](../../sdk/src/MacroDeck.Sdk/VideoStreams/)
- [`VideoStreamLimits`](../../protocol/src/MacroDeck.Plugin.Protocol/Limits/VideoStreamLimits.cs)
- [`VideoStreamProviderCapabilityHandler`](../../sdk/src/MacroDeck.Plugin.Hosting/Capabilities/VideoStreamProvider/VideoStreamProviderCapabilityHandler.cs)
- [`VideoStreamSessionTable`](../../sdk/src/MacroDeck.Plugin.Hosting/Capabilities/VideoStreamProvider/VideoStreamSessionTable.cs)
- [`VideoStreamProviderRegistry`](../../host/src/MacroDeckHost.Application/VideoStreams/VideoStreamProviderRegistry.cs)
- [`VideoStreamSessionBroker`](../../host/src/MacroDeckHost.Application/VideoStreams/VideoStreamSessionBroker.cs)
- [`InProcessVideoStreamEndpoint`](../../host/src/MacroDeckHost.Application/VideoStreams/InProcessVideoStreamEndpoint.cs)
  and [`RemoteVideoStreamEndpoint`](../../host/src/MacroDeckHost.Application/VideoStreams/RemoteVideoStreamEndpoint.cs)
- [`PluginCallbackRouter`](../../host/src/MacroDeckHost/Plugins/Capabilities/Callbacks/PluginCallbackRouter.cs)
- [`UiWebSocketDispatcher`](../../host/src/MacroDeckHost/Ui/UiWebSocketDispatcher.cs),
  [`VideoStreamConsumerClassifier`](../../host/src/MacroDeckHost/Ui/VideoStreamConsumerClassifier.cs) and
  [`VideoStreamUiPushHandler`](../../host/src/MacroDeckHost.Application/VideoStreams/VideoStreamUiPushHandler.cs)
- [`video-stream.ts`](../../ui/runtime/src/protocol/messages/video-stream.ts), the client session API's types
- [0050 - UI sessions are host-brokered](0050-ui-sessions-are-host-brokered.md)
- [0062 - UI realtime is a ticketed JSON WebSocket, and it never blocks on a provider](0062-ui-realtime-transport.md)
- [0093 - Plugins and integrations talk over a host-brokered message channel](0093-plugins-and-integrations-talk-over-a-host-brokered-message-channel.md)
