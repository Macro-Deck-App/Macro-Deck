---
title: Video streams
description: Offer live video to Macro Deck with IVideoStreamIntegration and IVideoStreamProvider - streams and their state, sessions, updates and signaling, consumer context, limits, reconnects, older hosts, security and testing.
---

A video stream provider offers live video to Macro Deck: the scenes of an OBS instance, the cameras of
a video recorder, a capture device. You list the streams you have; when a consumer wants to show one,
Macro Deck opens a **session** on your provider, and you answer with a description of how that consumer
plays the stream, such as an HLS URL or a WebRTC offer.

Macro Deck never carries video itself. It brokers the session: it opens it for the consumer, passes
your description on unchanged, relays signals in both directions and makes sure every session you opened
is closed exactly once. The media travels directly from your source to the consumer.

## Quick start

An integration implements `IVideoStreamIntegration` and registers one `IVideoStreamProvider` per source
it talks to. The provider below offers two cameras over HLS:

```csharp
using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.VideoStreams;

public sealed class DoorCameraIntegration(CameraServer server) : IPluginIntegration, IVideoStreamIntegration
{
	public Task InitializeAsync(IVideoStreamProviderContext context, CancellationToken cancellationToken = default)
		=> context.RegisterProviderAsync(new DoorCameras(server), cancellationToken);

	// IPluginIntegration members omitted.
}

public sealed class DoorCameras(CameraServer server) : IVideoStreamProvider
{
	public string Id => "door-cameras";

	public LocalizedText Name => Strings.Providers.DoorCameras();

	public Task<IReadOnlyList<VideoStreamDescriptor>> GetStreamsAsync(CancellationToken cancellationToken)
		=> Task.FromResult<IReadOnlyList<VideoStreamDescriptor>>(
		[
			new("front", LocalizedText.FromLiteral("Front door"), Width: 1920, Height: 1080),
			new("garage", LocalizedText.FromLiteral("Garage"), Width: 1280, Height: 720, HasAudio: true,
				State: server.IsOnline("garage") ? VideoStreamState.Connected : VideoStreamState.Disconnected)
		]);

	public async Task<VideoStreamSessionDescription> OpenAsync(
		VideoStreamOpenRequest request,
		CancellationToken cancellationToken)
	{
		if (!request.AcceptedTransports.Contains("hls"))
		{
			throw new VideoStreamException(VideoStreamErrorCode.TransportNotAccepted, "Only HLS is served.");
		}

		var token = await server.IssueTokenAsync(request.SessionId, request.StreamId, TimeSpan.FromMinutes(10));
		var host = request.Consumer.ConnectionKind == VideoStreamConnectionKind.Local ? "127.0.0.1" : server.LanAddress;

		return new VideoStreamSessionDescription("hls",
			Url: $"http://{host}:{server.Port}/{request.StreamId}/index.m3u8?token={token.Value}",
			ExpiresAt: token.ExpiresAt);
	}

	public Task CloseAsync(string sessionId, VideoStreamSessionReason reason, CancellationToken cancellationToken)
		=> server.RevokeTokenAsync(sessionId);
}
```

Things to know:

- **Order is fixed.** Macro Deck calls `IVideoStreamIntegration.InitializeAsync` after the integration's
  own `InitializeAsync`. Register what is configured then, and register or withdraw more later as the
  configuration changes; the context stays valid for as long as the integration runs.
- **A provider is a plain object**, not the integration. One integration can register several, for
  example one per OBS instance.
