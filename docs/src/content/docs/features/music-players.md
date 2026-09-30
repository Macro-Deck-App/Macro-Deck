---
title: Music players
description: Expose a music player with IMusicPlayerProvider and IMusicPlayer - instances, playback state, artwork, per-widget options, the standard actions, library browsing and device switching.
---

An integration exposes a music player by implementing `IMusicPlayerProvider`. It lists one
`MusicPlayerInstance` per configured account and resolves each one to an `IMusicPlayer`. Macro Deck
reads the state and sends the commands; how you talk to the player stays inside your integration.

## Quick start

```csharp
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeck.Sdk.MusicPlayer.Actions;

public sealed class JukeboxIntegration : IPluginIntegration, IMusicPlayerProvider
{
	private const string InstanceId = "default";

	private readonly JukeboxPlayer _player = new(new JukeboxClient("http://localhost:9090"));

	public JukeboxIntegration()
	{
		// Play, pause, toggle, next, previous, refresh, volume, seek, shuffle and repeat.
		Actions = MusicPlayerActions.Common(ResolvePlayer, GetInstances);
	}

	public IReadOnlyList<IActionDefinition> Actions { get; }

	public IReadOnlyList<MusicPlayerInstance> GetInstances()
		=> [new MusicPlayerInstance(InstanceId, "Jukebox")];

	public IMusicPlayer? GetPlayer(string instanceId)
		=> instanceId == InstanceId ? _player : null;

	// An empty id means "first available player" - the actions' default.
	private IMusicPlayer? ResolvePlayer(string? instanceId)
		=> GetPlayer(string.IsNullOrEmpty(instanceId) ? InstanceId : instanceId);

	// IPluginIntegration members omitted.
}
```

The user can now pick "Jukebox" in the **Music Player widget**, which shows the track, artists and
artwork, and control it with the ten standard music-player actions.

Things to know:

- **The interface lights up the widget, not the actions.** Actions come from `MusicPlayerActions`, and
  variables such as `{{ vars.jukebox_track }}` from your own [`IVariableProvider`](/features/variables/).
  Implementing `IMusicPlayerProvider` alone gives you neither.
- **Instance ids are local.** Return `"default"`, not a qualified id. The host stores it as
  `integrationId::default`, and it ends up in saved widgets, so keep it stable.
- **The host polls.** It calls `GetStateAsync` regularly for each instance, so keep it cheap. You never
  push state.

## Implementing the player

```csharp
using MacroDeck.Sdk.MusicPlayer;

internal sealed class JukeboxPlayer(JukeboxClient client) : IMusicPlayer
{
	public async Task<MusicPlayerState> GetStateAsync(CancellationToken cancellationToken = default)
	{
		if (!client.IsConfigured)
		{
			return MusicPlayerState.Disconnected;
		}

		JukeboxStatus status;
		try
		{
			status = await client.GetStatusAsync(cancellationToken);
		}
		catch (HttpRequestException)
		{
			return MusicPlayerState.Unavailable("Jukebox offline");
		}

		return new MusicPlayerState
		{
			IsConnected = true,
			PlaybackState = status.IsPlaying ? PlaybackState.Playing : PlaybackState.Paused,
			TrackName = status.Title,
			Artists = status.Artists,
			AlbumName = status.Album,
			ArtworkId = status.CoverHash,
			Position = TimeSpan.FromSeconds(status.PositionSeconds),
			Duration = TimeSpan.FromSeconds(status.LengthSeconds),
			VolumePercent = status.Volume,
			ShuffleEnabled = status.Shuffle,
			RepeatMode = status.RepeatOne ? RepeatMode.Track : RepeatMode.Off
		};
	}

	public async Task<MusicPlayerArtwork?> GetArtworkAsync(string artworkId,
		CancellationToken cancellationToken = default)
	{
		var bytes = await client.GetCoverAsync(artworkId, cancellationToken);
		return bytes is null ? null : new MusicPlayerArtwork(bytes, "image/jpeg");
	}

	public Task PlayAsync(CancellationToken cancellationToken = default) => client.SendAsync("play", cancellationToken);

	public Task PauseAsync(CancellationToken cancellationToken = default) => client.SendAsync("pause", cancellationToken);

	public Task TogglePlayPauseAsync(CancellationToken cancellationToken = default)
		=> client.SendAsync("toggle", cancellationToken);

	public Task NextAsync(CancellationToken cancellationToken = default) => client.SendAsync("next", cancellationToken);

	public Task PreviousAsync(CancellationToken cancellationToken = default)
		=> client.SendAsync("previous", cancellationToken);

	public Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default)
		=> client.SeekAsync(position, cancellationToken);

	public Task SetVolumeAsync(int volumePercent, CancellationToken cancellationToken = default)
		=> client.SetVolumeAsync(volumePercent, cancellationToken);

	public Task SetShuffleAsync(bool enabled, CancellationToken cancellationToken = default)
		=> client.SetShuffleAsync(enabled, cancellationToken);

	public Task SetRepeatModeAsync(RepeatMode mode, CancellationToken cancellationToken = default)
		=> client.SetRepeatAsync(mode != RepeatMode.Off, cancellationToken);
}
```

