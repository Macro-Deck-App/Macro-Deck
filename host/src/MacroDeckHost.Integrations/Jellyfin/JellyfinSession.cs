using System.Globalization;
using MacroDeckHost.Integrations.Jellyfin.Protocol;

namespace MacroDeckHost.Integrations.Jellyfin;

internal sealed record JellyfinMediaItem(
	string Id,
	string Name,
	string Type,
	string? MediaType,
	string? SeriesName,
	string? SeasonName,
	string? Album,
	IReadOnlyList<string> Artists,
	int? ProductionYear,
	TimeSpan? Duration,
	string ArtworkItemId,
	string? ArtworkTag)
{
	public bool IsEpisode => string.Equals(Type, "Episode", StringComparison.OrdinalIgnoreCase);

	public bool IsAudio => string.Equals(Type, "Audio", StringComparison.OrdinalIgnoreCase) ||
		string.Equals(MediaType, "Audio", StringComparison.OrdinalIgnoreCase);

	public bool IsMovie => string.Equals(Type, "Movie", StringComparison.OrdinalIgnoreCase);

	public IReadOnlyList<string> DisplayArtists
		=> IsEpisode && SeriesName is { Length: > 0 } series ? [series] : IsAudio ? Artists : [];

	public string? DisplayAlbum
		=> IsEpisode ? SeasonName :
			IsAudio ? Album :
			IsMovie ? ProductionYear?.ToString(CultureInfo.InvariantCulture) : null;
}

internal sealed record JellyfinSession(
	string Id,
	string DeviceId,
	string DeviceName,
	string Client,
	string? UserId,
	string? UserName,
	DateTimeOffset LastActivity,
	DateTimeOffset? LastPlaybackCheckIn,
	bool SupportsMediaControl,
	IReadOnlySet<string> SupportedCommands,
	IReadOnlySet<string> PlayableMediaTypes,
	JellyfinMediaItem? NowPlaying,
	TimeSpan? ReportedPosition,
	bool CanSeek,
	bool IsPaused,
	bool IsMuted,
	int? VolumePercent,
	bool IsTranscoding,
	string? RepeatMode,
	bool IsShuffled,
	DateTimeOffset ReceivedAt = default)
{
	private static readonly TimeSpan _checkInTolerance = TimeSpan.FromSeconds(2);

	public bool IsActive => NowPlaying is not null;

	public bool IsPlaying => NowPlaying is not null && !IsPaused;

	public bool Supports(string generalCommand) => SupportedCommands.Contains(generalCommand);

	// Jellyfin reports progress sparsely, so a playing position is carried forward from the last check-in.
	// The check-in is stamped by the server's clock; one that lies ahead of ours falls back to the receipt time.
	public TimeSpan? PositionAt(DateTimeOffset now)
	{
		if (ReportedPosition is not { } position)
		{
			return null;
		}

		if (!IsPlaying)
		{
			return position;
		}

		var received = ReceivedAt == default ? now : ReceivedAt;
		var anchor = LastPlaybackCheckIn is { } checkIn && checkIn <= now && checkIn - received <= _checkInTolerance
			? checkIn
			: received;
		var extrapolated = position + (now - anchor);
		return NowPlaying?.Duration is { } duration && extrapolated > duration ? duration : extrapolated;
	}

	public double? ProgressPercentAt(DateTimeOffset now)
		=> NowPlaying?.Duration is { TotalMilliseconds: > 0 } duration && PositionAt(now) is { } position
			? Math.Clamp(position.TotalMilliseconds / duration.TotalMilliseconds * 100, 0, 100)
			: null;
}

internal static class JellyfinSessionMapper
{
	public static IReadOnlyList<JellyfinSession> Map(
		IEnumerable<JellyfinSessionDto> sessions,
		string ownDeviceId,
		DateTimeOffset receivedAt = default)
		=> sessions
			.Where(dto => !string.IsNullOrEmpty(dto.Id) &&
				!string.IsNullOrEmpty(dto.DeviceId) &&
				!string.Equals(dto.DeviceId, ownDeviceId, StringComparison.Ordinal))
			.Select(dto => Map(dto) with { ReceivedAt = receivedAt })
			.ToList();

	public static JellyfinSession Map(JellyfinSessionDto dto)
	{
		var commands = dto.Capabilities?.SupportedCommands ?? dto.SupportedCommands ?? [];
		var mediaTypes = dto.Capabilities?.PlayableMediaTypes ?? dto.PlayableMediaTypes ?? [];
		var playState = dto.PlayState;
		var item = dto.NowPlayingItem is { Id.Length: > 0 } nowPlaying ? MapItem(nowPlaying) : null;

		return new JellyfinSession(dto.Id!,
			dto.DeviceId!,
			string.IsNullOrWhiteSpace(dto.DeviceName) ? dto.DeviceId! : dto.DeviceName!,
			dto.Client ?? string.Empty,
			NullIfEmpty(dto.UserId),
			NullIfEmpty(dto.UserName),
			dto.LastActivityDate ?? DateTimeOffset.MinValue,
			dto.LastPlaybackCheckIn is { Year: > 1 } checkIn ? checkIn : null,
			dto.SupportsMediaControl || dto.Capabilities?.SupportsMediaControl == true,
			new HashSet<string>(commands, StringComparer.OrdinalIgnoreCase),
			new HashSet<string>(mediaTypes, StringComparer.OrdinalIgnoreCase),
			item,
			item is not null && playState?.PositionTicks is { } ticks ? TimeSpan.FromTicks(ticks) : null,
			playState?.CanSeek ?? false,
			item is not null && (playState?.IsPaused ?? false),
			playState?.IsMuted ?? false,
			playState?.VolumeLevel,
			item is not null && (string.Equals(playState?.PlayMethod, "Transcode", StringComparison.OrdinalIgnoreCase) ||
				dto.TranscodingInfo is { IsVideoDirect: false } or { IsAudioDirect: false }),
			playState?.RepeatMode,
			string.Equals(playState?.PlaybackOrder, "Shuffle", StringComparison.OrdinalIgnoreCase));
	}

	public static JellyfinMediaItem MapItem(JellyfinItemDto dto)
	{
		var type = dto.Type ?? string.Empty;
		var artists = dto.Artists is { Count: > 0 } list
			? list
			: dto.AlbumArtist is { Length: > 0 } albumArtist
				? [albumArtist]
				: [];

		var (artworkId, artworkTag) = type switch
		{
			"Episode" when !string.IsNullOrEmpty(dto.SeriesId) => (dto.SeriesId!, dto.SeriesPrimaryImageTag),
			"Audio" when !string.IsNullOrEmpty(dto.AlbumId) && !string.IsNullOrEmpty(dto.AlbumPrimaryImageTag)
				=> (dto.AlbumId!, dto.AlbumPrimaryImageTag),
			_ => (dto.Id!, dto.ImageTags?.GetValueOrDefault("Primary"))
		};

		return new JellyfinMediaItem(dto.Id!,
			dto.Name ?? string.Empty,
			type,
			dto.MediaType,
			NullIfEmpty(dto.SeriesName),
			NullIfEmpty(dto.SeasonName),
			NullIfEmpty(dto.Album),
			artists,
			dto.ProductionYear,
			dto.RunTimeTicks is > 0 ? TimeSpan.FromTicks(dto.RunTimeTicks.Value) : null,
			artworkId,
			artworkTag);
	}

	private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;
}
