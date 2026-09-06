using System.Globalization;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;

namespace MacroDeck.Sdk.MusicPlayer.Actions;

/// <summary>
/// Resolves a music player instance id to its <see cref="IMusicPlayer"/>. A null/empty id returns
/// the provider's first available player (the "First available" fallback). Integrations pass their
/// own <c>GetPlayer</c> here so actions are scoped to a single provider.
/// </summary>
public delegate IMusicPlayer? MusicPlayerResolver(string? instanceId);

/// <summary>
/// A playback command run against a resolved player. Receives the action's parameters and the
/// runtime interactions (so a command can ask the originating client a question, e.g. a track
/// picker). An exception is caught by the executor and reported as a failed
/// <see cref="MacroDeck.Sdk.Actions.ActionResult"/> rather than taking the rest of the flow down with it.
/// </summary>
public delegate Task MusicPlayerCommand(
	IMusicPlayer player,
	IReadOnlyDictionary<string, object> values,
	IActionInteractions? interactions,
	CancellationToken cancellationToken);

/// <summary>
/// Builds the standard music-player action set for a single provider integration. Each integration
/// owns its actions (no shared generic control integration); it calls <see cref="MusicPlayerActions.Common"/>
/// for the transport controls and <see cref="MusicPlayerActions.PlayTrack"/>/<see cref="PlayPlaylist"/>
/// when it also implements <see cref="ICatalogMusicPlayer"/>.
/// </summary>
public static class MusicPlayerActions
{
	/// <summary>The provider-local instance id parameter name shared by every music-player action.</summary>
	public const string InstanceParameterName = "instance";

	/// <summary>The device id parameter name shared by the device actions.</summary>
	public const string DeviceParameterName = "device";

	/// <summary>
	/// Standard transport controls (play, pause, toggle, next, previous, refresh, volume, seek,
	/// shuffle, repeat).
	/// </summary>
	public static IReadOnlyList<IActionDefinition> Common(
		MusicPlayerResolver resolver,
		Func<IReadOnlyList<MusicPlayerInstance>> getInstances)
	{
		return
		[
			Command(resolver,
				getInstances,
				"play",
				"Play",
				"Resume playback",
				(p, _, _, ct) => p.PlayAsync(ct)),
			Command(resolver,
				getInstances,
				"pause",
				"Pause",
				"Pause playback",
				(p, _, _, ct) => p.PauseAsync(ct)),
			State(resolver,
				getInstances,
				"toggle-play-pause",
				"Toggle Play/Pause",
				"Toggle between play and pause",
				(p, _, _, ct) => p.TogglePlayPauseAsync(ct),
				PlayPauseState,
				PlayPauseExpectedState),
			Command(resolver,
				getInstances,
				"next",
				"Next Track",
				"Skip to the next track",
				(p, _, _, ct) => p.NextAsync(ct)),
			Command(resolver,
				getInstances,
				"previous",
				"Previous Track",
				"Skip to the previous track",
				(p, _, _, ct) => p.PreviousAsync(ct)),
			Command(resolver,
				getInstances,
				"refresh-state",
				"Refresh State",
				"Force a refresh of the now-playing state",
				(p, _, _, ct) => p.GetStateAsync(ct)),
			Command(resolver,
				getInstances,
				"set-volume",
				"Set Volume",
				"Sets the playback volume (0–100%)",
				[ActionParameter.Slider("volume", 0, 100, label: "Volume", step: 1, defaultValue: 50)],
				(p, values, _, ct) => p.SetVolumeAsync(GetInt(values, "volume", 50), ct)),
			Command(resolver,
				getInstances,
				"volume-up",
				"Volume Up",
				"Increase the playback volume",
				(p, _, _, ct) => AdjustVolume(p, VolumeStep, ct)),
			Command(resolver,
				getInstances,
				"volume-down",
				"Volume Down",
				"Decrease the playback volume",
				(p, _, _, ct) => AdjustVolume(p, -VolumeStep, ct)),
			Command(resolver,
				getInstances,
				"seek",
				"Seek",
				"Jumps to a position in the current track (seconds)",
				[
					ActionParameter.Number("positionSeconds",
						label: "Position (seconds)",
						min: 0,
						defaultValue: 0,
						required: true)
				],
				(p, values, _, ct) =>
					p.SeekAsync(TimeSpan.FromSeconds(Math.Max(0, GetInt(values, "positionSeconds", 0))), ct)),
			State(resolver,
				getInstances,
				"toggle-shuffle",
				"Toggle Shuffle",
				"Toggles or sets shuffle mode",
				[ModeChoice("toggle", "on", "off")],
				ToggleShuffle,
				ShuffleState),
			State(resolver,
				getInstances,
				"set-repeat-mode",
				"Set Repeat Mode",
				"Sets the repeat mode (off, track or context)",
				[
					ActionParameter.Choice("mode",
						options:
						[
							new ActionParameterOption { Value = "off", Label = "Off" },
							new ActionParameterOption { Value = "track", Label = "Repeat Track" },
							new ActionParameterOption { Value = "context", Label = "Repeat Album/Playlist" }
						],
						label: "Mode",
						defaultValue: "off",
						required: true)
				],
				(p, values, _, ct) => p.SetRepeatModeAsync(ParseRepeat(GetString(values, "mode")), ct),
				RepeatState)
		];
	}

