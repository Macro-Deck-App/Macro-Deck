using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.MusicPlayer;
using Serilog;

namespace MacroDeckHost.Integrations.SinusBot;

internal sealed class SinusBotMusicPlayer : ICatalogMusicPlayer
{
	private static readonly HttpClient _http = new();

	private static readonly ILogger _logger =
		IntegrationLog.For<SinusBotMusicPlayer>(SinusBotIntegration.IntegrationId);

	private static readonly TimeSpan _idleTimeout = TimeSpan.FromSeconds(30);

	private static readonly TimeSpan _defaultLastStateLifetime = TimeSpan.FromMinutes(2);

	private readonly ConcurrentDictionary<string, string> _artworkUrls = new(StringComparer.Ordinal);

	private readonly FailureEpisodeTracker _readFailures;
	private readonly TimeSpan _lastStateLifetime;

	private volatile ISinusBotClient? _client;
	private string? _instanceId;
	private string? _instanceName;
	private long? _lastPositionMs;
	private DateTime _lastActiveUtc = DateTime.UtcNow;
	private DateTime _lastSuccessfulReadUtc = DateTime.UtcNow;

	public SinusBotMusicPlayer(TimeSpan? failureSummaryInterval = null, TimeSpan? lastStateLifetime = null)
	{
		_readFailures = new FailureEpisodeTracker(failureSummaryInterval);
		_lastStateLifetime = lastStateLifetime ?? _defaultLastStateLifetime;
	}

	public MusicPlayerState LastState { get; private set; } = MusicPlayerState.Disconnected;

	public void Connect(ISinusBotClient client, string instanceId, string instanceName)
	{
		_client = client;
		_instanceId = instanceId;
		_instanceName = instanceName;
		_lastSuccessfulReadUtc = DateTime.UtcNow;
	}

	public void Disconnect()
	{
		_client = null;
		_instanceId = null;
		_instanceName = null;
		_lastPositionMs = null;
		_lastActiveUtc = DateTime.UtcNow;
		LastState = MusicPlayerState.Disconnected;
	}

	public async Task<MusicPlayerState> GetStateAsync(CancellationToken cancellationToken = default)
	{
		var client = _client;
		var instanceId = _instanceId;
		if (client is null || instanceId is null)
		{
			return LastState = MusicPlayerState.Disconnected;
		}

		try
		{
			var status = await client.GetInstanceStatusAsync(instanceId, cancellationToken);
			NoteReadRecovered();
			_lastSuccessfulReadUtc = DateTime.UtcNow;
			return LastState = Map(instanceId, status);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception ex) when (IsTransient(ex))
		{
			if (NoteReadFailure(ex))
			{
				_logger.Debug(ex, "Transient failure reading SinusBot playback state; keeping last state");
			}

			return KeepLastState();
		}
		catch (Exception ex)
		{
			if (NoteReadFailure(ex))
			{
				_logger.Debug(ex, "Failed to read SinusBot playback state");
			}

			return LastState = Unavailable();
		}
	}

	private MusicPlayerState KeepLastState()
	{
		if (DateTime.UtcNow - _lastSuccessfulReadUtc < _lastStateLifetime)
		{
			return LastState;
		}

		if (LastState.IsConnected)
		{
			_logger.Warning("No successful SinusBot state read for {Window}; reporting the player unavailable",
				_lastStateLifetime);
		}

		return LastState = Unavailable();
	}

	private MusicPlayerState Unavailable()
		=> MusicPlayerState.Unavailable("SinusBot is not responding") with { DeviceName = _instanceName };

	private bool NoteReadFailure(Exception ex)
	{
		var signal = _readFailures.RecordFailure(ex.Message);
		switch (signal.Kind)
		{
			case FailureEpisodeSignalKind.Onset:
				_logger.Warning(ex,
					"SinusBot state reads for {Instance} started failing; keeping last state",
					_instanceName);
				return false;
			case FailureEpisodeSignalKind.SummaryDue:
				_logger.Information(
					"SinusBot state reads for {Instance} have been failing for {Duration} ({Failures} " +
					"consecutive failures; last error: {LastError})",
					_instanceName,
					signal.Duration,
					signal.ConsecutiveFailures,
					signal.LastError);
				return false;
			default:
				return true;
		}
	}

	private void NoteReadRecovered()
	{
		if (_readFailures.RecordSuccess() is { } episode)
		{
			_logger.Information("SinusBot state reads for {Instance} recovered after {Duration} ({Failures} failures)",
				_instanceName,
				episode.Duration,
				episode.Failures);
		}
	}