- **Every `OpenAsync` that returned a description gets exactly one `CloseAsync`.** Release what a session
  holds there and nowhere else - see [Sessions](#sessions).
- **Refuse with `VideoStreamException`.** Its `VideoStreamErrorCode` reaches the consumer; its message is
  diagnostic only and is not shown to anyone.

## Registering providers

| `IVideoStreamProviderContext` member | What it does |
| --- | --- |
| `RegisterProviderAsync` | Registers a provider. Returns `VideoStreamProviderRegistration(QualifiedId, ProviderId)`; the qualified id is `plugin.id::provider-id`. Throws `ArgumentException` for an invalid or duplicate id, or a seventeenth provider. |
| `UnregisterProviderAsync` | Withdraws a provider. Its open sessions are closed first, each with one `CloseAsync` and `ProviderRemoved`; a session whose open is still running is closed as soon as the open returns. Unknown ids are ignored. |
| `NotifyStreamsChangedAsync` | Tells Macro Deck the provider's streams changed. Macro Deck calls `GetStreamsAsync` again. |
| `UpdateSessionAsync`, `SendSignalAsync`, `CloseSessionAsync` | Session calls from your side - see [Updates from the provider](#updates-from-the-provider). |

| `IVideoStreamProvider` member | Meaning |
| --- | --- |
| `Id` | Stable, unique among every provider your plugin registers across all its integrations. A resource local id: non-empty, at most 256 characters, no whitespace and no `::`. Consumers store it. |
| `Name`, `Description` | Shown in pickers, in the reader's own language. `Description` is optional. |

Registering again under the same id after unregistering is a new registration: Macro Deck closes every
session it had opened on the earlier one, even if both happened so quickly that it only saw the result.

Only providers of enabled integrations are listed and can be opened.

## Streams and their state

`GetStreamsAsync` returns the provider's streams right now, as `VideoStreamDescriptor` records:

| Parameter | Meaning |
| --- | --- |
| `Id` | Stable, unique within the provider: 1 to 256 characters, no control characters. Spaces are allowed, so a source's own name, such as an OBS scene name, can serve as the id. Consumers store it. |
| `Name`, `Description` | The stream as a picker shows it. |
| `Width`, `Height` | Native size in pixels, when known. Consumers use them to reserve the right aspect ratio before the first frame. |
| `HasAudio` | Whether the stream carries audio. |
| `State` | `Connected` (can be opened, the default), `Connecting`, `Disconnected` (the provider has no connection to its source) or `Unavailable` (the source does not offer it right now). |
| `Metadata` | Provider-defined, opaque to Macro Deck. |

Call `NotifyStreamsChangedAsync` whenever a stream is added or removed, or a stream's metadata or state
changes. The notification carries nothing: Macro Deck reads `GetStreamsAsync` again, and several
notifications in quick succession are coalesced into one read. A stream's `State` describes the source,
independent of any session; a consumer that shows it re-reads the list when it changes.

## Sessions

A session is one consumer showing one stream. Macro Deck mints its id and passes it to `OpenAsync` in
`VideoStreamOpenRequest.SessionId`; every later call for the session names it.

| Call | When | Your answer |
| --- | --- | --- |
| `OpenAsync` | A consumer starts showing a stream. | A `VideoStreamSessionDescription` whose transport is one of `AcceptedTransports`, or a `VideoStreamException`. |
| `SuspendAsync` | The consumer stopped showing the stream for now, for example because its page is hidden. | Pause whatever is expensive. The default does nothing and keeps the session running. |
| `ResumeAsync` | The consumer shows a suspended stream again. | A new description, or null when the previous one still works (the default). |
| `SignalAsync` | The consumer sent a signal. | The direct answer, or null. The default refuses with `SignalingUnsupported`. |
| `CloseAsync` | The session ended. | Release everything the session holds. |

**Exactly one close.** Every `OpenAsync` that returned a description is followed by exactly one
`CloseAsync` for that session id, unless you ended the session yourself with `CloseSessionAsync`. That
holds when the consumer went away while your `OpenAsync` was still running: the close then follows the
open's return. An `OpenAsync` that threw gets no `CloseAsync`.

`CloseAsync` tells you why in its `VideoStreamSessionReason`:

| Reason | Meaning |
| --- | --- |
| `ConsumerClosed` | The consumer closed the session. |
| `LeaseExpired` | The consumer stopped renewing the session, for example because it crashed. |
| `ConsumerDisconnected` | The consumer's connection to Macro Deck ended. |
| `ProviderRemoved` | You withdrew the provider, or the integration stopped, was disabled or is initializing again. |
| `HostDisconnected` | Your plugin lost its connection to Macro Deck; the session was opened on the earlier connection. |
| `HostShutdown` | Macro Deck is shutting down. |
| `Failed` | The session failed, for example because a relayed signal could not be delivered. |

Calls for different sessions can run concurrently. Macro Deck bounds every call by a timeout, the
capability invoke timeout for a plugin and ten seconds for a built-in integration, and treats one that
times out as failed. A late `OpenAsync` that returns after its timeout still gets its
`CloseAsync`.

### Choosing a transport

`VideoStreamSessionDescription.Transport` is a lowercase token that names how the stream is delivered:
`webrtc`, `whep`, `hls`, `mjpeg` or any other. The vocabulary is open. A consumer lists the transports it
can play in `AcceptedTransports`, most preferred first; pick the first one you serve, or refuse with
`TransportNotAccepted`. The rest of the description is for that transport:

| Member | Meaning |
| --- | --- |
| `Url` | Where the consumer fetches the stream, for transports that use one. It must be reachable from the consumer - see [The consumer](#the-consumer). |
| `Parameters` | Transport-specific settings. |
| `Payload` | A transport-specific document, for example a WebRTC offer. |
| `ExpiresAt` | When `Url` or `Payload` stops working. Send a replacement with `UpdateSessionAsync` before then. |

Macro Deck never interprets a description; it hands it to the consumer as it is.

## Updates from the provider

Sources drop out. Report that on the sessions it affects, and the consumer can show it instead of a frozen
frame:

```csharp
await context.UpdateSessionAsync(sessionId, VideoStreamSessionState.Reconnecting,
	reason: VideoStreamSessionReason.ProviderReconnecting,
	message: Strings.Status.ObsReconnecting());

// Once the source is back, optionally with a new description the consumer switches to:
await context.UpdateSessionAsync(sessionId, VideoStreamSessionState.Active, freshDescription,
	VideoStreamSessionReason.SourceRecovered);
```

`Reconnecting` means the session is interrupted and you are recovering it; `Active` means the consumer can
play from the current description. Use `SourceLost` when the source stopped delivering and
`SourceRecovered` when it delivers again. `message` is text the consumer may show, in the reader's own
language. An update for a session that is no longer open is ignored.

`CloseSessionAsync` ends a session from your side, with `ProviderClosed` unless you name another reason.
Macro Deck does not call `CloseAsync` for a session you closed yourself.

Updates and signals you send while `OpenAsync` is still running are held and delivered in order after the
open returns. At most 64 are held per session; beyond that the session fails.

## Signaling

Some transports need messages in both directions after the description, such as a WebRTC answer and
trickled ICE candidates. A `VideoStreamSignal` is a `Type` and a `Payload`, both agreed between you and the
consumer; Macro Deck relays them unchanged and defines no types of its own.

```csharp
public async Task<VideoStreamSessionDescription> OpenAsync(VideoStreamOpenRequest request, CancellationToken cancellationToken)
{
	var peer = await _webRtc.CreatePeerAsync(request.SessionId, request.StreamId, cancellationToken);
	peer.CandidateFound += candidate => _ = _context.SendSignalAsync(request.SessionId,
		new VideoStreamSignal("candidate", candidate.ToJson()));

	return new VideoStreamSessionDescription("webrtc", Payload: peer.CreateOffer());
}

public async Task<VideoStreamSignal?> SignalAsync(string sessionId, VideoStreamSignal signal, CancellationToken cancellationToken)
{
	switch (signal.Type)
	{
		case "answer":
			await _webRtc.Peer(sessionId).SetAnswerAsync(signal.Payload, cancellationToken);
			return null;
		case "candidate":
			await _webRtc.Peer(sessionId).AddCandidateAsync(signal.Payload, cancellationToken);
			return null;
		default:
			throw new VideoStreamException(VideoStreamErrorCode.Failed, $"Unknown signal type {signal.Type}.");
	}
}
```

- The consumer's signals arrive in `SignalAsync`. Return a direct answer, or null and send later signals
  with `SendSignalAsync`.
- The signals of one session arrive in the order they were sent, in both directions.
- A transport that needs no signaling, such as HLS or WHEP, where the consumer talks to your URL itself,
  keeps the default `SignalAsync`.
- The signal types above are the ones Macro Deck's clients use for `webrtc` - see
  [What Macro Deck's clients play](#what-macro-decks-clients-play).

## The consumer

`VideoStreamOpenRequest.Consumer` says who the session is for, so you can hand out a URL that consumer
can reach:

| Member | Meaning |
| --- | --- |
| `ConnectionKind` | `Local`: on the same computer as Macro Deck, so a loopback address reaches you. `Network`: over the network, so the consumer needs an address it can reach there, such as the computer's LAN address. `UsbTunnel`: a device tethered by USB whose traffic Macro Deck tunnels. |
| `HostAddress` | The address the consumer used to reach Macro Deck, when known. A source running next to Macro Deck can serve from the same host name. |
| `DeviceId` | Macro Deck's id of the device showing the stream, when the consumer is one. |

A `UsbTunnel` consumer connects through a loopback address on the device itself, so a `localhost` URL
points at the device, not at the computer, and only ports Macro Deck tunnels reach the computer. Hand such
a consumer a network address when the device can also reach the computer over the network, and otherwise
refuse with `StreamUnavailable`.

The consumer context is a hint for building reachable URLs. Never authorize anything by it.

## Showing a stream in Macro Deck UI

Put a [`macrodeck.video-stream`](/ui/components/video-stream/) in any tree you draw - a widget type, a folder
view, a modal - and name the stream by your provider's qualified id and the stream's id:

```csharp
new UiVideoStream
{
    Key = "preview",
    Stream = UiValue.Of(new UiVideoStreamReference { Provider = registration.QualifiedId, Id = "Preview" }),
    Fill = true,
}
```

The client that draws the tree opens, suspends and closes the session itself. The view can name any
provider's stream, not only your own.

## What Macro Deck's clients play

Macro Deck's web client and desktop app list, most preferred first, the transports the device they run on
can play:

| Transport | Offered when | What the client does |
| --- | --- | --- |
| `webrtc` | The engine has `RTCPeerConnection` | Takes `Payload` as the offer, answers with the convention below, plays the received track. |
| `whep` | Same | POSTs a receive-only offer as `application/sdp` to `Url`, applies the answer, and DELETEs the resource named by the `Location` header when it stops. |
| `hls` | The engine plays HLS natively and inline | Plays `Url` in a muted `<video>`. No HLS library is loaded, so most desktop browsers do not offer it. |
| `mjpeg` | Always | Shows `Url` as an image that keeps updating, a `multipart/x-mixed-replace` stream. |

The [Companion app](/guide/companion-app/) offers `hls`, played by the phone's own video player, then
`mjpeg`. It does not offer `webrtc` or `whep`. The phone fetches the URL itself over the network it shares
with the computer, so hand it an address it can reach there. On iPhone and iPad an `http:` URL plays only when
its host is an IP address, a name without a domain or a `.local` name.

**Serve `mjpeg` as well.** It is the one transport a client can play on every device Macro Deck supports,
old tablets included. Offer `webrtc` or `whep` for low latency, and the client falls back to
`mjpeg` where they are not available. A client that cannot play the transport you picked, for example because
autoplay is blocked, closes the session and opens a new one without that transport.

**The `webrtc` convention.** Macro Deck's clients speak this, and it does not change:

- `Payload` is the offer as a raw SDP string.
- The client answers with a signal of type `answer` whose payload is the raw SDP answer.
- Both sides send ICE candidates as signals of type `candidate`, each payload an `RTCIceCandidateInit` as
  JSON.

An offer can be answered only once. When the client suspends a `webrtc` session it closes its peer, so return a
fresh offer from `ResumeAsync`. A resume that returns no new description makes the client close the session and
open a new one.

**`whep`.** The endpoint is called from the client's own origin, so it has to allow that with CORS, including
`Access-Control-Expose-Headers: Location`. When `Parameters` has an `authorization` entry, the client sends it
as the `Authorization` header of both requests.

**Suspending.** The client stops playing as soon as it suspends: an `mjpeg` or `hls` client stops downloading, a
`whep` or `webrtc` client closes its peer. Your `SuspendAsync` can release what is expensive on your side. A
suspend can reach you up to about a minute late, or not at all when the session is closed first.

**Reachability.** A web client loaded over HTTPS cannot load an `http:` media URL; serve `https:` URLs to such
a consumer, or `http:` ones only on a local network the client reaches over HTTP.

Clients play every stream without sound.

## Limits

The SDK rejects a value past a bound with an `ArgumentException` before it is sent, and Macro Deck
refuses one that arrives anyway. The bounds are in
[`VideoStreamLimits`](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/protocol/src/MacroDeck.Plugin.Protocol/Limits/VideoStreamLimits.cs);
the ones you are most likely to meet:

- At most **16 providers** per plugin.
- At most **256 streams** per provider. Macro Deck keeps the first 256 of a longer list and logs a warning.
- `Metadata` and `Parameters`: at most **32 entries**, keys up to 64 and values up to 2048 characters.
- A description's `Url` up to **2048** characters and `Payload` up to **65536**.
- A signal's `Type` up to **64** characters and `Payload` up to **32768**.

Macro Deck also bounds what it asks of one plugin at once. Opens, suspends, resumes, signals and stream
reads run at most eight at a time per plugin, and up to 256 more wait, each for at most the capability
invoke timeout; beyond that, the consumer is told the provider is busy. Closes have their own slots and are never dropped. `video-streams` calls from your
plugin have their own rate limit, apart from other host calls, so a burst of signals cannot delay a button
press. When Macro Deck rate limits a `RegisterProviderAsync`, `NotifyStreamsChangedAsync`,
`UpdateSessionAsync`, `SendSignalAsync` or `CloseSessionAsync`, the SDK retries it a few times over about a
second and a half, keeping the session's messages in order, and then throws a `VideoStreamException` with
`Busy`. A session accepts no further updates or signals once you call `CloseSessionAsync`; if that close
throws `Busy`, call it again to retry the close.

## Errors

| `VideoStreamErrorCode` | Meaning | Worth retrying |
| --- | --- | --- |
| `Unsupported` | This Macro Deck, or this provider, does not support the operation. | No |
| `UnknownProvider`, `UnknownStream` | No provider or stream with that id. | No |
| `UnknownSession` | No session with that id is open, or it was already closed. | No |
| `StreamUnavailable` | The stream exists but cannot be served right now. | Yes |
| `TransportNotAccepted` | You serve none of the transports the consumer accepts. | No |
| `CapacityReached` | You cannot open another session right now. | Yes |
| `SignalingUnsupported` | You do not exchange signals. | No |
| `Busy` | You are busy. | Yes |
| `Failed` | Anything else. | No |

A session closed with `ProviderRemoved` can be opened again once the provider is listed again; consumers
treat that reason as retryable.

## Lifecycle

Your sessions end whenever the thing they depend on goes away:

| What happens | Your provider sees | The consumer sees |
| --- | --- | --- |
| You call `UnregisterProviderAsync` | `CloseAsync(ProviderRemoved)` per session | The session closed with `ProviderRemoved` |
| The integration stops, is disabled, or initializes again, for example after the user saved its configuration or after a reconnect that did not resume | `CloseAsync(ProviderRemoved)` per session, then every provider is withdrawn; a re-initialization registers them again | The session closed with `ProviderRemoved`; the provider returns once it is registered again |
| Your plugin loses its connection to Macro Deck | `CloseAsync(HostDisconnected)` per session | The session closed with `ProviderRemoved`, and the provider is not listed until the plugin is back |
| The plugin is uninstalled | `CloseAsync(ProviderRemoved)` per session | The session closed with `ProviderRemoved` |
| Macro Deck shuts down | `CloseAsync(HostShutdown)` per session, for at most two seconds | - |

**Sessions do not survive a reconnect**, not even one that resumes the same plugin session. Your
registrations survive a resumed one: the SDK keeps your providers registered, and Macro Deck reads them back
once your plugin is connected again. A reconnect that does not resume initializes your integrations again,
which registers them afresh. Either way consumers open new sessions, so a provider never has to reconcile
a session across a connection it did not see end.

## Security

Any client that is signed in to Macro Deck, the desktop app or a paired device, can list your streams and
open a session on them. What you put in a description reaches that client, so:

- Prefer short-lived credentials scoped to the one session over long-lived secrets: a token minted in
  `OpenAsync`, revoked in `CloseAsync`, with `ExpiresAt` set and renewed through `UpdateSessionAsync`.
- Never put your source's own password or API key into a `Url`, `Parameters` or `Payload`.
- Treat signals from the consumer like any other input and validate them.

Macro Deck never logs descriptions or signal payloads.

Declare `host:video-streams` in `manifest.json` so that people installing your plugin can see it offers
video streams:

```json
"permissions": ["host:video-streams"]
```

Macro Deck does not enforce it today. See [the security model](/policies/security/#video-streams).

## Built-in integrations

Macro Deck's own integrations implement the same `IVideoStreamIntegration` and `IVideoStreamProvider` and
are listed beside plugins; a consumer cannot tell them apart. There is no connection to lose in process, so
a built-in provider's sessions end with `ProviderRemoved`, `HostShutdown` or a consumer reason, never with
`HostDisconnected`. See [Capability parity](/reference/capability-parity/#video-stream-sessions).

## Older versions of Macro Deck

A Macro Deck that predates video streams rejects the `video-stream-provider` capability, and your plugin is
reported [partially incompatible](/policies/deprecations/) there: everything else it offers keeps working.
Nothing throws. `RegisterProviderAsync` returns a registration whose ids are both empty, and every other
context call does nothing, so one build of your plugin serves old and new hosts alike.

## Testing

`FakeVideoStreamProviderContext` applies the same registration and size rules as the SDK and Macro Deck,
and records every call:

```csharp
var context = new FakeVideoStreamProviderContext();
await new DoorCameraIntegration(server).InitializeAsync(context);

Assert.That(context.Providers.ContainsKey("door-cameras"), Is.True);
Assert.That(context.Calls.Last().Kind, Is.EqualTo(VideoStreamProviderCallKind.Register));
```

It does not open sessions. Drive them the way Macro Deck does, through `harness.VideoStreamProvider`, a
`VideoStreamProviderTestClient`:

```csharp
await harness.InitializeIntegrationsAsync();

var open = await harness.VideoStreamProvider.OpenSessionAsync("session-1", "door-cameras", "front", ["hls"]);
Assert.That(open.DataAs<VideoStreamSessionOpenResult>()!.Description.Transport, Is.EqualTo("hls"));

await harness.VideoStreamProvider.CloseSessionAsync("session-1", "door-cameras");
```

Use a fresh session id per open: an id that was closed before is refused, as it would be by Macro Deck.
A refusal arrives as a failed outcome whose `details.reason` is one of the `video_stream_` reasons.
`harness.Context.VideoStreams` is the fake behind the harness; it records what your plugin reports, such as
`SessionUpdate`, `Signal` and `SessionClose`. See [Testing](/features/testing/#testing-video-streams).

## Over the plugin protocol

The capability kind is `video-stream-provider`, version 1, declared at local id `provider` by a plugin
whose integration implements `IVideoStreamIntegration`, and by no other. Macro Deck is the only writer of
its copy of your providers and streams: it reads them with the kind's operations, and your side only tells
it when to read again.

| Direction | Name | Operations |
| --- | --- | --- |
| Host to plugin | `video-stream-provider` capability | `describe`, `streams`, `session.open`, `session.suspend`, `session.resume`, `session.signal`, `session.close` |
| Plugin to host | `video-streams` host API | `providers-changed`, `streams-changed`, `session-update`, `session-signal`, `session-close` |

Failures are `CAPABILITY_UNSUPPORTED` for `Unsupported`, and otherwise `CAPABILITY_UNAVAILABLE` refined by
a `video_stream_` reason. Payloads, reasons and rules are in the
[WebSocket reference](/reference/websocket/#video-streams).

## At a glance

| Member | Package | What it is |
| --- | --- | --- |
| `IVideoStreamIntegration` | SDK | Implemented by an integration that offers video streams. |
| `IVideoStreamProvider` | SDK | One source of streams: lists them and serves sessions. |
| `IVideoStreamProviderContext` | SDK | Registers providers and reports streams, session updates, signals and closes. |
| `VideoStreamDescriptor`, `VideoStreamState` | SDK | One stream and its state at the source. |
| `VideoStreamOpenRequest`, `VideoStreamConsumer`, `VideoStreamConnectionKind` | SDK | What `OpenAsync` receives. |
| `VideoStreamSessionDescription`, `VideoStreamSignal` | SDK | How the consumer plays a session, and a signal for it. |
| `VideoStreamSessionState`, `VideoStreamSessionReason` | SDK | A session's state and why it changed or closed. |
| `VideoStreamException`, `VideoStreamErrorCode` | SDK | Refusing an operation. |
| `VideoStreamLimits` | Protocol | Every size bound. |
| `PluginPermissions.HostVideoStreams` | Packaging | The `host:video-streams` manifest permission. |
| `FakeVideoStreamProviderContext`, `VideoStreamProviderTestClient` | Plugin testing | See [Testing](#testing). |
