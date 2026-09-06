namespace MacroDeck.Plugin.Protocol.Capabilities.MusicPlayer;

/// <summary>
/// Mirrors the SDK's <c>MusicPlayerInstance</c>, plus the two capability flags the host's adapter
/// factory needs to pick the right <c>RemoteMusicPlayer</c> leaf for this instance.
/// <see cref="HasCatalog" />/<see cref="HasDevices" /> reflect whether
/// <c>IMusicPlayerProvider.GetPlayer(Id)</c> also implements <c>IMusicPlayerCatalogProvider</c>/
/// <c>IMusicPlayerDeviceProvider</c> for this particular instance - those interfaces hang off the
/// per-instance player, not the provider, so the flag cannot live anywhere provider-wide.
/// </summary>
public sealed record MusicPlayerInstanceDto
{
	public required string Id { get; init; }

	public required string DisplayName { get; init; }

	public bool HasCatalog { get; init; }

	public bool HasDevices { get; init; }
}

/// <summary>The full result of the <c>music-player</c> capability's <c>describe</c> operation - what
/// <c>RemotePluginSnapshotRefresher</c> folds into the snapshot's provider name and instance list.</summary>
public sealed record MusicPlayerDescribePayload
{
	public required string ProviderName { get; init; }

	public required IReadOnlyList<MusicPlayerInstanceDto> Instances { get; init; }
}

/// <summary>
/// Result of the <c>instances</c> operation: the same instance list <c>describe</c> carries, exposed
/// as its own narrow round trip. <c>describe</c> is what the snapshot refresher actually calls (its
/// wire operation is fixed to <c>"describe"</c> for every kind); this one exists so a caller that only
/// wants a live re-enumeration - without paying for whatever else a future <c>describe</c> grows to
/// carry - has a way to ask for just that.
/// </summary>
public sealed record MusicPlayerInstancesResult
{
	public required IReadOnlyList<MusicPlayerInstanceDto> Instances { get; init; }
}

/// <summary>
/// Arguments every per-instance operation carries: which player instance to address. Music player
/// instance ids are config-entry GUIDs that do not exist at declaration time, so - unlike
/// <c>actions</c>/<c>variables</c> - this kind cannot address an instance through the declared local
/// id alone; see <c>ProviderCapabilityId</c>'s remarks.
/// </summary>
public sealed record MusicPlayerInstanceArguments
{
	public required string InstanceId { get; init; }
}

/// <summary>
/// Mirrors the SDK's <c>MusicPlayerState</c>. <see cref="PlaybackState" /> and <see cref="RepeatMode" />
/// are strings, not the SDK's enums - see <c>ActionParameterDto</c>'s remarks for why every wire DTO
/// in this project follows that rule.
/// </summary>
public sealed record MusicPlayerStateDto
{
	public bool IsConnected { get; init; }

	public bool IsUnavailable { get; init; }

	public string? StatusMessage { get; init; }

	/// <summary>One of the SDK's <c>PlaybackState</c> member names: "Stopped", "Playing", "Paused".</summary>
	public required string PlaybackState { get; init; }

	public string? TrackName { get; init; }

	public IReadOnlyList<string> Artists { get; init; } = [];

	public string? AlbumName { get; init; }

	public string? ArtworkId { get; init; }

	public double? PositionSeconds { get; init; }

	public double? DurationSeconds { get; init; }

	public int? VolumePercent { get; init; }

	public bool ShuffleEnabled { get; init; }

	/// <summary>One of the SDK's <c>RepeatMode</c> member names: "Off", "Track", "Context".</summary>
	public required string RepeatMode { get; init; }

	public string? DeviceName { get; init; }

	public string? DeviceType { get; init; }
}

/// <summary>Arguments for the <c>artwork</c> operation.</summary>
public sealed record MusicPlayerArtworkArguments
{
	public required string InstanceId { get; init; }

	public required string ArtworkId { get; init; }
}

