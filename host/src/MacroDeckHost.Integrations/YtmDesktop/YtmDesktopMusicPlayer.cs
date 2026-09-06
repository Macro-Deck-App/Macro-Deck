using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using MacroDeckHost.Integrations.YtmDesktop.Protocol;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.MusicPlayer;
using MacroDeckHost.Localization;
using Serilog;

namespace MacroDeckHost.Integrations.YtmDesktop;

internal sealed class YtmDesktopMusicPlayer : ICatalogMusicPlayer
{
	private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };

	private static readonly ILogger _logger =
		IntegrationLog.For<YtmDesktopMusicPlayer>(YtmDesktopIntegration.IntegrationId);

	private readonly ConcurrentDictionary<string, string> _artworkUrls = new(StringComparer.Ordinal);

	private volatile YtmDesktopConnection? _connection;

	public string RegisterArtwork(string url)
	{
		var id = Hash(url);
		_artworkUrls[id] = url;
		return id;
	}

	public void Connect(YtmDesktopConnection connection) => _connection = connection;

	public void Disconnect() => _connection = null;

	public YtmDesktopSnapshot Snapshot => _connection?.Snapshot ?? YtmDesktopSnapshot.Disconnected;

	public Task<MusicPlayerState> GetStateAsync(CancellationToken cancellationToken = default)
	{
		var connection = _connection;
		if (connection is null)
		{
			return Task.FromResult(MusicPlayerState.Disconnected);
		}

		connection.TouchState();
		var player = connection.Snapshot.Player;
		if (player.IsConnected)
		{
			return Task.FromResult(player);
		}

		return Task.FromResult(connection.NeedsAuthorization
			? MusicPlayerState.Disconnected
			: MusicPlayerState.Unavailable(connection.IsSessionLive
				? "Waiting for YouTube Music"
				: "YouTube Music is not running"));
	}

	public async Task<MusicPlayerArtwork?> GetArtworkAsync(
		string artworkId,
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
			_logger.Warning(ex, "Failed to download YouTube Music artwork");
			return null;
		}
	}

	public Task PlayAsync(CancellationToken cancellationToken = default)
		=> GuardCommand(YtmCommands.Play, null, "play", cancellationToken);

	public Task PauseAsync(CancellationToken cancellationToken = default)
		=> GuardCommand(YtmCommands.Pause, null, "pause", cancellationToken);

	public Task TogglePlayPauseAsync(CancellationToken cancellationToken = default)
		=> GuardCommand(YtmCommands.PlayPause, null, "toggle play/pause", cancellationToken);

	public Task NextAsync(CancellationToken cancellationToken = default)
		=> GuardCommand(YtmCommands.Next, null, "next", cancellationToken);

	public Task PreviousAsync(CancellationToken cancellationToken = default)
		=> GuardCommand(YtmCommands.Previous, null, "previous", cancellationToken);

	public Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default)
	{
		var connection = _connection;
		if (connection is null)
		{
			return Task.CompletedTask;
		}

		var duration = connection.Snapshot.Player.Duration?.TotalSeconds;
		var seconds = Math.Max(0, position.TotalSeconds);
		if (duration is > 0)
		{
			seconds = Math.Min(seconds, duration.Value);
		}

		connection.EnqueueCommand(YtmCommands.SeekTo, (int)seconds);
		return Task.CompletedTask;
	}

	public Task SetVolumeAsync(int volumePercent, CancellationToken cancellationToken = default)
	{
		var connection = _connection;
		if (connection is null)
		{
			return Task.CompletedTask;
		}

		var volume = Math.Clamp(volumePercent, 0, 100);
		connection.EnqueueCommand(YtmCommands.SetVolume, volume);
		connection.ApplyOptimisticVolume(volume);
		return Task.CompletedTask;
	}

	public Task SetShuffleAsync(bool enabled, CancellationToken cancellationToken = default)
	{
		var connection = _connection;
		if (connection is null || !connection.ShouldSendShuffle(enabled))
		{
			return Task.CompletedTask;
		}

		return GuardCommand(YtmCommands.Shuffle, null, "set shuffle", cancellationToken);
	}

	public Task SetRepeatModeAsync(RepeatMode mode, CancellationToken cancellationToken = default)
		=> GuardCommand(YtmCommands.RepeatMode,
			YtmDesktopStateMapper.ToCompanionRepeatMode(mode),
			"set repeat mode",
			cancellationToken);

	public Task PlayItemAsync(MusicPlayerCatalogItem item, CancellationToken cancellationToken = default)
	{
		if (item.Kind == MusicPlayerCatalogItemKind.Playlist)
		{
			return GuardCommand(YtmCommands.ChangeVideo,
				new { videoId = (string?)null, playlistId = item.Id },
				"play playlist",
				cancellationToken);
		}

		// Playing a track that is already queued keeps the queue; changeVideo would discard it and
		// build a fresh one around that video. The index is resolved now rather than persisted,
		// because only the video id is stable across sessions.
		var queue = Snapshot.Queue;
		for (var index = 0; index < queue.Count; index++)
		{
			if (string.Equals(queue[index].VideoId, item.Id, StringComparison.Ordinal))
			{
				return GuardCommand(YtmCommands.PlayQueueIndex, index, "play queue index", cancellationToken);
			}
		}

		return GuardCommand(YtmCommands.ChangeVideo,
			new { videoId = item.Id, playlistId = (string?)null },
			"play track",
			cancellationToken);
	}

	public async Task<IReadOnlyList<MusicPlayerCatalogItem>> GetCatalogAsync(
		string instanceId,
		MusicPlayerCatalogItemKind kind,
		string? filter,
		CancellationToken cancellationToken)
	{
		var connection = _connection;
		if (connection is null || !connection.IsSessionLive)
		{
			throw new InvalidOperationException("YouTube Music Desktop App is not connected.");
		}

		return kind == MusicPlayerCatalogItemKind.Playlist
			? await PlaylistCatalogAsync(connection, filter, cancellationToken)
			: TrackCatalog(connection, filter);
	}

	public async Task<ActionResult> SetRatingAsync(string mode, CancellationToken cancellationToken)
	{
		var connection = _connection;
		if (connection is null || !connection.IsSessionLive)
		{
			return ActionResult.Failed(ActionErrorCodes.NotConnected,
				AppStrings.Integrations.YtmDesktop.Errors.NotConnected());
		}

		var snapshot = connection.Snapshot;
		if (snapshot.VideoId is null)
		{
			return ActionResult.Failed(ActionErrorCodes.NotFound,
				AppStrings.Integrations.YtmDesktop.Errors.NothingPlaying());
		}

		var current = snapshot.LikeStatus;
		string? command;

		switch (mode)
		{
			case "toggle-like":
				command = YtmCommands.ToggleLike;
				break;
			case "toggle-dislike":
				command = YtmCommands.ToggleDislike;
				break;
			case "like":
				command = current == YtmLikeStatus.Like ? null : YtmCommands.ToggleLike;
				break;
			case "dislike":
				command = current == YtmLikeStatus.Dislike ? null : YtmCommands.ToggleDislike;
				break;
			case "clear":
				command = current switch
				{
					YtmLikeStatus.Like => YtmCommands.ToggleLike,
					YtmLikeStatus.Dislike => YtmCommands.ToggleDislike,
					_ => null
				};

				if (current is null)
				{
					_logger.Warning("The rating of the current track is unknown, so it was not cleared");
				}

				break;
			default:
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.YtmDesktop.Errors.UnknownRatingMode(mode: mode));
		}

		if (command is null)
		{
			return ActionResult.Success();
		}

		if (current is null && mode is "like" or "dislike")
		{
			_logger.Debug("The rating of the current track is unknown; {Mode} falls back to a toggle", mode);
		}

		return await SendActionCommandAsync(connection, command, null, cancellationToken);
	}

	public async Task<ActionResult> SetMuteAsync(string mode, CancellationToken cancellationToken)
	{
		var connection = _connection;
		if (connection is null || !connection.IsSessionLive)
		{
			return ActionResult.Failed(ActionErrorCodes.NotConnected,
				AppStrings.Integrations.YtmDesktop.Errors.NotConnected());
		}

		string command;
		switch (mode)
		{
			case "on":
				command = YtmCommands.Mute;
				break;
			case "off":
				command = YtmCommands.Unmute;
				break;
			case "toggle":
				var muted = connection.Snapshot.Muted;
				if (muted is null)
				{
					_logger.Warning(
						"YouTube Music Desktop App did not report the mute state; muting instead of toggling. " +
						"Reporting it needs version 2.0.1 or newer");
				}

				command = muted == true ? YtmCommands.Unmute : YtmCommands.Mute;
				break;
			default:
				return ActionResult.Failed(ActionErrorCodes.InvalidParameter,
					AppStrings.Integrations.YtmDesktop.Errors.UnknownMuteMode(mode: mode));
		}

		return await SendActionCommandAsync(connection, command, null, cancellationToken);
	}

	public async Task<ActionResult> PlayVideoOrUrlAsync(string value, CancellationToken cancellationToken)
	{
		var connection = _connection;
		if (connection is null || !connection.IsSessionLive)
		{
			return ActionResult.Failed(ActionErrorCodes.NotConnected,
				AppStrings.Integrations.YtmDesktop.Errors.NotConnected());
		}

		if (!YtmDesktopUrls.TryParse(value, out var videoId, out var playlistId))
		{
			return ActionResult.Failed(ActionErrorCodes.InvalidParameter,
				AppStrings.Integrations.YtmDesktop.Errors.EnterLinkOrId());
		}

		return await SendActionCommandAsync(connection,
			YtmCommands.ChangeVideo,
			new { videoId, playlistId },
			cancellationToken);
	}

	private static async Task<List<MusicPlayerCatalogItem>> PlaylistCatalogAsync(
		YtmDesktopConnection connection,
		string? filter,
		CancellationToken cancellationToken)
	{
		// No catch on purpose. A failed read must not come back as an empty list, or the picker shows
		// "No items found." for a server that could not answer (see IMusicPlayerCatalogProvider). The
		// cache is maintained from the realtime events, so this only fires when it never loaded.
		var playlists = connection.Playlists ?? await connection.RefreshPlaylistsAsync(cancellationToken);

		var items = playlists
			.Where(p => !string.IsNullOrWhiteSpace(p.Id))
			.Select(p => new MusicPlayerCatalogItem(p.Id, p.Title, MusicPlayerCatalogItemKind.Playlist));

		if (!string.IsNullOrWhiteSpace(filter))
		{
			var needle = filter.Trim();
			items = items.Where(i => Contains(i.Title, needle));
		}

		return items.ToList();
	}

	private List<MusicPlayerCatalogItem> TrackCatalog(YtmDesktopConnection connection, string? filter)
	{
		var queue = connection.Snapshot.Queue
			.Where(item => !string.IsNullOrWhiteSpace(item.VideoId));

		if (!string.IsNullOrWhiteSpace(filter))
		{
			var needle = filter.Trim();
			queue = queue.Where(item =>
				Contains(item.Title, needle) ||
				Contains(item.Author, needle));
		}

		return queue
			.Select(item => new MusicPlayerCatalogItem(item.VideoId,
				item.Title,
				MusicPlayerCatalogItemKind.Track,
				item.Author,
				LargestThumbnailId(item.Thumbnails)))
			.ToList();
	}

	private string? LargestThumbnailId(IReadOnlyList<YtmThumbnail> thumbnails)
	{
		YtmThumbnail? best = null;
		foreach (var thumbnail in thumbnails)
		{
			if (string.IsNullOrWhiteSpace(thumbnail.Url))
			{
				continue;
			}

			if (best is null || (long)thumbnail.Width * thumbnail.Height > (long)best.Width * best.Height)
			{
				best = thumbnail;
			}
		}

		return best is null ? null : RegisterArtwork(best.Url);
	}

	private static bool Contains(string? haystack, string needle)
		=> !string.IsNullOrEmpty(haystack) && haystack.Contains(needle, StringComparison.OrdinalIgnoreCase);

	private async Task GuardCommand(
		string command,
		object? data,
		string what,
		CancellationToken cancellationToken)
	{
		var connection = _connection;
		if (connection is null)
		{
			return;
		}

		try
		{
			await connection.SendCommandAsync(command, data, cancellationToken);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (YtmDesktopApiException ex)
		{
			_logger.Warning("YouTube Music Desktop App {What} failed: {Message}", what, ex.Message);
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "YouTube Music Desktop App {What} failed", what);
		}
	}

	private static async Task<ActionResult> SendActionCommandAsync(
		YtmDesktopConnection connection,
		string command,
		object? data,
		CancellationToken cancellationToken)
	{
		try
		{
			await connection.SendCommandAsync(command, data, cancellationToken);
			return ActionResult.Success();
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (YtmDesktopApiException ex)
		{
			_logger.Warning("YouTube Music Desktop App rejected {Command}: {Message}", command, ex.Message);
			return ActionResult.Failed(ToErrorCode(ex),
				AppStrings.Integrations.YtmDesktop.Errors.ApiRejected(details: ex.Message));
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "YouTube Music Desktop App could not run {Command}", command);
			return ActionResult.Failed(ActionErrorCodes.ProviderError,
				AppStrings.Integrations.YtmDesktop.Errors.ActionFailed());
		}
	}

	private static string ToErrorCode(YtmDesktopApiException ex)
		=> ex.ErrorCode switch
		{
			YtmErrorCodes.Unauthenticated => ActionErrorCodes.PermissionDenied,
			YtmErrorCodes.YoutubeMusicUnavailable or YtmErrorCodes.YoutubeMusicTimeOut => ActionErrorCodes.Unavailable,
			YtmErrorCodes.InvalidVolume
				or YtmErrorCodes.InvalidRepeatMode
				or YtmErrorCodes.InvalidSeekPosition
				or YtmErrorCodes.InvalidQueueIndex
				or YtmErrorCodes.InvalidChangeRequest => ActionErrorCodes.InvalidParameter,
			_ => (int)ex.StatusCode == 429 ? ActionErrorCodes.Timeout : ActionErrorCodes.ProviderError
		};

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

		if (bytes is [0x52, 0x49, 0x46, 0x46, _, _, _, _, 0x57, 0x45, 0x42, 0x50, ..])
		{
			return "image/webp";
		}

		return "image/jpeg";
	}
}