	// Mirrors SpotifyMusicPlayer.IsTransient: a momentary failure must not tear down the now-playing
	// UI, only a persistent one should. The bot is usually on the LAN, so blips are common.
	internal static bool IsTransient(Exception ex)
	{
		if (ex is SinusBotApiException { StatusCode: { } statusCode })
		{
			return MusicPlayerTransientFailure.IsTransientStatusCode((int)statusCode);
		}

		return MusicPlayerTransientFailure.IsNetworkLevel(ex) ||
			(ex is SinusBotAuthException && MusicPlayerTransientFailure.IsNetworkLevel(ex.InnerException ?? ex));
	}

	public async Task<MusicPlayerArtwork?> GetArtworkAsync(string artworkId,
		CancellationToken cancellationToken = default)
	{
		if (!_artworkUrls.TryGetValue(artworkId, out var url))
		{
			return null;
		}

		try
		{
			var bytes = await _http.GetByteArrayAsync(url, cancellationToken);
			return new MusicPlayerArtwork(bytes, SniffMime(bytes));
		}
		catch (Exception ex)
		{
			_logger.Warning(ex, "Failed to download SinusBot artwork");
			return null;
		}
	}

	public Task PlayAsync(CancellationToken cancellationToken = default)
		=> GuardCommand((client, id, ct) => client.PlayAsync(id, ct), "play", cancellationToken);

	public Task PauseAsync(CancellationToken cancellationToken = default)
		=> GuardCommand((client, id, ct) => client.PauseAsync(id, ct), "pause", cancellationToken);

	public Task StopAsync(CancellationToken cancellationToken = default)
		=> GuardCommand((client, id, ct) => client.StopAsync(id, ct), "stop", cancellationToken);

	public Task NextAsync(CancellationToken cancellationToken = default)
		=> GuardCommand((client, id, ct) => client.NextAsync(id, ct), "next", cancellationToken);

	public Task PreviousAsync(CancellationToken cancellationToken = default)
		=> GuardCommand((client, id, ct) => client.PreviousAsync(id, ct), "previous", cancellationToken);