/// <summary>
/// Result of the <c>artwork</c> operation. Artwork at or below <c>MusicPlayerCapabilityHandler</c>'s
/// inline threshold is returned directly in <see cref="Data" />; larger artwork travels over the
/// <c>asset.*</c> pipeline instead - the handler uploads it before replying, and this carries
/// <see cref="ContentHash" /> so the host can read the already-committed bytes back out of its
/// content-hash-keyed asset cache. Exactly one of <see cref="Data" />/<see cref="ContentHash" /> is set.
/// </summary>
public sealed record MusicPlayerArtworkResult
{
	/// <summary>Base64-encoded image bytes, when the artwork fit inline.</summary>
	public string? Data { get; init; }

	/// <summary>The content hash to look up in the host's asset cache, when the artwork travelled over
	/// <c>asset.*</c> instead of inline.</summary>
	public string? ContentHash { get; init; }

	public required string MimeType { get; init; }
}

/// <summary>Mirrors the SDK's <c>MusicPlayerCatalogItem</c>.</summary>
public sealed record MusicPlayerCatalogItemDto
{
	public required string Id { get; init; }

	public required string Title { get; init; }

	/// <summary>One of the SDK's <c>MusicPlayerCatalogItemKind</c> member names: "Track", "Playlist".</summary>
	public required string Kind { get; init; }

	public string? Subtitle { get; init; }

	public string? ArtworkId { get; init; }

	public double? DurationSeconds { get; init; }
}

/// <summary>Arguments for the <c>play-item</c> operation.</summary>
public sealed record MusicPlayerPlayItemArguments
{
	public required string InstanceId { get; init; }

	public required MusicPlayerCatalogItemDto Item { get; init; }
}

/// <summary>Arguments for the <c>seek</c> operation.</summary>
public sealed record MusicPlayerSeekArguments
{
	public required string InstanceId { get; init; }

	public required double PositionSeconds { get; init; }
}

/// <summary>Arguments for the <c>volume</c> operation.</summary>
public sealed record MusicPlayerVolumeArguments
{
	public required string InstanceId { get; init; }

	public required int VolumePercent { get; init; }
}

/// <summary>Arguments for the <c>shuffle</c> operation.</summary>
public sealed record MusicPlayerShuffleArguments
{
	public required string InstanceId { get; init; }

	public required bool Enabled { get; init; }
}

/// <summary>Arguments for the <c>repeat</c> operation.</summary>
public sealed record MusicPlayerRepeatArguments
{
	public required string InstanceId { get; init; }

	/// <summary>One of the SDK's <c>RepeatMode</c> member names.</summary>
	public required string Mode { get; init; }
}

/// <summary>Arguments for the <c>catalog</c> operation.</summary>
public sealed record MusicPlayerCatalogArguments
{
	public required string InstanceId { get; init; }

	/// <summary>One of the SDK's <c>MusicPlayerCatalogItemKind</c> member names.</summary>
	public required string Kind { get; init; }

	public string? Filter { get; init; }
}

/// <summary>
/// Result of the <c>catalog</c> operation. Only ever returned on success:
/// <c>IMusicPlayerCatalogProvider.GetCatalogAsync</c> must throw on a read failure rather than degrade
/// to an empty list (see its doc comments), so a failed read never reaches this DTO - it surfaces as
/// an <c>INTERNAL_ERROR</c> capability result instead, which the host adapter lets propagate rather
/// than swallowing.
/// </summary>
public sealed record MusicPlayerCatalogResult
{
	public required IReadOnlyList<MusicPlayerCatalogItemDto> Items { get; init; }
}

/// <summary>Mirrors the SDK's <c>MusicPlayerDevice</c>.</summary>
public sealed record MusicPlayerDeviceDto
{
	public required string Id { get; init; }

	public required string Name { get; init; }

	public string? Type { get; init; }

	public bool IsActive { get; init; }

	public int? VolumePercent { get; init; }
}

/// <summary>Result of the <c>devices</c> operation. See <see cref="MusicPlayerCatalogResult" />'s
/// remarks: <c>IMusicPlayerDeviceProvider.GetDevicesAsync</c> throws on failure too, so this DTO is
/// only ever the success shape.</summary>
public sealed record MusicPlayerDevicesResult
{
	public required IReadOnlyList<MusicPlayerDeviceDto> Devices { get; init; }
}

/// <summary>Arguments for the <c>transfer</c> operation.</summary>
public sealed record MusicPlayerTransferArguments
{
	public required string InstanceId { get; init; }

	public required string DeviceId { get; init; }

	public required bool StartPlayback { get; init; }
}
