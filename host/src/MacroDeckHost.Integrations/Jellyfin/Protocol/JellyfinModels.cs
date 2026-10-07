using System.Text.Json;

namespace MacroDeckHost.Integrations.Jellyfin.Protocol;

internal sealed record JellyfinPublicSystemInfo
{
	public string? ServerName { get; init; }

	public string? Version { get; init; }

	public string? Id { get; init; }
}

internal sealed record JellyfinAuthenticationResult
{
	public string? AccessToken { get; init; }

	public JellyfinUserDto? User { get; init; }
}

internal sealed record JellyfinUserDto
{
	public string? Id { get; init; }

	public string? Name { get; init; }
}

internal sealed record JellyfinSessionDto
{
	public string? Id { get; init; }

	public string? UserId { get; init; }

	public string? UserName { get; init; }

	public string? Client { get; init; }

	public string? DeviceName { get; init; }

	public string? DeviceId { get; init; }

	public DateTimeOffset? LastActivityDate { get; init; }

	public DateTimeOffset? LastPlaybackCheckIn { get; init; }

	public bool SupportsMediaControl { get; init; }

	public bool SupportsRemoteControl { get; init; }

	public IReadOnlyList<string>? PlayableMediaTypes { get; init; }

	public IReadOnlyList<string>? SupportedCommands { get; init; }

	public JellyfinCapabilitiesDto? Capabilities { get; init; }

	public JellyfinPlayStateDto? PlayState { get; init; }

	public JellyfinItemDto? NowPlayingItem { get; init; }

	public JellyfinTranscodingInfoDto? TranscodingInfo { get; init; }
}

internal sealed record JellyfinCapabilitiesDto
{
	public IReadOnlyList<string>? PlayableMediaTypes { get; init; }

	public IReadOnlyList<string>? SupportedCommands { get; init; }

	public bool SupportsMediaControl { get; init; }
}

internal sealed record JellyfinPlayStateDto
{
	public long? PositionTicks { get; init; }

	public bool CanSeek { get; init; }

	public bool IsPaused { get; init; }

	public bool IsMuted { get; init; }

	public int? VolumeLevel { get; init; }

	public string? PlayMethod { get; init; }

	public string? RepeatMode { get; init; }

	public string? PlaybackOrder { get; init; }
}

internal sealed record JellyfinTranscodingInfoDto
{
	public bool IsVideoDirect { get; init; }

	public bool IsAudioDirect { get; init; }
}

internal sealed record JellyfinItemDto
{
	public string? Id { get; init; }

	public string? Name { get; init; }

	public string? Type { get; init; }

	public string? MediaType { get; init; }

	public string? SeriesName { get; init; }

	public string? SeriesId { get; init; }

	public string? SeriesPrimaryImageTag { get; init; }

	public string? SeasonName { get; init; }

	public string? Album { get; init; }

	public string? AlbumId { get; init; }

	public string? AlbumPrimaryImageTag { get; init; }

	public string? AlbumArtist { get; init; }

	public IReadOnlyList<string>? Artists { get; init; }

	public int? IndexNumber { get; init; }

	public int? ParentIndexNumber { get; init; }

	public int? ProductionYear { get; init; }

	public long? RunTimeTicks { get; init; }

	public IReadOnlyDictionary<string, string>? ImageTags { get; init; }
}

internal sealed record JellyfinItemsResult
{
	public IReadOnlyList<JellyfinItemDto>? Items { get; init; }
}

internal sealed record JellyfinSocketMessage
{
	public string? MessageType { get; init; }

	public JsonElement Data { get; init; }
}