Return a state, don't throw one. `MusicPlayerState.Disconnected` means there is nothing to reach (not
set up, signed out). `MusicPlayerState.Unavailable(message)` means the player is set up but can't answer
right now (rate limit, outage). The widget shows the message in place of the artist line, so keep it to a
few words. The two look different on purpose: an outage that reads "Not connected" looks as if the player
had gone.

`ArtworkId` is an opaque key that the host passes back to `GetArtworkAsync`, never a URL. The UI only
ever asks the host for artwork.

### Source and badge

The widget's header shows where playback is happening on a second line under the player's name, and a
short badge of yours at the right, beside the playback icon:

```csharp
return new MusicPlayerState
{
	IsConnected = true,
	PlaybackState = PlaybackState.Playing,
	TrackName = track.Title,
	Artists = [track.Artist],
	DeviceName = session.AppName,               // "Firefox"
	Badge = $"{index + 1}/{sessions.Count}"     // "2/3"
};
```

- Both show only while `IsConnected` is true. A disconnected or unavailable state shows neither.
- `DeviceName` is left out when the player's name in the header already contains it as a whole word,
  so an instance named "SinusBot (Kitchen)" with the device "Kitchen" does not say it twice. A name that
  comes from `ProviderName` as a localized string is never compared.
- Keep `Badge` to a few characters. The widget reserves room for about 8, then shrinks and cuts off longer
  text. `null` or blank shows nothing.
- Users can hide both with the widget's **Source** option. The now-playing screen saver never shows them.
- A host older than `Badge` ignores it, so setting it is safe on every host. Don't put the badge into
  `Artists` or `TrackName` as a fallback: it would be read as part of the metadata by variables and triggers.

### Showing the cover in your own UI

