---
title: Music players
description: Expose a music player with IMusicPlayerProvider and IMusicPlayer - instances, playback state, artwork, the standard actions, library browsing and device switching.
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
| `DeviceName`, `DeviceType` | Where playback is happening. |

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

## Over the plugin protocol

Music players are fully supported out of process (capability kind `music-player`). The instance list is
a snapshot, so after `GetInstances` changes (an account was added in your config flow), call
`CatalogChanged(CapabilityKinds.MusicPlayer)` on an injected `IPluginCatalogNotifier`. State reads from
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
