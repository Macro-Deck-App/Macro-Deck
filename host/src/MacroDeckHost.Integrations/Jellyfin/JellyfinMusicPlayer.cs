using System.Globalization;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.MusicPlayer;
using Serilog;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.Jellyfin;

namespace MacroDeckHost.Integrations.Jellyfin;

internal sealed class JellyfinCommandException(string code, LocalizedText message)
	: Exception(message.ToString())
{
	public string Code { get; } = code;

	public LocalizedText LocalizedMessage { get; } = message;
}

internal sealed class JellyfinMusicPlayer : IMusicPlayer
{
	internal static readonly TimeSpan UnavailableAfter = TimeSpan.FromMinutes(2);

	private static readonly ILogger _logger = IntegrationLog.For<JellyfinMusicPlayer>(JellyfinIntegration.IntegrationId);

	private readonly JellyfinRuntime _runtime;
	private readonly TimeProvider _time;

	public JellyfinMusicPlayer(JellyfinRuntime runtime, string? deviceId, TimeProvider? time = null)
	{
		_runtime = runtime;
		DeviceId = deviceId;
		_time = time ?? TimeProvider.System;
	}

	public string? DeviceId { get; }

	public JellyfinRuntime Runtime => _runtime;

	public JellyfinSession? CurrentSession() => Resolve(_runtime.Connection.State);

	public Task<MusicPlayerState> GetStateAsync(CancellationToken cancellationToken = default)
		=> Task.FromResult(BuildState(_runtime.Connection.State, _time.GetUtcNow()));

	internal MusicPlayerState BuildState(JellyfinServerState state, DateTimeOffset now)
	{
		if (!state.IsConnected && (state.LastSuccess is not { } success || now - success > UnavailableAfter))
		{
			return MusicPlayerState.Unavailable();
		}

		var session = Resolve(state);
		if (session is null)
		{
			return DeviceId is null
				? new MusicPlayerState { IsConnected = true, PlaybackState = PlaybackState.Stopped }
				: MusicPlayerState.Disconnected with
				{
					DeviceName = _runtime.Devices.Find(DeviceId)?.Name
				};
		}

		var item = session.NowPlaying;
		return new MusicPlayerState
		{
			IsConnected = true,
			PlaybackState = item is null ? PlaybackState.Stopped :
				session.IsPaused ? PlaybackState.Paused : PlaybackState.Playing,
			TrackName = item?.Name,
			Artists = item?.DisplayArtists ?? [],
			AlbumName = item?.DisplayAlbum,
			ArtworkId = item is null ? null : _runtime.RegisterArtwork(item),
			Position = item is null ? null : session.PositionAt(now),
			Duration = item?.Duration,
			VolumePercent = session.VolumePercent,
			ShuffleEnabled = session.IsShuffled,
			RepeatMode = session.RepeatMode switch
			{
				"RepeatOne" => RepeatMode.Track,
				"RepeatAll" => RepeatMode.Context,
				_ => RepeatMode.Off
			},
			DeviceName = session.DeviceName,
			DeviceType = session.Client
		};
	}

	public async Task<MusicPlayerArtwork?> GetArtworkAsync(
		string artworkId,
		CancellationToken cancellationToken = default)
	{
		if (!_runtime.TryGetArtwork(artworkId, out var artwork))
		{
			return null;
		}

		var image = await _runtime.Connection.Client
			.GetPrimaryImageAsync(artwork.ItemId, artwork.Tag, cancellationToken)
			.ConfigureAwait(false);
		return image is null ? null : new MusicPlayerArtwork(image.Data, image.MimeType);
	}

	public Task PlayAsync(CancellationToken cancellationToken = default)
		=> PlaystateAsync("Unpause", null, cancellationToken);

	public Task PauseAsync(CancellationToken cancellationToken = default)
		=> PlaystateAsync("Pause", null, cancellationToken);

	public Task TogglePlayPauseAsync(CancellationToken cancellationToken = default)
		=> PlaystateAsync("PlayPause", null, cancellationToken);

	public Task NextAsync(CancellationToken cancellationToken = default)
		=> PlaystateAsync("NextTrack", null, cancellationToken);

	public Task PreviousAsync(CancellationToken cancellationToken = default)
		=> PlaystateAsync("PreviousTrack", null, cancellationToken);

	public Task StopAsync(CancellationToken cancellationToken = default)
		=> PlaystateAsync("Stop", null, cancellationToken);

