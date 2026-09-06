using MacroDeck.Plugin.Protocol.Capabilities.MusicPlayer;
using MacroDeck.Sdk.MusicPlayer;

namespace MacroDeck.Plugin.Hosting.Capabilities.MusicPlayer;

/// <summary>Maps the SDK's music-player types to their wire DTOs.</summary>
internal static class MusicPlayerDescriptorMapper
{
	public static MusicPlayerInstanceDto ToDto(MusicPlayerInstance instance, IMusicPlayer? player)
		=> new()
		{
			Id = instance.Id,
			DisplayName = instance.DisplayName,
			HasCatalog = player is IMusicPlayerCatalogProvider,
			HasDevices = player is IMusicPlayerDeviceProvider
		};

	public static MusicPlayerStateDto ToDto(MusicPlayerState state)
		=> new()
		{
			IsConnected = state.IsConnected,
			IsUnavailable = state.IsUnavailable,
			StatusMessage = state.StatusMessage,
			PlaybackState = state.PlaybackState.ToString(),
			TrackName = state.TrackName,
			Artists = state.Artists,
			AlbumName = state.AlbumName,
			ArtworkId = state.ArtworkId,
			PositionSeconds = state.Position?.TotalSeconds,
			DurationSeconds = state.Duration?.TotalSeconds,
			VolumePercent = state.VolumePercent,
			ShuffleEnabled = state.ShuffleEnabled,
			RepeatMode = state.RepeatMode.ToString(),
			DeviceName = state.DeviceName,
			DeviceType = state.DeviceType
		};

	public static MusicPlayerCatalogItemDto ToDto(MusicPlayerCatalogItem item)
		=> new()
		{
			Id = item.Id,
			Title = item.Title,
			Kind = item.Kind.ToString(),
			Subtitle = item.Subtitle,
			ArtworkId = item.ArtworkId,
			DurationSeconds = item.Duration?.TotalSeconds
		};

	public static MusicPlayerCatalogItem ToDomain(MusicPlayerCatalogItemDto dto)
	{
		if (!Enum.TryParse<MusicPlayerCatalogItemKind>(dto.Kind, ignoreCase: false, out var kind))
		{
			throw new InvalidOperationException($"Unknown music player catalog item kind '{dto.Kind}'.");
		}

		return new MusicPlayerCatalogItem(dto.Id,
			dto.Title,
			kind,
			dto.Subtitle,
			dto.ArtworkId,
			dto.DurationSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null);
	}

	public static MusicPlayerDeviceDto ToDto(MusicPlayerDevice device)
		=> new()
		{
			Id = device.Id,
			Name = device.Name,
			Type = device.Type,
			IsActive = device.IsActive,
			VolumePercent = device.VolumePercent
		};
}
