# ADR 0101: The host relays video stream media, and clients play only HLS and MJPEG

Status: Accepted

Partially supersedes [ADR 0099](0099-video-streams-are-host-brokered-sessions-with-transport-neutral-descriptions.md):
its rule that the media never flows through the host, the opaque description and the opaque signaling.
Everything else in ADR 0099 stands.

## Context

ADR 0099 let a provider answer an open with a transport token, a URL, parameters, a payload and an
expiry, and let the consumer fetch the media directly. In practice that pushed the work onto every
provider. A source had to open a port reachable from the network, guess an address per consumer (the
computer itself, the LAN, a USB-tunnelled phone whose loopback is its own) and mint per-session
credentials, which then travelled to every client. The web and desktop clients cannot play HLS, the phone
plays HLS and MJPEG, and no real source needed WebRTC or WHEP, yet the clients, the SDK and the protocol
carried a peer player, signaling and a consumer context for them.

Video streams have not been released: no tag contains them, so the public SDK and protocol surface could
still change without breaking a plugin in the field.

## Decision

**Macro Deck always relays the media.** A provider returns where the host fetches the stream, as HLS or
MJPEG. The host serves it to the consumer under a URL on itself, `/api/video-streams/relay/<token>/...`,
for every consumer alike, local ones included. The provider's URL never reaches a client, so a source can
listen on loopback only and needs no firewall rule, no address guessing and no client credentials.

**The relay URL is a capability.** The token is unguessable, bound to one session, stable for its lifetime
across updates and resumes, sent only over the authenticated UI WebSocket to the connection that owns the
session, and dead when the session ends or is suspended. The route is anonymous because an image or video
element cannot send headers. The relay pins the origin of the provider's URL, follows redirects and
rewrites HLS playlist URIs only within it, allows `GET` and `HEAD`, checks content types against the
session's transport, marks responses `nosniff`, `no-store` and sandboxed, bounds header and idle time and
concurrency, and redacts the token from logs. The [video streams guide](../../docs/src/content/docs/features/video-streams.md#the-relay)
and the [security model](../../docs/src/content/docs/policies/security.md#video-streams) state the exact
rules.

**Clients offer only `hls` and `mjpeg`.** `hls` only where the engine plays it natively, `mjpeg` always.
The host refuses any other transport with `TransportNotAccepted`. The `url` a client receives is
host-relative or absolute, and every client resolves it against the origin it already uses for the host.

**The surface shrinks to what is used.** Removed before release: signaling (`SignalAsync`,
`SendSignalAsync`, `VideoStreamSignal`, `session.signal`, `session-signal`), the consumer context and its
connection-kind classifier, and the description's `Parameters`, `Payload` and `ExpiresAt`, together with
the `webrtc` and `whep` players.

**The description is shaped to grow.** `VideoStreamSessionDescription` is a sealed class with a `Transport`
and a nullable `Url`, built through factories (`Hls`, `Mjpeg`, `FromUrl`); new source kinds add members and
factories additively. The wire object is `{transport, url}`, and a reader ignores members it does not
know. A `null` Url is reserved for source kinds that carry none.

## Consequences

- A provider's setup gets simpler: listen on loopback, return one URL, release resources in `CloseAsync`.
  The media path is one hop longer, through the host, which bounds each session and the host in total, and
  a plain-http listener is HTTP/1.1, so a browser shows about six live MJPEG widgets before other requests
  queue. HTTPS and HTTP/2 lift that.
- A plugin can point the relay at any service the host can reach, including the host itself. It could make
  those requests without the relay, so it adds no capability, and the content-type allowlist and sandbox
  stop the returned bytes from running as a page on the host's origin.
- A static camera that sends no bytes for the idle timeout is cut and the client reconnects.
- A plugin built against the earlier, unreleased shape must be updated. Protocol and capability versions do
  not move.
- The Companion app has to resolve a host-relative `url` against its host base before this reaches users.
- Two follow-ups extend the description additively. A binary channel in the plugin protocol lets a plugin
  hand bytes to the host. A frame source then lets a plugin push JPEG frames that the host serves as
  MJPEG through the same relay. `VideoStreamWire` requires a Url for `hls` and `mjpeg` today, and that check
  must change together with the frame source, which returns `mjpeg` without one.

## Alternatives rejected

- **WebRTC through the host.** The host would terminate or forward peer connections and the media clock,
  which is a media server. No source needs it, and the clients that do not play HLS are covered by MJPEG.
- **The provider serves the consumer directly, with better hints.** It keeps the firewall, address and
  credential problems with every provider and leaks the source's URL to every client.
- **Relaying only when the consumer is remote.** Two code paths, and a source that works locally would
  fail for a phone. One path means local and remote behave the same.
- **A session-bound path that is not a capability URL.** An image or video element cannot carry an
  authorization header, so authenticating the request itself would need cookies or a signed query, which
  add state and are no stronger than an unguessable session-bound token.

## References

- [Video streams](../../docs/src/content/docs/features/video-streams.md)
- [ADR 0099](0099-video-streams-are-host-brokered-sessions-with-transport-neutral-descriptions.md)
- [`VideoStreamSessionDescription`](../../sdk/src/MacroDeck.Sdk/VideoStreams/VideoStreamSessionDescription.cs)
- [`VideoStreamWire`](../../sdk/src/MacroDeck.Plugin.Hosting/Capabilities/VideoStreamProvider/VideoStreamWire.cs)