	public Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default)
	{
		var session = RequireMediaControl();
		if (!session.CanSeek)
		{
			throw NotSupported(session);
		}

		if (session.NowPlaying is null)
		{
			throw new JellyfinCommandException(ActionErrorCodes.Unavailable, Strings.Errors.NothingPlaying());
		}

		var target = position < TimeSpan.Zero ? TimeSpan.Zero : position;
		if (session.NowPlaying.Duration is { } duration && target > duration)
		{
			target = duration;
		}

		return Send(session,
			() => _runtime.Connection.Client.SendPlaystateAsync(session.Id, "Seek", target.Ticks, cancellationToken));
	}

	public Task SeekRelativeAsync(TimeSpan offset, CancellationToken cancellationToken = default)
	{
		var session = RequireMediaControl();
		var position = session.PositionAt(_time.GetUtcNow()) ??
			throw new JellyfinCommandException(ActionErrorCodes.Unavailable, Strings.Errors.NothingPlaying());
		return SeekAsync(position + offset, cancellationToken);
	}

	public Task SetVolumeAsync(int volumePercent, CancellationToken cancellationToken = default)
		=> GeneralCommandAsync("SetVolume",
			new Dictionary<string, string>
			{
				["Volume"] = Math.Clamp(volumePercent, 0, 100).ToString(CultureInfo.InvariantCulture)
			},
			cancellationToken);

	public Task SetShuffleAsync(bool enabled, CancellationToken cancellationToken = default)
		=> GeneralCommandAsync("SetShuffleQueue",
			new Dictionary<string, string> { ["ShuffleMode"] = enabled ? "Shuffle" : "Sorted" },
			cancellationToken);

	public Task SetRepeatModeAsync(RepeatMode mode, CancellationToken cancellationToken = default)
		=> GeneralCommandAsync("SetRepeatMode",
			new Dictionary<string, string>
			{
				["RepeatMode"] = mode switch
				{
					RepeatMode.Track => "RepeatOne",
					RepeatMode.Context => "RepeatAll",
					_ => "RepeatNone"
				}
			},
			cancellationToken);

	public Task GeneralCommandAsync(
		string command,
		IReadOnlyDictionary<string, string>? arguments,
		CancellationToken cancellationToken)
	{
		var session = RequireSession();
		if (!session.Supports(command))
		{
			throw NotSupported(session);
		}

		return Send(session,
			() => _runtime.Connection.Client.SendGeneralCommandAsync(session.Id, command, arguments, cancellationToken));
	}

	public Task DisplayMessageAsync(string? header, string text, int? timeoutMs, CancellationToken cancellationToken)
	{
		var session = RequireSession();
		if (!session.Supports("DisplayMessage"))
		{
			throw NotSupported(session);
		}

		return Send(session,
			() => _runtime.Connection.Client.SendMessageAsync(session.Id, header, text, timeoutMs, cancellationToken));
	}

	public Task PlayItemAsync(string itemId, string? mediaType, CancellationToken cancellationToken)
	{
		var session = RequireSession();
		if (!session.SupportsMediaControl ||
			(mediaType is { Length: > 0 } && !session.PlayableMediaTypes.Contains(mediaType)))
		{
			throw NotSupported(session);
		}

		return Send(session,
			() => _runtime.Connection.Client.PlayNowAsync(session.Id, [itemId], cancellationToken));
	}

	private Task PlaystateAsync(string command, long? ticks, CancellationToken cancellationToken)
	{
		var session = RequireMediaControl();
		return Send(session,
			() => _runtime.Connection.Client.SendPlaystateAsync(session.Id, command, ticks, cancellationToken));
	}

	private JellyfinSession RequireMediaControl()
	{
		var session = RequireSession();
		return session.SupportsMediaControl ? session : throw NotSupported(session);
	}

	private JellyfinSession RequireSession()
	{
		var state = _runtime.Connection.State;
		if (!state.IsConnected)
		{
			throw new JellyfinCommandException(ActionErrorCodes.NotConnected,
				Strings.Errors.ServerUnreachable(server: _runtime.Title));
		}

		return Resolve(state) ??
			throw new JellyfinCommandException(ActionErrorCodes.NotFound,
				Strings.Errors.NoSession());
	}

	private static JellyfinCommandException NotSupported(JellyfinSession session)
		=> new(ActionErrorCodes.Unavailable,
			Strings.Errors.NotSupportedByClient(device: session.DeviceName));

	private static async Task Send(JellyfinSession session, Func<Task> send)
	{
		try
		{
			await send().ConfigureAwait(false);
		}
		catch (Exception ex) when (ex is not OperationCanceledException and not JellyfinCommandException)
		{
			_logger.Warning(ex, "Jellyfin command to {Device} failed", session.DeviceName);
			throw new JellyfinCommandException(ActionErrorCodes.ProviderError,
				Strings.Errors.CommandFailed(device: session.DeviceName));
		}
	}

	internal JellyfinSession? Resolve(JellyfinServerState state)
	{
		if (DeviceId is null)
		{
			return state.Sessions
				.Where(session => session.IsActive)
				.OrderByDescending(session => session.SupportsMediaControl)
				.ThenByDescending(session => session.LastActivity)
				.FirstOrDefault();
		}

		return state.Sessions
			.Where(session => string.Equals(session.DeviceId, DeviceId, StringComparison.Ordinal))
			.OrderByDescending(session => session.IsActive)
			.ThenByDescending(session => session.LastActivity)
			.FirstOrDefault();
	}
}
