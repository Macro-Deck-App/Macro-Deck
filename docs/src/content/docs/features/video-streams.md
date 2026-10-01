---
title: Video streams
description: Offer live video to Macro Deck with IVideoStreamIntegration and IVideoStreamProvider - streams and their state, sessions, the relay that carries the media, updates, limits, reconnects, older hosts, security and testing.
---

A video stream provider offers live video to Macro Deck: the scenes of an OBS instance, the cameras of
a video recorder, a capture device. You list the streams you have; when a consumer wants to show one,
Macro Deck opens a **session** on your provider, and you answer with the URL where the stream can be
fetched, as HLS or as MJPEG.

Macro Deck fetches the media itself and relays it to the consumer. The consumer only ever talks to Macro
Deck, never to your source: your URL never leaves the computer, so your source can listen on `127.0.0.1`
only, needs no firewall rule, has no address to guess per consumer and mints no credentials for clients.
Macro Deck also opens every session for the consumer and makes sure each one you opened is closed
exactly once.

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

	public Task<VideoStreamSessionDescription> OpenAsync(
		VideoStreamOpenRequest request,
		CancellationToken cancellationToken)
	{
		if (!request.AcceptedTransports.Contains("hls"))
		{
			throw new VideoStreamException(VideoStreamErrorCode.TransportNotAccepted, "Only HLS is served.");
		}

		// The server listens on 127.0.0.1 only: Macro Deck, not the consumer, fetches this URL.
		return Task.FromResult(
			VideoStreamSessionDescription.Hls($"http://127.0.0.1:{server.Port}/{request.StreamId}/index.m3u8"));
	}

	public Task CloseAsync(string sessionId, VideoStreamSessionReason reason, CancellationToken cancellationToken)
		=> Task.CompletedTask;
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
| `UpdateSessionAsync`, `CloseSessionAsync` | Session calls from your side - see [Updates from the provider](#updates-from-the-provider). |

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
| `Failed` | The session failed, for example because Macro Deck refused a description you returned. |

Calls for different sessions can run concurrently. Macro Deck bounds every call by a timeout, the
capability invoke timeout for a plugin and ten seconds for a built-in integration, and treats one that
times out as failed. A late `OpenAsync` that returns after its timeout still gets its
`CloseAsync`.

### Choosing a transport

A consumer lists the transports it can play in `AcceptedTransports`, most preferred first: `hls` when its
device plays HLS natively, and `mjpeg` always. Pick the first one you serve, or refuse with
`TransportNotAccepted`. Macro Deck refuses any other transport the same way.

Build the description with a factory:

| Factory | Meaning |
| --- | --- |
| `VideoStreamSessionDescription.Hls(url)` | The URL of an HLS playlist. Every segment, key and map the playlist names must be on the same origin as the playlist. |
| `VideoStreamSessionDescription.Mjpeg(url)` | The URL of a `multipart/x-mixed-replace` stream of JPEG frames. |
| `VideoStreamSessionDescription.FromUrl(transport, url)` | Any transport delivered from a URL. Macro Deck plays `hls` and `mjpeg` only. |

The `Url` is an absolute `http` or `https` URL of at most 2048 characters, with no user info and no encoded
slash (`%2F`) in its path. It is required for `hls` and `mjpeg`; a `null` Url is reserved for source kinds
added later, and the description can gain members without breaking a plugin built against an earlier SDK.
A description that breaks a rule throws `ArgumentException` when you build it.

**Serve `mjpeg` as well.** It is the one transport every device Macro Deck supports can play, old tablets
included. Desktop browsers do not play HLS natively, so a stream that offers only HLS shows "cannot be
played on this device" there. See [What Macro Deck's clients play](#what-macro-decks-clients-play).

## The relay

Macro Deck fetches your URL and passes the bytes on. Consumers get a URL on Macro Deck itself, of the form
`/api/video-streams/relay/<token>/...`, so they never learn where your source is. What to rely on:

- **Bind to `127.0.0.1`.** The fetch starts on the computer that runs Macro Deck. Nothing needs to be
  reachable from the network, and there is no consumer address to work out.
- **The origin of your URL is pinned.** Macro Deck fetches only from that origin, using `GET` and `HEAD`. A
  redirect is followed only within the origin, at most three times; one to another origin fails.
- **HLS playlists are rewritten.** Every URI in a playlist, such as segments, `EXT-X-MAP` and `EXT-X-KEY`, is
  resolved against the playlist's own URL and sent through the relay, so relative, root-relative and
  absolute URIs all work. A URI on another origin fails the request with a 502, and so does the same host
  spelled differently, such as `localhost` in the playlist and `127.0.0.1` in your URL. Only `data:` and
  `skd:` URIs pass through untouched; any other scheme, such as `file:` or `javascript:`, fails the request
  with a 502.
- **Content types are checked.** A response that does not fit the session's transport fails with a 502:
  `multipart/x-mixed-replace` or `image/jpeg` for `mjpeg`; a playlist, `video/mp2t`, `video/mp4`,
  `video/iso.segment`, `audio/*`, `text/vtt` or `application/octet-stream` for `hls`. A playlist is found by
  its `.m3u8` path, its `mpegurl` type or, for a response typed `application/octet-stream`, `text/plain` or
  not at all, by `#EXTM3U` as its first line; it must start with that line and stay under 1 MiB, or the
  request fails with a 502. An error status from your source passes through, without its body, only when
  its content type is acceptable or missing. Responses carry `Cache-Control: no-store`,
  `X-Content-Type-Options: nosniff` and a sandboxing Content-Security-Policy.
- **Timeouts.** Your source has 15 seconds to start a response (a 504 otherwise), and media is cut when it
  sends nothing for 30 seconds; a playlist is exempt from the 30 seconds, because a live playlist may wait
  before it answers. A paused, static camera that sends no frame for that long is disconnected, and the
  client reconnects on its own; keep-alive frames avoid that.
- **Malformed requests never reach your source.** A path with an encoded slash or backslash (`%2F`,
  `%5C`, and a double-encoded `%252F`), a literal backslash or a control character gets a 404, as does a
  query with a raw line break or NUL, and so does a URL whose session has ended or is suspended. Your source
  is not contacted for any of them. Macro Deck's web server collapses dot segments (`.` and `..`) before the
  relay sees the path, so they cannot climb above your origin; a percent-encoded line break in a query is
  ordinary data and is forwarded as written.
- **Bounded concurrency.** At most 8 relayed requests run at once per session and 64 in all; beyond that
  the client is refused and retries.
- **One URL per session.** It stays the same while you change the URL or transport with
  `UpdateSessionAsync` or `ResumeAsync`, and stops working the moment the session ends or is suspended,
  which also aborts any request in flight.

**Browser connection limit.** Macro Deck's own listener speaks HTTP/1.1, and a browser opens about six
connections to one origin. Every live MJPEG widget holds one for as long as it plays, so a page with more
than a handful of live streams can starve its other requests. Streams that are scrolled out of sight or
covered suspend and release theirs. Serving Macro Deck over HTTPS lifts the limit, because HTTP/2 shares
one connection.

## Updates from the provider

Sources drop out. Report that on the sessions it affects, and the consumer can show it instead of a frozen
frame:

```csharp
await context.UpdateSessionAsync(sessionId, VideoStreamSessionState.Reconnecting,
	reason: VideoStreamSessionReason.ProviderReconnecting,
	message: Strings.Status.ObsReconnecting());

// Once the source is back, optionally with a new description Macro Deck fetches from instead:
await context.UpdateSessionAsync(sessionId, VideoStreamSessionState.Active, freshDescription,
	VideoStreamSessionReason.SourceRecovered);
```

`Reconnecting` means the session is interrupted and you are recovering it; `Active` means the consumer can
play again. Use `SourceLost` when the source stopped delivering and `SourceRecovered` when it delivers
again. `message` is text the consumer may show, in the reader's own language. An update for a session that
is no longer open is ignored. A new description is checked like the one from `OpenAsync`: a transport the
consumer did not accept, or a URL Macro Deck refuses, closes the session with `Failed`.

`CloseSessionAsync` ends a session from your side, with `ProviderClosed` unless you name another reason.
Macro Deck does not call `CloseAsync` for a session you closed yourself.

Updates you send while `OpenAsync` is still running are held and delivered in order after the open
returns. At most 64 are held per session; beyond that the session fails.

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

Macro Deck's web client and desktop app, and the Companion app, offer two transports, most preferred first:

| Transport | Offered when | What the client does |
| --- | --- | --- |
| `hls` | The engine plays HLS natively and inline: Safari, iPhone and iPad, and the Android player. Desktop Chrome and Firefox do not. | Plays the relayed playlist in a muted video element. No HLS library is loaded. |
| `mjpeg` | Always | Shows the relayed stream as an image that keeps updating. |

**Every client resolves the relay URL against the origin it already uses for Macro Deck.** The `url` a
client receives is host-relative (or absolute) and never your URL, so a client implementer must not assume
a full address. The [Companion app](/guide/companion-app/) has to resolve it against its host address
for video to play on a phone.

A client that cannot play the transport you picked, for example because autoplay is blocked, closes the
session and opens a new one without that transport. Clients play every stream without sound, and a
client stops downloading as soon as it suspends, so your `SuspendAsync` can release what is expensive on
your side. A suspend can reach you up to about a minute late, or not at all when the session is closed
first.

## Limits

The SDK rejects a value past a bound with an `ArgumentException` before it is sent, and Macro Deck
refuses one that arrives anyway. The bounds are in
[`VideoStreamLimits`](https://github.com/Macro-Deck-App/Macro-Deck/blob/main/protocol/src/MacroDeck.Plugin.Protocol/Limits/VideoStreamLimits.cs);
the ones you are most likely to meet:

- At most **16 providers** per plugin.
- At most **256 streams** per provider. Macro Deck keeps the first 256 of a longer list and logs a warning.
- `Metadata`: at most **32 entries**, keys up to 64 and values up to 2048 characters.
- A description's `Url` up to **2048** characters.

Macro Deck also bounds what it asks of one plugin at once. Opens, suspends, resumes and stream reads run
at most eight at a time per plugin, and up to 256 more wait, each for at most the capability invoke
timeout; beyond that, the consumer is told the provider is busy. Closes have their own slots and are never
dropped. `video-streams` calls from your plugin have their own rate limit, apart from other host calls, so
a burst of updates cannot delay a button press. When Macro Deck rate limits a `RegisterProviderAsync`,
`NotifyStreamsChangedAsync`, `UpdateSessionAsync` or `CloseSessionAsync`, the SDK retries it a few times
over about a second and a half, keeping the session's messages in order, and then throws a
`VideoStreamException` with `Busy`. A session accepts no further updates once you call
`CloseSessionAsync`; if that close throws `Busy`, call it again to retry the close. The relay has its own
bounds, listed in [The relay](#the-relay).

## Errors

| `VideoStreamErrorCode` | Meaning | Worth retrying |
| --- | --- | --- |
| `Unsupported` | This Macro Deck, or this provider, does not support the operation. | No |
| `UnknownProvider`, `UnknownStream` | No provider or stream with that id. | No |
| `UnknownSession` | No session with that id is open, or it was already closed. | No |
| `StreamUnavailable` | The stream exists but cannot be served right now. | Yes |
| `TransportNotAccepted` | You serve none of the transports the consumer accepts, or Macro Deck does not play the one you returned. | No |
| `CapacityReached` | You cannot open another session right now. | Yes |
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

Whenever a session ends, the relay URL stops working and any media still flowing for it is cut.

**Sessions do not survive a reconnect**, not even one that resumes the same plugin session. Your
registrations survive a resumed one: the SDK keeps your providers registered, and Macro Deck reads them back
once your plugin is connected again. A reconnect that does not resume initializes your integrations again,
which registers them afresh. Either way consumers open new sessions, so a provider never has to reconcile
a session across a connection it did not see end.

## Security

Any client that is signed in to Macro Deck, the desktop app or a paired device, can list your streams and
open a session on them, and then receives the media through the relay. So:

- Whatever your source serves is shown to every such client. Do not serve anything you would not show them.
- The client never sees your URL, so credentials in its query string stay with Macro Deck. Put a short-lived
  token in a URL only when your source needs one, scoped to the session and revoked in `CloseAsync`; never
  the source's own password or API key. Macro Deck never logs a description.
- The relay reaches whatever your URL points at, including other services on the computer or the local
  network. Point it only at your own source. See [the security model](/policies/security/#video-streams).

Declare `host:video-streams` in `manifest.json` so that people installing your plugin can see it offers
video streams:

```json
"permissions": ["host:video-streams"]
```

Macro Deck does not enforce it today.

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
`SessionUpdate` and `SessionClose`. The harness does not relay anything: it shows the description you
returned, not what a client would fetch. See [Testing](/features/testing/#testing-video-streams).

## Over the plugin protocol

The capability kind is `video-stream-provider`, version 1, declared at local id `provider` by a plugin
whose integration implements `IVideoStreamIntegration`, and by no other. Macro Deck is the only writer of
its copy of your providers and streams: it reads them with the kind's operations, and your side only tells
it when to read again.

| Direction | Name | Operations |
| --- | --- | --- |
| Host to plugin | `video-stream-provider` capability | `describe`, `streams`, `session.open`, `session.suspend`, `session.resume`, `session.close` |
| Plugin to host | `video-streams` host API | `providers-changed`, `streams-changed`, `session-update`, `session-close` |

Failures are `CAPABILITY_UNSUPPORTED` for `Unsupported`, and otherwise `CAPABILITY_UNAVAILABLE` refined by
a `video_stream_` reason. Payloads, reasons and rules are in the
[WebSocket reference](/reference/websocket/#video-streams).

## At a glance

| Member | Package | What it is |
| --- | --- | --- |
| `IVideoStreamIntegration` | SDK | Implemented by an integration that offers video streams. |
| `IVideoStreamProvider` | SDK | One source of streams: lists them and serves sessions. |
| `IVideoStreamProviderContext` | SDK | Registers providers and reports streams, session updates and closes. |
| `VideoStreamDescriptor`, `VideoStreamState` | SDK | One stream and its state at the source. |
| `VideoStreamOpenRequest` | SDK | What `OpenAsync` receives: session id, stream id and accepted transports. |
| `VideoStreamSessionDescription` | SDK | Where Macro Deck fetches a session's media: `Hls`, `Mjpeg` or `FromUrl`. |
| `VideoStreamSessionState`, `VideoStreamSessionReason` | SDK | A session's state and why it changed or closed. |
| `VideoStreamException`, `VideoStreamErrorCode` | SDK | Refusing an operation. |
| `VideoStreamLimits` | Protocol | Every size bound. |
| `PluginPermissions.HostVideoStreams` | Packaging | The `host:video-streams` manifest permission. |
| `FakeVideoStreamProviderContext`, `VideoStreamProviderTestClient` | Plugin testing | See [Testing](#testing). |