	public Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default)
		=> GuardCommand((client, id, ct) => client.SeekAsync(id, (int)position.TotalSeconds, ct),
			"seek",
			cancellationToken);

	public Task SetVolumeAsync(int volumePercent, CancellationToken cancellationToken = default)
		=> GuardCommand((client, id, ct) => client.SetVolumeAsync(id, Math.Clamp(volumePercent, 0, 100), ct),
			"set volume",
			cancellationToken);

	public Task SetShuffleAsync(bool enabled, CancellationToken cancellationToken = default)
		=> GuardCommand((client, id, ct) => client.SetShuffleAsync(id, enabled, ct), "set shuffle", cancellationToken);

	public Task SetRepeatModeAsync(RepeatMode mode, CancellationToken cancellationToken = default)
		=> GuardCommand((client, id, ct) => client.SetRepeatAsync(id, mode != RepeatMode.Off, ct),
			"set repeat",
			cancellationToken);

	public Task PlayItemAsync(MusicPlayerCatalogItem item, CancellationToken cancellationToken = default)
	{
		if (item.Kind == MusicPlayerCatalogItemKind.Playlist)
		{
			_logger.Warning("SinusBot does not support playing playlists; ignoring PlayItemAsync for {Id}", item.Id);
			return Task.CompletedTask;
		}

		return GuardCommand((client, id, ct) => client.PlayFileAsync(id, item.Id, ct), "play file", cancellationToken);
	}

	public async Task<IReadOnlyList<MusicPlayerCatalogItem>> GetCatalogAsync(
		string instanceId,
		MusicPlayerCatalogItemKind kind,
		string? filter,
		CancellationToken cancellationToken)
	{
		if (kind != MusicPlayerCatalogItemKind.Track)
		{
			return [];
		}

		var client = _client;
		var id = _instanceId;
		if (client is null || id is null)
		{
			throw new InvalidOperationException("SinusBot is not connected.");
		}

		// No catch here on purpose. A failed read must not come back as an empty list, or the picker
		// shows "No items found." for an unreachable server (see IMusicPlayerCatalogProvider). The
		// 10 s HttpClient timeout surfaces as an OperationCanceledException, which the host reports
		// as "unavailable" rather than as an empty library.
		var files = await client.GetFilesAsync(id, cancellationToken);
		var items = files
			.Where(f => !string.IsNullOrWhiteSpace(f.Uuid))
			.Select(x => MapFile(instanceId, x))
			.Where(i => i is not null)
			.Cast<MusicPlayerCatalogItem>();

		if (!string.IsNullOrWhiteSpace(filter))
		{
			var f = filter.Trim();
			items = items.Where(i => Contains(i.Title, f) || Contains(i.Subtitle, f));
		}

		return items.ToList();
	}

	private static bool Contains(string? haystack, string needle)
		=> !string.IsNullOrEmpty(haystack) && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

	private MusicPlayerCatalogItem? MapFile(string instanceId, SinusBotFile file)
	{
		if (string.IsNullOrWhiteSpace(file.Uuid))
		{
			return null;
		}

		return new MusicPlayerCatalogItem(file.Uuid!,
			file.Title ?? file.Uuid!,
			MusicPlayerCatalogItemKind.Track,
			file.Artist,
			RegisterArtwork(instanceId, file.Thumbnail),
			file.Duration is { } d ? TimeSpan.FromMilliseconds(d) : null);
	}

	public async Task TogglePlayPauseAsync(CancellationToken cancellationToken = default)
	{
		var client = _client;
		var instanceId = _instanceId;
		if (client is null || instanceId is null)
		{
			return;
		}

		try
		{
			var status = await client.GetInstanceStatusAsync(instanceId, cancellationToken);
			if (status.Playing == true)
			{
				await client.PauseAsync(instanceId, cancellationToken);
			}
			else
			{
				await client.PlayAsync(instanceId, cancellationToken);
			}
		}
		catch (Exception ex)
		{
			LogCommandFailure(ex, "toggle play/pause");
		}
	}

	private MusicPlayerState Map(string instanceId, SinusBotInstanceStatus status)
	{
		// SinusBot "Running" means the bot instance process is up. A stopped instance cannot play, so it
		// is not connected-but-idle - but the bot answered, so it is not "no music player" either: the
		// instance is right there and starting it is all it takes.
		if (status.Running is false)
		{
			return MusicPlayerState.Unavailable("Instance not running") with { DeviceName = _instanceName };
		}

		var now = DateTime.UtcNow;
		var positionMs = status.Position;
		if (status.Playing == true && positionMs is { } p && p != _lastPositionMs)
		{
			_lastActiveUtc = now;
		}

		_lastPositionMs = positionMs;

		if (now - _lastActiveUtc > _idleTimeout)
		{
			return new MusicPlayerState
			{
				IsConnected = true,
				PlaybackState = PlaybackState.Stopped,
				VolumePercent = status.Volume,
				DeviceName = _instanceName
			};
		}

		var track = status.CurrentTrack;
		var playbackState = status.Playing == true
			? PlaybackState.Playing
			: track is not null
				? PlaybackState.Paused
				: PlaybackState.Stopped;

		return new MusicPlayerState
		{
			IsConnected = true,
			PlaybackState = playbackState,
			TrackName = track?.Title,
			Artists = string.IsNullOrWhiteSpace(track?.Artist) ? [] : [track.Artist],
			AlbumName = null,
			ArtworkId = RegisterArtwork(instanceId, track?.Thumbnail),
			Position = status.Position is { } position ? TimeSpan.FromMilliseconds(position) : null,
			Duration = track?.Duration is { } duration ? TimeSpan.FromMilliseconds(duration) : null,
			VolumePercent = status.Volume,
			ShuffleEnabled = status.Shuffle ?? false,
			RepeatMode = status.Repeat == true ? RepeatMode.Track : RepeatMode.Off,
			DeviceName = _instanceName,
			DeviceType = null
		};
	}

	private string? RegisterArtwork(string instanceId, string? thumbnail)
	{
		if (string.IsNullOrWhiteSpace(thumbnail))
		{
			return null;
		}

		var thumbnailUrl = _client?.GetThumbnailUrl(instanceId, thumbnail);

		if (string.IsNullOrEmpty(thumbnailUrl))
		{
			return null;
		}

		var id = Hash(thumbnailUrl);
		_artworkUrls[id] = thumbnailUrl;
		return id;
	}

	private async Task GuardCommand(
		Func<ISinusBotClient, string, CancellationToken, Task> command,
		string what,
		CancellationToken cancellationToken)
	{
		var client = _client;
		var instanceId = _instanceId;
		if (client is null || instanceId is null)
		{
			return;
		}

		try
		{
			await command(client, instanceId, cancellationToken);
		}
		catch (Exception ex)
		{
			LogCommandFailure(ex, what);
		}
	}

	private static void LogCommandFailure(Exception ex, string what)
	{
		// A failed command here usually means the instance is offline or the bot rejected the
		// request - log, don't throw, so an action flow continues gracefully.
		if (ex is SinusBotApiException)
		{
			_logger.Warning("SinusBot {What} failed: {Message}", what, ex.Message);
			return;
		}

		_logger.Error(ex, "SinusBot {What} failed", what);
	}

	private static string Hash(string value)
	{
		var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
		return Convert.ToHexString(bytes[..8]).ToLowerInvariant();
	}

	private static string SniffMime(byte[] bytes)
	{
		if (bytes is [0x89, 0x50, 0x4E, 0x47, ..])
		{
			return "image/png";
		}

		if (bytes is [0xFF, 0xD8, ..])
		{
			return "image/jpeg";
		}

		return "image/jpeg";
	}
}