	/// <summary>
	/// Play a specific track. With no track selected, the triggering client is asked to pick one at
	/// run time.
	/// </summary>
	public static IActionDefinition PlayTrack(
		string integrationId,
		MusicPlayerResolver resolver,
		Func<IReadOnlyList<MusicPlayerInstance>> getInstances)
		=> new MusicPlayerItemActionDefinition(integrationId,
			resolver,
			getInstances,
			"play-track",
			"Play Track",
			"Plays a specific track. Leave empty to pick a track at run time.",
			MusicPlayerCatalogItemKind.Track,
			"track",
			"Track");

	/// <summary>
	/// Play a specific playlist. With no playlist selected, the triggering client is asked to pick one
	/// at run time.
	/// </summary>
	public static IActionDefinition PlayPlaylist(
		string integrationId,
		MusicPlayerResolver resolver,
		Func<IReadOnlyList<MusicPlayerInstance>> getInstances)
		=> new MusicPlayerItemActionDefinition(integrationId,
			resolver,
			getInstances,
			"play-playlist",
			"Play Playlist",
			"Plays a specific playlist. Leave empty to pick a playlist at run time.",
			MusicPlayerCatalogItemKind.Playlist,
			"playlist",
			"Playlist");

	/// <summary>
	/// Transfers playback to a specific device and starts it. With no device selected, the triggering
	/// client is asked to pick one at run time.
	/// </summary>
	public static IActionDefinition PlayOnDevice(
		string integrationId,
		MusicPlayerResolver resolver,
		Func<IReadOnlyList<MusicPlayerInstance>> getInstances)
		=> new MusicPlayerDeviceActionDefinition(integrationId,
			resolver,
			getInstances,
			"play-on-device",
			"Play on Device",
			"Starts playback on a specific device. Leave empty to pick a device at run time.",
			startPlayback: true);

	/// <summary>
	/// Transfers playback to a specific device, keeping its current play/pause state. With no device
	/// selected, the triggering client is asked to pick one at run time.
	/// </summary>
	public static IActionDefinition TransferPlayback(
		string integrationId,
		MusicPlayerResolver resolver,
		Func<IReadOnlyList<MusicPlayerInstance>> getInstances)
		=> new MusicPlayerDeviceActionDefinition(integrationId,
			resolver,
			getInstances,
			"transfer-playback",
			"Transfer Playback",
			"Moves playback to a specific device, keeping it playing or paused. Leave empty to pick one at run time.",
			startPlayback: false);

	/// <summary>Volume change applied by the volume-up / volume-down actions, in percent.</summary>
	private const int VolumeStep = 10;

	private static MusicPlayerActionDefinition Command(
		MusicPlayerResolver resolver,
		Func<IReadOnlyList<MusicPlayerInstance>> getInstances,
		string id,
		string name,
		string description,
		MusicPlayerCommand command)
		=> new(resolver, getInstances, id, name, description, [], command);

	private static MusicPlayerActionDefinition Command(
		MusicPlayerResolver resolver,
		Func<IReadOnlyList<MusicPlayerInstance>> getInstances,
		string id,
		string name,
		string description,
		IReadOnlyList<ActionParameter> extra,
		MusicPlayerCommand command)
		=> new(resolver, getInstances, id, name, description, extra, command);

	private static MusicPlayerStateActionDefinition State(
		MusicPlayerResolver resolver,
		Func<IReadOnlyList<MusicPlayerInstance>> getInstances,
		string id,
		string name,
		string description,
		MusicPlayerCommand command,
		Func<MusicPlayerState?, ActionStateSnapshot> stateMapper,
		Func<MusicPlayerState, string?> expectedStateMapper)
		=> new(resolver, getInstances, id, name, description, [], command, stateMapper, expectedStateMapper);

	private static MusicPlayerStateActionDefinition State(
		MusicPlayerResolver resolver,
		Func<IReadOnlyList<MusicPlayerInstance>> getInstances,
		string id,
		string name,
		string description,
		IReadOnlyList<ActionParameter> extra,
		MusicPlayerCommand command,
		Func<MusicPlayerState?, ActionStateSnapshot> stateMapper)
		=> new(resolver, getInstances, id, name, description, extra, command, stateMapper, expectedStateMapper: null);

	private static readonly ActionStateDefinition _unavailableState =
		new("unavailable", MacroDeckStrings.States.Unavailable())
		{
			DefaultAppearance = new ActionStateAppearance { BackgroundColor = "#4a5568", LabelColor = "#cbd5e0" }
		};