The deck's music player widget fetches artwork from the host on its own. When your plugin also draws the
cover in a tree of its own, `GetArtworkAsUiResourceAsync` resolves the artwork and registers it as a
[UI resource](/ui/reference/resources/#registering-your-own-images) in one call:

```csharp
UiResource? cover = await player.GetArtworkAsUiResourceAsync(
	context.UiResources,
	resourceName: "now-playing-cover",
	artworkId: state.ArtworkId,
	cancellationToken);
```

- It returns `null` when `artworkId` is null or empty or `GetArtworkAsync` has none. Nothing is registered
  or removed then, so the name keeps the previous cover until you drop it from the tree.
- Use one fixed name per place the cover appears, not one per track. Registering the name again replaces
  its bytes, while a name per track keeps every cover in your quota for the whole session.
- The registry's rules apply to the artwork. An invalid name, empty data, more than 2 MiB, or a media type
  Macro Deck does not accept is an `ArgumentException`. Only PNG, JPEG, WebP and GIF are accepted, so SVG
  is refused. The deck widget copes with some of these by re-encoding on the host, so a cover that shows
  there can still be refused here.
- Exceptions from your own `GetArtworkAsync`, including an `OperationCanceledException`, reach the caller
  unchanged.
- Await one call before starting the next for the same name. Two overlapping calls can finish out of
  order, and the older cover would then replace the newer one.
- An [in-process integration](/reference/capability-parity/) has no `UiResources`; the call throws
  `UiResourceException` with `Unsupported`.

### Showing another player's cover

`GetArtworkAsUiResourceAsync` needs an `IMusicPlayer` of your own. To show the cover of a player that
belongs to another integration, ask Macro Deck to register it for you with `UiResources.RegisterMusicPlayerArtworkAsync`:

```csharp
UiResource? cover = await context.UiResources.RegisterMusicPlayerArtworkAsync(
	name: "now-playing-cover",
	instanceId: "net.example.jukebox::default",
	artworkId: "c0ffee",
	cancellationToken);
```

- **Where the two ids come from.** Nothing in the SDK lists other integrations' players. Both ids appear in
  the URL that a player's `*-album-art-url` variable holds, for example
  `/api/music-player/artwork/c0ffee?instanceId=net.example.jukebox%3A%3Adefault`: the last path segment is
  the `artworkId` and the `instanceId` query value is the qualified instance id, both URL-encoded, so decode
  them first. A plugin that parses text the user writes, with such a variable in it, reads the pair from
  there. For an integration that allows only one configuration, `integrationId::default` resolves to its
  first player.
- **What you get.** The handle of the current artwork registered under `name`, as if you had registered the
  bytes yourself: it counts against the [quota](/ui/reference/resources/#registering-your-own-images), is
  released with the session, and registering the name again replaces it. Macro Deck may re-encode the image,
  so the media type can differ from the player's. Use one fixed name per place the cover is shown.
- **`null`** when Macro Deck knows no such player or the player has no artwork for that id. Nothing is
  registered or removed then, so the name keeps its previous picture.
- **Errors.** `ArgumentException` for an invalid name, before anything is sent. `UiResourceException` with
  `QuotaExceeded`; `RateLimited`, because this call has a tighter budget than other registrations and at most
  four run at once; `Unsupported` on a Macro Deck that predates it or an
  [in-process integration](/reference/capability-parity/); or `Failed`. `Failed` covers a player that does
  not answer within 20 seconds, where retrying can succeed, and artwork larger than `maxUiResourceBytes` or
  of a media type Macro Deck does not accept, where it cannot.
- Any plugin can ask for the artwork of any player, the same images the deck shows to every client. Listing
  players is not part of this call.

In tests, `FakeUiResourceRegistry.AddMusicPlayerArtwork(instanceId, artworkId, bytes, mediaType)` seeds what
the call finds, and `MacroDeckTestHost` answers it over the wire as a Macro Deck without other players.

### `MusicPlayerState`

| Property | Meaning |
| --- | --- |
| `IsConnected` | There is a usable session. |
| `IsUnavailable`, `StatusMessage` | Configured but unreachable right now, and a short reason. |
| `PlaybackState` | `Stopped`, `Playing` or `Paused`. |
| `TrackName`, `Artists`, `AlbumName` | What is playing. `Artists` defaults to empty. |
| `ArtworkId` | Opaque id resolved through `GetArtworkAsync`. |
| `Position`, `Duration` | Playback position and track length. `null` when unknown. |
| `VolumePercent` | 0-100, or `null` when the player does not report it. |
| `ShuffleEnabled`, `RepeatMode` | `RepeatMode` is `Off`, `Track` or `Context` (album, playlist or queue). |
| `DeviceName`, `DeviceType` | Where playback is happening. The widget shows `DeviceName` as the source. |
| `Badge` | Short text beside the playback badge, such as `2/3`. See [Source and badge](#source-and-badge). |

## Actions

```csharp
Actions =
[
	.. MusicPlayerActions.Common(ResolvePlayer, GetInstances),
	MusicPlayerActions.PlayTrack(IntegrationId, ResolvePlayer, GetInstances),
	MusicPlayerActions.PlayPlaylist(IntegrationId, ResolvePlayer, GetInstances),
	MusicPlayerActions.PlayOnDevice(IntegrationId, ResolvePlayer, GetInstances),
	MusicPlayerActions.TransferPlayback(IntegrationId, ResolvePlayer, GetInstances)
];
```

Every action has an `instance` parameter (`MusicPlayerActions.InstanceParameterName`) filled from
`GetInstances`. Left empty, the action gets the first available player through your resolver. If the
command throws, the action reports a failed result and the rest of the flow keeps running.

`IntegrationId` is your integration's id, which for a plugin is the `id` in
[`manifest.json`](/reference/manifest/). The item and device actions use it to build the qualified
instance id when they ask the triggering client to pick a track, playlist or device at run time.

## Browsing and playing the library

```csharp
internal sealed class JukeboxPlayer(JukeboxClient client) : ICatalogMusicPlayer
{
	public async Task<IReadOnlyList<MusicPlayerCatalogItem>> GetCatalogAsync(
		string instanceId,
		MusicPlayerCatalogItemKind kind,
		string? filter,
		CancellationToken cancellationToken)
	{
		// Throws on failure: the picker shows "could not load, retry" instead of "no tracks".
		var songs = await client.SearchAsync(kind == MusicPlayerCatalogItemKind.Playlist, filter, cancellationToken);

		return songs
			.Select(s => new MusicPlayerCatalogItem(s.Id, s.Title, kind, Subtitle: s.Artist, ArtworkId: s.CoverHash))
			.ToList();
	}

	public async Task PlayItemAsync(MusicPlayerCatalogItem item, CancellationToken cancellationToken = default)
	{
		try
		{
			await client.PlayAsync(item.Id, cancellationToken);
		}
		catch (HttpRequestException ex)
		{
			_logger.Warning(ex, "Could not play {ItemId}", item.Id);
		}
	}

	// IMusicPlayer members as above.
}
```

`ICatalogMusicPlayer` is `IMusicPlayer` plus `IMusicPlayerCatalogProvider`. It powers the
**Play Track** and **Play Playlist** action pickers when the user configures an action, and the pick
dialog when the action runs with nothing selected. The host finds it by casting the object `GetPlayer`
returns.

Reads and commands fail in opposite ways:

| Member | On failure | Why |
| --- | --- | --- |
| `GetCatalogAsync` | **Throw.** Return empty only for an empty library. | Empty renders "no tracks", a throw renders "retry". |
| `PlayItemAsync` | **Log and return.** | A command failure must not abort the user's action flow. |

Let `OperationCanceledException` propagate from both. `GetCatalogAsync` is only called from a REST
request and may take as long as the library needs, but its token is the only thing that ends a request
the remote service never answers.

## Switching playback devices

```csharp
internal sealed class JukeboxPlayer(JukeboxClient client) : IMusicPlayer, IMusicPlayerDeviceProvider
{
	public async Task<IReadOnlyList<MusicPlayerDevice>> GetDevicesAsync(CancellationToken cancellationToken)
		=> (await client.GetOutputsAsync(cancellationToken))
			.Select(o => new MusicPlayerDevice(o.Id, o.Name, Type: "Speaker", IsActive: o.IsCurrent))
			.ToList();

	public async Task TransferPlaybackAsync(string deviceId, bool startPlayback, CancellationToken cancellationToken)
	{
		try
		{
			await client.SwitchOutputAsync(deviceId, startPlayback, cancellationToken);
		}
		catch (HttpRequestException ex)
		{
			_logger.Warning(ex, "Could not switch to {DeviceId}", deviceId);
		}
	}

	// IMusicPlayer members as above.
}
```

`IMusicPlayerDeviceProvider` powers **Play on Device** and **Transfer Playback**. It takes no instance id
because the object is already one instance. `MusicPlayerDevice.Type` is a free-form display string
("Computer", "Speaker"), not an enum. The failure rules are the same as for the library: `GetDevicesAsync`
throws on failure, and `TransferPlaybackAsync` logs and returns. A player without this interface fails
the device actions with `Unavailable`.

## Per-widget options

An instance can declare options that each Music Player widget sets for itself. One widget can then cycle
through the apps that are playing every 10 seconds while another widget on the same deck follows the app
the system calls current, without a second instance cluttering the player picker.

```csharp
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.MusicPlayer;

public IReadOnlyList<MusicPlayerInstance> GetInstances()
	=>
	[
		new MusicPlayerInstance("any", "Any app")
		{
			Options =
			[
				ActionParameter.Toggle("cycle", "Cycle between apps", defaultValue: false),
				ActionParameter.Number("cycleSeconds", "Cycle every (seconds)", min: 5, max: 60, defaultValue: 10)
			]
		},
		.. _sessions.Select(app => new MusicPlayerInstance(app.Id, app.Name))
	];

public IMusicPlayer? GetPlayerWithOptions(MusicPlayerOptionsRequest request)
{
	if (request.InstanceId != "any" || request.Options["cycle"] is not true)
	{
		return GetPlayer(request.InstanceId);
	}

	var seconds = (double)request.Options["cycleSeconds"];
	return new CyclingPlayer(_sessions, TimeSpan.FromSeconds(seconds));
}
```

The widget editor shows the options of the picked player below the player picker, and each widget stores
its own values. The host resolves the player with `GetPlayerWithOptions` and polls it separately from the
plain instance, so two widgets with different values show different tracks and covers.

- **Supported kinds.** `String`, `Number` (with `Min`, `Max`, `Step` and the slider), `Boolean`, and
  `Choice` with static `Options`. Any other kind, a `Choice` with dynamic options, an invalid name or a
  duplicate name is skipped with a warning in the host log. A kind the host does not know at all, from a
  newer SDK, is skipped as well.
- **Names.** 1 to 64 letters, digits, hyphens or underscores, starting with a letter or digit. The name is
  the key in the widget's stored data, so keep it stable.
- **Labels.** `Label` and `Description` are shown, localized like any other `LocalizedText`. Set a label:
  without one, the editor shows the raw name. `VisibleWhen`, `Required` and `Placeholder` are ignored.
- **Values.** `request.Options` always has one entry per supported option: the widget's value, or your
  default when the widget stored none or an invalid one. Values are `string` for String and Choice,
  `double` for Number (clamped to `Min` and `Max`), and `bool` for Boolean. A Choice value is always one of
  your option values.
- **Only for instances with options.** `GetPlayerWithOptions` is called only for an instance that declares
  options, and only for a widget that picked that instance. A widget set to "Active player" uses
  `GetPlayer`. The Now Playing screen saver has no option fields and shows the instance with your defaults.
- **Keep it cheap.** The host may call `GetPlayerWithOptions` for the same values again and again, and it
  never tells you when a set of values is no longer shown. Return a cached or lightweight object, and derive
  time-based behaviour such as cycling from the clock when `GetStateAsync` runs, not from a timer per set of
  values.
- **Display only.** The host reads state and artwork from this player and sends it no commands. Actions in
  a widget's flows keep addressing the instance through their own `instance` parameter, so a Next action on
  a cycling widget acts on the plain "Any app" player.
- **Artwork ids.** The host caches covers by instance and artwork id, shared across option values. An
  artwork id must name the same image whichever values produced it.
- **Default.** `GetPlayerWithOptions` defaults to `GetPlayer(request.InstanceId)`. Providers without options
  do not implement it.
- **Older hosts** ignore `Options` and show the plain instance.

## Volume and position variables

```csharp
VariableDefinition.Eager("jukebox_volume", VariableType.Numeric) with
{
	Id = "volume",
	Unit = "%",
	SemanticKind = VariableSemanticKinds.Percentage,
	Write = MusicPlayerVariableWrites.Volume
},
VariableDefinition.Eager("jukebox_position", VariableType.Numeric, refreshInterval: TimeSpan.FromSeconds(1)) with
{
	Id = "position",
	SemanticKind = VariableSemanticKinds.Duration,
	Write = MusicPlayerVariableWrites.Position
}

public ValueTask<VariableWriteResult> SetValueAsync(string localId, object? value,
	CancellationToken cancellationToken = default)
	=> localId switch
	{
		"volume" => MusicPlayerVariableWrites.SetVolumeAsync(GetPlayer(InstanceId), value, cancellationToken),
		"position" => MusicPlayerVariableWrites.SeekAsync(GetPlayer(InstanceId), value, cancellationToken),
		_ => ValueTask.FromResult(VariableWriteResult.NotWritable())
	};
```

`MusicPlayerVariableWrites` gives a bound Slider the same write behaviour as the built-in players:
volume streams while the user drags, position commits on release, values are clamped, and a missing
player answers `Unavailable`. See [Writable variables](/features/variables/#writable-variables).

## Edge cases

- **An instance disappears.** Drop it from `GetInstances` and return `null` from `GetPlayer`. Widgets
  pointing at it show as disconnected, and they come back if the id returns.
- **Invalid or duplicate ids** from `GetInstances` are skipped and logged.
- **`ProviderName`** is optional. Leave it out and the integration's name (the manifest name for a plugin)
  is used.
- **A disabled integration** has no instances.
- **Options change.** Widgets pick up a changed `Options` list on their own; an editor that is already open
  shows the new fields when it is opened again. Values a widget stored for an option that no longer exists
  are ignored.

## Over the plugin protocol

Music players are fully supported out of process (capability kind `music-player`). The instance list is
a snapshot, so after `GetInstances` changes (an account was added in your config flow, or an instance's
`Options` changed), call `CatalogChanged(CapabilityKinds.MusicPlayer)` on an injected `IPluginCatalogNotifier`.
Options travel as optional fields within `music-player` version 1: the instance list carries them, and
only the `state` and `artwork` operations carry a widget's values. State reads from
an unreachable plugin degrade to unavailable. Library and device failures stay real failures, so an
unreachable plugin never looks like an empty library. See
[Capability parity](/reference/capability-parity/) and
[the WebSocket reference](/reference/websocket/#capabilities).

## See also

- [Variables](/features/variables/) - track, artist and playback variables.
- [Setup flows](/features/setup-flows/) - adding accounts, and so instances.
- [Actions](/features/actions/)
- [Virtual profiles](/features/virtual-profiles/) - ship a ready-made layout with a Music Player widget.
- [Testing](/features/testing/)