	private static readonly IReadOnlyList<ActionStateDefinition> _playPauseStates =
	[
		new("playing", MacroDeckStrings.States.Playing())
		{
			DefaultAppearance = new ActionStateAppearance { BackgroundColor = "#2f855a" }
		},
		new("paused", MacroDeckStrings.States.Paused())
		{
			DefaultAppearance = new ActionStateAppearance { BackgroundColor = "#2b6cb0" }
		},
		new("stopped", MacroDeckStrings.States.Stopped())
		{
			DefaultAppearance = new ActionStateAppearance { BackgroundColor = "#4a5568" }
		},
		_unavailableState
	];

	private static ActionStateSnapshot PlayPauseState(MusicPlayerState? state)
	{
		var activeId = state?.PlaybackState switch
		{
			PlaybackState.Playing => "playing",
			PlaybackState.Paused => "paused",
			PlaybackState.Stopped => "stopped",
			_ => "unavailable"
		};

		return new ActionStateSnapshot(_playPauseStates, activeId);
	}

	private static string PlayPauseExpectedState(MusicPlayerState state)
		=> state.PlaybackState == PlaybackState.Playing ? "paused" : "playing";

	private static readonly IReadOnlyList<ActionStateDefinition> _shuffleStates =
	[
		new("off", MacroDeckStrings.States.Off())
		{
			DefaultAppearance = new ActionStateAppearance { BackgroundColor = "#4a5568" }
		},
		new("on", MacroDeckStrings.States.On())
		{
			DefaultAppearance = new ActionStateAppearance { BackgroundColor = "#2f855a" }
		},
		_unavailableState
	];

	private static readonly IReadOnlyList<ActionStateDefinition> _repeatStates =
	[
		new("off", MacroDeckStrings.States.RepeatOff())
		{
			DefaultAppearance = new ActionStateAppearance { BackgroundColor = "#4a5568" }
		},
		new("track", MacroDeckStrings.States.RepeatTrack())
		{
			DefaultAppearance = new ActionStateAppearance { BackgroundColor = "#2b6cb0" }
		},
		new("context", MacroDeckStrings.States.RepeatContext())
		{
			DefaultAppearance = new ActionStateAppearance { BackgroundColor = "#2f855a" }
		},
		_unavailableState
	];

	// A disconnected player still has a known state set, so both mappers answer with the full set and
	// only the active id changes - the same shape PlayPauseState uses.
	private static ActionStateSnapshot ShuffleState(MusicPlayerState? state)
		=> new(_shuffleStates, Reachable(state) ? state!.ShuffleEnabled ? "on" : "off" : "unavailable");

	private static ActionStateSnapshot RepeatState(MusicPlayerState? state)
		=> new(_repeatStates,
			!Reachable(state)
				? "unavailable"
				: state!.RepeatMode switch
				{
					RepeatMode.Track => "track",
					RepeatMode.Context => "context",
					_ => "off"
				});

	private static bool Reachable(MusicPlayerState? state) => state is { IsConnected: true, IsUnavailable: false };

	private static async Task AdjustVolume(IMusicPlayer player, int delta, CancellationToken ct)
	{
		var state = await player.GetStateAsync(ct);
		var target = Math.Clamp((state.VolumePercent ?? 0) + delta, 0, 100);
		await player.SetVolumeAsync(target, ct);
	}

	private static async Task ToggleShuffle(
		IMusicPlayer player,
		IReadOnlyDictionary<string, object> values,
		IActionInteractions? _,
		CancellationToken ct)
	{
		var mode = GetString(values, "mode") ?? "toggle";
		var enabled = mode switch
		{
			"on" => true,
			"off" => false,
			_ => !(await player.GetStateAsync(ct)).ShuffleEnabled
		};

		await player.SetShuffleAsync(enabled, ct);
	}

	private static ActionParameter ModeChoice(params string[] values)
		=> ActionParameter.Choice("mode",
			options: values.Select(v => new ActionParameterOption
			{
				Value = v,
				Label = char.ToUpperInvariant(v[0]) + v[1..]
			}).ToList(),
			label: "Mode",
			defaultValue: values[0]);

	private static RepeatMode ParseRepeat(string? mode) => mode switch
	{
		"track" => RepeatMode.Track,
		"context" => RepeatMode.Context,
		_ => RepeatMode.Off
	};

	internal static int GetInt(IReadOnlyDictionary<string, object> values, string name, int fallback)
	{
		if (!values.TryGetValue(name, out var value) || value is null)
		{
			return fallback;
		}

		return value switch
		{
			int i => i,
			long l => (int)l,
			double d => (int)d,
			string s when int.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) => parsed,
			string s when double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsedD) =>
				(int)parsedD,
			_ => fallback
		};
	}

	internal static string? GetString(IReadOnlyDictionary<string, object> values, string name)
		=> values.TryGetValue(name, out var value) ? value.ToString() : null;
}
